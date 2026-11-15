using System.Numerics;
using kajarlabs.Framework.Graphics;
using kajarlabs.Framework.Graphics.Rendering;
using kajarlabs.Framework.Numerics;

namespace kajarlabs.Framework.Tests.TestUtilities;

/// <summary>
/// Base <see cref="IGpuBackend"/> test double providing thread-safe synthetic handles and default no-op/success behavior.
/// Test suites override only the GPU backend operations they observe or fault-inject.
/// </summary>
internal abstract class StubGpuBackend : IGpuBackend
{
    private long nextHandle = 0x1000;

    public virtual int PixelWidth { get; set; } = 1280;

    public virtual int PixelHeight { get; set; } = 720;

    public virtual GpuTextureFormat Rgba8TextureFormat => new(12);

    protected nint NextHandle() => (nint)Interlocked.Increment(ref nextHandle);

    public virtual nint CreateDevice(GpuShaderFormats shaderFormats, bool debugMode, string? preferredDriver)
        => NextHandle();

    public virtual void DestroyDevice(nint device)
    {
    }

    public virtual string? GetDeviceDriver(nint device) => "vulkan";

    public virtual GpuShaderFormats GetSupportedShaderFormats(nint device) => GpuShaderFormats.SpirV;

    public virtual bool ClaimWindow(nint device, nint windowHandle) => true;

    public virtual void ReleaseWindow(nint device, nint windowHandle)
    {
    }

    public virtual bool WindowSupportsSwapchainComposition(
        nint device,
        nint windowHandle,
        GpuSwapchainComposition composition)
        => true;

    public virtual bool WindowSupportsPresentMode(
        nint device,
        nint windowHandle,
        GpuPresentMode presentMode)
        => true;

    public virtual bool SetSwapchainParameters(
        nint device,
        nint windowHandle,
        GpuSwapchainComposition composition,
        GpuPresentMode presentMode)
        => true;

    public virtual GpuTextureFormat GetSwapchainTextureFormat(nint device, nint windowHandle) => Rgba8TextureFormat;

    public virtual GpuTextureFormat ResolveOffscreenColorFormat(nint device, GpuTextureFormat swapchainFormat)
        => swapchainFormat;

    public virtual bool GetWindowPixelSize(nint windowHandle, out int width, out int height)
    {
        width = PixelWidth;
        height = PixelHeight;
        return true;
    }

    public virtual bool IsWindowMinimized(nint windowHandle) => false;

    public virtual nint CreateTexture(nint device, in GpuTextureDescription description) => NextHandle();

    public virtual void ReleaseTexture(nint device, nint texture)
    {
    }

    public virtual nint CreateSampler(nint device, TextureSamplerParameters parameters) => NextHandle();

    public virtual void ReleaseSampler(nint device, nint sampler)
    {
    }

    public virtual nint CreateTransferBuffer(nint device, GpuTransferBufferUsage usage, uint sizeInBytes)
        => NextHandle();

    public virtual void ReleaseTransferBuffer(nint device, nint transferBuffer)
    {
    }

    public virtual nint MapTransferBuffer(nint device, nint transferBuffer, bool cycle) => NextHandle();

    public virtual void CopyToMappedBuffer(
        nint mappedDestination,
        int byteOffset,
        byte[] source,
        int sourceOffset,
        int byteCount)
    {
    }

    public virtual void UnmapTransferBuffer(nint device, nint transferBuffer)
    {
    }

    public virtual nint BeginCopyPass(nint commandBuffer) => NextHandle();

    public virtual void UploadToTexture(
        nint copyPass,
        in GpuTextureUploadSource source,
        in GpuTextureRegion destination,
        bool cycle)
    {
    }

    public virtual void EndCopyPass(nint copyPass)
    {
    }

    public virtual nint CreateShader(nint device, in GpuShaderCreateDescription description) => NextHandle();

    public virtual void ReleaseShader(nint device, nint shader)
    {
    }

    public virtual nint CreateGraphicsPipeline(nint device, in GpuGraphicsPipelineDescription description)
        => NextHandle();

    public virtual void ReleaseGraphicsPipeline(nint device, nint pipeline)
    {
    }

    public virtual nint CreateBuffer(nint device, GpuBufferUsage usage, uint sizeInBytes) => NextHandle();

    public virtual void ReleaseBuffer(nint device, nint buffer)
    {
    }

    public virtual void UploadToBuffer(
        nint copyPass,
        nint transferBuffer,
        uint transferOffset,
        nint destinationBuffer,
        uint destinationOffset,
        uint sizeInBytes,
        bool cycle)
    {
    }

    public virtual nint BeginRenderPass(nint commandBuffer, nint colorTargetTexture, Color4? clearColor)
        => NextHandle();

    public virtual void EndRenderPass(nint renderPass)
    {
    }

    public virtual void BindGraphicsPipeline(nint renderPass, nint pipeline)
    {
    }

    public virtual void SetViewport(nint renderPass, Viewport viewport)
    {
    }

    public virtual void SetScissor(nint renderPass, RectangleI scissor)
    {
    }

    public virtual void BindVertexBuffer(nint renderPass, uint firstSlot, nint vertexBuffer, uint offset)
    {
    }

    public virtual void BindIndexBuffer(nint renderPass, nint indexBuffer, uint offset, GpuIndexElementSize indexElementSize)
    {
    }

    public virtual void BindFragmentSampler(nint renderPass, uint firstSlot, nint texture, nint sampler)
    {
    }

    public virtual void PushVertexUniformData(nint commandBuffer, uint slotIndex, in Matrix4x4 matrix)
    {
    }

    public virtual void DrawIndexedPrimitives(
        nint renderPass,
        uint numIndices,
        uint numInstances,
        uint firstIndex,
        int vertexOffset,
        uint firstInstance)
    {
    }

    public virtual nint AcquireCommandBuffer(nint device) => NextHandle();

    public virtual bool SubmitCommandBuffer(nint commandBuffer) => true;

    public virtual nint SubmitCommandBufferAndAcquireFence(nint commandBuffer) => NextHandle();

    public virtual bool CancelCommandBuffer(nint commandBuffer) => true;

    public virtual void ClearColorTarget(nint commandBuffer, nint colorTargetTexture, Color4 clearColor)
    {
    }

    public virtual bool AcquireSwapchainTexture(
        nint commandBuffer,
        nint windowHandle,
        out nint swapchainTexture,
        out uint width,
        out uint height)
    {
        swapchainTexture = NextHandle();
        width = (uint)Math.Max(0, PixelWidth);
        height = (uint)Math.Max(0, PixelHeight);
        return true;
    }

    public virtual void BlitTexture(
        nint commandBuffer,
        nint sourceTexture,
        uint sourceWidth,
        uint sourceHeight,
        nint destinationTexture,
        uint destinationWidth,
        uint destinationHeight)
    {
    }

    public virtual bool QueryFence(nint device, nint fence) => true;

    public virtual bool WaitForFences(nint device, ReadOnlySpan<nint> fences) => true;

    public virtual void ReleaseFence(nint device, nint fence)
    {
    }

    public virtual bool WaitForIdle(nint device) => true;
}
