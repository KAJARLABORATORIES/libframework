namespace kajarlabs.Framework.Graphics.Rendering;

/// <summary>
/// Built-in precompiled shader descriptors embedded as assembly resources.
/// </summary>
internal static class BuiltInShaders
{
    private const string resource_prefix = "kajarlabs.Framework.Resources.Shaders.Compiled.";

    public static readonly GpuShaderDescriptor SPRITE_2D_VERTEX = createSprite2DVertex();

    public static readonly GpuShaderDescriptor SPRITE_2D_FRAGMENT = createSprite2DFragment();

    private static GpuShaderDescriptor createSprite2DVertex()
    {
        var assembly = typeof(BuiltInShaders).Assembly;

        return new GpuShaderDescriptor(
            name: "Sprite2D.vert",
            stage: GpuShaderStage.Vertex,
            numSamplers: 0,
            numStorageTextures: 0,
            numStorageBuffers: 0,
            numUniformBuffers: 1,
            variants:
            [
                new GpuShaderVariant(GpuShaderFormats.SpirV, "main", assembly, resource_prefix + "Sprite2D.vert.spv"),
                new GpuShaderVariant(GpuShaderFormats.Dxil, "main", assembly, resource_prefix + "Sprite2D.vert.dxil"),
                new GpuShaderVariant(GpuShaderFormats.Msl, "main0", assembly, resource_prefix + "Sprite2D.vert.msl"),
            ]);
    }

    private static GpuShaderDescriptor createSprite2DFragment()
    {
        var assembly = typeof(BuiltInShaders).Assembly;

        return new GpuShaderDescriptor(
            name: "Sprite2D.frag",
            stage: GpuShaderStage.Fragment,
            numSamplers: 1,
            numStorageTextures: 0,
            numStorageBuffers: 0,
            numUniformBuffers: 0,
            variants:
            [
                new GpuShaderVariant(GpuShaderFormats.SpirV, "main", assembly, resource_prefix + "Sprite2D.frag.spv"),
                new GpuShaderVariant(GpuShaderFormats.Dxil, "main", assembly, resource_prefix + "Sprite2D.frag.dxil"),
                new GpuShaderVariant(GpuShaderFormats.Msl, "main0", assembly, resource_prefix + "Sprite2D.frag.msl"),
            ]);
    }
}
