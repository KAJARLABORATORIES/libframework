using kajarlabs.Framework.Graphics;
using kajarlabs.Framework.Graphics.Rendering;
using kajarlabs.Framework.Numerics;

namespace kajarlabs.Framework.Tests.Graphics.Rendering;

public sealed class GpuPipelineCacheTests
{
    private static readonly nint fake_device = new(0x1234);
    private static readonly GpuTextureFormat default_format = new(12);

    [Fact]
    public void GetOrCreatePipelineReusesHandleOnCacheHitAndCreatesNewOnBlendMiss()
    {
        var backend = new RecordingGpuBackend();
        using var cache = new GpuPipelineCache(backend, fake_device, GpuShaderFormats.SpirV);

        var alphaKey = createSpriteKey(BlendingParameters.ALPHA);
        var equalAlphaKey = new GpuGraphicsPipelineKey(
            BuiltInShaders.SPRITE_2D_VERTEX,
            BuiltInShaders.SPRITE_2D_FRAGMENT,
            new GpuVertexLayout(
                [new GpuVertexBufferBinding(0, 32)],
                [
                    new GpuVertexAttributeDescription(0, 0, GpuVertexElementFormat.Float2, 0),
                    new GpuVertexAttributeDescription(1, 0, GpuVertexElementFormat.Float2, 8),
                    new GpuVertexAttributeDescription(2, 0, GpuVertexElementFormat.Float4, 16),
                ]),
            BlendingParameters.ALPHA,
            GpuPrimitiveTopology.TriangleList,
            default_format);
        var additiveKey = createSpriteKey(BlendingParameters.ADDITIVE);
        var noneKey = createSpriteKey(BlendingParameters.NONE);

        var alphaHandle1 = cache.GetOrCreatePipeline(alphaKey);
        var alphaHandle2 = cache.GetOrCreatePipeline(equalAlphaKey);
        var additiveHandle = cache.GetOrCreatePipeline(additiveKey);
        var noneHandle = cache.GetOrCreatePipeline(noneKey);

        Assert.Equal(alphaHandle1, alphaHandle2);
        Assert.NotEqual(alphaHandle1, additiveHandle);
        Assert.NotEqual(alphaHandle1, noneHandle);
        Assert.NotEqual(additiveHandle, noneHandle);

        // Two shaders (vert + frag) reused across three blend-mode pipelines
        Assert.Equal(2, backend.CreatedShaders.Count);
        Assert.Equal(3, backend.CreatedPipelines.Count);
        Assert.Equal(2, cache.CachedShaderCount);
        Assert.Equal(3, cache.CachedPipelineCount);
    }

    [Fact]
    public void SelectsMatchingBackendShaderFormatForDevice()
    {
        (GpuShaderFormats Format, string Entrypoint)[] cases =
        [
            (GpuShaderFormats.SpirV, "main"),
            (GpuShaderFormats.Dxil, "main"),
            (GpuShaderFormats.Msl, "main0"),
        ];

        foreach (var (supportedFormats, expectedEntrypoint) in cases)
        {
            var backend = new RecordingGpuBackend();
            using var cache = new GpuPipelineCache(backend, fake_device, supportedFormats);

            cache.GetOrCreatePipeline(createSpriteKey(BlendingParameters.ALPHA));

            Assert.Equal(2, backend.ShaderDescriptions.Count);
            Assert.All(backend.ShaderDescriptions, desc =>
            {
                Assert.Equal(supportedFormats, desc.Format);
                Assert.Equal(expectedEntrypoint, desc.Entrypoint);
                Assert.False(desc.Bytecode.IsEmpty);
            });
        }
    }

    [Fact]
    public void DisposeReleasesPipelinesAndShadersOnceAndRejectsSubsequentRequests()
    {
        var backend = new RecordingGpuBackend();
        var cache = new GpuPipelineCache(backend, fake_device, GpuShaderFormats.Dxil);

        cache.GetOrCreatePipeline(createSpriteKey(BlendingParameters.ALPHA));
        cache.GetOrCreatePipeline(createSpriteKey(BlendingParameters.PREMULTIPLIED_ALPHA));

        cache.Dispose();
        cache.Dispose();

        Assert.Equal(0, cache.CachedPipelineCount);
        Assert.Equal(0, cache.CachedShaderCount);
        Assert.Equal(2, backend.ReleasedPipelines.Count);
        Assert.Equal(2, backend.ReleasedShaders.Count);
        Assert.Equal(backend.CreatedPipelines, backend.ReleasedPipelines);
        Assert.Equal(backend.CreatedShaders, backend.ReleasedShaders);

        var firstPipelineReleaseIndex = backend.ReleaseSequence.IndexOf("pipeline");
        var firstShaderReleaseIndex = backend.ReleaseSequence.IndexOf("shader");
        Assert.True(firstPipelineReleaseIndex >= 0);
        Assert.True(firstShaderReleaseIndex > firstPipelineReleaseIndex);

        Assert.Throws<ObjectDisposedException>(() => cache.GetOrCreatePipeline(createSpriteKey(BlendingParameters.NONE)));
    }

    private static GpuGraphicsPipelineKey createSpriteKey(BlendingParameters blending)
        => new(
            BuiltInShaders.SPRITE_2D_VERTEX,
            BuiltInShaders.SPRITE_2D_FRAGMENT,
            GpuVertexLayout.TEXTURED_VERTEX_2D,
            blending,
            GpuPrimitiveTopology.TriangleList,
            default_format);

    private sealed class RecordingGpuBackend : IGpuBackend
    {
        private nint nextHandle = 0x100;

        public List<nint> CreatedShaders { get; } = [];
        public List<nint> ReleasedShaders { get; } = [];
        public List<nint> CreatedPipelines { get; } = [];
        public List<nint> ReleasedPipelines { get; } = [];
        public List<string> ReleaseSequence { get; } = [];
        public List<(GpuShaderStage Stage, GpuShaderFormats Format, string Entrypoint, ReadOnlyMemory<byte> Bytecode)> ShaderDescriptions { get; } = [];

        public nint CreateShader(nint device, in GpuShaderCreateDescription description)
        {
            var handle = ++nextHandle;
            CreatedShaders.Add(handle);
            ShaderDescriptions.Add((description.Stage, description.Format, description.Entrypoint, description.Bytecode));
            return handle;
        }

        public void ReleaseShader(nint device, nint shader)
        {
            ReleasedShaders.Add(shader);
            ReleaseSequence.Add("shader");
        }

        public nint CreateGraphicsPipeline(nint device, in GpuGraphicsPipelineDescription description)
        {
            var handle = ++nextHandle;
            CreatedPipelines.Add(handle);
            return handle;
        }

        public void ReleaseGraphicsPipeline(nint device, nint pipeline)
        {
            ReleasedPipelines.Add(pipeline);
            ReleaseSequence.Add("pipeline");
        }

        public nint CreateDevice(GpuShaderFormats shaderFormats, bool debugMode, string? preferredDriver) => 0;
        public void DestroyDevice(nint device) { }
        public string? GetDeviceDriver(nint device) => null;
        public GpuShaderFormats GetSupportedShaderFormats(nint device) => GpuShaderFormats.None;
        public bool ClaimWindow(nint device, nint windowHandle) => false;
        public void ReleaseWindow(nint device, nint windowHandle) { }
        public bool WindowSupportsSwapchainComposition(nint device, nint windowHandle, GpuSwapchainComposition composition) => false;
        public bool WindowSupportsPresentMode(nint device, nint windowHandle, GpuPresentMode presentMode) => false;
        public bool SetSwapchainParameters(nint device, nint windowHandle, GpuSwapchainComposition composition, GpuPresentMode presentMode) => false;
        public GpuTextureFormat GetSwapchainTextureFormat(nint device, nint windowHandle) => GpuTextureFormat.INVALID;
        public GpuTextureFormat ResolveOffscreenColorFormat(nint device, GpuTextureFormat swapchainFormat) => GpuTextureFormat.INVALID;
        public bool GetWindowPixelSize(nint windowHandle, out int width, out int height) { width = 0; height = 0; return false; }
        public bool IsWindowMinimized(nint windowHandle) => false;
        public nint CreateTexture(nint device, in GpuTextureDescription description) => 0;
        public void ReleaseTexture(nint device, nint texture) { }
        public nint AcquireCommandBuffer(nint device) => 0;
        public bool SubmitCommandBuffer(nint commandBuffer) => false;
        public nint SubmitCommandBufferAndAcquireFence(nint commandBuffer) => 0;
        public bool CancelCommandBuffer(nint commandBuffer) => false;
        public void ClearColorTarget(nint commandBuffer, nint colorTargetTexture, Color4 clearColor) { }
        public bool AcquireSwapchainTexture(nint commandBuffer, nint windowHandle, out nint swapchainTexture, out uint width, out uint height) { swapchainTexture = 0; width = 0; height = 0; return false; }
        public void BlitTexture(nint commandBuffer, nint sourceTexture, uint sourceWidth, uint sourceHeight, nint destinationTexture, uint destinationWidth, uint destinationHeight) { }
        public bool QueryFence(nint device, nint fence) => false;
        public bool WaitForFences(nint device, ReadOnlySpan<nint> fences) => false;
        public void ReleaseFence(nint device, nint fence) { }
        public bool WaitForIdle(nint device) => false;
    }
}
