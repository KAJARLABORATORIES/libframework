using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using kajarlabs.Framework.Graphics;
using kajarlabs.Framework.Graphics.Rendering;
using kajarlabs.Framework.Graphics.Rendering.Vertices;
using kajarlabs.Framework.Numerics;
using kajarlabs.Framework.Tests.TestUtilities;
using Microsoft.Extensions.Logging.Abstractions;

namespace kajarlabs.Framework.Tests.Graphics.Rendering;

public sealed class GpuQuadBatcherTests
{
    private static readonly nint fake_device = new(0x3000);
    private static readonly GpuTextureFormat color_format = new(12);

    [Fact]
    public void ConstructorUploadsStaticQuadIndexBufferOnceWithExpectedPattern()
    {
        var backend = new BatcherRecordingBackend();
        using var pipelines = new GpuPipelineCache(backend, fake_device, GpuShaderFormats.SpirV);
        using var samplers = new GpuSamplerCache(backend, fake_device);
        using var batcher = new GpuQuadBatcher(backend, fake_device, pipelines, samplers, color_format, maxQuadsPerBatch: 3);

        Assert.Single(backend.CreatedVertexBuffers);
        Assert.Single(backend.CreatedIndexBuffers);

        // Index upload transfer buffer is released right after initial upload; only vertex transfer buffer stays live.
        Assert.Single(backend.LiveTransferBuffers);
        Assert.Single(backend.IndexUploads);
        Assert.Equal(
            new ushort[] { 0, 1, 2, 2, 3, 0, 4, 5, 6, 6, 7, 4, 8, 9, 10, 10, 11, 8 },
            backend.IndexUploads[0]);
    }

    [Fact]
    public void EmptyFrameIssuesZeroDrawCallsAndZeroVertexUploads()
    {
        var backend = new BatcherRecordingBackend();
        using var renderer = new SdlGpuRenderer(backend, NullLogger.Instance);
        renderer.Initialize(new nint(0x2000));

        backend.VertexUploads.Clear();
        backend.DrawCalls.Clear();

        renderer.BeginFrame();
        renderer.Clear(Color4.GREEN);
        renderer.EndFrame();

        Assert.Empty(backend.VertexUploads);
        Assert.Empty(backend.DrawCalls);
        Assert.Equal(0, renderer.LastFrameDrawCallCount);
        Assert.Equal(0, renderer.LastFrameQuadCount);
        Assert.Equal(1, backend.ClearColorTargetCount);
        Assert.Equal(Color4.GREEN, backend.LastClearColorTargetColor);
    }

    [Fact]
    public void DrawQuadPacksVerticesUvsColorAndPushesViewportOrthographicProjection()
    {
        var backend = new BatcherRecordingBackend
        {
            PixelWidth = 800,
            PixelHeight = 600,
        };
        using var renderer = new SdlGpuRenderer(backend, NullLogger.Instance);
        renderer.Initialize(new nint(0x2000));

        using var texture = renderer.CreateTexture(16, 16);
        texture.SetData(new byte[16 * 16 * 4]);

        var tint = new Color4(0.25f, 0.5f, 0.75f, 0.9f);
        var destination = new RectangleF(10f, 20f, 100f, 50f);
        var uvs = new RectangleF(0.25f, 0.5f, 0.5f, 0.25f);

        renderer.BeginFrame();
        renderer.Clear(Color4.BLACK);
        renderer.DrawQuad(texture, destination, uvs, tint);
        renderer.EndFrame();

        Assert.Equal(1, renderer.LastFrameDrawCallCount);
        Assert.Equal(1, renderer.LastFrameQuadCount);
        Assert.Single(backend.DrawCalls);
        Assert.Single(backend.VertexUploads);

        var draw = backend.DrawCalls[0];
        Assert.Equal(6u, draw.NumIndices);
        Assert.Equal(1u, draw.NumInstances);
        Assert.Equal(0u, draw.FirstIndex);
        Assert.Equal(0, draw.VertexOffset);
        Assert.Equal(new RectangleI(0, 0, 800, 600), draw.Scissor);
        Assert.Equal(GraphicsMath.CreateOrthographicProjection(800f, 600f), draw.Projection);

        var verts = backend.VertexUploads[0].Vertices;
        Assert.Equal(4, verts.Length);
        Assert.Equal(new TexturedVertex2D(10f, 20f, 0.25f, 0.5f, tint), verts[0]);
        Assert.Equal(new TexturedVertex2D(110f, 20f, 0.75f, 0.5f, tint), verts[1]);
        Assert.Equal(new TexturedVertex2D(110f, 70f, 0.75f, 0.75f, tint), verts[2]);
        Assert.Equal(new TexturedVertex2D(10f, 70f, 0.25f, 0.75f, tint), verts[3]);
    }

    [Fact]
    public void AutomaticFlushingSplitsAcrossCapacityAndStateChangesWithBufferCycling()
    {
        var backend = new BatcherRecordingBackend
        {
            PixelWidth = 640,
            PixelHeight = 480,
        };
        using var renderer = new SdlGpuRenderer(backend, NullLogger.Instance, maxQuadsPerBatch: 2);
        renderer.Initialize(new nint(0x2000));

        using var texA = renderer.CreateTexture(4, 4);
        using var texB = renderer.CreateTexture(4, 4);
        texA.SetData(new byte[4 * 4 * 4]);
        texB.SetData(new byte[4 * 4 * 4]);

        var customProjection = Matrix4x4.CreateScale(2f);

        renderer.BeginFrame();
        renderer.Clear(Color4.BLUE);

        // Batch 1 & 2: 3 quads on texA exceed capacity=2 -> flush 2 quads, keep 1 queued, then texture switch flushes remaining 1
        renderer.DrawQuad(texA, new RectangleF(0f, 0f, 10f, 10f), Color4.WHITE);
        renderer.DrawQuad(texA, new RectangleF(10f, 0f, 10f, 10f), Color4.WHITE);
        renderer.DrawQuad(texA, new RectangleF(20f, 0f, 10f, 10f), Color4.WHITE);

        // Texture change -> flushes batch 2 (1 quad) -> batch 3 starts on texB
        renderer.DrawQuad(texB, new RectangleF(30f, 0f, 10f, 10f), Color4.RED);

        // Sampler change -> flushes batch 3
        renderer.SetSampler(TextureSamplerParameters.NEAREST_REPEAT);
        renderer.DrawQuad(texB, new RectangleF(40f, 0f, 10f, 10f), Color4.GREEN);

        // Blending change -> flushes batch 4
        renderer.SetBlending(BlendingParameters.ADDITIVE);
        renderer.DrawQuad(texB, new RectangleF(50f, 0f, 10f, 10f), Color4.YELLOW);

        // Scissor change -> flushes batch 5
        renderer.SetScissor(new RectangleI(10, 20, 100, 80));
        renderer.DrawQuad(texB, new RectangleF(60f, 0f, 10f, 10f), Color4.CYAN);

        // Projection change -> flushes batch 6 -> batch 7 flushed at EndFrame
        renderer.SetProjection(customProjection);
        renderer.DrawQuad(texB, new RectangleF(70f, 0f, 10f, 10f), Color4.WHITE);

        renderer.EndFrame();

        Assert.Equal(7, renderer.LastFrameDrawCallCount);
        Assert.Equal(8, renderer.LastFrameQuadCount);
        Assert.Equal(7, backend.DrawCalls.Count);
        Assert.Equal(7, backend.VertexUploads.Count);

        // Every dynamic upload must cycle both the transfer buffer and destination vertex buffer.
        Assert.All(backend.VertexMapCycleFlags, cycled => Assert.True(cycled));
        Assert.All(backend.VertexUploads, upload => Assert.True(upload.Cycled));

        // First render pass clears the color target; subsequent passes in the same frame preserve content (LoadOp.Load).
        Assert.Equal(Color4.BLUE, backend.RenderPassClearColors[0]);
        Assert.All(backend.RenderPassClearColors.Skip(1), clear => Assert.Null(clear));

        Assert.Equal(12u, backend.DrawCalls[0].NumIndices);
        Assert.Equal(6u, backend.DrawCalls[1].NumIndices);
        Assert.Equal(customProjection, backend.DrawCalls[^1].Projection);
        Assert.Equal(new RectangleI(10, 20, 100, 80), backend.DrawCalls[^1].Scissor);

        // Consecutive frame also starts cleanly with reset defaults and LoadOp.Clear on pass 0.
        backend.DrawCalls.Clear();
        backend.RenderPassClearColors.Clear();

        renderer.BeginFrame();
        renderer.Clear(Color4.RED);
        renderer.DrawQuad(texA, new RectangleF(0f, 0f, 8f, 8f), Color4.WHITE);
        renderer.EndFrame();

        Assert.Single(backend.DrawCalls);
        Assert.Equal(Color4.RED, backend.RenderPassClearColors[0]);
        Assert.Equal(BlendingParameters.ALPHA, renderer.QuadBatcher!.CurrentBlending);
        Assert.Equal(TextureSamplerParameters.LINEAR_CLAMP, renderer.QuadBatcher.CurrentSampler);
        Assert.Equal(GraphicsMath.CreateOrthographicProjection(640f, 480f), backend.DrawCalls[0].Projection);
        Assert.Equal(new RectangleI(0, 0, 640, 480), backend.DrawCalls[0].Scissor);
    }

    [Fact]
    public void ScissorRectangleClampsToViewportBoundsAndSkipsDrawsOutsideViewport()
    {
        var backend = new BatcherRecordingBackend
        {
            PixelWidth = 640,
            PixelHeight = 480,
        };
        using var renderer = new SdlGpuRenderer(backend, NullLogger.Instance);
        renderer.Initialize(new nint(0x2000));

        renderer.BeginFrame();

        renderer.SetScissor(new RectangleI(-20, -10, 200, 150));
        renderer.DrawQuad(renderer.WhitePixel, new RectangleF(0f, 0f, 50f, 50f), Color4.WHITE);

        renderer.SetScissor(new RectangleI(580, 440, 200, 200));
        renderer.DrawQuad(renderer.WhitePixel, new RectangleF(580f, 440f, 20f, 20f), Color4.WHITE);

        // Completely outside viewport: skipped with zero draw calls
        renderer.SetScissor(new RectangleI(700, 500, 50, 50));
        Assert.True(renderer.QuadBatcher!.EffectiveScissor.IsEmpty);
        renderer.DrawQuad(renderer.WhitePixel, new RectangleF(700f, 500f, 10f, 10f), Color4.WHITE);

        // Restore full viewport scissor
        renderer.SetScissor(null);
        renderer.DrawQuad(renderer.WhitePixel, new RectangleF(0f, 0f, 10f, 10f), Color4.WHITE);

        renderer.EndFrame();

        Assert.Equal(3, renderer.LastFrameDrawCallCount);
        Assert.Equal(3, renderer.LastFrameQuadCount);
        Assert.Equal(new RectangleI(0, 0, 180, 140), backend.DrawCalls[0].Scissor);
        Assert.Equal(new RectangleI(580, 440, 60, 40), backend.DrawCalls[1].Scissor);
        Assert.Equal(new RectangleI(0, 0, 640, 480), backend.DrawCalls[2].Scissor);
    }

    [Fact]
    public void DisposeReleasesBuffersAndRejectsSubsequentCalls()
    {
        var backend = new BatcherRecordingBackend();
        using var pipelines = new GpuPipelineCache(backend, fake_device, GpuShaderFormats.SpirV);
        using var samplers = new GpuSamplerCache(backend, fake_device);
        var batcher = new GpuQuadBatcher(backend, fake_device, pipelines, samplers, color_format, maxQuadsPerBatch: 4);

        Assert.NotEmpty(backend.LiveBuffers);
        Assert.NotEmpty(backend.LiveTransferBuffers);

        batcher.Dispose();
        batcher.Dispose();

        Assert.Empty(backend.LiveBuffers);
        Assert.Empty(backend.LiveTransferBuffers);

        Assert.Throws<ObjectDisposedException>(() => batcher.SetBlending(BlendingParameters.NONE));
        Assert.Throws<ObjectDisposedException>(() => batcher.SetSampler(TextureSamplerParameters.NEAREST_CLAMP));
        Assert.Throws<ObjectDisposedException>(() => batcher.SetScissor(null));
        Assert.Throws<ObjectDisposedException>(() => batcher.SetProjection(null));
    }

    private sealed class BatcherRecordingBackend : StubGpuBackend
    {
        private readonly Dictionary<nint, byte[]> mappedBytes = [];
        private readonly Dictionary<nint, GpuBufferUsage> bufferUsages = [];
        private readonly Dictionary<nint, bool> lastMapCycleByTransferBuffer = [];
        private Matrix4x4 activeProjection = Matrix4x4.Identity;
        private RectangleI activeScissor;

        public List<nint> CreatedVertexBuffers { get; } = [];
        public List<nint> CreatedIndexBuffers { get; } = [];
        public HashSet<nint> LiveBuffers { get; } = [];
        public HashSet<nint> LiveTransferBuffers { get; } = [];
        public List<bool> VertexMapCycleFlags { get; } = [];
        public List<ushort[]> IndexUploads { get; } = [];
        public List<(bool Cycled, TexturedVertex2D[] Vertices)> VertexUploads { get; } = [];
        public List<Color4?> RenderPassClearColors { get; } = [];
        public List<RecordedDrawCall> DrawCalls { get; } = [];
        public int ClearColorTargetCount { get; private set; }
        public Color4 LastClearColorTargetColor { get; private set; }

        public override nint CreateBuffer(nint device, GpuBufferUsage usage, uint sizeInBytes)
        {
            var handle = NextHandle();
            bufferUsages[handle] = usage;
            LiveBuffers.Add(handle);

            if ((usage & GpuBufferUsage.Vertex) != 0)
                CreatedVertexBuffers.Add(handle);

            if ((usage & GpuBufferUsage.Index) != 0)
                CreatedIndexBuffers.Add(handle);

            return handle;
        }

        public override void ReleaseBuffer(nint device, nint buffer)
        {
            LiveBuffers.Remove(buffer);
            bufferUsages.Remove(buffer);
        }

        public override nint CreateTransferBuffer(nint device, GpuTransferBufferUsage usage, uint sizeInBytes)
        {
            var handle = NextHandle();
            mappedBytes[handle] = new byte[sizeInBytes];
            LiveTransferBuffers.Add(handle);
            return handle;
        }

        public override void ReleaseTransferBuffer(nint device, nint transferBuffer)
        {
            LiveTransferBuffers.Remove(transferBuffer);
            mappedBytes.Remove(transferBuffer);
            lastMapCycleByTransferBuffer.Remove(transferBuffer);
        }

        public override nint MapTransferBuffer(nint device, nint transferBuffer, bool cycle)
        {
            lastMapCycleByTransferBuffer[transferBuffer] = cycle;
            return transferBuffer;
        }

        public override void CopyToMappedBuffer(nint mappedDestination, int byteOffset, byte[] source, int sourceOffset, int byteCount)
            => Buffer.BlockCopy(source, sourceOffset, mappedBytes[mappedDestination], byteOffset, byteCount);

        public override void UploadToBuffer(
            nint copyPass,
            nint transferBuffer,
            uint transferOffset,
            nint destinationBuffer,
            uint destinationOffset,
            uint sizeInBytes,
            bool cycle)
        {
            var raw = mappedBytes[transferBuffer].AsSpan((int)transferOffset, (int)sizeInBytes);

            if ((bufferUsages[destinationBuffer] & GpuBufferUsage.Index) != 0)
            {
                IndexUploads.Add(MemoryMarshal.Cast<byte, ushort>(raw).ToArray());
                return;
            }

            VertexMapCycleFlags.Add(lastMapCycleByTransferBuffer[transferBuffer]);
            VertexUploads.Add((cycle, MemoryMarshal.Cast<byte, TexturedVertex2D>(raw).ToArray()));
        }

        public override void ClearColorTarget(nint commandBuffer, nint colorTargetTexture, Color4 clearColor)
        {
            ClearColorTargetCount++;
            LastClearColorTargetColor = clearColor;
        }

        public override nint BeginRenderPass(nint commandBuffer, nint colorTargetTexture, Color4? clearColor)
        {
            RenderPassClearColors.Add(clearColor);
            return NextHandle();
        }

        public override void SetScissor(nint renderPass, RectangleI scissor) => activeScissor = scissor;

        public override void PushVertexUniformData(nint commandBuffer, uint slotIndex, in Matrix4x4 matrix)
            => activeProjection = matrix;

        public override void DrawIndexedPrimitives(
            nint renderPass,
            uint numIndices,
            uint numInstances,
            uint firstIndex,
            int vertexOffset,
            uint firstInstance)
        {
            DrawCalls.Add(new RecordedDrawCall(
                numIndices,
                numInstances,
                firstIndex,
                vertexOffset,
                activeScissor,
                activeProjection));
        }

        public readonly record struct RecordedDrawCall(
            uint NumIndices,
            uint NumInstances,
            uint FirstIndex,
            int VertexOffset,
            RectangleI Scissor,
            Matrix4x4 Projection);
    }
}
