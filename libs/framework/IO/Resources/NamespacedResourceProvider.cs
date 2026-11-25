namespace kajarlabs.Framework.IO.Resources;

/// <summary>
/// Wraps an inner <see cref="IResourceProvider"/> and scopes all lookups and resource listings under a
/// normalized <c>/</c>-separated prefix without allowing <c>..</c> path escapes.
/// </summary>
public sealed class NamespacedResourceProvider : IResourceProvider
{
    private readonly IResourceProvider inner;
    private readonly string prefixWithSlash;
    private readonly bool leaveOpen;
    private int disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="NamespacedResourceProvider"/> class.
    /// </summary>
    /// <param name="inner">The underlying resource provider to scope.</param>
    /// <param name="prefix">The relative namespace prefix (for example, <c>"Textures"</c> or <c>"Textures/UI"</c>).</param>
    /// <param name="leaveOpen">
    /// <see langword="true"/> to leave <paramref name="inner"/> open when this provider is disposed;
    /// <see langword="false"/> to dispose <paramref name="inner"/> with this provider.
    /// </param>
    public NamespacedResourceProvider(IResourceProvider inner, string prefix, bool leaveOpen = false)
    {
        ArgumentNullException.ThrowIfNull(inner);

        Prefix = ResourcePath.NormalizeRequired(prefix, nameof(prefix));
        prefixWithSlash = string.Concat(Prefix, "/");
        this.inner = inner;
        this.leaveOpen = leaveOpen;
    }

    /// <summary>
    /// Gets the normalized <c>/</c>-separated prefix enforced by this provider.
    /// </summary>
    public string Prefix { get; }

    /// <inheritdoc />
    public Stream? GetStream(string name)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);

        var normalized = ResourcePath.TryNormalize(name);
        return normalized is null ? null : inner.GetStream(string.Concat(prefixWithSlash, normalized));
    }

    /// <inheritdoc />
    public byte[]? GetBytes(string name)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);

        var normalized = ResourcePath.TryNormalize(name);
        return normalized is null ? null : inner.GetBytes(string.Concat(prefixWithSlash, normalized));
    }

    /// <inheritdoc />
    public Task<byte[]?> GetBytesAsync(string name, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);

        var normalized = ResourcePath.TryNormalize(name);

        if (cancellationToken.IsCancellationRequested)
            return Task.FromCanceled<byte[]?>(cancellationToken);

        if (normalized is null)
            return Task.FromResult<byte[]?>(null);

        return inner.GetBytesAsync(string.Concat(prefixWithSlash, normalized), cancellationToken);
    }

    /// <inheritdoc />
    public bool Exists(string name)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);

        var normalized = ResourcePath.TryNormalize(name);
        return normalized is not null && inner.Exists(string.Concat(prefixWithSlash, normalized));
    }

    /// <inheritdoc />
    public IEnumerable<string> GetAvailableResources()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        return enumerateAvailableResources();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
            return;

        if (!leaveOpen)
            inner.Dispose();
    }

    private IEnumerable<string> enumerateAvailableResources()
    {
        foreach (var resource in inner.GetAvailableResources())
        {
            if (string.IsNullOrEmpty(resource))
                continue;

            var normalized = resource.Contains('\\') ? resource.Replace('\\', '/') : resource;

            if (normalized.StartsWith(prefixWithSlash, StringComparison.Ordinal) && normalized.Length > prefixWithSlash.Length)
                yield return normalized[prefixWithSlash.Length..];
        }
    }
}
