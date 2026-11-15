using System.Buffers;
using kajarlabs.Framework.Numerics;

namespace kajarlabs.Framework.Graphics.Rendering;

/// <summary>
/// Manages GPU-backed 2D RGBA8 textures, staged transfer-buffer uploads, built-in white pixel fallback,
/// and fence-synchronized deferred texture disposal.
/// </summary>
internal sealed class GpuTextureManager : IDisposable
{
    private readonly IGpuBackend backend;
    private readonly nint device;
    private readonly Lock stateLock = new();
    private readonly HashSet<GpuTexture> liveTextures = [];
    private readonly List<PendingUpload> pendingUploads = [];
    private readonly List<nint> pendingTextureDisposals = [];
    private readonly List<nint> pendingTransferBufferDisposals = [];
    private readonly List<RetiredBatch> retiredBatches = [];
    private readonly GpuTexture whitePixel;

    private nint uploadTransferBuffer;
    private uint uploadTransferBufferCapacity;
    private bool disposed;

    public GpuTextureManager(IGpuBackend backend, nint device)
    {
        ArgumentNullException.ThrowIfNull(backend);

        if (device == nint.Zero)
            throw new ArgumentException("GPU device handle must not be zero.", nameof(device));

        this.backend = backend;
        this.device = device;

        whitePixel = createTextureCore(1, 1, allowUserDispose: false);
        whitePixel.SetData([255, 255, 255, 255]);
    }

    internal ITexture WhitePixel => whitePixel;

    internal int LiveTextureCount
    {
        get
        {
            lock (stateLock)
                return liveTextures.Count;
        }
    }

    internal int PendingUploadCount
    {
        get
        {
            lock (stateLock)
                return pendingUploads.Count;
        }
    }

    internal int PendingDisposalCount
    {
        get
        {
            lock (stateLock)
            {
                var count = pendingTextureDisposals.Count;

                for (var i = 0; i < retiredBatches.Count; i++)
                    count += retiredBatches[i].Textures.Length;

                return count;
            }
        }
    }

    internal bool HasUnfencedDisposals
    {
        get
        {
            lock (stateLock)
                return pendingTextureDisposals.Count > 0 || pendingTransferBufferDisposals.Count > 0;
        }
    }

    public GpuTexture CreateTexture(int width, int height)
        => createTextureCore(width, height, allowUserDispose: true);

    internal void EnqueueUpload(GpuTexture texture, RectangleI region, ReadOnlySpan<byte> rgba8Pixels)
    {
        var byteCount = rgba8Pixels.Length;
        var stagedBuffer = ArrayPool<byte>.Shared.Rent(byteCount);

        try
        {
            rgba8Pixels.CopyTo(stagedBuffer);

            lock (stateLock)
            {
                ObjectDisposedException.ThrowIf(disposed || texture.IsDisposed, texture);
                pendingUploads.Add(new PendingUpload(texture, region, stagedBuffer, byteCount));
            }
        }
        catch
        {
            ArrayPool<byte>.Shared.Return(stagedBuffer);
            throw;
        }
    }

    internal void EnqueueDeferredDisposal(GpuTexture texture)
    {
        lock (stateLock)
        {
            if (disposed)
                return;

            liveTextures.Remove(texture);

            if (texture.Handle != nint.Zero)
                pendingTextureDisposals.Add(texture.Handle);
        }
    }

    internal void FlushPendingUploads(nint commandBuffer)
    {
        if (commandBuffer == nint.Zero)
            throw new ArgumentException("Command buffer handle must not be zero.", nameof(commandBuffer));

        List<PendingUpload>? drainedUploads;
        lock (stateLock)
        {
            if (disposed || pendingUploads.Count == 0)
                return;

            drainedUploads = [.. pendingUploads];
            pendingUploads.Clear();
        }

        try
        {
            ulong totalBytes = 0;
            var activeCount = 0;

            for (var i = 0; i < drainedUploads.Count; i++)
            {
                if (!drainedUploads[i].Texture.IsDisposed)
                {
                    totalBytes += (uint)drainedUploads[i].ByteCount;
                    activeCount++;
                }
            }

            if (activeCount == 0 || totalBytes == 0)
                return;

            if (totalBytes > uint.MaxValue)
                throw new InvalidOperationException("Queued texture uploads exceed maximum transfer buffer size.");

            var transferBuffer = ensureUploadTransferBuffer((uint)totalBytes);
            var mappedPtr = backend.MapTransferBuffer(device, transferBuffer, cycle: true);

            if (mappedPtr == nint.Zero)
                throw new InvalidOperationException("Failed to map SDL_GPUTransferBuffer for texture upload.");

            try
            {
                var writeOffset = 0;

                for (var i = 0; i < drainedUploads.Count; i++)
                {
                    var upload = drainedUploads[i];

                    if (upload.Texture.IsDisposed)
                        continue;

                    backend.CopyToMappedBuffer(mappedPtr, writeOffset, upload.Buffer, 0, upload.ByteCount);
                    writeOffset += upload.ByteCount;
                }
            }
            finally
            {
                backend.UnmapTransferBuffer(device, transferBuffer);
            }

            var copyPass = backend.BeginCopyPass(commandBuffer);

            if (copyPass == nint.Zero)
                throw new InvalidOperationException("Failed to begin SDL_GPUCopyPass for texture upload.");

            try
            {
                uint readOffset = 0;

                for (var i = 0; i < drainedUploads.Count; i++)
                {
                    var upload = drainedUploads[i];

                    if (upload.Texture.IsDisposed)
                        continue;

                    var source = new GpuTextureUploadSource(
                        transferBuffer,
                        readOffset,
                        (uint)upload.Region.Width,
                        (uint)upload.Region.Height);

                    var destination = new GpuTextureRegion(
                        upload.Texture.Handle,
                        (uint)upload.Region.X,
                        (uint)upload.Region.Y,
                        (uint)upload.Region.Width,
                        (uint)upload.Region.Height);

                    var isFullTexture = upload.Region.X == 0
                        && upload.Region.Y == 0
                        && upload.Region.Width == upload.Texture.Width
                        && upload.Region.Height == upload.Texture.Height;

                    backend.UploadToTexture(copyPass, in source, in destination, cycle: isFullTexture);
                    readOffset += (uint)upload.ByteCount;
                }
            }
            finally
            {
                backend.EndCopyPass(copyPass);
            }
        }
        finally
        {
            for (var i = 0; i < drainedUploads.Count; i++)
                ArrayPool<byte>.Shared.Return(drainedUploads[i].Buffer);
        }
    }

    internal void AttachDisposalsToFence(nint fence)
    {
        if (fence == nint.Zero)
            return;

        lock (stateLock)
        {
            if (disposed || (pendingTextureDisposals.Count == 0 && pendingTransferBufferDisposals.Count == 0))
                return;

            var textures = pendingTextureDisposals.Count > 0 ? [.. pendingTextureDisposals] : Array.Empty<nint>();
            var transferBuffers = pendingTransferBufferDisposals.Count > 0
                ? [.. pendingTransferBufferDisposals]
                : Array.Empty<nint>();

            pendingTextureDisposals.Clear();
            pendingTransferBufferDisposals.Clear();
            retiredBatches.Add(new RetiredBatch(fence, textures, transferBuffers));
        }
    }

    internal void CollectCompletedDisposals()
    {
        List<RetiredBatch>? completedBatches = null;

        lock (stateLock)
        {
            if (disposed || retiredBatches.Count == 0)
                return;

            for (var i = retiredBatches.Count - 1; i >= 0; i--)
            {
                var batch = retiredBatches[i];

                if (!backend.QueryFence(device, batch.Fence))
                    continue;

                completedBatches ??= [];
                completedBatches.Add(batch);
                retiredBatches.RemoveAt(i);
            }
        }

        if (completedBatches is null)
            return;

        for (var i = 0; i < completedBatches.Count; i++)
            releaseRetiredBatch(completedBatches[i]);
    }

    public void Dispose()
    {
        GpuTexture[] texturesToRelease;
        nint[] pendingTextures;
        nint[] pendingTransferBuffers;
        RetiredBatch[] remainingBatches;
        PendingUpload[] leftoverUploads;
        nint activeTransferBuffer;

        lock (stateLock)
        {
            if (disposed)
                return;

            disposed = true;
            texturesToRelease = [.. liveTextures];
            pendingTextures = [.. pendingTextureDisposals];
            pendingTransferBuffers = [.. pendingTransferBufferDisposals];
            remainingBatches = [.. retiredBatches];
            leftoverUploads = [.. pendingUploads];
            activeTransferBuffer = uploadTransferBuffer;

            liveTextures.Clear();
            pendingTextureDisposals.Clear();
            pendingTransferBufferDisposals.Clear();
            retiredBatches.Clear();
            pendingUploads.Clear();
            uploadTransferBuffer = nint.Zero;
            uploadTransferBufferCapacity = 0;
        }

        for (var i = 0; i < leftoverUploads.Length; i++)
            ArrayPool<byte>.Shared.Return(leftoverUploads[i].Buffer);

        for (var i = 0; i < texturesToRelease.Length; i++)
        {
            texturesToRelease[i].MarkManagerDisposed();

            if (texturesToRelease[i].Handle != nint.Zero)
                backend.ReleaseTexture(device, texturesToRelease[i].Handle);
        }

        for (var i = 0; i < pendingTextures.Length; i++)
            backend.ReleaseTexture(device, pendingTextures[i]);

        for (var i = 0; i < pendingTransferBuffers.Length; i++)
            backend.ReleaseTransferBuffer(device, pendingTransferBuffers[i]);

        for (var i = 0; i < remainingBatches.Length; i++)
            releaseRetiredBatch(remainingBatches[i]);

        if (activeTransferBuffer != nint.Zero)
            backend.ReleaseTransferBuffer(device, activeTransferBuffer);
    }

    private GpuTexture createTextureCore(int width, int height, bool allowUserDispose)
    {
        TextureValidation.ValidateDimensions(width, height);

        lock (stateLock)
            ObjectDisposedException.ThrowIf(disposed, this);

        var description = new GpuTextureDescription(
            (uint)width,
            (uint)height,
            backend.Rgba8TextureFormat,
            GpuTextureUsage.Sampler);

        var handle = backend.CreateTexture(device, in description);

        if (handle == nint.Zero)
            throw new InvalidOperationException($"Failed to create SDL_GPUTexture ({width}x{height}).");

        var texture = new GpuTexture(this, handle, width, height, allowUserDispose);

        lock (stateLock)
        {
            if (disposed)
            {
                texture.MarkManagerDisposed();
                backend.ReleaseTexture(device, handle);
                throw new ObjectDisposedException(nameof(GpuTextureManager));
            }

            liveTextures.Add(texture);
        }

        return texture;
    }

    private nint ensureUploadTransferBuffer(uint requiredBytes)
    {
        if (uploadTransferBuffer != nint.Zero && uploadTransferBufferCapacity >= requiredBytes)
            return uploadTransferBuffer;

        var previousBuffer = uploadTransferBuffer;
        var nextCapacity = uploadTransferBufferCapacity == 0
            ? requiredBytes
            : Math.Max(requiredBytes, (uint)Math.Min(uint.MaxValue, (ulong)uploadTransferBufferCapacity * 2));

        var createdBuffer = backend.CreateTransferBuffer(device, GpuTransferBufferUsage.Upload, nextCapacity);

        if (createdBuffer == nint.Zero)
            throw new InvalidOperationException($"Failed to allocate SDL_GPUTransferBuffer ({nextCapacity} bytes).");

        uploadTransferBuffer = createdBuffer;
        uploadTransferBufferCapacity = nextCapacity;

        if (previousBuffer != nint.Zero)
        {
            lock (stateLock)
                pendingTransferBufferDisposals.Add(previousBuffer);
        }

        return createdBuffer;
    }

    private void releaseRetiredBatch(in RetiredBatch batch)
    {
        for (var i = 0; i < batch.Textures.Length; i++)
            backend.ReleaseTexture(device, batch.Textures[i]);

        for (var i = 0; i < batch.TransferBuffers.Length; i++)
            backend.ReleaseTransferBuffer(device, batch.TransferBuffers[i]);

        backend.ReleaseFence(device, batch.Fence);
    }

    private readonly record struct PendingUpload(
        GpuTexture Texture,
        RectangleI Region,
        byte[] Buffer,
        int ByteCount);

    private readonly record struct RetiredBatch(
        nint Fence,
        nint[] Textures,
        nint[] TransferBuffers);
}
