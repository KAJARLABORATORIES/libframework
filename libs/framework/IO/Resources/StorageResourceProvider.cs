namespace kajarlabs.Framework.IO.Resources;

/// <summary>
/// A read-only <see cref="IResourceProvider"/> backed by a <see cref="Storage"/> instance, preserving its path
/// containment guarantees and mapping missing resources to <see langword="null"/> / <see langword="false"/>.
/// </summary>
public sealed class StorageResourceProvider : IResourceProvider
{
    private readonly Storage storage;
    private int disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="StorageResourceProvider"/> class wrapping
    /// <paramref name="storage"/>.
    /// </summary>
    /// <param name="storage">The backing storage to read resources from.</param>
    public StorageResourceProvider(Storage storage)
    {
        ArgumentNullException.ThrowIfNull(storage);
        this.storage = storage;
    }

    /// <inheritdoc />
    public Stream? GetStream(string name)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);

        var normalized = ResourcePath.TryNormalize(name);
        return normalized is null ? null : openStream(normalized);
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

        if (normalized is null)
            return Task.FromResult<byte[]?>(null);

        return getBytesCoreAsync(normalized, cancellationToken);
    }

    /// <inheritdoc />
    public bool Exists(string name)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);

        var normalized = ResourcePath.TryNormalize(name);
        return normalized is not null && storage.Exists(normalized);
    }

    /// <inheritdoc />
    public IEnumerable<string> GetAvailableResources()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        return enumerateAvailableResources();
    }

    /// <inheritdoc />
    public void Dispose() => Interlocked.Exchange(ref disposed, 1);

    private Stream? openStream(string normalized)
    {
        if (!storage.Exists(normalized))
            return null;

        try
        {
            return storage.GetStream(normalized, FileAccess.Read, FileMode.Open);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
    }

    private async Task<byte[]?> getBytesCoreAsync(string normalized, CancellationToken cancellationToken)
    {
        await using var stream = openStream(normalized);

        if (stream is null)
            return null;

        return await ResourceStreamHelper.ReadAllBytesAsync(stream, cancellationToken).ConfigureAwait(false);
    }

    private IEnumerable<string> enumerateAvailableResources()
    {
        var pendingDirectories = new Stack<string>();
        var filesInDirectory = new List<string>();
        pendingDirectories.Push(string.Empty);

        while (pendingDirectories.Count > 0)
        {
            var directory = pendingDirectories.Pop();
            filesInDirectory.Clear();

            try
            {
                foreach (var file in storage.GetFiles(directory))
                {
                    if (!string.IsNullOrEmpty(file))
                        filesInDirectory.Add(file.Contains('\\') ? file.Replace('\\', '/') : file);
                }

                foreach (var subDirectory in storage.GetDirectories(directory))
                {
                    if (!string.IsNullOrEmpty(subDirectory))
                        pendingDirectories.Push(subDirectory);
                }
            }
            catch (DirectoryNotFoundException)
            {
                continue;
            }

            for (var i = 0; i < filesInDirectory.Count; i++)
                yield return filesInDirectory[i];
        }
    }
}
