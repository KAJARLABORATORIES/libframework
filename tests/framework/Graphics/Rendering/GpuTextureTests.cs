using kajarlabs.Framework.Graphics;
using kajarlabs.Framework.Graphics.Rendering;
using kajarlabs.Framework.Numerics;
using kajarlabs.Framework.Tests.TestUtilities;
using Microsoft.Extensions.Logging.Abstractions;

namespace kajarlabs.Framework.Tests.Graphics.Rendering;

public sealed class GpuTextureTests
{
    private static readonly nint fake_window = new(0x2000);

    [Fact]
    public void BuiltInWhitePixelIs1x1AndUploadsOpaqueWhiteBeforeFirstRenderPass()
    {
        var backend = new TextureRecordingBackend();
        using var renderer = new SdlGpuRenderer(backend, NullLogger.Instance);
        renderer.Initialize(fake_window);
        backend.ResetCopyPassCounters();

        var white = renderer.WhitePixel;
        Assert.Equal(1, white.Width);
        Assert.Equal(1, white.Height);

        // User-facing Dispose on built-in WhitePixel must not destroy or invalidate it.
        white.Dispose();
        Assert.Equal(1, renderer.TextureManager!.LiveTextureCount);

        renderer.BeginFrame();
        renderer.EndFrame();

        Assert.Single(backend.CopyPassEvents);
        Assert.Equal(1, backend.CopyPassCount);
        Assert.Single(backend.Uploads);
        Assert.Equal(new byte[] { 255, 255, 255, 255 }, backend.Uploads[0].PixelBytes);
        Assert.True(backend.Uploads[0].CycleTexture);
        Assert.True(backend.Events.IndexOf("EndCopyPass") < backend.Events.IndexOf("ClearColorTarget"));

        // Steady-state frame with no pending SetData executes zero copy passes.
        renderer.BeginFrame();
        renderer.EndFrame();
        Assert.Equal(1, backend.CopyPassCount);
    }

    [Fact]
    public void CreateTextureAndSetDataValidateDimensionsBoundsBufferLengthAndDisposedState()
    {
        var backend = new TextureRecordingBackend();
        using var renderer = new SdlGpuRenderer(backend, NullLogger.Instance);
        renderer.Initialize(fake_window);
        backend.ResetCopyPassCounters();

        Assert.Throws<ArgumentOutOfRangeException>(() => renderer.CreateTexture(0, 4));
        Assert.Throws<ArgumentOutOfRangeException>(() => renderer.CreateTexture(4, -1));

        using var texture = renderer.CreateTexture(4, 4);
        Assert.Equal(4, texture.Width);
        Assert.Equal(4, texture.Height);

        var validFull = new byte[4 * 4 * 4];
        var validSub = new byte[2 * 2 * 4];

        // Buffer length mismatches
        Assert.Throws<ArgumentException>(() => texture.SetData(new byte[4 * 4 * 4 - 1]));
        Assert.Throws<ArgumentException>(() => texture.SetData(new byte[4 * 4 * 4 + 4]));
        Assert.Throws<ArgumentException>(() => texture.SetData(new RectangleI(0, 0, 2, 2), new byte[12]));

        // Region bounds violations (negative origin, zero/negative size, out of bounds, int overflow)
        Assert.Throws<ArgumentOutOfRangeException>(() => texture.SetData(new RectangleI(-1, 0, 2, 2), validSub));
        Assert.Throws<ArgumentOutOfRangeException>(() => texture.SetData(new RectangleI(0, -1, 2, 2), validSub));
        Assert.Throws<ArgumentOutOfRangeException>(() => texture.SetData(new RectangleI(0, 0, 0, 2), []));
        Assert.Throws<ArgumentOutOfRangeException>(() => texture.SetData(new RectangleI(0, 0, 2, 0), []));
        Assert.Throws<ArgumentOutOfRangeException>(() => texture.SetData(new RectangleI(3, 0, 2, 2), validSub));
        Assert.Throws<ArgumentOutOfRangeException>(() => texture.SetData(new RectangleI(0, 3, 2, 2), validSub));
        Assert.Throws<ArgumentOutOfRangeException>(() => texture.SetData(new RectangleI(int.MaxValue, 0, 2, 2), validSub));

        // NullRenderer validates identical contracts without GPU backend.
        using var nullRenderer = new NullRenderer(800, 600);
        Assert.Equal(1, nullRenderer.WhitePixel.Width);
        Assert.Equal(1, nullRenderer.WhitePixel.Height);
        using var nullTex = nullRenderer.CreateTexture(4, 4);
        Assert.Throws<ArgumentException>(() => nullTex.SetData(new byte[4]));
        Assert.Throws<ArgumentOutOfRangeException>(() => nullTex.SetData(new RectangleI(3, 3, 2, 2), validSub));
        nullTex.SetData(validFull);
        nullTex.SetData(new RectangleI(1, 1, 2, 2), validSub);
        nullTex.Dispose();
        Assert.Throws<ObjectDisposedException>(() => nullTex.SetData(validFull));

        // Disposing GPU texture rejects subsequent uploads immediately.
        texture.Dispose();
        Assert.Throws<ObjectDisposedException>(() => texture.SetData(validFull));
    }

    [Fact]
    public void BatchesFullAndSubRegionUploadsIntoSingleCopyPass()
    {
        var backend = new TextureRecordingBackend();
        using var renderer = new SdlGpuRenderer(backend, NullLogger.Instance);
        renderer.Initialize(fake_window);
        backend.ResetCopyPassCounters();

        // Drain initial WhitePixel upload on frame 1.
        renderer.BeginFrame();
        renderer.EndFrame();
        backend.Uploads.Clear();

        using var tex = renderer.CreateTexture(4, 4);
        var fullData = new byte[4 * 4 * 4];
        fullData[0] = 10;
        fullData[63] = 20;

        var subData = new byte[2 * 1 * 4];
        subData[0] = 99;
        subData[7] = 77;

        tex.SetData(fullData);
        tex.SetData(new RectangleI(1, 2, 2, 1), subData);

        renderer.BeginFrame();
        renderer.EndFrame();

        Assert.Equal(2, backend.CopyPassCount);
        Assert.Equal(2, backend.Uploads.Count);

        Assert.Equal(0u, backend.Uploads[0].Destination.X);
        Assert.Equal(0u, backend.Uploads[0].Destination.Y);
        Assert.Equal(4u, backend.Uploads[0].Destination.Width);
        Assert.Equal(4u, backend.Uploads[0].Destination.Height);
        Assert.True(backend.Uploads[0].CycleTexture);
        Assert.Equal(fullData, backend.Uploads[0].PixelBytes);

        Assert.Equal(1u, backend.Uploads[1].Destination.X);
        Assert.Equal(2u, backend.Uploads[1].Destination.Y);
        Assert.Equal(2u, backend.Uploads[1].Destination.Width);
        Assert.Equal(1u, backend.Uploads[1].Destination.Height);
        Assert.False(backend.Uploads[1].CycleTexture);
        Assert.Equal(subData, backend.Uploads[1].PixelBytes);
    }

    [Fact]
    public void DeferredDisposalKeepsTexturesAliveWhileFenceInFlightAndReleasesOnceSignaled()
    {
        var backend = new TextureRecordingBackend { SignalFencesImmediately = false };
        using var renderer = new SdlGpuRenderer(backend, NullLogger.Instance);
        renderer.Initialize(fake_window);

        var texture = (GpuTexture)renderer.CreateTexture(8, 8);
        var textureHandle = texture.Handle;

        // Disposing mid-frame queues the texture rather than calling ReleaseTexture immediately.
        renderer.BeginFrame();
        texture.Dispose();
        Assert.DoesNotContain(textureHandle, backend.ReleasedTextures);
        Assert.Equal(1, renderer.TextureManager!.PendingDisposalCount);

        // Submitting EndFrame attaches a fence while GPU command buffer is still executing.
        renderer.EndFrame();
        Assert.Single(backend.AcquiredDrawFences);
        var fenceHandle = backend.AcquiredDrawFences[0];
        Assert.DoesNotContain(textureHandle, backend.ReleasedTextures);
        Assert.DoesNotContain(fenceHandle, backend.ReleasedFences);

        // Next frame while fence is still unsignaled must NOT release the texture.
        renderer.BeginFrame();
        Assert.DoesNotContain(textureHandle, backend.ReleasedTextures);
        renderer.EndFrame();

        // Once the GPU signals the fence, the next BeginFrame releases both texture and fence.
        backend.SignalFencesImmediately = true;
        renderer.BeginFrame();
        Assert.Contains(textureHandle, backend.ReleasedTextures);
        Assert.Contains(fenceHandle, backend.ReleasedFences);
        Assert.Equal(0, renderer.TextureManager.PendingDisposalCount);
        renderer.EndFrame();

        // Textures disposed right before Shutdown are drained after WaitForIdle and before DestroyDevice.
        var lateTexture = (GpuTexture)renderer.CreateTexture(2, 2);
        var lateHandle = lateTexture.Handle;
        lateTexture.Dispose();
        Assert.DoesNotContain(lateHandle, backend.ReleasedTextures);

        renderer.Shutdown();
        Assert.Contains(lateHandle, backend.ReleasedTextures);
        Assert.True(backend.Events.IndexOf("WaitForIdle") < backend.Events.LastIndexOf("ReleaseTexture"));
        Assert.True(backend.Events.LastIndexOf("ReleaseTexture") < backend.Events.IndexOf("DestroyDevice"));
    }

    [Fact]
    public async Task ConcurrentTextureCreationAndUploadsWithShutdownNeverTouchDestroyedDevice()
    {
        var backend = new ConcurrentShutdownRecordingBackend();
        using var renderer = new SdlGpuRenderer(backend, NullLogger.Instance);
        renderer.Initialize(fake_window);

        var ct = TestContext.Current.CancellationToken;
        using var startGate = new ManualResetEventSlim(false);
        var workerTasks = new List<Task>();

        for (var w = 0; w < 6; w++)
        {
            var workerId = w;
            workerTasks.Add(Task.Run(() =>
            {
                startGate.Wait(ct);

                for (var i = 0; i < 60; i++)
                {
                    try
                    {
                        var texture = renderer.CreateTexture(4, 4);
                        var pixels = new byte[4 * 4 * 4];
                        Array.Fill(pixels, (byte)(workerId + i));
                        texture.SetData(pixels);

                        if (i % 2 == 0)
                            texture.Dispose();
                    }
                    catch (ObjectDisposedException)
                    {
                        break;
                    }
                    catch (InvalidOperationException)
                    {
                        break;
                    }
                }
            }, ct));
        }

        startGate.Set();

        // Pump several frames concurrently while background threads create and upload textures.
        for (var frame = 0; frame < 8; frame++)
        {
            renderer.BeginFrame();
            renderer.EndFrame();
            await Task.Delay(1, ct);
        }

        // Shutdown while workers may still be racing to call CreateTexture/SetData.
        renderer.Shutdown();
        await Task.WhenAll(workerTasks);

        Assert.False(backend.SawTextureCallAfterDestroyDevice);
        Assert.Equal(backend.CreatedSamplerTextures.Order(), backend.ReleasedTextures.Order());
    }

    [Fact]
    public void DisposingTextureBetweenCopyAndUploadPreservesTransferOffsetOfSubsequentUploads()
    {
        var backend = new MidFlushDisposeBackend();
        using var renderer = new SdlGpuRenderer(backend, NullLogger.Instance);
        renderer.Initialize(fake_window);

        // Drain initial WhitePixel upload.
        renderer.BeginFrame();
        renderer.EndFrame();
        backend.Uploads.Clear();

        using var first = renderer.CreateTexture(2, 2);
        using var second = renderer.CreateTexture(2, 2);

        var firstPixels = new byte[2 * 2 * 4];
        Array.Fill(firstPixels, (byte)0x11);

        var secondPixels = new byte[2 * 2 * 4];
        Array.Fill(secondPixels, (byte)0x77);

        first.SetData(firstPixels);
        second.SetData(secondPixels);

        // Dispose the first texture right when UnmapTransferBuffer runs (between staging copy and CopyPass upload).
        backend.OnUnmapTransferBuffer = first.Dispose;

        renderer.BeginFrame();
        renderer.EndFrame();

        Assert.Single(backend.Uploads);
        Assert.Equal(secondPixels, backend.Uploads[0].PixelBytes);
    }

    [Fact]
    public async Task NullRendererSupportsConcurrentTextureCreationAndUploadsAndRejectsPostDisposal()
    {
        var renderer = new NullRenderer(800, 600);
        var ct = TestContext.Current.CancellationToken;
        var tasks = new List<Task>();

        for (var w = 0; w < 6; w++)
        {
            var workerId = w;
            tasks.Add(Task.Run(() =>
            {
                var pixels = new byte[4 * 4 * 4];
                Array.Fill(pixels, (byte)workerId);

                for (var i = 0; i < 80; i++)
                {
                    using var tex = renderer.CreateTexture(4, 4);
                    tex.SetData(pixels);
                    tex.SetData(new RectangleI(1, 1, 2, 2), new byte[2 * 2 * 4]);
                }
            }, ct));
        }

        await Task.WhenAll(tasks);

        renderer.Dispose();
        renderer.Dispose();

        Assert.Throws<ObjectDisposedException>(() => renderer.CreateTexture(2, 2));
    }

    [Fact]
    public void SdlGpuRendererRejectsTextureOperationsAfterDispose()
    {
        var backend = new TextureRecordingBackend();
        var renderer = new SdlGpuRenderer(backend, NullLogger.Instance);
        renderer.Initialize(fake_window);

        renderer.Dispose();
        renderer.Dispose();

        Assert.Throws<ObjectDisposedException>(() => renderer.CreateTexture(2, 2));
        Assert.Throws<ObjectDisposedException>(() => _ = renderer.WhitePixel);
    }

    private sealed class ConcurrentShutdownRecordingBackend : StubGpuBackend
    {
        private readonly Lock backendLock = new();
        private readonly Dictionary<nint, byte[]> mappedBuffers = [];
        private int deviceDestroyed;

        public bool SawTextureCallAfterDestroyDevice { get; private set; }

        public List<nint> CreatedSamplerTextures { get; } = [];

        public List<nint> ReleasedTextures { get; } = [];

        public override nint CreateTexture(nint device, in GpuTextureDescription description)
        {
            var handle = NextHandle();

            lock (backendLock)
            {
                if (Volatile.Read(ref deviceDestroyed) != 0)
                    SawTextureCallAfterDestroyDevice = true;

                if ((description.Usage & GpuTextureUsage.ColorTarget) == 0)
                    CreatedSamplerTextures.Add(handle);
            }

            return handle;
        }

        public override void ReleaseTexture(nint device, nint texture)
        {
            lock (backendLock)
            {
                if (Volatile.Read(ref deviceDestroyed) != 0)
                    SawTextureCallAfterDestroyDevice = true;

                if (CreatedSamplerTextures.Contains(texture))
                    ReleasedTextures.Add(texture);
            }
        }

        public override void DestroyDevice(nint device)
        {
            lock (backendLock)
                Volatile.Write(ref deviceDestroyed, 1);
        }

        public override nint CreateTransferBuffer(nint device, GpuTransferBufferUsage usage, uint sizeInBytes)
        {
            var handle = NextHandle();

            lock (backendLock)
                mappedBuffers[handle] = new byte[sizeInBytes];

            return handle;
        }

        public override void ReleaseTransferBuffer(nint device, nint transferBuffer)
        {
            lock (backendLock)
                mappedBuffers.Remove(transferBuffer);
        }

        public override nint MapTransferBuffer(nint device, nint transferBuffer, bool cycle) => transferBuffer;

        public override void CopyToMappedBuffer(nint mappedDestination, int byteOffset, byte[] source, int sourceOffset, int byteCount)
        {
            lock (backendLock)
                Buffer.BlockCopy(source, sourceOffset, mappedBuffers[mappedDestination], byteOffset, byteCount);
        }
    }

    private sealed class MidFlushDisposeBackend : TextureRecordingBackend
    {
        public Action? OnUnmapTransferBuffer { get; set; }

        public override void UnmapTransferBuffer(nint device, nint transferBuffer)
        {
            base.UnmapTransferBuffer(device, transferBuffer);

            var callback = OnUnmapTransferBuffer;
            OnUnmapTransferBuffer = null;
            callback?.Invoke();
        }
    }

    private class TextureRecordingBackend : StubGpuBackend
    {
        private readonly Dictionary<nint, byte[]> mappedBuffers = [];

        public TextureRecordingBackend()
        {
            PixelWidth = 640;
            PixelHeight = 480;
        }

        public bool SignalFencesImmediately { get; set; } = true;
        public int CopyPassCount { get; private set; }
        public List<string> Events { get; } = [];
        public List<string> CopyPassEvents { get; } = [];
        public List<nint> ReleasedTextures { get; } = [];
        public List<nint> AcquiredDrawFences { get; } = [];
        public List<nint> ReleasedFences { get; } = [];
        public List<(GpuTextureRegion Destination, bool CycleTexture, byte[] PixelBytes)> Uploads { get; } = [];

        public void ResetCopyPassCounters()
        {
            CopyPassCount = 0;
            CopyPassEvents.Clear();
            Events.Clear();
        }

        public override void DestroyDevice(nint device) => Events.Add(nameof(DestroyDevice));

        public override void ReleaseTexture(nint device, nint texture)
        {
            ReleasedTextures.Add(texture);
            Events.Add(nameof(ReleaseTexture));
        }

        public override void ReleaseSampler(nint device, nint sampler) => Events.Add(nameof(ReleaseSampler));

        public override nint CreateTransferBuffer(nint device, GpuTransferBufferUsage usage, uint sizeInBytes)
        {
            var handle = NextHandle();
            mappedBuffers[handle] = new byte[sizeInBytes];
            return handle;
        }

        public override void ReleaseTransferBuffer(nint device, nint transferBuffer)
            => mappedBuffers.Remove(transferBuffer);

        public override nint MapTransferBuffer(nint device, nint transferBuffer, bool cycle) => transferBuffer;

        public override void CopyToMappedBuffer(nint mappedDestination, int byteOffset, byte[] source, int sourceOffset, int byteCount)
            => Buffer.BlockCopy(source, sourceOffset, mappedBuffers[mappedDestination], byteOffset, byteCount);

        public override nint BeginCopyPass(nint commandBuffer)
        {
            CopyPassCount++;
            CopyPassEvents.Add(nameof(BeginCopyPass));
            Events.Add(nameof(BeginCopyPass));
            return NextHandle();
        }

        public override void UploadToTexture(nint copyPass, in GpuTextureUploadSource source, in GpuTextureRegion destination, bool cycle)
        {
            var byteCount = (int)(destination.Width * destination.Height * 4);
            var copy = new byte[byteCount];
            Buffer.BlockCopy(mappedBuffers[source.TransferBuffer], (int)source.Offset, copy, 0, byteCount);
            Uploads.Add((destination, cycle, copy));
        }

        public override void EndCopyPass(nint copyPass) => Events.Add(nameof(EndCopyPass));

        public override nint SubmitCommandBufferAndAcquireFence(nint commandBuffer)
        {
            var fence = NextHandle();
            AcquiredDrawFences.Add(fence);
            return fence;
        }

        public override void ClearColorTarget(nint commandBuffer, nint colorTargetTexture, Color4 clearColor)
            => Events.Add(nameof(ClearColorTarget));

        public override bool QueryFence(nint device, nint fence) => SignalFencesImmediately;

        public override void ReleaseFence(nint device, nint fence)
        {
            ReleasedFences.Add(fence);
            Events.Add(nameof(ReleaseFence));
        }

        public override bool WaitForIdle(nint device)
        {
            Events.Add(nameof(WaitForIdle));
            return true;
        }
    }
}
