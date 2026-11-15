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

    private sealed class TextureRecordingBackend : StubGpuBackend
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
