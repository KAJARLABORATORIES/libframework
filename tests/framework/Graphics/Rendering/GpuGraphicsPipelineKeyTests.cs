using kajarlabs.Framework.Graphics;
using kajarlabs.Framework.Graphics.Rendering;

namespace kajarlabs.Framework.Tests.Graphics.Rendering;

public sealed class GpuGraphicsPipelineKeyTests
{
    private static readonly GpuTextureFormat target_format = new(12);

    [Fact]
    public void EqualKeysMatchEqualityAndHashCodeWithStructuralVertexLayout()
    {
        var layoutA = new GpuVertexLayout(
            [new GpuVertexBufferBinding(0, 32)],
            [
                new GpuVertexAttributeDescription(0, 0, GpuVertexElementFormat.Float2, 0),
                new GpuVertexAttributeDescription(1, 0, GpuVertexElementFormat.Float2, 8),
                new GpuVertexAttributeDescription(2, 0, GpuVertexElementFormat.Float4, 16),
            ]);

        var keyA = new GpuGraphicsPipelineKey(
            BuiltInShaders.SPRITE_2D_VERTEX,
            BuiltInShaders.SPRITE_2D_FRAGMENT,
            layoutA,
            BlendingParameters.ALPHA,
            GpuPrimitiveTopology.TriangleList,
            target_format);

        var keyB = new GpuGraphicsPipelineKey(
            BuiltInShaders.SPRITE_2D_VERTEX,
            BuiltInShaders.SPRITE_2D_FRAGMENT,
            GpuVertexLayout.TEXTURED_VERTEX_2D,
            BlendingParameters.ALPHA,
            GpuPrimitiveTopology.TriangleList,
            new GpuTextureFormat(12));

        Assert.Equal(keyA, keyB);
        Assert.True(keyA == keyB);
        Assert.Equal(keyA.GetHashCode(), keyB.GetHashCode());
    }

    [Fact]
    public void KeysDifferAcrossPipelineStateComponents()
    {
        var baseKey = new GpuGraphicsPipelineKey(
            BuiltInShaders.SPRITE_2D_VERTEX,
            BuiltInShaders.SPRITE_2D_FRAGMENT,
            GpuVertexLayout.TEXTURED_VERTEX_2D,
            BlendingParameters.ALPHA,
            GpuPrimitiveTopology.TriangleList,
            target_format);

        var differentBlend = new GpuGraphicsPipelineKey(
            BuiltInShaders.SPRITE_2D_VERTEX,
            BuiltInShaders.SPRITE_2D_FRAGMENT,
            GpuVertexLayout.TEXTURED_VERTEX_2D,
            BlendingParameters.ADDITIVE,
            GpuPrimitiveTopology.TriangleList,
            target_format);

        var differentTopology = new GpuGraphicsPipelineKey(
            BuiltInShaders.SPRITE_2D_VERTEX,
            BuiltInShaders.SPRITE_2D_FRAGMENT,
            GpuVertexLayout.TEXTURED_VERTEX_2D,
            BlendingParameters.ALPHA,
            GpuPrimitiveTopology.TriangleStrip,
            target_format);

        var differentFormat = new GpuGraphicsPipelineKey(
            BuiltInShaders.SPRITE_2D_VERTEX,
            BuiltInShaders.SPRITE_2D_FRAGMENT,
            GpuVertexLayout.TEXTURED_VERTEX_2D,
            BlendingParameters.ALPHA,
            GpuPrimitiveTopology.TriangleList,
            new GpuTextureFormat(28));

        var differentLayout = new GpuGraphicsPipelineKey(
            BuiltInShaders.SPRITE_2D_VERTEX,
            BuiltInShaders.SPRITE_2D_FRAGMENT,
            new GpuVertexLayout(
                [new GpuVertexBufferBinding(0, 16)],
                [new GpuVertexAttributeDescription(0, 0, GpuVertexElementFormat.Float4, 0)]),
            BlendingParameters.ALPHA,
            GpuPrimitiveTopology.TriangleList,
            target_format);

        var customFrag = new GpuShaderDescriptor(
            "Custom.frag",
            GpuShaderStage.Fragment,
            1,
            0,
            0,
            0,
            [new GpuShaderVariant(GpuShaderFormats.SpirV, "main", new byte[] { 1, 2, 3, 4 })]);

        var differentShader = new GpuGraphicsPipelineKey(
            BuiltInShaders.SPRITE_2D_VERTEX,
            customFrag,
            GpuVertexLayout.TEXTURED_VERTEX_2D,
            BlendingParameters.ALPHA,
            GpuPrimitiveTopology.TriangleList,
            target_format);

        Assert.NotEqual(baseKey, differentBlend);
        Assert.NotEqual(baseKey, differentTopology);
        Assert.NotEqual(baseKey, differentFormat);
        Assert.NotEqual(baseKey, differentLayout);
        Assert.NotEqual(baseKey, differentShader);
    }

    [Fact]
    public void RejectsMismatchedShaderStagesOrInvalidTargetFormat()
    {
        Assert.Throws<ArgumentException>(() => new GpuGraphicsPipelineKey(
            BuiltInShaders.SPRITE_2D_FRAGMENT,
            BuiltInShaders.SPRITE_2D_FRAGMENT,
            GpuVertexLayout.TEXTURED_VERTEX_2D,
            BlendingParameters.ALPHA,
            GpuPrimitiveTopology.TriangleList,
            target_format));

        Assert.Throws<ArgumentException>(() => new GpuGraphicsPipelineKey(
            BuiltInShaders.SPRITE_2D_VERTEX,
            BuiltInShaders.SPRITE_2D_VERTEX,
            GpuVertexLayout.TEXTURED_VERTEX_2D,
            BlendingParameters.ALPHA,
            GpuPrimitiveTopology.TriangleList,
            target_format));

        Assert.Throws<ArgumentException>(() => new GpuGraphicsPipelineKey(
            BuiltInShaders.SPRITE_2D_VERTEX,
            BuiltInShaders.SPRITE_2D_FRAGMENT,
            GpuVertexLayout.TEXTURED_VERTEX_2D,
            BlendingParameters.ALPHA,
            GpuPrimitiveTopology.TriangleList,
            GpuTextureFormat.INVALID));
    }
}
