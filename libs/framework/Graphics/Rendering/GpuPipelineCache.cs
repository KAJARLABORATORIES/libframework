namespace kajarlabs.Framework.Graphics.Rendering;

/// <summary>
/// Caches compiled shader objects and <c>SDL_GPUGraphicsPipeline</c> handles keyed by immutable pipeline state.
/// </summary>
internal sealed class GpuPipelineCache : IDisposable
{
    private readonly IGpuBackend backend;
    private readonly nint device;
    private readonly GpuShaderFormats supportedFormats;
    private readonly Lock cacheLock = new();
    private readonly Dictionary<(GpuShaderDescriptor Descriptor, GpuShaderFormats Format), nint> shaders = new();
    private readonly Dictionary<GpuGraphicsPipelineKey, nint> pipelines = new();
    private bool disposed;

    public GpuPipelineCache(IGpuBackend backend, nint device, GpuShaderFormats supportedFormats)
    {
        ArgumentNullException.ThrowIfNull(backend);

        if (device == nint.Zero)
            throw new ArgumentException("GPU device handle must not be zero.", nameof(device));

        this.backend = backend;
        this.device = device;
        this.supportedFormats = supportedFormats;
    }

    internal int CachedShaderCount
    {
        get
        {
            lock (cacheLock)
                return shaders.Count;
        }
    }

    internal int CachedPipelineCount
    {
        get
        {
            lock (cacheLock)
                return pipelines.Count;
        }
    }

    public nint GetOrCreatePipeline(GpuGraphicsPipelineKey key)
    {
        ArgumentNullException.ThrowIfNull(key);

        lock (cacheLock)
        {
            ObjectDisposedException.ThrowIf(disposed, this);

            if (pipelines.TryGetValue(key, out var existingPipeline))
                return existingPipeline;

            var vertexShader = getOrCreateShaderLocked(key.VertexShader);
            var fragmentShader = getOrCreateShaderLocked(key.FragmentShader);

            var pipelineDescription = new GpuGraphicsPipelineDescription(
                vertexShader,
                fragmentShader,
                key.VertexLayout,
                key.Blending,
                key.Topology,
                key.ColorTargetFormat);

            var createdPipeline = backend.CreateGraphicsPipeline(device, in pipelineDescription);

            if (createdPipeline == nint.Zero)
            {
                throw new InvalidOperationException(
                    $"Failed to create SDL_GPUGraphicsPipeline for shaders '{key.VertexShader.Name}' / '{key.FragmentShader.Name}'.");
            }

            pipelines[key] = createdPipeline;
            return createdPipeline;
        }
    }

    public void Dispose()
    {
        nint[] pipelinesToRelease;
        nint[] shadersToRelease;

        lock (cacheLock)
        {
            if (disposed)
                return;

            disposed = true;
            pipelinesToRelease = [.. pipelines.Values];
            shadersToRelease = [.. shaders.Values];
            pipelines.Clear();
            shaders.Clear();
        }

        foreach (var pipeline in pipelinesToRelease)
            backend.ReleaseGraphicsPipeline(device, pipeline);

        foreach (var shader in shadersToRelease)
            backend.ReleaseShader(device, shader);
    }

    private nint getOrCreateShaderLocked(GpuShaderDescriptor descriptor)
    {
        var variant = descriptor.SelectVariant(supportedFormats);
        var shaderKey = (descriptor, variant.Format);

        if (shaders.TryGetValue(shaderKey, out var existingShader))
            return existingShader;

        var bytecode = variant.LoadBytecode();
        var description = new GpuShaderCreateDescription(
            descriptor.Stage,
            variant.Format,
            variant.Entrypoint,
            bytecode,
            descriptor.NumSamplers,
            descriptor.NumStorageTextures,
            descriptor.NumStorageBuffers,
            descriptor.NumUniformBuffers);

        var createdShader = backend.CreateShader(device, in description);

        if (createdShader == nint.Zero)
            throw new InvalidOperationException($"Failed to create SDL_GPUShader '{descriptor.Name}' ({variant.Format}).");

        shaders[shaderKey] = createdShader;
        return createdShader;
    }
}
