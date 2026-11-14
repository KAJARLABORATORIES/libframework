namespace kajarlabs.Framework.Graphics.Rendering;

/// <summary>
/// Immutable key identifying a cached graphics pipeline state configuration.
/// </summary>
internal sealed class GpuGraphicsPipelineKey : IEquatable<GpuGraphicsPipelineKey>
{
    public GpuGraphicsPipelineKey(
        GpuShaderDescriptor vertexShader,
        GpuShaderDescriptor fragmentShader,
        GpuVertexLayout vertexLayout,
        BlendingParameters blending,
        GpuPrimitiveTopology topology,
        GpuTextureFormat colorTargetFormat)
    {
        ArgumentNullException.ThrowIfNull(vertexShader);
        ArgumentNullException.ThrowIfNull(fragmentShader);
        ArgumentNullException.ThrowIfNull(vertexLayout);

        if (vertexShader.Stage != GpuShaderStage.Vertex)
            throw new ArgumentException("Vertex shader descriptor must have stage Vertex.", nameof(vertexShader));

        if (fragmentShader.Stage != GpuShaderStage.Fragment)
            throw new ArgumentException("Fragment shader descriptor must have stage Fragment.", nameof(fragmentShader));

        if (colorTargetFormat == GpuTextureFormat.INVALID)
            throw new ArgumentException("Color target format must be valid.", nameof(colorTargetFormat));

        VertexShader = vertexShader;
        FragmentShader = fragmentShader;
        VertexLayout = vertexLayout;
        Blending = blending;
        Topology = topology;
        ColorTargetFormat = colorTargetFormat;
    }

    public GpuShaderDescriptor VertexShader { get; }

    public GpuShaderDescriptor FragmentShader { get; }

    public GpuVertexLayout VertexLayout { get; }

    public BlendingParameters Blending { get; }

    public GpuPrimitiveTopology Topology { get; }

    public GpuTextureFormat ColorTargetFormat { get; }

    public static bool operator ==(GpuGraphicsPipelineKey? left, GpuGraphicsPipelineKey? right) => Equals(left, right);

    public static bool operator !=(GpuGraphicsPipelineKey? left, GpuGraphicsPipelineKey? right) => !Equals(left, right);

    public bool Equals(GpuGraphicsPipelineKey? other)
    {
        if (ReferenceEquals(this, other))
            return true;

        if (other is null)
            return false;

        return ReferenceEquals(VertexShader, other.VertexShader)
            && ReferenceEquals(FragmentShader, other.FragmentShader)
            && VertexLayout.Equals(other.VertexLayout)
            && Blending.Equals(other.Blending)
            && Topology == other.Topology
            && ColorTargetFormat == other.ColorTargetFormat;
    }

    public override bool Equals(object? obj) => obj is GpuGraphicsPipelineKey other && Equals(other);

    public override int GetHashCode()
        => HashCode.Combine(VertexShader, FragmentShader, VertexLayout, Blending, Topology, ColorTargetFormat);
}
