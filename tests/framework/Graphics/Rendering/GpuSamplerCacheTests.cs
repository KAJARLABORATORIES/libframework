using kajarlabs.Framework.Graphics;
using kajarlabs.Framework.Graphics.Rendering;
using kajarlabs.Framework.Numerics;

namespace kajarlabs.Framework.Tests.Graphics.Rendering;

public sealed class GpuSamplerCacheTests
{
    private static readonly nint fake_device = new(0x5555);

    [Fact]
    public void GetOrCreateSamplerHitsOnEqualParametersAndMissesAcrossFilterOrWrap()
    {
        var backend = new SamplerRecordingBackend();
        using var cache = new GpuSamplerCache(backend, fake_device);

        var h1 = cache.GetOrCreateSampler(TextureSamplerParameters.LINEAR_CLAMP);
        var h2 = cache.GetOrCreateSampler(new TextureSamplerParameters(TextureFilteringMode.Linear, TextureWrapMode.ClampToEdge));
        var hNearestClamp = cache.GetOrCreateSampler(TextureSamplerParameters.NEAREST_CLAMP);
        var hLinearRepeat = cache.GetOrCreateSampler(TextureSamplerParameters.LINEAR_REPEAT);
        var hNearestRepeat = cache.GetOrCreateSampler(TextureSamplerParameters.NEAREST_REPEAT);

        Assert.Equal(h1, h2);
        Assert.NotEqual(h1, hNearestClamp);
        Assert.NotEqual(h1, hLinearRepeat);
        Assert.NotEqual(h1, hNearestRepeat);
        Assert.NotEqual(hNearestClamp, hLinearRepeat);

        Assert.Equal(4, backend.CreatedSamplers.Count);
        Assert.Equal(4, cache.CachedSamplerCount);
    }

    [Fact]
    public void DisposeReleasesAllCachedSamplersOnceAndRejectsFurtherRequests()
    {
        var backend = new SamplerRecordingBackend();
        var cache = new GpuSamplerCache(backend, fake_device);

        cache.GetOrCreateSampler(TextureSamplerParameters.LINEAR_CLAMP);
        cache.GetOrCreateSampler(TextureSamplerParameters.NEAREST_REPEAT);

        cache.Dispose();
        cache.Dispose();

        Assert.Equal(0, cache.CachedSamplerCount);
        Assert.Equal(backend.CreatedSamplers, backend.ReleasedSamplers);
        Assert.Throws<ObjectDisposedException>(() => cache.GetOrCreateSampler(TextureSamplerParameters.LINEAR_CLAMP));
    }

    private sealed class SamplerRecordingBackend : IGpuBackend
    {
        private nint nextHandle = 0x200;

        public List<nint> CreatedSamplers { get; } = [];
        public List<nint> ReleasedSamplers { get; } = [];

        public nint CreateSampler(nint device, TextureSamplerParameters parameters)
        {
            var handle = ++nextHandle;
            CreatedSamplers.Add(handle);
            return handle;
        }

        public void ReleaseSampler(nint device, nint sampler) => ReleasedSamplers.Add(sampler);

        public GpuTextureFormat Rgba8TextureFormat => GpuTextureFormat.INVALID;
        public nint CreateDevice(GpuShaderFormats shaderFormats, bool debugMode, string? preferredDriver) => 0;
        public void DestroyDevice(nint device) { }
        public string? GetDeviceDriver(nint device) => null;
        public GpuShaderFormats GetSupportedShaderFormats(nint device) => GpuShaderFormats.None;
        public bool ClaimWindow(nint device, nint windowHandle) => false;
        public void ReleaseWindow(nint device, nint windowHandle) { }
        public bool WindowSupportsSwapchainComposition(nint device, nint windowHandle, GpuSwapchainComposition composition) => false;
        public bool WindowSupportsPresentMode(nint device, nint windowHandle, GpuPresentMode presentMode) => false;
        public bool SetSwapchainParameters(nint device, nint windowHandle, GpuSwapchainComposition composition, GpuPresentMode presentMode) => false;
        public GpuTextureFormat GetSwapchainTextureFormat(nint device, nint windowHandle) => GpuTextureFormat.INVALID;
        public GpuTextureFormat ResolveOffscreenColorFormat(nint device, GpuTextureFormat swapchainFormat) => GpuTextureFormat.INVALID;
        public bool GetWindowPixelSize(nint windowHandle, out int width, out int height) { width = 0; height = 0; return false; }
        public bool IsWindowMinimized(nint windowHandle) => false;
        public nint CreateTexture(nint device, in GpuTextureDescription description) => 0;
        public void ReleaseTexture(nint device, nint texture) { }
        public nint CreateTransferBuffer(nint device, GpuTransferBufferUsage usage, uint sizeInBytes) => 0;
        public void ReleaseTransferBuffer(nint device, nint transferBuffer) { }
        public nint MapTransferBuffer(nint device, nint transferBuffer, bool cycle) => 0;
        public void CopyToMappedBuffer(nint mappedDestination, int byteOffset, byte[] source, int sourceOffset, int byteCount) { }
        public void UnmapTransferBuffer(nint device, nint transferBuffer) { }
        public nint BeginCopyPass(nint commandBuffer) => 0;
        public void UploadToTexture(nint copyPass, in GpuTextureUploadSource source, in GpuTextureRegion destination, bool cycle) { }
        public void EndCopyPass(nint copyPass) { }
        public nint CreateShader(nint device, in GpuShaderCreateDescription description) => 0;
        public void ReleaseShader(nint device, nint shader) { }
        public nint CreateGraphicsPipeline(nint device, in GpuGraphicsPipelineDescription description) => 0;
        public void ReleaseGraphicsPipeline(nint device, nint pipeline) { }
        public nint AcquireCommandBuffer(nint device) => 0;
        public bool SubmitCommandBuffer(nint commandBuffer) => false;
        public nint SubmitCommandBufferAndAcquireFence(nint commandBuffer) => 0;
        public bool CancelCommandBuffer(nint commandBuffer) => false;
        public void ClearColorTarget(nint commandBuffer, nint colorTargetTexture, Color4 clearColor) { }
        public bool AcquireSwapchainTexture(nint commandBuffer, nint windowHandle, out nint swapchainTexture, out uint width, out uint height) { swapchainTexture = 0; width = 0; height = 0; return false; }
        public void BlitTexture(nint commandBuffer, nint sourceTexture, uint sourceWidth, uint sourceHeight, nint destinationTexture, uint destinationWidth, uint destinationHeight) { }
        public bool QueryFence(nint device, nint fence) => false;
        public bool WaitForFences(nint device, ReadOnlySpan<nint> fences) => false;
        public void ReleaseFence(nint device, nint fence) { }
        public bool WaitForIdle(nint device) => false;
    }
}
