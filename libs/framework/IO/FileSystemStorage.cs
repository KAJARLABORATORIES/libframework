namespace kajarlabs.Framework.IO;

/// <summary>
/// A <see cref="Storage"/> backed by a directory on the local file system.
/// </summary>
/// <remarks>
/// Containment is checked on path strings. Symbolic links inside the root that point elsewhere are followed.
/// </remarks>
public sealed class FileSystemStorage : Storage
{
    private readonly string root;

    /// <summary>
    /// Creates a storage rooted at <paramref name="root"/>. The directory is created on first write.
    /// </summary>
    public FileSystemStorage(string root)
    {
        ArgumentException.ThrowIfNullOrEmpty(root);

        this.root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
    }

    protected override string GetFullPathCore(string relativePath) => combine(relativePath);

    protected override bool ExistsCore(string relativePath) => File.Exists(combine(relativePath));

    protected override bool ExistsDirectoryCore(string relativePath) => Directory.Exists(combine(relativePath));

    protected override IEnumerable<string> GetFilesCore(string relativePath)
    {
        var fullPath = combine(relativePath);

        return Directory.Exists(fullPath)
            ? Directory.EnumerateFiles(fullPath).Select(toRelative)
            : [];
    }

    protected override IEnumerable<string> GetDirectoriesCore(string relativePath)
    {
        var fullPath = combine(relativePath);

        return Directory.Exists(fullPath)
            ? Directory.EnumerateDirectories(fullPath).Select(toRelative)
            : [];
    }

    protected override Stream GetStreamCore(string relativePath, FileAccess access, FileMode mode)
    {
        var fullPath = combine(relativePath);

        var createsFile = access.HasFlag(FileAccess.Write) && mode is not (FileMode.Open or FileMode.Truncate);

        if (createsFile && Path.GetDirectoryName(fullPath) is { } directory)
            Directory.CreateDirectory(directory);

        return new FileStream(fullPath, mode, access, FileShare.Read);
    }

    protected override void DeleteCore(string relativePath)
    {
        var fullPath = combine(relativePath);

        if (File.Exists(fullPath))
            File.Delete(fullPath);
    }

    protected override void DeleteDirectoryCore(string relativePath)
    {
        var fullPath = combine(relativePath);

        if (Directory.Exists(fullPath))
            Directory.Delete(fullPath, true);
    }

    protected override Storage GetStorageForDirectoryCore(string relativePath) => new FileSystemStorage(combine(relativePath));

    /// <summary>
    /// Maps a canonical relative path to a full path. The base class already guarantees containment; this
    /// checks the result again so a bug in either place cannot silently leave the root.
    /// </summary>
    private string combine(string relativePath)
    {
        var fullPath = relativePath.Length == 0 ? root : Path.GetFullPath(Path.Combine(root, relativePath));
        var escape = Path.GetRelativePath(root, fullPath);

        if (Path.IsPathRooted(escape) || escape == ".." || escape.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new ArgumentException($"Path '{relativePath}' resolves outside the storage root.", nameof(relativePath));

        return fullPath;
    }

    private string toRelative(string fullPath) => Path.GetRelativePath(root, fullPath).Replace(Path.DirectorySeparatorChar, '/');
}
