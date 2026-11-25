using System.Reflection;

namespace kajarlabs.Framework.IO.Resources;

/// <summary>
/// A read-only <see cref="IResourceProvider"/> that serves embedded manifest resources from an explicit
/// <see cref="Assembly"/>.
/// </summary>
public sealed class AssemblyResourceProvider : IResourceProvider
{
    private readonly Assembly assembly;
    private readonly Dictionary<string, string> exactLookup;
    private readonly Dictionary<string, string>? fallbackLookup;
    private readonly string[] availableResources;
    private int disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="AssemblyResourceProvider"/> class.
    /// </summary>
    /// <param name="assembly">The assembly containing embedded manifest resources.</param>
    /// <param name="prefix">
    /// The optional namespace/path prefix to strip from manifest resource names. Pass <see langword="null"/> to
    /// default to <see cref="AssemblyName.Name"/>, or <see cref="string.Empty"/> to expose all manifest resources
    /// without prefix stripping.
    /// </param>
    /// <param name="comparer">
    /// The lookup comparer used when exact-case resolution does not match, or <see langword="null"/> to default
    /// to <see cref="StringComparer.OrdinalIgnoreCase"/>. Exact-case matches always take precedence on collision.
    /// </param>
    public AssemblyResourceProvider(Assembly assembly, string? prefix = null, StringComparer? comparer = null)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        this.assembly = assembly;
        Prefix = normalizePrefix(assembly, prefix);

        var effectiveComparer = comparer ?? StringComparer.OrdinalIgnoreCase;
        var isOrdinal = ReferenceEquals(effectiveComparer, StringComparer.Ordinal);
        var dotPrefix = Prefix.Length == 0 ? string.Empty : Prefix.Replace('/', '.');

        var manifestNames = assembly.GetManifestResourceNames();
        Array.Sort(manifestNames, StringComparer.Ordinal);

        exactLookup = new Dictionary<string, string>(StringComparer.Ordinal);
        fallbackLookup = isOrdinal ? null : new Dictionary<string, string>(effectiveComparer);

        var available = new List<string>(manifestNames.Length);

        for (var i = 0; i < manifestNames.Length; i++)
        {
            var manifestName = manifestNames[i];

            if (!tryStripPrefix(manifestName, dotPrefix, effectiveComparer, out var relativeName))
                continue;

            available.Add(relativeName);
            exactLookup.TryAdd(relativeName, manifestName);

            var dotKey = relativeName.Contains('/') ? relativeName.Replace('/', '.') : relativeName;

            if (!ReferenceEquals(dotKey, relativeName))
                exactLookup.TryAdd(dotKey, manifestName);

            fallbackLookup?.TryAdd(dotKey, manifestName);
        }

        availableResources = [.. available];
    }

    /// <summary>
    /// Gets the normalized resource prefix stripped from manifest resource names, or <see cref="string.Empty"/>
    /// when no prefix is applied.
    /// </summary>
    public string Prefix { get; }

    /// <inheritdoc />
    public Stream? GetStream(string name)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);

        var normalized = ResourcePath.TryNormalize(name);

        if (normalized is null || !tryResolveManifestName(normalized, out var manifestName))
            return null;

        return assembly.GetManifestResourceStream(manifestName);
    }

    /// <inheritdoc />
    public byte[]? GetBytes(string name)
    {
        using var stream = GetStream(name);
        return stream is null ? null : ResourceStreamHelper.ReadAllBytes(stream);
    }

    /// <inheritdoc />
    public Task<byte[]?> GetBytesAsync(string name, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);

        var normalized = ResourcePath.TryNormalize(name);

        if (cancellationToken.IsCancellationRequested)
            return Task.FromCanceled<byte[]?>(cancellationToken);

        if (normalized is null || !tryResolveManifestName(normalized, out var manifestName))
            return Task.FromResult<byte[]?>(null);

        return getBytesCoreAsync(manifestName, cancellationToken);
    }

    /// <inheritdoc />
    public bool Exists(string name)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);

        var normalized = ResourcePath.TryNormalize(name);
        return normalized is not null && tryResolveManifestName(normalized, out _);
    }

    /// <inheritdoc />
    public IEnumerable<string> GetAvailableResources()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        return enumerateAvailableResources();
    }

    /// <inheritdoc />
    public void Dispose() => Interlocked.Exchange(ref disposed, 1);

    private async Task<byte[]?> getBytesCoreAsync(string manifestName, CancellationToken cancellationToken)
    {
        await using var stream = assembly.GetManifestResourceStream(manifestName);

        if (stream is null)
            return null;

        return await ResourceStreamHelper.ReadAllBytesAsync(stream, cancellationToken).ConfigureAwait(false);
    }

    private IEnumerable<string> enumerateAvailableResources()
    {
        for (var i = 0; i < availableResources.Length; i++)
            yield return availableResources[i];
    }

    private bool tryResolveManifestName(string normalized, out string manifestName)
    {
        if (exactLookup.TryGetValue(normalized, out manifestName!))
            return true;

        var dotKey = normalized.Contains('/') ? normalized.Replace('/', '.') : normalized;

        if (!ReferenceEquals(dotKey, normalized) && exactLookup.TryGetValue(dotKey, out manifestName!))
            return true;

        if (fallbackLookup is not null && fallbackLookup.TryGetValue(dotKey, out manifestName!))
            return true;

        manifestName = string.Empty;
        return false;
    }

    private static string normalizePrefix(Assembly assembly, string? prefix)
    {
        if (prefix is null)
            return assembly.GetName().Name ?? string.Empty;

        if (prefix.Length == 0)
            return string.Empty;

        if (string.IsNullOrWhiteSpace(prefix))
            throw new ArgumentException("Resource prefix cannot be whitespace.", nameof(prefix));

        if (prefix[0] is '/' or '\\')
            throw new ArgumentException($"Prefix '{prefix}' cannot start with a path separator.", nameof(prefix));

        if (prefix.Contains(':'))
            throw new ArgumentException($"Prefix '{prefix}' cannot contain ':'.", nameof(prefix));

        var normalized = prefix.Replace('\\', '/').TrimEnd('/', '.');

        if (normalized.Length == 0)
            throw new ArgumentException($"Prefix '{prefix}' must contain at least one valid segment.", nameof(prefix));

        return normalized;
    }

    private static bool tryStripPrefix(
        string manifestName,
        string dotPrefix,
        StringComparer comparer,
        out string relativeName)
    {
        if (dotPrefix.Length == 0)
        {
            relativeName = (manifestName.Contains('\\') ? manifestName.Replace('\\', '/') : manifestName).TrimStart('/');
            return relativeName.Length > 0;
        }

        if (manifestName.Length <= dotPrefix.Length)
        {
            relativeName = string.Empty;
            return false;
        }

        var separator = manifestName[dotPrefix.Length];

        if (separator is not ('.' or '/' or '\\'))
        {
            relativeName = string.Empty;
            return false;
        }

        var prefixSlice = manifestName[..dotPrefix.Length];

        if (prefixSlice.Contains('/') || prefixSlice.Contains('\\'))
            prefixSlice = prefixSlice.Replace('\\', '.').Replace('/', '.');

        if (!comparer.Equals(prefixSlice, dotPrefix))
        {
            relativeName = string.Empty;
            return false;
        }

        var remainder = manifestName[(dotPrefix.Length + 1)..];
        relativeName = (remainder.Contains('\\') ? remainder.Replace('\\', '/') : remainder).TrimStart('/');
        return relativeName.Length > 0;
    }
}
