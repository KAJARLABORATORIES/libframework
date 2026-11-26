using System.Diagnostics.CodeAnalysis;
using kajarlabs.Framework.Graphics.Decoding;
using kajarlabs.Framework.Graphics.Rendering;
using kajarlabs.Framework.IO.Resources;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace kajarlabs.Framework.Graphics;

/// <summary>
/// Loads, decodes, uploads, and caches 2D RGBA8 textures from an <see cref="IResourceProvider"/> using an
/// <see cref="IRenderer"/>, with thread-safe concurrent <see cref="GetAsync"/> coalescing and shared lifetime
/// protection.
/// </summary>
public sealed partial class TextureCache : IDisposable
{
    private static readonly string[] default_extensions = [".png", ".jpg", ".jpeg", ".bmp", ".tga"];

    private readonly IRenderer renderer;
    private readonly IResourceProvider resources;
    private readonly IImageDecoder decoder;
    private readonly ILogger logger;
    private readonly Lock cacheLock = new();
    private readonly Dictionary<string, CacheEntry> cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Task<ITexture?>> inFlight = new(StringComparer.OrdinalIgnoreCase);
    private long cacheVersion;
    private int disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="TextureCache"/> class.
    /// </summary>
    /// <param name="renderer">The renderer used to allocate and upload GPU textures.</param>
    /// <param name="resources">
    /// The resource provider queried for encoded image streams and bytes. This cache does not take ownership of
    /// <paramref name="resources"/> and does not dispose it.
    /// </param>
    /// <param name="logger">Optional logger for reporting image decoding failures.</param>
    public TextureCache(IRenderer renderer, IResourceProvider resources, ILogger? logger = null)
        : this(renderer, resources, new StbImageDecoder(), logger)
    {
    }

    internal TextureCache(IRenderer renderer, IResourceProvider resources, IImageDecoder decoder, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(decoder);

        this.renderer = renderer;
        this.resources = resources;
        this.decoder = decoder;
        this.logger = logger ?? NullLogger.Instance;
    }

    /// <summary>
    /// Synchronously retrieves a cached texture or loads, decodes, uploads, and caches it from the underlying
    /// <see cref="IResourceProvider"/>.
    /// </summary>
    /// <param name="name">
    /// The relative resource name, with or without an image extension (<c>.png</c>, <c>.jpg</c>, <c>.jpeg</c>,
    /// <c>.bmp</c>, <c>.tga</c>).
    /// </param>
    /// <returns>
    /// A cache-owned <see cref="ITexture"/> whose <see cref="IDisposable.Dispose"/> is a safe no-op, or
    /// <see langword="null"/> if the resource is missing or fails to decode.
    /// </returns>
    public ITexture? Get(string name)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);

        var normalized = ResourcePath.TryNormalize(name);

        if (normalized is null)
            return null;

        long version;

        lock (cacheLock)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);

            if (cache.TryGetValue(normalized, out var cached))
                return cached.Texture;

            version = cacheVersion;
        }

        if (!tryFindCandidateStream(normalized, out var resolvedKey, out var stream))
            return null;

        using (stream)
        {
            if (tryAliasExisting(normalized, resolvedKey, out var existing))
                return existing;

            DecodedImage decoded;

            try
            {
                decoded = decoder.Decode(stream);
            }
            catch (InvalidDataException ex)
            {
                logDecodeFailure(logger, resolvedKey, ex);
                return null;
            }

            if (isInvalidated(version))
                return null;

            var rawTexture = createAndUploadTexture(in decoded);
            return commitLoadedTexture(normalized, resolvedKey, rawTexture, version);
        }
    }

    /// <summary>
    /// Asynchronously retrieves a cached texture or loads, decodes, uploads, and caches it, coalescing concurrent
    /// requests for the same key so decoding and GPU upload occur only once.
    /// </summary>
    /// <param name="name">
    /// The relative resource name, with or without an image extension (<c>.png</c>, <c>.jpg</c>, <c>.jpeg</c>,
    /// <c>.bmp</c>, <c>.tga</c>).
    /// </param>
    /// <param name="cancellationToken">
    /// Cancels waiting for this caller without aborting a shared in-flight load for other callers.
    /// </param>
    /// <returns>
    /// A task producing a cache-owned <see cref="ITexture"/>, or <see langword="null"/> if the resource is missing
    /// or fails to decode.
    /// </returns>
    public Task<ITexture?> GetAsync(string name, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);

        var normalized = ResourcePath.TryNormalize(name);

        if (cancellationToken.IsCancellationRequested)
            return Task.FromCanceled<ITexture?>(cancellationToken);

        if (normalized is null)
            return Task.FromResult<ITexture?>(null);

        Task<ITexture?> sharedTask;
        TaskCompletionSource<ITexture?>? ownerTcs = null;
        long version = 0;

        lock (cacheLock)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);

            if (cache.TryGetValue(normalized, out var cached))
                return Task.FromResult<ITexture?>(cached.Texture);

            if (inFlight.TryGetValue(normalized, out var existingTask))
            {
                sharedTask = existingTask;
            }
            else
            {
                ownerTcs = new TaskCompletionSource<ITexture?>(TaskCreationOptions.RunContinuationsAsynchronously);
                sharedTask = ownerTcs.Task;
                inFlight[normalized] = sharedTask;
                version = cacheVersion;
            }
        }

        if (ownerTcs is not null)
            _ = executeSharedLoadAsync(normalized, ownerTcs, version);

        return cancellationToken.CanBeCanceled ? sharedTask.WaitAsync(cancellationToken) : sharedTask;
    }

    /// <summary>
    /// Attempts to retrieve an already cached texture without triggering I/O or decoding.
    /// </summary>
    /// <param name="name">The relative resource name.</param>
    /// <param name="texture">When this method returns <see langword="true"/>, the cached texture.</param>
    /// <returns><see langword="true"/> if the texture is present in the cache; otherwise, <see langword="false"/>.</returns>
    public bool TryGetCached(string name, [NotNullWhen(true)] out ITexture? texture)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);

        var normalized = ResourcePath.TryNormalize(name);

        if (normalized is not null)
        {
            lock (cacheLock)
            {
                ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);

                if (cache.TryGetValue(normalized, out var entry))
                {
                    texture = entry.Texture;
                    return true;
                }
            }
        }

        texture = null;
        return false;
    }

    /// <summary>
    /// Removes the cached texture associated with <paramref name="name"/> (along with any extensionless or
    /// resolved-key aliases) and disposes its underlying renderer texture.
    /// </summary>
    /// <param name="name">The relative resource name.</param>
    /// <returns><see langword="true"/> if a cached texture was removed; otherwise, <see langword="false"/>.</returns>
    public bool Remove(string name)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);

        var normalized = ResourcePath.TryNormalize(name);

        if (normalized is null)
            return false;

        CachedTexture textureToRelease;

        lock (cacheLock)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);

            if (!cache.TryGetValue(normalized, out var entry))
                return false;

            for (var i = 0; i < entry.Keys.Count; i++)
                cache.Remove(entry.Keys[i]);

            textureToRelease = entry.Texture;
        }

        textureToRelease.ReleaseUnderlying();
        return true;
    }

    /// <summary>
    /// Evicts and disposes all currently cached renderer textures.
    /// </summary>
    public void Clear()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);

        List<CachedTexture>? texturesToRelease = null;

        lock (cacheLock)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);

            cacheVersion++;

            if (cache.Count > 0)
            {
                texturesToRelease = collectUniqueTexturesLocked();
                cache.Clear();
            }
        }

        if (texturesToRelease is not null)
            releaseTextures(texturesToRelease, texturesToRelease.Count - 1);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
            return;

        List<CachedTexture>? texturesToRelease = null;

        lock (cacheLock)
        {
            cacheVersion++;

            if (cache.Count > 0)
            {
                texturesToRelease = collectUniqueTexturesLocked();
                cache.Clear();
            }

            inFlight.Clear();
        }

        if (texturesToRelease is not null)
            releaseTextures(texturesToRelease, texturesToRelease.Count - 1);
    }

    private async Task executeSharedLoadAsync(string normalized, TaskCompletionSource<ITexture?> tcs, long expectedVersion)
    {
        try
        {
            var (resolvedKey, bytes) = await readCandidateBytesAsync(normalized).ConfigureAwait(false);

            if (resolvedKey is null || bytes is null)
            {
                tcs.TrySetResult(null);
                return;
            }

            if (tryAliasExisting(normalized, resolvedKey, out var existing))
            {
                tcs.TrySetResult(existing);
                return;
            }

            DecodedImage decoded;

            try
            {
                using var stream = new MemoryStream(bytes, writable: false);
                decoded = decoder.Decode(stream);
            }
            catch (InvalidDataException ex)
            {
                logDecodeFailure(logger, resolvedKey, ex);
                tcs.TrySetResult(null);
                return;
            }

            if (isInvalidated(expectedVersion))
            {
                tcs.TrySetResult(null);
                return;
            }

            var rawTexture = createAndUploadTexture(in decoded);
            var committed = commitLoadedTexture(normalized, resolvedKey, rawTexture, expectedVersion);
            tcs.TrySetResult(committed);
        }
        catch (Exception ex)
        {
            tcs.TrySetException(ex);
        }
        finally
        {
            lock (cacheLock)
            {
                if (inFlight.TryGetValue(normalized, out var current) && ReferenceEquals(current, tcs.Task))
                    inFlight.Remove(normalized);
            }
        }
    }

    private bool tryFindCandidateStream(
        string normalized,
        [NotNullWhen(true)] out string? resolvedKey,
        [NotNullWhen(true)] out Stream? stream)
    {
        var exactStream = resources.GetStream(normalized);

        if (exactStream is not null)
        {
            resolvedKey = normalized;
            stream = exactStream;
            return true;
        }

        for (var i = 0; i < default_extensions.Length; i++)
        {
            var extension = default_extensions[i];

            if (normalized.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                continue;

            var candidate = string.Concat(normalized, extension);
            var candidateStream = resources.GetStream(candidate);

            if (candidateStream is not null)
            {
                resolvedKey = candidate;
                stream = candidateStream;
                return true;
            }
        }

        resolvedKey = null;
        stream = null;
        return false;
    }

    private async Task<(string? ResolvedKey, byte[]? Bytes)> readCandidateBytesAsync(string normalized)
    {
        var exactBytes = await resources.GetBytesAsync(normalized, CancellationToken.None).ConfigureAwait(false);

        if (exactBytes is not null)
            return (normalized, exactBytes);

        for (var i = 0; i < default_extensions.Length; i++)
        {
            var extension = default_extensions[i];

            if (normalized.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                continue;

            var candidate = string.Concat(normalized, extension);
            var candidateBytes = await resources.GetBytesAsync(candidate, CancellationToken.None).ConfigureAwait(false);

            if (candidateBytes is not null)
                return (candidate, candidateBytes);
        }

        return (null, null);
    }

    private ITexture createAndUploadTexture(in DecodedImage decoded)
    {
        var rawTexture = renderer.CreateTexture(decoded.Width, decoded.Height);

        try
        {
            rawTexture.SetData(decoded.Pixels);
            return rawTexture;
        }
        catch
        {
            rawTexture.Dispose();
            throw;
        }
    }

    private bool tryAliasExisting(string requestedKey, string resolvedKey, [NotNullWhen(true)] out ITexture? existing)
    {
        lock (cacheLock)
        {
            if (Volatile.Read(ref disposed) != 0)
            {
                existing = null;
                return false;
            }

            if (cache.TryGetValue(resolvedKey, out var entry))
            {
                addAliasLocked(requestedKey, entry);
                existing = entry.Texture;
                return true;
            }

            if (cache.TryGetValue(requestedKey, out entry))
            {
                addAliasLocked(resolvedKey, entry);
                existing = entry.Texture;
                return true;
            }
        }

        existing = null;
        return false;
    }

    private bool isInvalidated(long expectedVersion)
    {
        lock (cacheLock)
            return Volatile.Read(ref disposed) != 0 || cacheVersion != expectedVersion;
    }

    private ITexture? commitLoadedTexture(string requestedKey, string resolvedKey, ITexture rawTexture, long expectedVersion)
    {
        ITexture? redundantToDispose = null;
        ITexture? result;

        lock (cacheLock)
        {
            if (Volatile.Read(ref disposed) != 0 || cacheVersion != expectedVersion)
            {
                redundantToDispose = rawTexture;
                result = null;
            }
            else if (cache.TryGetValue(resolvedKey, out var existingByResolved))
            {
                addAliasLocked(requestedKey, existingByResolved);
                redundantToDispose = rawTexture;
                result = existingByResolved.Texture;
            }
            else if (cache.TryGetValue(requestedKey, out var existingByRequested))
            {
                addAliasLocked(resolvedKey, existingByRequested);
                redundantToDispose = rawTexture;
                result = existingByRequested.Texture;
            }
            else
            {
                var cachedTexture = new CachedTexture(rawTexture);
                var entry = new CacheEntry(cachedTexture);
                addAliasLocked(resolvedKey, entry);

                if (!string.Equals(requestedKey, resolvedKey, StringComparison.OrdinalIgnoreCase))
                    addAliasLocked(requestedKey, entry);

                result = cachedTexture;
            }
        }

        redundantToDispose?.Dispose();
        return result;
    }

    private void addAliasLocked(string key, CacheEntry entry)
    {
        if (cache.TryAdd(key, entry))
            entry.Keys.Add(key);
    }

    private List<CachedTexture> collectUniqueTexturesLocked()
    {
        var seen = new HashSet<CacheEntry>();
        var result = new List<CachedTexture>();

        foreach (var entry in cache.Values)
        {
            if (seen.Add(entry))
                result.Add(entry.Texture);
        }

        return result;
    }

    private static void releaseTextures(List<CachedTexture> textures, int index)
    {
        if (index < 0)
            return;

        try
        {
            textures[index].ReleaseUnderlying();
        }
        finally
        {
            releaseTextures(textures, index - 1);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to decode texture '{ResourceName}'")]
    private static partial void logDecodeFailure(ILogger logger, string resourceName, Exception exception);

    private sealed class CacheEntry
    {
        public CacheEntry(CachedTexture texture)
        {
            Texture = texture;
        }

        public CachedTexture Texture { get; }

        public List<string> Keys { get; } = [];
    }
}
