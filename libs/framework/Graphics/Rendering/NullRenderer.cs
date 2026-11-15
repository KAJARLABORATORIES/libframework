using kajarlabs.Framework.Numerics;

namespace kajarlabs.Framework.Graphics.Rendering;

internal sealed class NullRenderer : IHostRenderer
{
    private readonly Lock stateLock = new();
    private readonly NullTexture whitePixel = new(1, 1, allowUserDispose: false);
    private long completedFrames;
    private int resizeCount;

    public NullRenderer(int initialWidth = 0, int initialHeight = 0)
    {
        Viewport = new Viewport(Math.Max(0, initialWidth), Math.Max(0, initialHeight));
        whitePixel.SetData([255, 255, 255, 255]);
    }

    public Viewport Viewport
    {
        get
        {
            lock (stateLock)
                return field;
        }

        private set;
    }

    internal Color4 LastClearColor
    {
        get
        {
            lock (stateLock)
                return field;
        }

        private set;
    } = Color4.BLACK;

    internal long CompletedFrames => Volatile.Read(ref completedFrames);

    internal int ResizeCount => Volatile.Read(ref resizeCount);

    public ITexture WhitePixel => whitePixel;

    public ITexture CreateTexture(int width, int height)
    {
        TextureValidation.ValidateDimensions(width, height);
        return new NullTexture(width, height);
    }

    public void Initialize(nint windowHandle)
    {
    }

    public void BeginFrame(Viewport? viewport = null)
    {
        if (viewport is { } resolvedViewport)
        {
            lock (stateLock)
                Viewport = resolvedViewport;
        }
    }

    public void Clear(Color4 color)
    {
        lock (stateLock)
            LastClearColor = color;
    }

    public void EndFrame(bool faulted = false)
    {
        if (!faulted)
            Interlocked.Increment(ref completedFrames);
    }

    public bool Present() => false;

    public void Resize(int width, int height)
    {
        var clampedWidth = Math.Max(0, width);
        var clampedHeight = Math.Max(0, height);

        lock (stateLock)
            Viewport = new Viewport(clampedWidth, clampedHeight);

        Interlocked.Increment(ref resizeCount);
    }

    public void RefreshSurface()
    {
    }

    public void Shutdown()
    {
    }

    public void Dispose()
    {
        whitePixel.ForceDispose();
    }

    private sealed class NullTexture : ITexture
    {
        private readonly bool allowUserDispose;
        private int disposed;

        public NullTexture(int width, int height, bool allowUserDispose = true)
        {
            Width = width;
            Height = height;
            this.allowUserDispose = allowUserDispose;
        }

        public int Width { get; }

        public int Height { get; }

        public void SetData(ReadOnlySpan<byte> rgba8Pixels)
            => SetData(new RectangleI(0, 0, Width, Height), rgba8Pixels);

        public void SetData(RectangleI region, ReadOnlySpan<byte> rgba8Pixels)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
            TextureValidation.ValidateUpload(Width, Height, region, rgba8Pixels.Length);
        }

        public void Dispose()
        {
            if (allowUserDispose)
                Interlocked.Exchange(ref disposed, 1);
        }

        internal void ForceDispose() => Interlocked.Exchange(ref disposed, 1);
    }
}
