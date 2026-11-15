using kajarlabs.Framework.Graphics;
using kajarlabs.Framework.Graphics.Rendering;
using kajarlabs.Framework.Numerics;
using kajarlabs.Framework.Tests.TestUtilities;

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

    private sealed class RecordingGpuBackend : StubGpuBackend
    {
        public List<nint> CreatedShaders { get; } = [];
        public List<nint> ReleasedShaders { get; } = [];
        public List<nint> CreatedPipelines { get; } = [];
        public List<nint> ReleasedPipelines { get; } = [];
        public List<string> ReleaseSequence { get; } = [];
        public List<(GpuShaderStage Stage, GpuShaderFormats Format, string Entrypoint, ReadOnlyMemory<byte> Bytecode)> ShaderDescriptions { get; } = [];

        public override nint CreateShader(nint device, in GpuShaderCreateDescription description)
        {
            var handle = NextHandle();
            CreatedShaders.Add(handle);
            ShaderDescriptions.Add((description.Stage, description.Format, description.Entrypoint, description.Bytecode));
            return handle;
        }

        public override void ReleaseShader(nint device, nint shader)
        {
            ReleasedShaders.Add(shader);
            ReleaseSequence.Add("shader");
        }

        public override nint CreateGraphicsPipeline(nint device, in GpuGraphicsPipelineDescription description)
        {
            var handle = NextHandle();
            CreatedPipelines.Add(handle);
            return handle;
        }

        public override void ReleaseGraphicsPipeline(nint device, nint pipeline)
        {
            ReleasedPipelines.Add(pipeline);
            ReleaseSequence.Add("pipeline");
        }
    }
}
