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
