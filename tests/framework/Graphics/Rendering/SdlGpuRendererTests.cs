using kajarlabs.Framework.Graphics;
using kajarlabs.Framework.Graphics.Rendering;
using kajarlabs.Framework.Numerics;
using Microsoft.Extensions.Logging.Abstractions;

namespace kajarlabs.Framework.Tests.Graphics.Rendering;

public sealed class SdlGpuRendererTests
{
    private static readonly nint fake_window = new(0x2000);

    [Fact]
    public void BeginFrameClearAndEndFrameClearsColorTargetAndPublishesFrame()
    {
        var backend = new FakeGpuBackend
        {
            PixelWidth = 1024,
            PixelHeight = 576,
        };
        using var renderer = new SdlGpuRenderer(backend, NullLogger.Instance, 800, 600);

        renderer.Initialize(fake_window);

        Assert.Equal(new Viewport(1024, 576), renderer.Viewport);

        renderer.BeginFrame(new Viewport(800, 600));
        Assert.Equal(new Viewport(1024, 576), renderer.Viewport);

        renderer.Clear(Color4.CYAN);
        renderer.EndFrame();

        Assert.Equal(1, backend.ClearCount);
        Assert.Equal(Color4.CYAN, backend.LastClearColor);
        Assert.True(renderer.Present());
        Assert.Equal(1, backend.BlitCount);
    }

    [Fact]
    public void EndFrameDefaultsToBlackClearWhenClearNotCalled()
    {
        var backend = new FakeGpuBackend
        {
            PixelWidth = 640,
            PixelHeight = 480,
        };
        using var renderer = new SdlGpuRenderer(backend, NullLogger.Instance);

        renderer.Initialize(fake_window);
        renderer.BeginFrame(new Viewport(640, 480));
        renderer.EndFrame();

        Assert.Equal(1, backend.ClearCount);
        Assert.Equal(Color4.BLACK, backend.LastClearColor);
    }

    [Fact]
    public void EndFrameFaultedCancelsCommandBufferWithoutClearingOrPublishing()
    {
        var backend = new FakeGpuBackend
        {
            PixelWidth = 640,
            PixelHeight = 480,
        };
        using var renderer = new SdlGpuRenderer(backend, NullLogger.Instance);

        renderer.Initialize(fake_window);
        renderer.BeginFrame(new Viewport(640, 480));
        renderer.Clear(Color4.RED);
        renderer.EndFrame(faulted: true);

        Assert.Equal(0, backend.ClearCount);
        Assert.Equal(1, backend.CancelCount);
        Assert.False(renderer.Present());
    }

    [Fact]
    public void ResizeAndMinimizedSkipAreHandledSafely()
    {
        var backend = new FakeGpuBackend
        {
            PixelWidth = 640,
            PixelHeight = 480,
        };
        using var renderer = new SdlGpuRenderer(backend, NullLogger.Instance);

        renderer.Initialize(fake_window);
        renderer.Resize(0, 0);

        renderer.BeginFrame(new Viewport(0, 0));
        renderer.Clear(Color4.GREEN);
        renderer.EndFrame();

        Assert.Equal(0, backend.ClearCount);
        Assert.False(renderer.Present());

        renderer.Resize(1280, 720);
        Assert.Equal(new Viewport(1280, 720), renderer.Viewport);

        renderer.BeginFrame(new Viewport(1280, 720));
        renderer.Clear(Color4.BLUE);
        renderer.EndFrame();

        Assert.Equal(1, backend.ClearCount);
        Assert.Equal(Color4.BLUE, backend.LastClearColor);
        Assert.True(renderer.Present());
    }

    private sealed class FakeGpuBackend : IGpuBackend
    {
        private nint nextHandle = 0x100;

        public int PixelWidth { get; set; } = 1280;
        public int PixelHeight { get; set; } = 720;
        public int ClearCount { get; private set; }
        public int CancelCount { get; private set; }
        public int BlitCount { get; private set; }
        public Color4 LastClearColor { get; private set; }

        public nint CreateDevice(GpuShaderFormats shaderFormats, bool debugMode, string? preferredDriver) => ++nextHandle;
        public void DestroyDevice(nint device) { }
        public string? GetDeviceDriver(nint device) => "vulkan";
        public GpuShaderFormats GetSupportedShaderFormats(nint device) => GpuShaderFormats.SpirV;
        public bool ClaimWindow(nint device, nint windowHandle) => true;
        public void ReleaseWindow(nint device, nint windowHandle) { }
        public bool WindowSupportsSwapchainComposition(nint device, nint windowHandle, GpuSwapchainComposition composition) => true;
        public bool WindowSupportsPresentMode(nint device, nint windowHandle, GpuPresentMode presentMode) => true;
        public bool SetSwapchainParameters(nint device, nint windowHandle, GpuSwapchainComposition composition, GpuPresentMode presentMode) => true;
        public GpuTextureFormat GetSwapchainTextureFormat(nint device, nint windowHandle) => new(12);
        public GpuTextureFormat ResolveOffscreenColorFormat(nint device, GpuTextureFormat swapchainFormat) => swapchainFormat;

        public bool GetWindowPixelSize(nint windowHandle, out int width, out int height)
        {
            width = PixelWidth;
            height = PixelHeight;
            return true;
        }

        public bool IsWindowMinimized(nint windowHandle) => false;
        public nint CreateTexture(nint device, in GpuTextureDescription description) => ++nextHandle;
        public void ReleaseTexture(nint device, nint texture) { }
        public nint AcquireCommandBuffer(nint device) => ++nextHandle;
        public bool SubmitCommandBuffer(nint commandBuffer) => true;
        public nint SubmitCommandBufferAndAcquireFence(nint commandBuffer) => ++nextHandle;

        public bool CancelCommandBuffer(nint commandBuffer)
        {
            CancelCount++;
            return true;
        }

        public void ClearColorTarget(nint commandBuffer, nint colorTargetTexture, Color4 clearColor)
        {
            ClearCount++;
            LastClearColor = clearColor;
        }

        public bool AcquireSwapchainTexture(nint commandBuffer, nint windowHandle, out nint swapchainTexture, out uint width, out uint height)
        {
            swapchainTexture = ++nextHandle;
            width = (uint)Math.Max(0, PixelWidth);
            height = (uint)Math.Max(0, PixelHeight);
            return true;
        }

        public void BlitTexture(nint commandBuffer, nint sourceTexture, uint sourceWidth, uint sourceHeight, nint destinationTexture, uint destinationWidth, uint destinationHeight)
            => BlitCount++;

        public bool QueryFence(nint device, nint fence) => true;
        public bool WaitForFences(nint device, ReadOnlySpan<nint> fences) => true;
        public void ReleaseFence(nint device, nint fence) { }
        public bool WaitForIdle(nint device) => true;
    }
}
