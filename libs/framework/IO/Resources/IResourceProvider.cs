namespace kajarlabs.Framework.IO.Resources;

/// <summary>
/// Provides read-only access to named binary resources.
/// </summary>
/// <remarks>
/// <para>
/// Resource names use forward-slash (<c>/</c>) relative paths without rooted prefixes or <c>..</c> escapes.
/// Missing resources return <see langword="null"/> (or <see langword="false"/> from <see cref="Exists"/>)
/// rather than throwing <see cref="FileNotFoundException"/>.
/// </para>
/// <para>
/// Invoking lookup operations after <see cref="IDisposable.Dispose"/> throws <see cref="ObjectDisposedException"/>.
/// </para>
/// </remarks>
public interface IResourceProvider : IDisposable
{
    /// <summary>
    /// Opens a read-only stream for the resource identified by <paramref name="name"/>, or returns
    /// <see langword="null"/> if the resource does not exist.
    /// </summary>
    /// <param name="name">The relative resource name.</param>
    /// <returns>
    /// A readable <see cref="Stream"/> owned by the caller, or <see langword="null"/> if the resource was not found.
    /// </returns>
    Stream? GetStream(string name);

    /// <summary>
    /// Reads all bytes of the resource identified by <paramref name="name"/>, or returns <see langword="null"/>
    /// if the resource does not exist.
    /// </summary>
    /// <param name="name">The relative resource name.</param>
    /// <returns>A byte array containing the resource contents, or <see langword="null"/> if not found.</returns>
    byte[]? GetBytes(string name);

    /// <summary>
    /// Asynchronously reads all bytes of the resource identified by <paramref name="name"/>, or returns
    /// <see langword="null"/> if the resource does not exist.
    /// </summary>
    /// <param name="name">The relative resource name.</param>
    /// <param name="cancellationToken">A token that cancels the asynchronous read operation.</param>
    /// <returns>
    /// A task producing a byte array containing the resource contents, or <see langword="null"/> if not found.
    /// </returns>
    Task<byte[]?> GetBytesAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Determines whether a resource identified by <paramref name="name"/> exists in this provider.
    /// </summary>
    /// <param name="name">The relative resource name.</param>
    /// <returns><see langword="true"/> if the resource exists; otherwise, <see langword="false"/>.</returns>
    bool Exists(string name);

    /// <summary>
    /// Enumerates the canonical <c>/</c>-separated names of resources available from this provider.
    /// </summary>
    /// <returns>A sequence of available resource names.</returns>
    IEnumerable<string> GetAvailableResources();
}
