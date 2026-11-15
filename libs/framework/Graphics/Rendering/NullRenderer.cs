using System.Numerics;
using kajarlabs.Framework.Numerics;

namespace kajarlabs.Framework.Graphics.Rendering;

internal sealed class NullRenderer : IHostRenderer
{
    private readonly Lock stateLock = new();
    private readonly NullTexture whitePixel = new(1, 1, allowUserDispose: false);
    private long completedFrames;
    private int resizeCount;
    private int lastFrameDrawCallCount;
    private int lastFrameQuadCount;
    private int frameDrawCallCount;
    private int frameQuadCount;
    private int queuedQuadCount;
    private ITexture? currentTexture;
    private BlendingParameters currentBlending = BlendingParameters.ALPHA;
    private TextureSamplerParameters currentSampler = TextureSamplerParameters.LINEAR_CLAMP;
    private RectangleI? currentScissor;
    private Matrix4x4? currentProjection;

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

    public int LastFrameDrawCallCount => Volatile.Read(ref lastFrameDrawCallCount);

    public int LastFrameQuadCount => Volatile.Read(ref lastFrameQuadCount);

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
        lock (stateLock)
        {
            if (viewport is { } resolvedViewport)
                Viewport = resolvedViewport;

            currentBlending = BlendingParameters.ALPHA;
            currentSampler = TextureSamplerParameters.LINEAR_CLAMP;
            currentScissor = null;
            currentProjection = null;
            currentTexture = null;
            queuedQuadCount = 0;
            frameDrawCallCount = 0;
            frameQuadCount = 0;
        }
    }

    public void Clear(Color4 color)
    {
        lock (stateLock)
        {
            LastClearColor = color;
            queuedQuadCount = 0;
        }
    }

    public void SetBlending(BlendingParameters blending)
    {
        lock (stateLock)
        {
            if (currentBlending == blending)
                return;

            flushNullBatchLocked();
            currentBlending = blending;
        }
    }

    public void SetSampler(TextureSamplerParameters sampler)
    {
        lock (stateLock)
        {
            if (currentSampler == sampler)
                return;

            flushNullBatchLocked();
            currentSampler = sampler;
        }
    }

    public void SetScissor(RectangleI? scissor)
    {
        lock (stateLock)
        {
            if (currentScissor == scissor)
                return;

            flushNullBatchLocked();
            currentScissor = scissor;
        }
    }

    public void SetProjection(Matrix4x4? projection)
    {
        lock (stateLock)
        {
            if (currentProjection == projection)
                return;

            flushNullBatchLocked();
            currentProjection = projection;
        }
    }

    public void DrawQuad(ITexture texture, RectangleF destination, Color4 color)
        => DrawQuad(texture, destination, new RectangleF(0f, 0f, 1f, 1f), color);

    public void DrawQuad(ITexture texture, RectangleF destination, RectangleF textureCoords, Color4 color)
    {
        ArgumentNullException.ThrowIfNull(texture);

        if (texture is not NullTexture nullTexture)
            throw new ArgumentException("Texture does not belong to this headless renderer.", nameof(texture));

        ObjectDisposedException.ThrowIf(nullTexture.IsDisposed, texture);

        lock (stateLock)
        {
            var effectiveScissor = currentScissor is { } scissor
                ? RectangleI.Intersect(scissor, Viewport.Bounds)
                : Viewport.Bounds;

            if (effectiveScissor.IsEmpty)
                return;

            if (queuedQuadCount > 0 && !ReferenceEquals(currentTexture, texture))
                flushNullBatchLocked();

            currentTexture = texture;
            queuedQuadCount++;
        }
    }

    public void EndFrame(bool faulted = false)
    {
        if (faulted)
        {
            lock (stateLock)
            {
                queuedQuadCount = 0;
                Volatile.Write(ref lastFrameDrawCallCount, 0);
                Volatile.Write(ref lastFrameQuadCount, 0);
            }

            return;
        }

        lock (stateLock)
        {
            flushNullBatchLocked();
            Volatile.Write(ref lastFrameDrawCallCount, frameDrawCallCount);
            Volatile.Write(ref lastFrameQuadCount, frameQuadCount);
        }

        Interlocked.Increment(ref completedFrames);
    }

    private void flushNullBatchLocked()
    {
        if (queuedQuadCount == 0)
            return;

        frameDrawCallCount++;
        frameQuadCount += queuedQuadCount;
        queuedQuadCount = 0;
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

        internal bool IsDisposed => Volatile.Read(ref disposed) != 0;

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
