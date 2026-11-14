using System.Reflection;

namespace kajarlabs.Framework.Graphics.Rendering;

/// <summary>
/// Represents a single backend-specific bytecode or source payload for a shader descriptor.
/// </summary>
internal sealed class GpuShaderVariant
{
    private readonly Lock loadLock = new();
    private readonly Assembly? resourceAssembly;
    private readonly string? resourceName;
    private ReadOnlyMemory<byte> cachedPayload;
    private bool hasCachedPayload;

    public GpuShaderVariant(GpuShaderFormats format, string entrypoint, ReadOnlyMemory<byte> bytecode)
    {
        if (format == GpuShaderFormats.None)
            throw new ArgumentException("Shader variant must specify a concrete format flag.", nameof(format));

        ArgumentException.ThrowIfNullOrWhiteSpace(entrypoint);

        if (bytecode.IsEmpty)
            throw new ArgumentException("Shader bytecode payload must not be empty.", nameof(bytecode));

        Format = format;
        Entrypoint = entrypoint;
        cachedPayload = bytecode;
        hasCachedPayload = true;
    }

    public GpuShaderVariant(GpuShaderFormats format, string entrypoint, Assembly resourceAssembly, string resourceName)
    {
        if (format == GpuShaderFormats.None)
            throw new ArgumentException("Shader variant must specify a concrete format flag.", nameof(format));

        ArgumentException.ThrowIfNullOrWhiteSpace(entrypoint);
        ArgumentNullException.ThrowIfNull(resourceAssembly);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceName);

        Format = format;
        Entrypoint = entrypoint;
        this.resourceAssembly = resourceAssembly;
        this.resourceName = resourceName;
    }

    public GpuShaderFormats Format { get; }

    public string Entrypoint { get; }

    public ReadOnlyMemory<byte> LoadBytecode()
    {
        lock (loadLock)
        {
            if (hasCachedPayload)
                return cachedPayload;

            using var stream = resourceAssembly!.GetManifestResourceStream(resourceName!)
                ?? throw new InvalidOperationException($"Embedded shader resource '{resourceName}' was not found.");

            var buffer = new byte[stream.Length];
            stream.ReadExactly(buffer);

            cachedPayload = buffer;
            hasCachedPayload = true;
            return cachedPayload;
        }
    }
}

/// <summary>
/// Format-tagged shader asset descriptor containing resource binding counts and multi-backend variants.
/// </summary>
internal sealed class GpuShaderDescriptor
{
    private static readonly GpuShaderFormats[] preferred_format_order =
    [
        GpuShaderFormats.SpirV,
        GpuShaderFormats.Dxil,
        GpuShaderFormats.Msl,
        GpuShaderFormats.Dxbc,
        GpuShaderFormats.MetalLib,
        GpuShaderFormats.Private,
    ];

    public GpuShaderDescriptor(
        string name,
        GpuShaderStage stage,
        uint numSamplers,
        uint numStorageTextures,
        uint numStorageBuffers,
        uint numUniformBuffers,
        IReadOnlyList<GpuShaderVariant> variants)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(variants);

        if (variants.Count == 0)
            throw new ArgumentException("Shader descriptor must provide at least one format variant.", nameof(variants));

        Name = name;
        Stage = stage;
        NumSamplers = numSamplers;
        NumStorageTextures = numStorageTextures;
        NumStorageBuffers = numStorageBuffers;
        NumUniformBuffers = numUniformBuffers;
        Variants = variants;
    }

    public string Name { get; }

    public GpuShaderStage Stage { get; }

    public uint NumSamplers { get; }

    public uint NumStorageTextures { get; }

    public uint NumStorageBuffers { get; }

    public uint NumUniformBuffers { get; }

    public IReadOnlyList<GpuShaderVariant> Variants { get; }

    public GpuShaderVariant SelectVariant(GpuShaderFormats supportedFormats)
    {
        foreach (var preferredFormat in preferred_format_order)
        {
            if ((supportedFormats & preferredFormat) == 0)
                continue;

            for (var i = 0; i < Variants.Count; i++)
            {
                if ((Variants[i].Format & preferredFormat) != 0)
                    return Variants[i];
            }
        }

        for (var i = 0; i < Variants.Count; i++)
        {
            if ((supportedFormats & Variants[i].Format) != 0)
                return Variants[i];
        }

        throw new NotSupportedException(
            $"Shader '{Name}' ({Stage}) has no payload compatible with device shader formats '{supportedFormats}'.");
    }
}
