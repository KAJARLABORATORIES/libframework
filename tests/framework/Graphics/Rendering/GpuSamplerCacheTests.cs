using kajarlabs.Framework.Graphics;
using kajarlabs.Framework.Graphics.Rendering;
using kajarlabs.Framework.Tests.TestUtilities;

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

    private sealed class SamplerRecordingBackend : StubGpuBackend
    {
        public List<nint> CreatedSamplers { get; } = [];
        public List<nint> ReleasedSamplers { get; } = [];

        public override nint CreateSampler(nint device, TextureSamplerParameters parameters)
        {
            var handle = NextHandle();
            CreatedSamplers.Add(handle);
            return handle;
        }

        public override void ReleaseSampler(nint device, nint sampler) => ReleasedSamplers.Add(sampler);
    }
}
