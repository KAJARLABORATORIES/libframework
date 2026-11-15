namespace kajarlabs.Framework.Graphics.Rendering;

/// <summary>
/// Caches <c>SDL_GPUSampler</c> handles keyed by <see cref="TextureSamplerParameters"/>.
/// </summary>
internal sealed class GpuSamplerCache : IDisposable
{
    private readonly IGpuBackend backend;
    private readonly nint device;
    private readonly Lock cacheLock = new();
    private readonly Dictionary<TextureSamplerParameters, nint> samplers = new();
    private bool disposed;

    public GpuSamplerCache(IGpuBackend backend, nint device)
    {
        ArgumentNullException.ThrowIfNull(backend);

        if (device == nint.Zero)
            throw new ArgumentException("GPU device handle must not be zero.", nameof(device));

        this.backend = backend;
        this.device = device;
    }

    internal int CachedSamplerCount
    {
        get
        {
            lock (cacheLock)
                return samplers.Count;
        }
    }

    public nint GetOrCreateSampler(TextureSamplerParameters parameters)
    {
        lock (cacheLock)
        {
            ObjectDisposedException.ThrowIf(disposed, this);

            if (samplers.TryGetValue(parameters, out var existingSampler))
                return existingSampler;

            var createdSampler = backend.CreateSampler(device, parameters);

            if (createdSampler == nint.Zero)
            {
                throw new InvalidOperationException(
                    $"Failed to create SDL_GPUSampler for ({parameters.Filtering}, {parameters.WrapMode}).");
            }

            samplers[parameters] = createdSampler;
            return createdSampler;
        }
    }

    public void Dispose()
    {
        nint[] samplersToRelease;

        lock (cacheLock)
        {
            if (disposed)
                return;

            disposed = true;
            samplersToRelease = [.. samplers.Values];
            samplers.Clear();
        }

        foreach (var sampler in samplersToRelease)
            backend.ReleaseSampler(device, sampler);
    }
}
