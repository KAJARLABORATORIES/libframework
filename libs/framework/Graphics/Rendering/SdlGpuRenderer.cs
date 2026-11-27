using System.Numerics;
using kajarlabs.Framework.Numerics;
using Microsoft.Extensions.Logging;

namespace kajarlabs.Framework.Graphics.Rendering;

internal sealed class SdlGpuRenderer : IHostRenderer
{
    private static readonly RectangleF full_texture_coords = new(0f, 0f, 1f, 1f);
    private readonly IGpuBackend backend;
    private readonly int maxQuadsPerBatch;
    private readonly Lock viewportLock = new();
    private GpuPipelineCache? pipelineCache;
    private GpuSamplerCache? samplerCache;
    private GpuTextureManager? textureManager;
    private GpuQuadBatcher? quadBatcher;
    private GpuOffscreenFrame activeFrame;
    private bool hasActiveFrame;
    private int lastFrameDrawCallCount;
    private int lastFrameQuadCount;
    private int disposed;

    public SdlGpuRenderer(
        IGpuBackend backend,
        ILogger logger,
        int initialWidth = 0,
        int initialHeight = 0,
        int maxQuadsPerBatch = GpuQuadBatcher.DEFAULT_MAX_QUADS_PER_BATCH)
    {
        ArgumentNullException.ThrowIfNull(backend);
        ArgumentNullException.ThrowIfNull(logger);

        if (maxQuadsPerBatch <= 0 || maxQuadsPerBatch > GpuQuadBatcher.MAX_QUADS_WITH_16BIT_INDICES)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxQuadsPerBatch),
                maxQuadsPerBatch,
                $"Batch capacity must be in [1, {GpuQuadBatcher.MAX_QUADS_WITH_16BIT_INDICES}].");
        }

        this.backend = backend;
        this.maxQuadsPerBatch = maxQuadsPerBatch;
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
            ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);

            var manager = Volatile.Read(ref textureManager)
                ?? throw new InvalidOperationException("Renderer texture manager is not initialized.");

            return manager.WhitePixel;
        }
    }

    public int LastFrameDrawCallCount => Volatile.Read(ref lastFrameDrawCallCount);

    public int LastFrameQuadCount => Volatile.Read(ref lastFrameQuadCount);

    internal GpuDeviceContext Context { get; }

    internal GpuPipelineCache? PipelineCache => Volatile.Read(ref pipelineCache);

    internal GpuSamplerCache? SamplerCache => Volatile.Read(ref samplerCache);

    internal GpuTextureManager? TextureManager => Volatile.Read(ref textureManager);

    internal GpuQuadBatcher? QuadBatcher => Volatile.Read(ref quadBatcher);

    public ITexture CreateTexture(int width, int height)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);

        var manager = Volatile.Read(ref textureManager)
            ?? throw new InvalidOperationException("Renderer texture manager is not initialized.");

        return manager.CreateTexture(width, height);
    }

    public void Initialize(nint windowHandle)
    {
        Context.Initialize(windowHandle);

        var (width, height) = Context.SurfaceSize;

        if (width > 0 && height > 0)
            Viewport = new Viewport(width, height);

        var createdPipelineCache = new GpuPipelineCache(backend, Context.DeviceHandle, Context.SupportedShaderFormats);
        var createdSamplerCache = new GpuSamplerCache(backend, Context.DeviceHandle);
        var createdTextureManager = new GpuTextureManager(backend, Context.DeviceHandle);
        var createdQuadBatcher = new GpuQuadBatcher(
            backend,
            Context.DeviceHandle,
            createdPipelineCache,
            createdSamplerCache,
            Context.OffscreenColorFormat,
            createdTextureManager,
            maxQuadsPerBatch);

        Interlocked.Exchange(ref quadBatcher, createdQuadBatcher)?.Dispose();
        Interlocked.Exchange(ref pipelineCache, createdPipelineCache)?.Dispose();
        Interlocked.Exchange(ref samplerCache, createdSamplerCache)?.Dispose();
        Interlocked.Exchange(ref textureManager, createdTextureManager)?.Dispose();
    }

    public void BeginFrame(Viewport? viewport = null)
    {
        hasActiveFrame = false;

        Volatile.Read(ref textureManager)?.CollectCompletedDisposals();

        if (!Context.BeginOffscreenFrame(out activeFrame))
        {
            if (viewport is { } fallbackViewport)
                Viewport = fallbackViewport;

            Volatile.Write(ref lastFrameDrawCallCount, 0);
            Volatile.Write(ref lastFrameQuadCount, 0);
            return;
        }

        hasActiveFrame = true;
        var frameViewport = new Viewport(activeFrame.Width, activeFrame.Height);
        Viewport = frameViewport;

        var batcher = Volatile.Read(ref quadBatcher)
            ?? throw new InvalidOperationException("Renderer quad batcher is not initialized.");

        batcher.BeginFrame(in activeFrame, frameViewport);
    }

    public void Clear(Color4 color)
    {
        if (!hasActiveFrame)
            return;

        Volatile.Read(ref quadBatcher)?.SetClearColor(color);
    }

    public void SetBlending(BlendingParameters blending)
        => Volatile.Read(ref quadBatcher)?.SetBlending(blending);

    public void SetSampler(TextureSamplerParameters sampler)
        => Volatile.Read(ref quadBatcher)?.SetSampler(sampler);

    public void SetScissor(RectangleI? scissor)
        => Volatile.Read(ref quadBatcher)?.SetScissor(scissor);

    public void SetProjection(Matrix4x4? projection)
        => Volatile.Read(ref quadBatcher)?.SetProjection(projection);

    public void DrawQuad(ITexture texture, RectangleF destination, Color4 color)
        => DrawQuad(texture, Quad.FromRectangle(destination), full_texture_coords, color);

    public void DrawQuad(ITexture texture, RectangleF destination, RectangleF textureCoords, Color4 color)
        => DrawQuad(texture, Quad.FromRectangle(destination), textureCoords, color);

    public void DrawQuad(ITexture texture, Quad destination, Color4 color)
        => DrawQuad(texture, destination, full_texture_coords, color);

    public void DrawQuad(ITexture texture, Quad destination, RectangleF textureCoords, Color4 color)
    {
        var batcher = Volatile.Read(ref quadBatcher)
            ?? throw new InvalidOperationException("Renderer quad batcher is not initialized.");

        batcher.DrawQuad(texture, destination, textureCoords, color);
    }

    public void DrawMsdfQuad(
        ITexture texture,
        RectangleF destination,
        RectangleF textureCoords,
        Color4 color,
        in MsdfRenderParameters parameters)
        => DrawMsdfQuad(texture, Quad.FromRectangle(destination), textureCoords, color, in parameters);

    public void DrawMsdfQuad(
        ITexture texture,
        Quad destination,
        RectangleF textureCoords,
        Color4 color,
        in MsdfRenderParameters parameters)
    {
        var batcher = Volatile.Read(ref quadBatcher)
            ?? throw new InvalidOperationException("Renderer quad batcher is not initialized.");

        batcher.DrawMsdfQuad(texture, destination, textureCoords, color, in parameters);
    }

    public void EndFrame(bool faulted = false)
    {
        if (!hasActiveFrame)
            return;

        hasActiveFrame = false;

        var batcher = Volatile.Read(ref quadBatcher);
        batcher?.EndFrame(faulted);

        if (faulted)
        {
            Volatile.Write(ref lastFrameDrawCallCount, 0);
            Volatile.Write(ref lastFrameQuadCount, 0);
            Context.CancelOffscreenFrame(in activeFrame);
            activeFrame = default;
            return;
        }

        var textures = Volatile.Read(ref textureManager);
        var acquireFence = textures is { HasUnfencedDisposals: true };
        Context.EndOffscreenFrame(in activeFrame, acquireFence, out var submittedFence);

        if (acquireFence && submittedFence != nint.Zero)
            textures?.AttachDisposalsToFence(submittedFence);

        Volatile.Write(ref lastFrameDrawCallCount, batcher?.LastFrameDrawCallCount ?? 0);
        Volatile.Write(ref lastFrameQuadCount, batcher?.LastFrameQuadCount ?? 0);
        activeFrame = default;
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
        if (Interlocked.Exchange(ref disposed, 1) != 0)
            return;

        Context.WaitForIdle();
        disposeRenderingCaches();
        Context.Dispose();
    }

    private void disposeRenderingCaches()
    {
        Interlocked.Exchange(ref quadBatcher, null)?.Dispose();
        Interlocked.Exchange(ref textureManager, null)?.Dispose();
        Interlocked.Exchange(ref samplerCache, null)?.Dispose();
        Interlocked.Exchange(ref pipelineCache, null)?.Dispose();
    }
}
