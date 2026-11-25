using System.Collections.Concurrent;

namespace kajarlabs.Framework.IO.Resources;

/// <summary>
/// A thread-safe in-memory <see cref="IResourceProvider"/> for tests, headless hosts, and procedural assets.
/// </summary>
public sealed class MemoryResourceProvider : IResourceProvider
{
    private readonly ConcurrentDictionary<string, byte[]> entries;
    private int disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="MemoryResourceProvider"/> class.
    /// </summary>
    /// <param name="comparer">
    /// The key comparer used for resource lookups, or <see langword="null"/> to default to
    /// <see cref="StringComparer.OrdinalIgnoreCase"/>.
    /// </param>
    public MemoryResourceProvider(StringComparer? comparer = null)
    {
        entries = new ConcurrentDictionary<string, byte[]>(comparer ?? StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Adds or replaces an in-memory resource with a defensive copy of <paramref name="data"/>.
    /// </summary>
    /// <param name="name">The relative resource name.</param>
    /// <param name="data">The raw resource bytes to store.</param>
    public void Add(string name, ReadOnlySpan<byte> data)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);

        var normalized = ResourcePath.NormalizeRequired(name);
        var copy = data.Length == 0 ? [] : data.ToArray();
        entries[normalized] = copy;
    }

    /// <summary>
    /// Removes the resource identified by <paramref name="name"/> if it exists.
    /// </summary>
    /// <param name="name">The relative resource name.</param>
    /// <returns><see langword="true"/> if the resource was removed; otherwise, <see langword="false"/>.</returns>
    public bool Remove(string name)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);

        var normalized = ResourcePath.TryNormalize(name);

        if (normalized is null)
            return false;

        return entries.TryRemove(normalized, out _);
    }

    /// <inheritdoc />
    public Stream? GetStream(string name)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);

        var normalized = ResourcePath.TryNormalize(name);

        if (normalized is null || !entries.TryGetValue(normalized, out var data))
            return null;

        return new MemoryStream(data, writable: false);
    }

    /// <inheritdoc />
    public byte[]? GetBytes(string name)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);

        var normalized = ResourcePath.TryNormalize(name);

        if (normalized is null || !entries.TryGetValue(normalized, out var data))
            return null;

        return copyBytes(data);
    }

    /// <inheritdoc />
    public Task<byte[]?> GetBytesAsync(string name, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);

        var normalized = ResourcePath.TryNormalize(name);

        if (cancellationToken.IsCancellationRequested)
            return Task.FromCanceled<byte[]?>(cancellationToken);

        if (normalized is null || !entries.TryGetValue(normalized, out var data))
            return Task.FromResult<byte[]?>(null);

        return Task.FromResult<byte[]?>(copyBytes(data));
    }

    /// <inheritdoc />
    public bool Exists(string name)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);

        var normalized = ResourcePath.TryNormalize(name);
        return normalized is not null && entries.ContainsKey(normalized);
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

        entries.Clear();
    }

    private IEnumerable<string> enumerateAvailableResources()
    {
        foreach (var entry in entries)
            yield return entry.Key;
    }

    private static byte[] copyBytes(byte[] data) => data.Length == 0 ? [] : data.AsSpan().ToArray();
}
