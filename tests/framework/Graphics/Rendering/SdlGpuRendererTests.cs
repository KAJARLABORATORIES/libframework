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

    [Fact]
    public void ShutdownAndDisposeReleaseAllCachedPipelinesAndShadersBeforeDestroyDevice()
    {
        var backend = new FakeGpuBackend
        {
            PixelWidth = 640,
            PixelHeight = 480,
        };
        using var renderer = new SdlGpuRenderer(backend, NullLogger.Instance);

        Assert.Null(renderer.PipelineCache);
        Assert.Null(renderer.SamplerCache);
        Assert.Null(renderer.TextureManager);

        renderer.Initialize(fake_window);
        Assert.NotNull(renderer.PipelineCache);
        Assert.NotNull(renderer.SamplerCache);
        Assert.NotNull(renderer.TextureManager);

        var samplerHandle = renderer.GetOrCreateSampler(TextureSamplerParameters.LINEAR_CLAMP);
        Assert.NotEqual(nint.Zero, samplerHandle);
        Assert.Equal(samplerHandle, renderer.GetOrCreateSampler(TextureSamplerParameters.LINEAR_CLAMP));

        using var customTexture = renderer.CreateTexture(4, 4);
        customTexture.Dispose();

        var keyAlpha = new GpuGraphicsPipelineKey(
            BuiltInShaders.SPRITE_2D_VERTEX,
            BuiltInShaders.SPRITE_2D_FRAGMENT,
            GpuVertexLayout.TEXTURED_VERTEX_2D,
            BlendingParameters.ALPHA,
            GpuPrimitiveTopology.TriangleList,
            new GpuTextureFormat(12));

        var keyAdditive = new GpuGraphicsPipelineKey(
            BuiltInShaders.SPRITE_2D_VERTEX,
            BuiltInShaders.SPRITE_2D_FRAGMENT,
            GpuVertexLayout.TEXTURED_VERTEX_2D,
            BlendingParameters.ADDITIVE,
            GpuPrimitiveTopology.TriangleList,
            new GpuTextureFormat(12));

        var handle1 = renderer.GetOrCreatePipeline(keyAlpha);
        var handle2 = renderer.GetOrCreatePipeline(keyAlpha);
        var handle3 = renderer.GetOrCreatePipeline(keyAdditive);

        Assert.Equal(handle1, handle2);
        Assert.NotEqual(handle1, handle3);
        Assert.Equal(2, backend.CreatedPipelines.Count);
        Assert.Equal(2, backend.CreatedShaders.Count);

        renderer.Shutdown();
        renderer.Dispose();

        Assert.Null(renderer.PipelineCache);
        Assert.Null(renderer.SamplerCache);
        Assert.Null(renderer.TextureManager);
        Assert.Equal(backend.CreatedPipelines, backend.ReleasedPipelines);
        Assert.Equal(backend.CreatedShaders, backend.ReleasedShaders);
        Assert.Equal(backend.CreatedSamplers, backend.ReleasedSamplers);
        Assert.Equal(backend.CreatedTextures, backend.ReleasedTextures);

        var destroyIndex = backend.LifecycleEvents.IndexOf("DestroyDevice");
        Assert.True(destroyIndex > 0);
        Assert.True(backend.LifecycleEvents.LastIndexOf("ReleaseGraphicsPipeline") < destroyIndex);
        Assert.True(backend.LifecycleEvents.LastIndexOf("ReleaseShader") < destroyIndex);
        Assert.True(backend.LifecycleEvents.LastIndexOf("ReleaseSampler") < destroyIndex);
        Assert.True(backend.LifecycleEvents.LastIndexOf("ReleaseTexture") < destroyIndex);
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
        public List<nint> CreatedShaders { get; } = [];
        public List<nint> ReleasedShaders { get; } = [];
        public List<nint> CreatedPipelines { get; } = [];
        public List<nint> ReleasedPipelines { get; } = [];
        public List<nint> CreatedSamplers { get; } = [];
        public List<nint> ReleasedSamplers { get; } = [];
        public List<nint> CreatedTextures { get; } = [];
        public List<nint> ReleasedTextures { get; } = [];
        public List<string> LifecycleEvents { get; } = [];

        public nint CreateDevice(GpuShaderFormats shaderFormats, bool debugMode, string? preferredDriver) => ++nextHandle;

        public void DestroyDevice(nint device) => LifecycleEvents.Add(nameof(DestroyDevice));

        public string? GetDeviceDriver(nint device) => "vulkan";
        public GpuShaderFormats GetSupportedShaderFormats(nint device) => GpuShaderFormats.SpirV;
        public bool ClaimWindow(nint device, nint windowHandle) => true;
        public void ReleaseWindow(nint device, nint windowHandle) { }
        public bool WindowSupportsSwapchainComposition(nint device, nint windowHandle, GpuSwapchainComposition composition) => true;
        public bool WindowSupportsPresentMode(nint device, nint windowHandle, GpuPresentMode presentMode) => true;
        public bool SetSwapchainParameters(nint device, nint windowHandle, GpuSwapchainComposition composition, GpuPresentMode presentMode) => true;
        public GpuTextureFormat Rgba8TextureFormat => new(12);
        public GpuTextureFormat GetSwapchainTextureFormat(nint device, nint windowHandle) => new(12);
        public GpuTextureFormat ResolveOffscreenColorFormat(nint device, GpuTextureFormat swapchainFormat) => swapchainFormat;

        public bool GetWindowPixelSize(nint windowHandle, out int width, out int height)
        {
            width = PixelWidth;
            height = PixelHeight;
            return true;
        }

        public bool IsWindowMinimized(nint windowHandle) => false;

        public nint CreateTexture(nint device, in GpuTextureDescription description)
        {
            var handle = ++nextHandle;

            if ((description.Usage & GpuTextureUsage.ColorTarget) == 0)
                CreatedTextures.Add(handle);

            return handle;
        }

        public void ReleaseTexture(nint device, nint texture)
        {
            if (CreatedTextures.Contains(texture))
                ReleasedTextures.Add(texture);

            LifecycleEvents.Add(nameof(ReleaseTexture));
        }

        public nint CreateSampler(nint device, TextureSamplerParameters parameters)
        {
            var handle = ++nextHandle;
            CreatedSamplers.Add(handle);
            return handle;
        }

        public void ReleaseSampler(nint device, nint sampler)
        {
            ReleasedSamplers.Add(sampler);
            LifecycleEvents.Add(nameof(ReleaseSampler));
        }

        public nint CreateTransferBuffer(nint device, GpuTransferBufferUsage usage, uint sizeInBytes) => ++nextHandle;
        public void ReleaseTransferBuffer(nint device, nint transferBuffer) { }
        public nint MapTransferBuffer(nint device, nint transferBuffer, bool cycle) => ++nextHandle;
        public void CopyToMappedBuffer(nint mappedDestination, int byteOffset, byte[] source, int sourceOffset, int byteCount) { }
        public void UnmapTransferBuffer(nint device, nint transferBuffer) { }
        public nint BeginCopyPass(nint commandBuffer) => ++nextHandle;
        public void UploadToTexture(nint copyPass, in GpuTextureUploadSource source, in GpuTextureRegion destination, bool cycle) { }
        public void EndCopyPass(nint copyPass) { }

        public nint CreateShader(nint device, in GpuShaderCreateDescription description)
        {
            var handle = ++nextHandle;
            CreatedShaders.Add(handle);
            LifecycleEvents.Add(nameof(CreateShader));
            return handle;
        }

        public void ReleaseShader(nint device, nint shader)
        {
            ReleasedShaders.Add(shader);
            LifecycleEvents.Add(nameof(ReleaseShader));
        }

        public nint CreateGraphicsPipeline(nint device, in GpuGraphicsPipelineDescription description)
        {
            var handle = ++nextHandle;
            CreatedPipelines.Add(handle);
            LifecycleEvents.Add(nameof(CreateGraphicsPipeline));
            return handle;
        }

        public void ReleaseGraphicsPipeline(nint device, nint pipeline)
        {
            ReleasedPipelines.Add(pipeline);
            LifecycleEvents.Add(nameof(ReleaseGraphicsPipeline));
        }
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
