namespace kajarlabs.Framework.IO;

/// <summary>
/// A rooted location that files and directories can be read from and written to.
/// </summary>
/// <remarks>
/// <para>
/// Every path handed to a public member is resolved against the storage root first. Paths that are rooted
/// or that climb above the root (<c>..</c> segments) are rejected with an <see cref="ArgumentException"/>
/// before any implementation code runs, so subclasses inherit that guarantee.
/// </para>
/// <para>
/// Implementations receive a canonical relative path in the protected <c>*Core</c> members: separated by
/// <c>/</c>, with no empty, <c>.</c> or <c>..</c> segments. The empty string denotes the root itself.
/// </para>
/// <para>
/// The abstract members form a frozen core. Members added in the future must be virtual with a default
/// implementation so that existing subclasses keep working.
/// </para>
/// </remarks>
public abstract class Storage
{
    private static readonly char[] separators = ['/', '\\'];

    protected Storage()
    {
    }

    /// <summary>
    /// Gets the full path on the underlying medium for <paramref name="path"/>.
    /// </summary>
    /// <exception cref="NotSupportedException">The storage is not backed by a file system.</exception>
    public string GetFullPath(string path) => GetFullPathCore(resolve(path));

    /// <summary>
    /// Whether a file exists at <paramref name="path"/>.
    /// </summary>
    public bool Exists(string path) => ExistsCore(resolve(path));

    /// <summary>
    /// Whether a directory exists at <paramref name="path"/>.
    /// </summary>
    public bool ExistsDirectory(string path) => ExistsDirectoryCore(resolve(path));

    /// <summary>
    /// Gets the files directly inside <paramref name="path"/>, as paths relative to the storage root.
    /// Returns nothing if the directory does not exist.
    /// </summary>
    public IEnumerable<string> GetFiles(string path) => GetFilesCore(resolve(path));

    /// <summary>
    /// Gets the directories directly inside <paramref name="path"/>, as paths relative to the storage root.
    /// Returns nothing if the directory does not exist.
    /// </summary>
    public IEnumerable<string> GetDirectories(string path) => GetDirectoriesCore(resolve(path));

    /// <summary>
    /// Opens a stream over the file at <paramref name="path"/>. The caller owns the stream and must dispose it.
    /// </summary>
    public Stream GetStream(string path, FileAccess access = FileAccess.Read, FileMode mode = FileMode.Open)
        => GetStreamCore(resolve(path), access, mode);

    /// <summary>
    /// Deletes the file at <paramref name="path"/>. Does nothing if it does not exist.
    /// </summary>
    public void Delete(string path) => DeleteCore(resolve(path));

    /// <summary>
    /// Deletes the directory at <paramref name="path"/> and everything inside it. Does nothing if it does not
    /// exist. The storage root itself cannot be deleted.
    /// </summary>
    public void DeleteDirectory(string path)
    {
        var relativePath = resolve(path);

        if (relativePath.Length == 0)
            throw new ArgumentException("The storage root cannot be deleted.", nameof(path));

        DeleteDirectoryCore(relativePath);
    }

    /// <summary>
    /// Gets a storage rooted at the directory <paramref name="path"/>. Containment is enforced relative to the
    /// new root.
    /// </summary>
    public Storage GetStorageForDirectory(string path) => GetStorageForDirectoryCore(resolve(path));

    protected virtual string GetFullPathCore(string relativePath)
        => throw new NotSupportedException($"{GetType().Name} is not backed by a file system.");

    protected abstract bool ExistsCore(string relativePath);

    protected abstract bool ExistsDirectoryCore(string relativePath);

    protected abstract IEnumerable<string> GetFilesCore(string relativePath);

    protected abstract IEnumerable<string> GetDirectoriesCore(string relativePath);

    protected abstract Stream GetStreamCore(string relativePath, FileAccess access, FileMode mode);

    protected abstract void DeleteCore(string relativePath);

    protected abstract void DeleteDirectoryCore(string relativePath);

    protected abstract Storage GetStorageForDirectoryCore(string relativePath);

    /// <summary>
    /// Turns <paramref name="path"/> into a canonical path relative to the root, or throws if it escapes the root.
    /// Works on the string only, so it behaves the same on every operating system and never touches the disk.
    /// </summary>
    private static string resolve(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        if (path.Length > 0 && path[0] is '/' or '\\')
            throw new ArgumentException($"Path '{path}' is rooted; storage paths must be relative.", nameof(path));

        var segments = new List<string>();

        foreach (var segment in path.Split(separators, StringSplitOptions.RemoveEmptyEntries))
        {
            // Drive letters ("C:") and NTFS alternate data streams ("file:stream") are not portable.
            if (segment.Contains(':'))
                throw new ArgumentException($"Path '{path}' contains ':', which is not allowed.", nameof(path));

            switch (segment)
            {
                case ".":
                    break;

                case "..":
                    if (segments.Count == 0)
                        throw new ArgumentException($"Path '{path}' resolves outside the storage root.", nameof(path));

                    segments.RemoveAt(segments.Count - 1);
                    break;

                default:
                    segments.Add(segment);
                    break;
            }
        }

        return string.Join('/', segments);
    }
}
