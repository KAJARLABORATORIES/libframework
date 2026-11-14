using kajarlabs.Framework.Numerics;

namespace kajarlabs.Framework.Graphics.Rendering;

internal sealed class NullRenderer : IHostRenderer
{
    private readonly Lock stateLock = new();
    private long completedFrames;
    private int resizeCount;

    public NullRenderer(int initialWidth = 0, int initialHeight = 0)
    {
        Viewport = new Viewport(Math.Max(0, initialWidth), Math.Max(0, initialHeight));
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
    }
}
