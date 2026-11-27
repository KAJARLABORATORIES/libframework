using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using kajarlabs.Framework.Graphics.Rendering.Vertices;
using kajarlabs.Framework.Numerics;

namespace kajarlabs.Framework.Graphics.Rendering;

/// <summary>
/// Batches 2D textured quads into a pre-allocated vertex buffer and static index buffer,
/// flushing automatically with <c>cycle = true</c> on state changes or buffer capacity.
/// </summary>
internal sealed class GpuQuadBatcher : IDisposable
{
    public const int DEFAULT_MAX_QUADS_PER_BATCH = 8192;
    public const int MAX_QUADS_WITH_16BIT_INDICES = 16384;

    private static readonly int vertex_size_bytes = Unsafe.SizeOf<TexturedVertex2D>();

    private readonly IGpuBackend backend;
    private readonly nint device;
    private readonly GpuPipelineCache pipelineCache;
    private readonly GpuSamplerCache samplerCache;
    private readonly GpuTextureFormat colorTargetFormat;
    private readonly GpuTextureManager? textureManager;
    private readonly byte[] vertexStagingBytes;

    private nint vertexBuffer;
    private nint indexBuffer;
    private nint vertexTransferBuffer;

    private GpuOffscreenFrame activeFrame;
    private bool hasActiveFrame;
    private Viewport activeViewport;
    private Matrix4x4 currentProjection = Matrix4x4.Identity;
    private nint currentTextureHandle;
    private Color4 pendingClearColor = Color4.BLACK;
    private bool hasIssuedRenderPassInFrame;
    private int frameDrawCallCount;
    private int frameQuadCount;
    private bool disposed;

    public GpuQuadBatcher(
        IGpuBackend backend,
        nint device,
        GpuPipelineCache pipelineCache,
        GpuSamplerCache samplerCache,
        GpuTextureFormat colorTargetFormat,
        GpuTextureManager? textureManager = null,
        int maxQuadsPerBatch = DEFAULT_MAX_QUADS_PER_BATCH)
    {
        ArgumentNullException.ThrowIfNull(backend);
        ArgumentNullException.ThrowIfNull(pipelineCache);
        ArgumentNullException.ThrowIfNull(samplerCache);

        if (device == nint.Zero)
            throw new ArgumentException("GPU device handle must not be zero.", nameof(device));

        if (maxQuadsPerBatch <= 0 || maxQuadsPerBatch > MAX_QUADS_WITH_16BIT_INDICES)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxQuadsPerBatch),
                maxQuadsPerBatch,
                $"Batch capacity must be in [1, {MAX_QUADS_WITH_16BIT_INDICES}].");
        }

        this.backend = backend;
        this.device = device;
        this.pipelineCache = pipelineCache;
        this.samplerCache = samplerCache;
        this.colorTargetFormat = colorTargetFormat;
        this.textureManager = textureManager;
        MaxQuadsPerBatch = maxQuadsPerBatch;

        var vertexBufferByteSize = (uint)(maxQuadsPerBatch * 4 * vertex_size_bytes);
        var indexBufferByteSize = (uint)(maxQuadsPerBatch * 6 * sizeof(ushort));
        vertexStagingBytes = new byte[vertexBufferByteSize];

        try
        {
            vertexBuffer = backend.CreateBuffer(device, GpuBufferUsage.Vertex, vertexBufferByteSize);

            if (vertexBuffer == nint.Zero)
                throw new InvalidOperationException("Failed to create GPU vertex buffer for quad batcher.");

            indexBuffer = backend.CreateBuffer(device, GpuBufferUsage.Index, indexBufferByteSize);

            if (indexBuffer == nint.Zero)
                throw new InvalidOperationException("Failed to create GPU index buffer for quad batcher.");

            vertexTransferBuffer = backend.CreateTransferBuffer(device, GpuTransferBufferUsage.Upload, vertexBufferByteSize);

            if (vertexTransferBuffer == nint.Zero)
                throw new InvalidOperationException("Failed to create GPU vertex transfer buffer for quad batcher.");

            uploadStaticIndexBuffer(indexBufferByteSize);
        }
        catch
        {
            releaseGpuResources();
            throw;
        }
    }

    internal int MaxQuadsPerBatch { get; }

    internal int QueuedQuadCount { get; private set; }

    internal int LastFrameDrawCallCount { get; private set; }

    internal int LastFrameQuadCount { get; private set; }

    internal BlendingParameters CurrentBlending { get; private set; } = BlendingParameters.ALPHA;

    internal TextureSamplerParameters CurrentSampler { get; private set; } = TextureSamplerParameters.LINEAR_CLAMP;

    internal RectangleI EffectiveScissor { get; private set; }

    internal Matrix4x4 CurrentProjection => currentProjection;

    internal QuadShaderMode CurrentShaderMode { get; private set; }

    internal MsdfRenderParameters CurrentMsdfParameters { get; private set; }

    public void BeginFrame(in GpuOffscreenFrame frame, Viewport viewport)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        activeFrame = frame;
        hasActiveFrame = true;
        activeViewport = viewport;
        CurrentBlending = BlendingParameters.ALPHA;
        CurrentSampler = TextureSamplerParameters.LINEAR_CLAMP;
        EffectiveScissor = computeEffectiveScissor(null, viewport, frame.Width, frame.Height);
        currentProjection = createViewportProjection(viewport);
        currentTextureHandle = nint.Zero;
        CurrentShaderMode = QuadShaderMode.Sprite2D;
        CurrentMsdfParameters = default;
        pendingClearColor = Color4.BLACK;
        hasIssuedRenderPassInFrame = false;
        QueuedQuadCount = 0;
        frameDrawCallCount = 0;
        frameQuadCount = 0;
    }

    public void SetClearColor(Color4 color)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        if (!hasActiveFrame)
            return;

        // Discard un-flushed quads preceding an explicit clear and mark the next pass to clear.
        QueuedQuadCount = 0;
        pendingClearColor = color;
        hasIssuedRenderPassInFrame = false;
    }

    public void SetBlending(BlendingParameters blending)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        if (!hasActiveFrame || CurrentBlending == blending)
            return;

        flushQueuedQuads();
        CurrentBlending = blending;
    }

    public void SetSampler(TextureSamplerParameters sampler)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        if (!hasActiveFrame || CurrentSampler == sampler)
            return;

        flushQueuedQuads();
        CurrentSampler = sampler;
    }

    public void SetScissor(RectangleI? scissor)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        if (!hasActiveFrame)
            return;

        var nextEffective = computeEffectiveScissor(scissor, activeViewport, activeFrame.Width, activeFrame.Height);

        if (nextEffective == EffectiveScissor)
            return;

        flushQueuedQuads();
        EffectiveScissor = nextEffective;
    }

    public void SetProjection(Matrix4x4? projection)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        if (!hasActiveFrame)
            return;

        var nextProjection = projection ?? createViewportProjection(activeViewport);

        if (nextProjection == currentProjection)
            return;

        flushQueuedQuads();
        currentProjection = nextProjection;
    }

    public void DrawQuad(ITexture texture, RectangleF destination, RectangleF textureCoords, Color4 color)
        => DrawQuad(texture, Quad.FromRectangle(destination), textureCoords, color);

    public void DrawQuad(ITexture texture, Quad destination, RectangleF textureCoords, Color4 color)
    {
        MsdfRenderParameters unused = default;
        queueQuad(texture, destination, textureCoords, color, QuadShaderMode.Sprite2D, in unused);
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
        parameters.Validate(nameof(parameters));
        queueQuad(texture, destination, textureCoords, color, QuadShaderMode.MsdfText2D, in parameters);
    }

    private void queueQuad(
        ITexture texture,
        Quad destination,
        RectangleF textureCoords,
        Color4 color,
        QuadShaderMode shaderMode,
        in MsdfRenderParameters msdfParameters)
    {
        ArgumentNullException.ThrowIfNull(texture);

        while (texture is ITextureWrapper wrapper)
            texture = wrapper.Underlying;

        if (texture is not GpuTexture gpuTexture)
            throw new ArgumentException("Texture does not belong to this GPU renderer.", nameof(texture));

        ObjectDisposedException.ThrowIf(disposed, this);
        ObjectDisposedException.ThrowIf(gpuTexture.IsDisposed, texture);

        if (!hasActiveFrame || EffectiveScissor.IsEmpty)
            return;

        var textureHandle = gpuTexture.Handle;

        if (QueuedQuadCount > 0
            && (currentTextureHandle != textureHandle
                || CurrentShaderMode != shaderMode
                || (shaderMode == QuadShaderMode.MsdfText2D && CurrentMsdfParameters != msdfParameters)
                || QueuedQuadCount >= MaxQuadsPerBatch))
        {
            flushQueuedQuads();
        }

        currentTextureHandle = textureHandle;
        CurrentShaderMode = shaderMode;

        if (shaderMode == QuadShaderMode.MsdfText2D)
            CurrentMsdfParameters = msdfParameters;

        var vertices = MemoryMarshal.Cast<byte, TexturedVertex2D>(vertexStagingBytes);
        var baseVertex = QueuedQuadCount * 4;

        vertices[baseVertex + 0] = new TexturedVertex2D(destination.TopLeft, new Vector2(textureCoords.Left, textureCoords.Top), color);
        vertices[baseVertex + 1] = new TexturedVertex2D(destination.TopRight, new Vector2(textureCoords.Right, textureCoords.Top), color);
        vertices[baseVertex + 2] = new TexturedVertex2D(destination.BottomRight, new Vector2(textureCoords.Right, textureCoords.Bottom), color);
        vertices[baseVertex + 3] = new TexturedVertex2D(destination.BottomLeft, new Vector2(textureCoords.Left, textureCoords.Bottom), color);

        QueuedQuadCount++;
    }

    public void EndFrame(bool faulted)
    {
        if (!hasActiveFrame)
            return;

        if (faulted)
        {
            QueuedQuadCount = 0;
            hasActiveFrame = false;
            activeFrame = default;
            LastFrameDrawCallCount = 0;
            LastFrameQuadCount = 0;
            return;
        }

        if (QueuedQuadCount > 0)
        {
            flushQueuedQuads();
        }
        else if (!hasIssuedRenderPassInFrame)
        {
            textureManager?.FlushPendingUploads(activeFrame.CommandBuffer);
            backend.ClearColorTarget(activeFrame.CommandBuffer, activeFrame.ColorTargetTexture, pendingClearColor);
        }

        LastFrameDrawCallCount = frameDrawCallCount;
        LastFrameQuadCount = frameQuadCount;
        hasActiveFrame = false;
        activeFrame = default;
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        hasActiveFrame = false;
        activeFrame = default;
        releaseGpuResources();
    }

    private void flushQueuedQuads()
    {
        if (!hasActiveFrame || QueuedQuadCount == 0 || currentTextureHandle == nint.Zero)
            return;

        var quadCount = QueuedQuadCount;
        var vertexByteCount = quadCount * 4 * vertex_size_bytes;

        // Pending texture pixel uploads must finish in a CopyPass before any RenderPass begins.
        textureManager?.FlushPendingUploads(activeFrame.CommandBuffer);

        var mappedPtr = backend.MapTransferBuffer(device, vertexTransferBuffer, cycle: true);

        if (mappedPtr == nint.Zero)
            throw new InvalidOperationException("Failed to map quad batch vertex transfer buffer.");

        try
        {
            backend.CopyToMappedBuffer(mappedPtr, 0, vertexStagingBytes, 0, vertexByteCount);
        }
        finally
        {
            backend.UnmapTransferBuffer(device, vertexTransferBuffer);
        }

        var copyPass = backend.BeginCopyPass(activeFrame.CommandBuffer);

        if (copyPass == nint.Zero)
            throw new InvalidOperationException("Failed to begin GPU copy pass for quad batch vertex upload.");

        try
        {
            backend.UploadToBuffer(copyPass, vertexTransferBuffer, 0, vertexBuffer, 0, (uint)vertexByteCount, cycle: true);
        }
        finally
        {
            backend.EndCopyPass(copyPass);
        }

        var fragmentShader = CurrentShaderMode == QuadShaderMode.MsdfText2D
            ? BuiltInShaders.MSDF_TEXT_2D_FRAGMENT
            : BuiltInShaders.SPRITE_2D_FRAGMENT;

        var pipelineKey = new GpuGraphicsPipelineKey(
            BuiltInShaders.SPRITE_2D_VERTEX,
            fragmentShader,
            GpuVertexLayout.TEXTURED_VERTEX_2D,
            CurrentBlending,
            GpuPrimitiveTopology.TriangleList,
            colorTargetFormat);

        var pipeline = pipelineCache.GetOrCreatePipeline(pipelineKey);
        var sampler = samplerCache.GetOrCreateSampler(CurrentSampler);

        backend.PushVertexUniformData(activeFrame.CommandBuffer, 0, in currentProjection);

        if (CurrentShaderMode == QuadShaderMode.MsdfText2D)
        {
            var msdfUniform = CurrentMsdfParameters.ToFragmentUniform();
            backend.PushFragmentUniformData(activeFrame.CommandBuffer, 0, in msdfUniform);
        }

        var clearColor = hasIssuedRenderPassInFrame ? (Color4?)null : pendingClearColor;
        var renderPass = backend.BeginRenderPass(activeFrame.CommandBuffer, activeFrame.ColorTargetTexture, clearColor);

        if (renderPass == nint.Zero)
            throw new InvalidOperationException("Failed to begin GPU render pass for quad batch.");

        try
        {
            backend.BindGraphicsPipeline(renderPass, pipeline);
            backend.SetViewport(renderPass, activeViewport);
            backend.SetScissor(renderPass, EffectiveScissor);
            backend.BindVertexBuffer(renderPass, 0, vertexBuffer, 0);
            backend.BindIndexBuffer(renderPass, indexBuffer, 0, GpuIndexElementSize.SixteenBit);
            backend.BindFragmentSampler(renderPass, 0, currentTextureHandle, sampler);
            backend.DrawIndexedPrimitives(renderPass, (uint)(quadCount * 6), 1, 0, 0, 0);
        }
        finally
        {
            backend.EndRenderPass(renderPass);
        }

        hasIssuedRenderPassInFrame = true;
        QueuedQuadCount = 0;
        frameDrawCallCount++;
        frameQuadCount += quadCount;
    }

    private void uploadStaticIndexBuffer(uint indexBufferByteSize)
    {
        var indexTransferBuffer = backend.CreateTransferBuffer(device, GpuTransferBufferUsage.Upload, indexBufferByteSize);

        if (indexTransferBuffer == nint.Zero)
            throw new InvalidOperationException("Failed to create GPU transfer buffer for static quad index upload.");

        try
        {
            var indexStagingBytes = new byte[indexBufferByteSize];
            var indices = MemoryMarshal.Cast<byte, ushort>(indexStagingBytes);

            for (var quadIndex = 0; quadIndex < MaxQuadsPerBatch; quadIndex++)
            {
                var baseVertex = (ushort)(quadIndex * 4);
                var baseIndex = quadIndex * 6;

                indices[baseIndex + 0] = baseVertex;
                indices[baseIndex + 1] = (ushort)(baseVertex + 1);
                indices[baseIndex + 2] = (ushort)(baseVertex + 2);
                indices[baseIndex + 3] = (ushort)(baseVertex + 2);
                indices[baseIndex + 4] = (ushort)(baseVertex + 3);
                indices[baseIndex + 5] = baseVertex;
            }

            var mappedPtr = backend.MapTransferBuffer(device, indexTransferBuffer, cycle: false);

            if (mappedPtr == nint.Zero)
                throw new InvalidOperationException("Failed to map quad index transfer buffer.");

            try
            {
                backend.CopyToMappedBuffer(mappedPtr, 0, indexStagingBytes, 0, indexStagingBytes.Length);
            }
            finally
            {
                backend.UnmapTransferBuffer(device, indexTransferBuffer);
            }

            var commandBuffer = backend.AcquireCommandBuffer(device);

            if (commandBuffer == nint.Zero)
                throw new InvalidOperationException("Failed to acquire command buffer for static quad index upload.");

            var copyPass = backend.BeginCopyPass(commandBuffer);

            if (copyPass == nint.Zero)
            {
                backend.CancelCommandBuffer(commandBuffer);
                throw new InvalidOperationException("Failed to begin copy pass for static quad index upload.");
            }

            try
            {
                backend.UploadToBuffer(copyPass, indexTransferBuffer, 0, indexBuffer, 0, indexBufferByteSize, cycle: false);
            }
            finally
            {
                backend.EndCopyPass(copyPass);
            }

            if (!backend.SubmitCommandBuffer(commandBuffer))
                throw new InvalidOperationException("Failed to submit command buffer for static quad index upload.");
        }
        finally
        {
            backend.ReleaseTransferBuffer(device, indexTransferBuffer);
        }
    }

    private void releaseGpuResources()
    {
        if (vertexTransferBuffer != nint.Zero)
        {
            backend.ReleaseTransferBuffer(device, vertexTransferBuffer);
            vertexTransferBuffer = nint.Zero;
        }

        if (vertexBuffer != nint.Zero)
        {
            backend.ReleaseBuffer(device, vertexBuffer);
            vertexBuffer = nint.Zero;
        }

        if (indexBuffer != nint.Zero)
        {
            backend.ReleaseBuffer(device, indexBuffer);
            indexBuffer = nint.Zero;
        }
    }

    private static RectangleI computeEffectiveScissor(RectangleI? requested, Viewport viewport, int targetWidth, int targetHeight)
    {
        var targetBounds = new RectangleI(0, 0, Math.Max(0, targetWidth), Math.Max(0, targetHeight));
        var viewportBounds = RectangleI.Intersect(viewport.Bounds, targetBounds);

        return requested is { } rect
            ? RectangleI.Intersect(rect, viewportBounds)
            : viewportBounds;
    }

    private static Matrix4x4 createViewportProjection(Viewport viewport)
        => viewport.Width > 0f && viewport.Height > 0f
            ? GraphicsMath.CreateOrthographicProjection(viewport)
            : Matrix4x4.Identity;
}
