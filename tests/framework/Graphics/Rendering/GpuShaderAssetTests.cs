using System.Text;
using kajarlabs.Framework.Graphics.Rendering;

namespace kajarlabs.Framework.Tests.Graphics.Rendering;

public sealed class GpuShaderAssetTests
{
    [Fact]
    public void BuiltInSprite2DShadersExposeAllMultiBackendEmbeddedPayloads()
    {
        Assert.Equal(GpuShaderStage.Vertex, BuiltInShaders.SPRITE_2D_VERTEX.Stage);
        Assert.Equal(1u, BuiltInShaders.SPRITE_2D_VERTEX.NumUniformBuffers);
        Assert.Equal(0u, BuiltInShaders.SPRITE_2D_VERTEX.NumSamplers);
        Assert.Equal(0u, BuiltInShaders.SPRITE_2D_VERTEX.NumStorageTextures);
        Assert.Equal(0u, BuiltInShaders.SPRITE_2D_VERTEX.NumStorageBuffers);

        Assert.Equal(GpuShaderStage.Fragment, BuiltInShaders.SPRITE_2D_FRAGMENT.Stage);
        Assert.Equal(0u, BuiltInShaders.SPRITE_2D_FRAGMENT.NumUniformBuffers);
        Assert.Equal(1u, BuiltInShaders.SPRITE_2D_FRAGMENT.NumSamplers);
        Assert.Equal(0u, BuiltInShaders.SPRITE_2D_FRAGMENT.NumStorageTextures);
        Assert.Equal(0u, BuiltInShaders.SPRITE_2D_FRAGMENT.NumStorageBuffers);

        foreach (var descriptor in new[] { BuiltInShaders.SPRITE_2D_VERTEX, BuiltInShaders.SPRITE_2D_FRAGMENT })
        {
            var spirv = descriptor.SelectVariant(GpuShaderFormats.SpirV);
            Assert.Equal("main", spirv.Entrypoint);
            var spirvBytes = spirv.LoadBytecode();
            Assert.True(spirvBytes.Length > 16);
            // SPIR-V magic number: 0x07230203 (little-endian: 03 02 23 07)
            Assert.Equal(0x03, spirvBytes.Span[0]);
            Assert.Equal(0x02, spirvBytes.Span[1]);
            Assert.Equal(0x23, spirvBytes.Span[2]);
            Assert.Equal(0x07, spirvBytes.Span[3]);

            var dxil = descriptor.SelectVariant(GpuShaderFormats.Dxil);
            Assert.Equal("main", dxil.Entrypoint);
            var dxilBytes = dxil.LoadBytecode();
            Assert.True(dxilBytes.Length > 16);
            // DXIL container magic: "DXBC"
            Assert.Equal("DXBC", Encoding.ASCII.GetString(dxilBytes.Span[..4]));

            var msl = descriptor.SelectVariant(GpuShaderFormats.Msl);
            Assert.Equal("main0", msl.Entrypoint);
            var mslSource = Encoding.UTF8.GetString(msl.LoadBytecode().Span);
            Assert.Contains("metal_stdlib", mslSource, StringComparison.Ordinal);
            Assert.Contains("main0(", mslSource, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void SelectVariantMatchesSupportedFormatOrThrowsWhenIncompatible()
    {
        var vertex = BuiltInShaders.SPRITE_2D_VERTEX;

        Assert.Equal(GpuShaderFormats.SpirV, vertex.SelectVariant(GpuShaderFormats.SpirV | GpuShaderFormats.Dxil).Format);
        Assert.Equal(GpuShaderFormats.Dxil, vertex.SelectVariant(GpuShaderFormats.Dxil).Format);
        Assert.Equal(GpuShaderFormats.Msl, vertex.SelectVariant(GpuShaderFormats.Msl).Format);

        Assert.Throws<NotSupportedException>(() => vertex.SelectVariant(GpuShaderFormats.None));
        Assert.Throws<NotSupportedException>(() => vertex.SelectVariant(GpuShaderFormats.Private));
    }
}
