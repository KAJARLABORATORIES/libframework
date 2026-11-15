using kajarlabs.Framework.Numerics;
using Microsoft.Extensions.Logging;

namespace kajarlabs.Framework.Graphics.Rendering;

internal sealed class SdlGpuRenderer : IHostRenderer
{
    private readonly IGpuBackend backend;
    private readonly Lock viewportLock = new();
    private GpuPipelineCache? pipelineCache;
    private GpuSamplerCache? samplerCache;
    private GpuTextureManager? textureManager;
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

    public ITexture WhitePixel
    {
        get
        {
            var manager = Volatile.Read(ref textureManager)
                ?? throw new InvalidOperationException("Renderer texture manager is not initialized.");

            return manager.WhitePixel;
        }
    }

    internal GpuDeviceContext Context { get; }

    internal GpuPipelineCache? PipelineCache => Volatile.Read(ref pipelineCache);

    internal GpuSamplerCache? SamplerCache => Volatile.Read(ref samplerCache);

    internal GpuTextureManager? TextureManager => Volatile.Read(ref textureManager);

    public ITexture CreateTexture(int width, int height)
    {
        var manager = Volatile.Read(ref textureManager)
            ?? throw new InvalidOperationException("Renderer texture manager is not initialized.");

        return manager.CreateTexture(width, height);
    }

    public void Initialize(nint windowHandle)
    {
        Context.Initialize(windowHandle);

        var createdPipelineCache = new GpuPipelineCache(backend, Context.DeviceHandle, Context.SupportedShaderFormats);
        var createdSamplerCache = new GpuSamplerCache(backend, Context.DeviceHandle);
        var createdTextureManager = new GpuTextureManager(backend, Context.DeviceHandle);

        Interlocked.Exchange(ref pipelineCache, createdPipelineCache)?.Dispose();
        Interlocked.Exchange(ref samplerCache, createdSamplerCache)?.Dispose();
        Interlocked.Exchange(ref textureManager, createdTextureManager)?.Dispose();

        var (width, height) = Context.SurfaceSize;

        if (width > 0 && height > 0)
            Viewport = new Viewport(width, height);
    }

    public void BeginFrame(Viewport? viewport = null)
    {
        hasActiveFrame = false;
        pendingClearColor = null;

        Volatile.Read(ref textureManager)?.CollectCompletedDisposals();

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

        var textures = Volatile.Read(ref textureManager);
        textures?.FlushPendingUploads(activeFrame.CommandBuffer);

        backend.ClearColorTarget(
            activeFrame.CommandBuffer,
            activeFrame.ColorTargetTexture,
            pendingClearColor ?? Color4.BLACK);

        var acquireFence = textures is { HasUnfencedDisposals: true };
        Context.EndOffscreenFrame(in activeFrame, acquireFence, out var submittedFence);

        if (acquireFence && submittedFence != nint.Zero)
            textures?.AttachDisposalsToFence(submittedFence);

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

    internal nint GetOrCreatePipeline(GpuGraphicsPipelineKey key)
    {
        var cache = Volatile.Read(ref pipelineCache)
            ?? throw new InvalidOperationException("Renderer pipeline cache is not initialized.");

        return cache.GetOrCreatePipeline(key);
    }

    internal nint GetOrCreateSampler(TextureSamplerParameters parameters)
    {
        var cache = Volatile.Read(ref samplerCache)
            ?? throw new InvalidOperationException("Renderer sampler cache is not initialized.");

        return cache.GetOrCreateSampler(parameters);
    }

    public void Shutdown()
    {
        Context.WaitForIdle();
        disposeRenderingCaches();
        Context.Shutdown();
    }

    public void Dispose()
    {
        Context.WaitForIdle();
        disposeRenderingCaches();
        Context.Dispose();
    }

    private void disposeRenderingCaches()
    {
        Interlocked.Exchange(ref textureManager, null)?.Dispose();
        Interlocked.Exchange(ref samplerCache, null)?.Dispose();
        Interlocked.Exchange(ref pipelineCache, null)?.Dispose();
    }
}
