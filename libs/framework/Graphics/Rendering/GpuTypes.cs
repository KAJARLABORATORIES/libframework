using System.Runtime.InteropServices;
using kajarlabs.Framework.Graphics.Rendering.Vertices;

namespace kajarlabs.Framework.Graphics.Rendering;

[Flags]
internal enum GpuShaderFormats : uint
{
    None = 0,
    Private = 1 << 0,
    SpirV = 1 << 1,
    Dxbc = 1 << 2,
    Dxil = 1 << 3,
    Msl = 1 << 4,
    MetalLib = 1 << 5,
}

internal enum GpuSwapchainComposition
{
    Sdr = 0,
    SdrLinear = 1,
    HdrExtendedLinear = 2,
    Hdr10St2084 = 3,
}

internal enum GpuPresentMode
{
    VSync = 0,
    Immediate = 1,
    Mailbox = 2,
}

[Flags]
internal enum GpuTextureUsage : uint
{
    None = 0,
    Sampler = 1 << 0,
    ColorTarget = 1 << 1,
    DepthStencilTarget = 1 << 2,
}

internal readonly record struct GpuTextureFormat(uint RawValue)
{
    public static readonly GpuTextureFormat INVALID = new(0);
}

internal readonly record struct GpuTextureDescription(
    uint Width,
    uint Height,
    GpuTextureFormat Format,
    GpuTextureUsage Usage);

internal enum GpuDeviceLifecycleState
{
    Uninitialized = 0,
    Initialized = 1,
    ShuttingDown = 2,
    Disposed = 3,
}

internal readonly record struct GpuOffscreenFrame(
    int SlotIndex,
    nint CommandBuffer,
    nint ColorTargetTexture,
    int Width,
    int Height);

internal enum GpuShaderStage
{
    Vertex = 0,
    Fragment = 1,
}

internal enum GpuPrimitiveTopology
{
    TriangleList = 0,
    TriangleStrip = 1,
    LineList = 2,
    LineStrip = 3,
    PointList = 4,
}

internal enum GpuVertexInputRate
{
    Vertex = 0,
    Instance = 1,
}

internal enum GpuVertexElementFormat
{
    Float = 0,
    Float2 = 1,
    Float3 = 2,
    Float4 = 3,
}

internal readonly record struct GpuVertexBufferBinding(
    uint Slot,
    uint Pitch,
    GpuVertexInputRate InputRate = GpuVertexInputRate.Vertex,
    uint InstanceStepRate = 0);

internal readonly record struct GpuVertexAttributeDescription(
    uint Location,
    uint BufferSlot,
    GpuVertexElementFormat Format,
    uint Offset);

internal sealed class GpuVertexLayout : IEquatable<GpuVertexLayout>
{
    public static readonly GpuVertexLayout TEXTURED_VERTEX_2D = new(
        [new GpuVertexBufferBinding(0, (uint)Marshal.SizeOf<TexturedVertex2D>())],
        [
            new GpuVertexAttributeDescription(0, 0, GpuVertexElementFormat.Float2, 0),
            new GpuVertexAttributeDescription(1, 0, GpuVertexElementFormat.Float2, 8),
            new GpuVertexAttributeDescription(2, 0, GpuVertexElementFormat.Float4, 16),
        ]);

    private readonly GpuVertexBufferBinding[] bufferBindings;
    private readonly GpuVertexAttributeDescription[] attributes;
    private readonly int cachedHashCode;

    public GpuVertexLayout(
        ReadOnlySpan<GpuVertexBufferBinding> bufferBindings,
        ReadOnlySpan<GpuVertexAttributeDescription> attributes)
    {
        this.bufferBindings = bufferBindings.ToArray();
        this.attributes = attributes.ToArray();
        cachedHashCode = computeHashCode(this.bufferBindings, this.attributes);
    }

    public ReadOnlySpan<GpuVertexBufferBinding> BufferBindings => bufferBindings;

    public ReadOnlySpan<GpuVertexAttributeDescription> Attributes => attributes;

    public static bool operator ==(GpuVertexLayout? left, GpuVertexLayout? right) => Equals(left, right);

    public static bool operator !=(GpuVertexLayout? left, GpuVertexLayout? right) => !Equals(left, right);

    public bool Equals(GpuVertexLayout? other)
    {
        if (ReferenceEquals(this, other))
            return true;

        if (other is null || cachedHashCode != other.cachedHashCode)
            return false;

        return bufferBindings.AsSpan().SequenceEqual(other.bufferBindings)
            && attributes.AsSpan().SequenceEqual(other.attributes);
    }

    public override bool Equals(object? obj) => obj is GpuVertexLayout other && Equals(other);

    public override int GetHashCode() => cachedHashCode;

    private static int computeHashCode(
        ReadOnlySpan<GpuVertexBufferBinding> bindings,
        ReadOnlySpan<GpuVertexAttributeDescription> attrs)
    {
        var hash = new HashCode();
        hash.Add(bindings.Length);

        foreach (var binding in bindings)
            hash.Add(binding);

        hash.Add(attrs.Length);

        foreach (var attr in attrs)
            hash.Add(attr);

        return hash.ToHashCode();
    }
}

internal readonly record struct GpuShaderCreateDescription(
    GpuShaderStage Stage,
    GpuShaderFormats Format,
    string Entrypoint,
    ReadOnlyMemory<byte> Bytecode,
    uint NumSamplers,
    uint NumStorageTextures,
    uint NumStorageBuffers,
    uint NumUniformBuffers);

internal readonly record struct GpuGraphicsPipelineDescription(
    nint VertexShader,
    nint FragmentShader,
    GpuVertexLayout VertexLayout,
    BlendingParameters Blending,
    GpuPrimitiveTopology Topology,
    GpuTextureFormat ColorTargetFormat);
