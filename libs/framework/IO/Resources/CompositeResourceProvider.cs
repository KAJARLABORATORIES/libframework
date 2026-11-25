namespace kajarlabs.Framework.IO.Resources;

/// <summary>
/// Aggregates multiple <see cref="IResourceProvider"/> instances in last-added-wins precedence order with
/// optional extension fallback and lock-free concurrent reads.
/// </summary>
public sealed class CompositeResourceProvider : IResourceProvider
{
    private readonly Lock mutationLock = new();
    private volatile ProviderEntry[] providers = [];
    private volatile string[] extensions = [];
    private int disposed;

    /// <summary>
    /// Initializes an empty <see cref="CompositeResourceProvider"/>.
    /// </summary>
    public CompositeResourceProvider()
    {
    }

    /// <summary>
    /// Initializes a <see cref="CompositeResourceProvider"/> with the specified child providers in registration
    /// order (the last provider has highest precedence). All initial providers are owned and disposed when this
    /// composite is disposed.
    /// </summary>
    /// <param name="providers">The initial child providers to register.</param>
    public CompositeResourceProvider(params ReadOnlySpan<IResourceProvider> providers)
    {
        for (var i = 0; i < providers.Length; i++)
            AddProvider(providers[i]);
    }

    /// <summary>
    /// Registers <paramref name="provider"/> at highest precedence (last-added wins). If the same provider
    /// instance is already registered, it is promoted to highest precedence and its ownership flag is updated.
    /// </summary>
    /// <param name="provider">The child provider to register.</param>
    /// <param name="leaveOpen">
    /// <see langword="true"/> to keep <paramref name="provider"/> open when this composite is disposed;
    /// <see langword="false"/> to dispose it with this composite.
    /// </param>
    public void AddProvider(IResourceProvider provider, bool leaveOpen = false)
    {
        ArgumentNullException.ThrowIfNull(provider);

        if (ReferenceEquals(provider, this))
            throw new ArgumentException("A CompositeResourceProvider cannot contain itself.", nameof(provider));

        lock (mutationLock)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);

            var current = providers;
            var existingIndex = findProviderIndex(current, provider);
            var nextEntry = new ProviderEntry(provider, leaveOpen);

            if (existingIndex < 0)
            {
                var next = new ProviderEntry[current.Length + 1];
                current.CopyTo(next, 0);
                next[current.Length] = nextEntry;
                providers = next;
            }
            else
            {
                var next = new ProviderEntry[current.Length];
                var targetIndex = 0;

                for (var i = 0; i < current.Length; i++)
                {
                    if (i != existingIndex)
                        next[targetIndex++] = current[i];
                }

                next[targetIndex] = nextEntry;
                providers = next;
            }
        }
    }

    /// <summary>
    /// Detaches <paramref name="provider"/> from this composite without disposing it.
    /// </summary>
    /// <param name="provider">The child provider to detach.</param>
    /// <returns><see langword="true"/> if the provider was found and removed; otherwise, <see langword="false"/>.</returns>
    public bool RemoveProvider(IResourceProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        lock (mutationLock)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);

            var current = providers;
            var existingIndex = findProviderIndex(current, provider);

            if (existingIndex < 0)
                return false;

            if (current.Length == 1)
            {
                providers = [];
                return true;
            }

            var next = new ProviderEntry[current.Length - 1];
            var targetIndex = 0;

            for (var i = 0; i < current.Length; i++)
            {
                if (i != existingIndex)
                    next[targetIndex++] = current[i];
            }

            providers = next;
            return true;
        }
    }

    /// <summary>
    /// Registers a fallback file extension (for example, <c>".png"</c> or <c>"png"</c>) probed after the exact
    /// resource name on each child provider. Duplicate extensions are ignored case-insensitively.
    /// </summary>
    /// <param name="extension">The extension to append when exact lookups miss.</param>
    public void AddExtension(string extension)
    {
        var normalized = ResourcePath.NormalizeExtension(extension);

        lock (mutationLock)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);

            var current = extensions;

            for (var i = 0; i < current.Length; i++)
            {
                if (string.Equals(current[i], normalized, StringComparison.OrdinalIgnoreCase))
                    return;
            }

            var next = new string[current.Length + 1];
            current.CopyTo(next, 0);
            next[current.Length] = normalized;
            extensions = next;
        }
    }

    /// <inheritdoc />
    public Stream? GetStream(string name)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);

        var normalized = ResourcePath.TryNormalize(name);

        if (normalized is null)
            return null;

        var currentProviders = providers;
        var currentExtensions = extensions;

        for (var i = currentProviders.Length - 1; i >= 0; i--)
        {
            var provider = currentProviders[i].Provider;
            var stream = provider.GetStream(normalized);

            if (stream is not null)
                return stream;

            for (var e = 0; e < currentExtensions.Length; e++)
            {
                var extension = currentExtensions[e];

                if (normalized.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                    continue;

                stream = provider.GetStream(string.Concat(normalized, extension));

                if (stream is not null)
                    return stream;
            }
        }

        return null;
    }

    /// <inheritdoc />
    public byte[]? GetBytes(string name)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);

        var normalized = ResourcePath.TryNormalize(name);

        if (normalized is null)
            return null;

        var currentProviders = providers;
        var currentExtensions = extensions;

        for (var i = currentProviders.Length - 1; i >= 0; i--)
        {
            var provider = currentProviders[i].Provider;
            var bytes = provider.GetBytes(normalized);

            if (bytes is not null)
                return bytes;

            for (var e = 0; e < currentExtensions.Length; e++)
            {
                var extension = currentExtensions[e];

                if (normalized.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                    continue;

                bytes = provider.GetBytes(string.Concat(normalized, extension));

                if (bytes is not null)
                    return bytes;
            }
        }

        return null;
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

        return getBytesCoreAsync(normalized, cancellationToken);
    }

    /// <inheritdoc />
    public bool Exists(string name)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);

        var normalized = ResourcePath.TryNormalize(name);

        if (normalized is null)
            return false;

        var currentProviders = providers;
        var currentExtensions = extensions;

        for (var i = currentProviders.Length - 1; i >= 0; i--)
        {
            var provider = currentProviders[i].Provider;

            if (provider.Exists(normalized))
                return true;

            for (var e = 0; e < currentExtensions.Length; e++)
            {
                var extension = currentExtensions[e];

                if (normalized.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (provider.Exists(string.Concat(normalized, extension)))
                    return true;
            }
        }

        return false;
    }

    /// <inheritdoc />
    public IEnumerable<string> GetAvailableResources()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);

        var snapshot = providers;
        return enumerateAvailableResources(snapshot);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
            return;

        ProviderEntry[] toDispose;

        lock (mutationLock)
        {
            toDispose = providers;
            providers = [];
            extensions = [];
        }

        disposeOwnedProviders(toDispose, toDispose.Length - 1);
    }

    private async Task<byte[]?> getBytesCoreAsync(string normalized, CancellationToken cancellationToken)
    {
        var currentProviders = providers;
        var currentExtensions = extensions;

        for (var i = currentProviders.Length - 1; i >= 0; i--)
        {
            var provider = currentProviders[i].Provider;

            cancellationToken.ThrowIfCancellationRequested();

            var bytes = await provider.GetBytesAsync(normalized, cancellationToken).ConfigureAwait(false);

            if (bytes is not null)
                return bytes;

            for (var e = 0; e < currentExtensions.Length; e++)
            {
                var extension = currentExtensions[e];

                if (normalized.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                    continue;

                cancellationToken.ThrowIfCancellationRequested();

                bytes = await provider.GetBytesAsync(string.Concat(normalized, extension), cancellationToken).ConfigureAwait(false);

                if (bytes is not null)
                    return bytes;
            }
        }

        return null;
    }

    private static IEnumerable<string> enumerateAvailableResources(ProviderEntry[] snapshot)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (var i = snapshot.Length - 1; i >= 0; i--)
        {
            foreach (var resource in snapshot[i].Provider.GetAvailableResources())
            {
                if (seen.Add(resource))
                    yield return resource;
            }
        }
    }

    private static int findProviderIndex(ProviderEntry[] entries, IResourceProvider provider)
    {
        for (var i = 0; i < entries.Length; i++)
        {
            if (ReferenceEquals(entries[i].Provider, provider))
                return i;
        }

        return -1;
    }

    private static void disposeOwnedProviders(ProviderEntry[] entries, int index)
    {
        while (index >= 0 && entries[index].LeaveOpen)
            index--;

        if (index < 0)
            return;

        try
        {
            entries[index].Provider.Dispose();
        }
        finally
        {
            disposeOwnedProviders(entries, index - 1);
        }
    }

    private readonly struct ProviderEntry
    {
        public ProviderEntry(IResourceProvider provider, bool leaveOpen)
        {
            Provider = provider;
            LeaveOpen = leaveOpen;
        }

        public IResourceProvider Provider { get; }

        public bool LeaveOpen { get; }
    }
}
