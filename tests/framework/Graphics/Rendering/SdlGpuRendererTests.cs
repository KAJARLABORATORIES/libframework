using kajarlabs.Framework.Graphics;
using kajarlabs.Framework.Graphics.Rendering;
using kajarlabs.Framework.Numerics;
using kajarlabs.Framework.Tests.TestUtilities;
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
        Assert.Null(renderer.QuadBatcher);
        Assert.Equal(backend.CreatedPipelines, backend.ReleasedPipelines);
        Assert.Equal(backend.CreatedShaders, backend.ReleasedShaders);
        Assert.Equal(backend.CreatedSamplers, backend.ReleasedSamplers);
        Assert.Equal(backend.CreatedTextures, backend.ReleasedTextures);
        Assert.Equal(backend.CreatedBuffers, backend.ReleasedBuffers);

        var destroyIndex = backend.LifecycleEvents.IndexOf("DestroyDevice");
        Assert.True(destroyIndex > 0);
        Assert.True(backend.LifecycleEvents.LastIndexOf("ReleaseGraphicsPipeline") < destroyIndex);
        Assert.True(backend.LifecycleEvents.LastIndexOf("ReleaseShader") < destroyIndex);
        Assert.True(backend.LifecycleEvents.LastIndexOf("ReleaseSampler") < destroyIndex);
        Assert.True(backend.LifecycleEvents.LastIndexOf("ReleaseTexture") < destroyIndex);
        Assert.True(backend.LifecycleEvents.LastIndexOf("ReleaseBuffer") < destroyIndex);
    }

    [Fact]
    public void MidFrameFlushUploadsPendingTexturesBeforeOpeningRenderPass()
    {
        var backend = new FakeGpuBackend
        {
            PixelWidth = 640,
            PixelHeight = 480,
        };
        using var renderer = new SdlGpuRenderer(backend, NullLogger.Instance, maxQuadsPerBatch: 1);
        renderer.Initialize(fake_window);

        using var custom = renderer.CreateTexture(2, 2);
        custom.SetData(new byte[2 * 2 * 4]);
        backend.LifecycleEvents.Clear();

        renderer.BeginFrame();
        // Second DrawQuad exceeds capacity=1, forcing an immediate mid-frame flush
        renderer.DrawQuad(custom, new RectangleF(0f, 0f, 10f, 10f), Color4.WHITE);
        renderer.DrawQuad(custom, new RectangleF(10f, 0f, 10f, 10f), Color4.WHITE);
        renderer.EndFrame();

        var uploadIndex = backend.LifecycleEvents.IndexOf("UploadToTexture");
        var renderPassIndex = backend.LifecycleEvents.IndexOf("BeginRenderPass");

        Assert.True(uploadIndex >= 0);
        Assert.True(renderPassIndex > uploadIndex);
        Assert.Equal(2, renderer.LastFrameDrawCallCount);
        Assert.Equal(2, renderer.LastFrameQuadCount);
    }

    [Fact]
    public void NullRendererTracksDrawCallAndQuadTelemetryAcrossFrames()
    {
        using var renderer = new NullRenderer(800, 600);
        using var texture = renderer.CreateTexture(4, 4);

        renderer.BeginFrame();
        renderer.Clear(Color4.BLACK);
        renderer.EndFrame();

        Assert.Equal(0, renderer.LastFrameDrawCallCount);
        Assert.Equal(0, renderer.LastFrameQuadCount);

        renderer.BeginFrame();
        renderer.DrawQuad(renderer.WhitePixel, new RectangleF(0f, 0f, 10f, 10f), Color4.WHITE);
        renderer.DrawQuad(renderer.WhitePixel, new RectangleF(10f, 0f, 10f, 10f), Color4.RED);
        renderer.SetBlending(BlendingParameters.ADDITIVE);
        renderer.DrawQuad(texture, new RectangleF(20f, 0f, 10f, 10f), Color4.GREEN);
        renderer.EndFrame();

        Assert.Equal(2, renderer.LastFrameDrawCallCount);
        Assert.Equal(3, renderer.LastFrameQuadCount);
    }

    private sealed class FakeGpuBackend : StubGpuBackend
    {
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
        public List<nint> CreatedBuffers { get; } = [];
        public List<nint> ReleasedBuffers { get; } = [];
        public List<string> LifecycleEvents { get; } = [];

        public override nint CreateBuffer(nint device, GpuBufferUsage usage, uint sizeInBytes)
        {
            var handle = NextHandle();
            CreatedBuffers.Add(handle);
            return handle;
        }

        public override void ReleaseBuffer(nint device, nint buffer)
        {
            ReleasedBuffers.Add(buffer);
            LifecycleEvents.Add(nameof(ReleaseBuffer));
        }

        public override void UploadToTexture(nint copyPass, in GpuTextureUploadSource source, in GpuTextureRegion destination, bool cycle)
            => LifecycleEvents.Add(nameof(UploadToTexture));

        public override nint BeginRenderPass(nint commandBuffer, nint colorTargetTexture, Color4? clearColor)
        {
            LifecycleEvents.Add(nameof(BeginRenderPass));
            return NextHandle();
        }

        public override void DestroyDevice(nint device) => LifecycleEvents.Add(nameof(DestroyDevice));

        public override nint CreateTexture(nint device, in GpuTextureDescription description)
        {
            var handle = NextHandle();

            if ((description.Usage & GpuTextureUsage.ColorTarget) == 0)
                CreatedTextures.Add(handle);

            return handle;
        }

        public override void ReleaseTexture(nint device, nint texture)
        {
            if (CreatedTextures.Contains(texture))
                ReleasedTextures.Add(texture);

            LifecycleEvents.Add(nameof(ReleaseTexture));
        }

        public override nint CreateSampler(nint device, TextureSamplerParameters parameters)
        {
            var handle = NextHandle();
            CreatedSamplers.Add(handle);
            return handle;
        }

        public override void ReleaseSampler(nint device, nint sampler)
        {
            ReleasedSamplers.Add(sampler);
            LifecycleEvents.Add(nameof(ReleaseSampler));
        }

        public override nint CreateShader(nint device, in GpuShaderCreateDescription description)
        {
            var handle = NextHandle();
            CreatedShaders.Add(handle);
            LifecycleEvents.Add(nameof(CreateShader));
            return handle;
        }

        public override void ReleaseShader(nint device, nint shader)
        {
            ReleasedShaders.Add(shader);
            LifecycleEvents.Add(nameof(ReleaseShader));
        }

        public override nint CreateGraphicsPipeline(nint device, in GpuGraphicsPipelineDescription description)
        {
            var handle = NextHandle();
            CreatedPipelines.Add(handle);
            LifecycleEvents.Add(nameof(CreateGraphicsPipeline));
            return handle;
        }

        public override void ReleaseGraphicsPipeline(nint device, nint pipeline)
        {
            ReleasedPipelines.Add(pipeline);
            LifecycleEvents.Add(nameof(ReleaseGraphicsPipeline));
        }

        public override bool CancelCommandBuffer(nint commandBuffer)
        {
            CancelCount++;
            return true;
        }

        public override void ClearColorTarget(nint commandBuffer, nint colorTargetTexture, Color4 clearColor)
        {
            ClearCount++;
            LastClearColor = clearColor;
        }

        public override void BlitTexture(nint commandBuffer, nint sourceTexture, uint sourceWidth, uint sourceHeight, nint destinationTexture, uint destinationWidth, uint destinationHeight)
            => BlitCount++;
    }
}
