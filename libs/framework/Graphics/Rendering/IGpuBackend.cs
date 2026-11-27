using System.Numerics;
using kajarlabs.Framework.Numerics;

namespace kajarlabs.Framework.Graphics.Rendering;

internal interface IGpuBackend
{
    nint CreateDevice(GpuShaderFormats shaderFormats, bool debugMode, string? preferredDriver);

    void DestroyDevice(nint device);

    string? GetDeviceDriver(nint device);

    GpuShaderFormats GetSupportedShaderFormats(nint device);

    bool ClaimWindow(nint device, nint windowHandle);

    void ReleaseWindow(nint device, nint windowHandle);

    bool WindowSupportsSwapchainComposition(nint device, nint windowHandle, GpuSwapchainComposition composition);

    bool WindowSupportsPresentMode(nint device, nint windowHandle, GpuPresentMode presentMode);

    bool SetSwapchainParameters(
        nint device,
        nint windowHandle,
        GpuSwapchainComposition composition,
        GpuPresentMode presentMode);

    GpuTextureFormat Rgba8TextureFormat { get; }

    GpuTextureFormat GetSwapchainTextureFormat(nint device, nint windowHandle);

    GpuTextureFormat ResolveOffscreenColorFormat(nint device, GpuTextureFormat swapchainFormat);

    bool GetWindowPixelSize(nint windowHandle, out int width, out int height);

    bool IsWindowMinimized(nint windowHandle);

    nint CreateTexture(nint device, in GpuTextureDescription description);

    void ReleaseTexture(nint device, nint texture);

    nint CreateSampler(nint device, TextureSamplerParameters parameters);

    void ReleaseSampler(nint device, nint sampler);

    nint CreateTransferBuffer(nint device, GpuTransferBufferUsage usage, uint sizeInBytes);

    void ReleaseTransferBuffer(nint device, nint transferBuffer);

    nint MapTransferBuffer(nint device, nint transferBuffer, bool cycle);

    void CopyToMappedBuffer(nint mappedDestination, int byteOffset, byte[] source, int sourceOffset, int byteCount);

    void UnmapTransferBuffer(nint device, nint transferBuffer);

    nint BeginCopyPass(nint commandBuffer);

    void UploadToTexture(
        nint copyPass,
        in GpuTextureUploadSource source,
        in GpuTextureRegion destination,
        bool cycle);

    void EndCopyPass(nint copyPass);

    nint CreateShader(nint device, in GpuShaderCreateDescription description);

    void ReleaseShader(nint device, nint shader);

    nint CreateGraphicsPipeline(nint device, in GpuGraphicsPipelineDescription description);

    void ReleaseGraphicsPipeline(nint device, nint pipeline);

    nint CreateBuffer(nint device, GpuBufferUsage usage, uint sizeInBytes);

    void ReleaseBuffer(nint device, nint buffer);

    void UploadToBuffer(
        nint copyPass,
        nint transferBuffer,
        uint transferOffset,
        nint destinationBuffer,
        uint destinationOffset,
        uint sizeInBytes,
        bool cycle);

    nint BeginRenderPass(nint commandBuffer, nint colorTargetTexture, Color4? clearColor);

    void EndRenderPass(nint renderPass);

    void BindGraphicsPipeline(nint renderPass, nint pipeline);

    void SetViewport(nint renderPass, Viewport viewport);

    void SetScissor(nint renderPass, RectangleI scissor);

    void BindVertexBuffer(nint renderPass, uint firstSlot, nint vertexBuffer, uint offset);

    void BindIndexBuffer(nint renderPass, nint indexBuffer, uint offset, GpuIndexElementSize indexElementSize);

    void BindFragmentSampler(nint renderPass, uint firstSlot, nint texture, nint sampler);

    void PushVertexUniformData(nint commandBuffer, uint slotIndex, in Matrix4x4 matrix);

    void PushFragmentUniformData(nint commandBuffer, uint slotIndex, in GpuMsdfFragmentUniform uniform);

    void DrawIndexedPrimitives(
        nint renderPass,
        uint numIndices,
        uint numInstances,
        uint firstIndex,
        int vertexOffset,
        uint firstInstance);

    nint AcquireCommandBuffer(nint device);

    bool SubmitCommandBuffer(nint commandBuffer);

    nint SubmitCommandBufferAndAcquireFence(nint commandBuffer);

    bool CancelCommandBuffer(nint commandBuffer);

    void ClearColorTarget(nint commandBuffer, nint colorTargetTexture, Color4 clearColor);

    bool AcquireSwapchainTexture(
        nint commandBuffer,
        nint windowHandle,
        out nint swapchainTexture,
        out uint width,
        out uint height);

    void BlitTexture(
        nint commandBuffer,
        nint sourceTexture,
        uint sourceWidth,
        uint sourceHeight,
        nint destinationTexture,
        uint destinationWidth,
        uint destinationHeight);

    bool QueryFence(nint device, nint fence);

    bool WaitForFences(nint device, ReadOnlySpan<nint> fences);

    void ReleaseFence(nint device, nint fence);

    bool WaitForIdle(nint device);
}
