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

    GpuTextureFormat GetSwapchainTextureFormat(nint device, nint windowHandle);

    GpuTextureFormat ResolveOffscreenColorFormat(nint device, GpuTextureFormat swapchainFormat);

    bool GetWindowPixelSize(nint windowHandle, out int width, out int height);

    bool IsWindowMinimized(nint windowHandle);

    nint CreateTexture(nint device, in GpuTextureDescription description);

    void ReleaseTexture(nint device, nint texture);

    nint AcquireCommandBuffer(nint device);

    bool SubmitCommandBuffer(nint commandBuffer);

    nint SubmitCommandBufferAndAcquireFence(nint commandBuffer);

    bool CancelCommandBuffer(nint commandBuffer);

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
