using kajarlabs.Framework.IO;

namespace kajarlabs.Framework.Tests.IO;

/// <summary>
/// A <see cref="Storage"/> that only exists for tests. It is a plain subclass of the public API, which is what
/// proves that games can provide their own storage.
/// </summary>
/// <remarks>
/// Only <c>FileAccess.Read</c> + <c>FileMode.Open</c> and <c>FileAccess.Write</c> + <c>FileMode.Create</c> are supported.
/// </remarks>
internal sealed class InMemoryStorage : Storage
{
    private readonly Dictionary<string, byte[]> files;
    private readonly string prefix;

    public InMemoryStorage()
        : this(new Dictionary<string, byte[]>(StringComparer.Ordinal), string.Empty)
    {
    }

    private InMemoryStorage(Dictionary<string, byte[]> files, string prefix)
    {
        this.files = files;
        this.prefix = prefix;
    }

    /// <summary>
    /// The last relative path a public member handed to this storage's protected members.
    /// </summary>
    public string? LastRelativePath { get; private set; }

    protected override bool ExistsCore(string relativePath)
    {
        LastRelativePath = relativePath;
        return files.ContainsKey(key(relativePath));
    }

    protected override bool ExistsDirectoryCore(string relativePath)
    {
        LastRelativePath = relativePath;

        if (relativePath.Length == 0)
            return true;

        var directoryPrefix = key(relativePath) + "/";
        return files.Keys.Any(k => k.StartsWith(directoryPrefix, StringComparison.Ordinal));
    }

    protected override IEnumerable<string> GetFilesCore(string relativePath)
    {
        LastRelativePath = relativePath;

        return [.. childrenOf(relativePath)
               .Where(child => !child.Contains('/'))
               .Select(child => join(relativePath, child))];
    }

    protected override IEnumerable<string> GetDirectoriesCore(string relativePath)
    {
        LastRelativePath = relativePath;

        return [.. childrenOf(relativePath)
               .Where(child => child.Contains('/'))
               .Select(child => join(relativePath, child[..child.IndexOf('/', StringComparison.Ordinal)]))
               .Distinct(StringComparer.Ordinal)];
    }

    protected override Stream GetStreamCore(string relativePath, FileAccess access, FileMode mode)
    {
        LastRelativePath = relativePath;
        var fullKey = key(relativePath);

        if (access == FileAccess.Read && mode == FileMode.Open)
        {
            return files.TryGetValue(fullKey, out var bytes)
                ? new MemoryStream(bytes, false)
                : throw new FileNotFoundException(null, relativePath);
        }

        if (access == FileAccess.Write && mode == FileMode.Create)
            return new CommitOnDisposeStream(bytes => files[fullKey] = bytes);

        throw new NotSupportedException($"{access} + {mode} is not supported by {nameof(InMemoryStorage)}.");
    }

    protected override void DeleteCore(string relativePath)
    {
        LastRelativePath = relativePath;
        files.Remove(key(relativePath));
    }

    protected override void DeleteDirectoryCore(string relativePath)
    {
        LastRelativePath = relativePath;

        var directoryPrefix = key(relativePath) + "/";

        foreach (var k in files.Keys.Where(k => k.StartsWith(directoryPrefix, StringComparison.Ordinal)).ToList())
            files.Remove(k);
    }

    protected override Storage GetStorageForDirectoryCore(string relativePath)
    {
        LastRelativePath = relativePath;
        return new InMemoryStorage(files, key(relativePath).Length == 0 ? string.Empty : key(relativePath) + "/");
    }

    private string key(string relativePath) => prefix + relativePath;

    private static string join(string directory, string name) => directory.Length == 0 ? name : directory + "/" + name;

    /// <summary>
    /// The paths below <paramref name="relativePath"/>, relative to it.
    /// </summary>
    private IEnumerable<string> childrenOf(string relativePath)
    {
        var directoryPrefix = relativePath.Length == 0 ? prefix : key(relativePath) + "/";

        return files.Keys
                    .Where(k => k.StartsWith(directoryPrefix, StringComparison.Ordinal))
                    .Select(k => k[directoryPrefix.Length..]);
    }

    private sealed class CommitOnDisposeStream : MemoryStream
    {
        private readonly Action<byte[]> commit;

        public CommitOnDisposeStream(Action<byte[]> commit)
        {
            this.commit = commit;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                commit(ToArray());

            base.Dispose(disposing);
        }
    }
}
