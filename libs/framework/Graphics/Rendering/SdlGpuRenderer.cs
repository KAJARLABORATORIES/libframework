using kajarlabs.Framework.Numerics;
using Microsoft.Extensions.Logging;

namespace kajarlabs.Framework.Graphics.Rendering;

internal sealed class SdlGpuRenderer : IHostRenderer
{
    private readonly IGpuBackend backend;
    private readonly Lock viewportLock = new();
    private GpuOffscreenFrame activeFrame;
    private bool hasActiveFrame;
    private Color4? pendingClearColor;

    public SdlGpuRenderer(IGpuBackend backend, ILogger logger, int initialWidth = 0, int initialHeight = 0)
    {
        ArgumentNullException.ThrowIfNull(backend);
        ArgumentNullException.ThrowIfNull(logger);

        this.backend = backend;
        Context = new GpuDeviceContext(backend, logger);
        Viewport = new Viewport(Math.Max(0, initialWidth), Math.Max(0, initialHeight));
    }

    public Viewport Viewport
    {
        get
        {
            lock (viewportLock)
                return field;
        }
        private set
        {
            lock (viewportLock)
                field = value;
        }
    }

    internal GpuDeviceContext Context { get; }

    public void Initialize(nint windowHandle)
    {
        Context.Initialize(windowHandle);

        var (width, height) = Context.SurfaceSize;

        if (width > 0 && height > 0)
            Viewport = new Viewport(width, height);
    }

    public void BeginFrame(Viewport? viewport = null)
    {
        hasActiveFrame = false;
        pendingClearColor = null;

        if (!Context.BeginOffscreenFrame(out activeFrame))
        {
            if (viewport is { } fallbackViewport)
                Viewport = fallbackViewport;

            return;
        }

        hasActiveFrame = true;
        Viewport = new Viewport(activeFrame.Width, activeFrame.Height);
    }

    public void Clear(Color4 color) => pendingClearColor = color;

    public void EndFrame(bool faulted = false)
    {
        if (!hasActiveFrame)
            return;

        hasActiveFrame = false;

        if (faulted)
        {
            Context.CancelOffscreenFrame(in activeFrame);
            activeFrame = default;
            pendingClearColor = null;
            return;
        }

        backend.ClearColorTarget(
            activeFrame.CommandBuffer,
            activeFrame.ColorTargetTexture,
            pendingClearColor ?? Color4.BLACK);

        Context.EndOffscreenFrame(in activeFrame);
        activeFrame = default;
        pendingClearColor = null;
    }

    public bool Present() => Context.PresentLatestFrame();

    public void Resize(int width, int height)
    {
        Context.HandleResize(width, height);
        Viewport = new Viewport(Math.Max(0, width), Math.Max(0, height));
    }

    public void RefreshSurface()
    {
        Context.RefreshSurfaceMetrics();

        var (width, height) = Context.SurfaceSize;

        if (width > 0 && height > 0)
            Viewport = new Viewport(width, height);
    }

    public void Shutdown() => Context.Shutdown();

    public void Dispose() => Context.Dispose();
}
