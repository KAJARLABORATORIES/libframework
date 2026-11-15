using kajarlabs.Framework.Numerics;

namespace kajarlabs.Framework.Graphics.Rendering;

internal sealed class GpuTexture : ITexture
{
    private readonly GpuTextureManager manager;
    private readonly bool allowUserDispose;
    private int disposed;

    internal GpuTexture(
        GpuTextureManager manager,
        nint handle,
        int width,
        int height,
        bool allowUserDispose = true)
    {
        this.manager = manager;
        Handle = handle;
        Width = width;
        Height = height;
        this.allowUserDispose = allowUserDispose;
    }

    public int Width { get; }

    public int Height { get; }

    internal nint Handle { get; }

    internal bool IsDisposed => Volatile.Read(ref disposed) != 0;

    public void SetData(ReadOnlySpan<byte> rgba8Pixels)
        => SetData(new RectangleI(0, 0, Width, Height), rgba8Pixels);

    public void SetData(RectangleI region, ReadOnlySpan<byte> rgba8Pixels)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        TextureValidation.ValidateUpload(Width, Height, region, rgba8Pixels.Length);
        manager.EnqueueUpload(this, region, rgba8Pixels);
    }

    public void Dispose()
    {
        if (!allowUserDispose)
            return;

        if (Interlocked.Exchange(ref disposed, 1) != 0)
            return;

        manager.EnqueueDeferredDisposal(this);
    }

    internal void MarkManagerDisposed() => Interlocked.Exchange(ref disposed, 1);
}
