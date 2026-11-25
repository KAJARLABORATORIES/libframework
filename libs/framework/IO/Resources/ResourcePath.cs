using System.Buffers;

namespace kajarlabs.Framework.IO.Resources;

/// <summary>
/// Normalizes and validates relative resource paths, prefixes, and file extensions using the same containment
/// rules as <see cref="Storage"/>.
/// </summary>
internal static class ResourcePath
{
    private static readonly char[] separators = ['/', '\\'];
    private static readonly SearchValues<char> invalid_extension_chars = SearchValues.Create("/\\:");

    /// <summary>
    /// Normalizes a resource lookup path to canonical <c>/</c>-separated form.
    /// </summary>
    /// <param name="name">The raw resource path.</param>
    /// <param name="paramName">The parameter name for thrown argument exceptions.</param>
    /// <returns>
    /// The canonical relative path, or <see langword="null"/> if <paramref name="name"/> is empty, whitespace,
    /// or collapses to the root (e.g. <c>"."</c> or <c>"a/.."</c>).
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="name"/> is rooted, contains <c>:</c>, or escapes above the root via <c>..</c>.
    /// </exception>
    public static string? TryNormalize(string name, string paramName = "name")
    {
        ArgumentNullException.ThrowIfNull(name, paramName);

        if (string.IsNullOrWhiteSpace(name))
            return null;

        if (name[0] is '/' or '\\')
            throw new ArgumentException($"Path '{name}' is rooted; resource paths must be relative.", paramName);

        if (isCanonical(name.AsSpan()))
            return name;

        return normalizeSlow(name, paramName);
    }

    /// <summary>
    /// Normalizes a required resource path or namespace prefix, rejecting empty or root-collapsing values.
    /// </summary>
    /// <param name="path">The raw path or prefix.</param>
    /// <param name="paramName">The parameter name for thrown argument exceptions.</param>
    /// <returns>The non-empty canonical <c>/</c>-separated path.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="path"/> is empty, whitespace, collapses to root, is rooted, contains <c>:</c>, or escapes
    /// above the root via <c>..</c>.
    /// </exception>
    public static string NormalizeRequired(string path, string paramName = "name")
        => TryNormalize(path, paramName)
           ?? throw new ArgumentException("Resource path cannot be empty or resolve to the root.", paramName);

    /// <summary>
    /// Normalizes a file extension to begin with <c>.</c> and validates that it cannot introduce path traversal.
    /// </summary>
    /// <param name="extension">The raw extension (for example, <c>"png"</c> or <c>".png"</c>).</param>
    /// <param name="paramName">The parameter name for thrown argument exceptions.</param>
    /// <returns>The normalized extension prefixed with <c>.</c>.</returns>
    public static string NormalizeExtension(string extension, string paramName = "extension")
    {
        ArgumentNullException.ThrowIfNull(extension, paramName);

        if (string.IsNullOrWhiteSpace(extension))
            throw new ArgumentException("Extension cannot be empty or whitespace.", paramName);

        if (extension.AsSpan().IndexOfAny(invalid_extension_chars) >= 0)
            throw new ArgumentException($"Extension '{extension}' cannot contain path separators or ':'.", paramName);

        var normalized = extension.StartsWith('.') ? extension : string.Concat(".", extension);

        if (normalized.Length == 1 || normalized.AsSpan(1).IsWhiteSpace())
            throw new ArgumentException($"Extension '{extension}' must contain characters after '.'.", paramName);

        return normalized;
    }

    private static bool isCanonical(ReadOnlySpan<char> span)
    {
        if (span.Length == 0 || span[^1] == '/')
            return false;

        var segmentStart = 0;

        for (var i = 0; i < span.Length; i++)
        {
            var c = span[i];

            if (c is '\\' or ':')
                return false;

            if (c == '/')
            {
                if (!isCanonicalSegment(span[segmentStart..i]))
                    return false;

                segmentStart = i + 1;
            }
        }

        return isCanonicalSegment(span[segmentStart..]);
    }

    private static bool isCanonicalSegment(ReadOnlySpan<char> segment)
    {
        if (segment.Length == 0)
            return false;

        if (segment.Length == 1 && segment[0] == '.')
            return false;

        if (segment.Length == 2 && segment[0] == '.' && segment[1] == '.')
            return false;

        return true;
    }

    private static string? normalizeSlow(string path, string paramName)
    {
        var segments = new List<string>();

        foreach (var segment in path.Split(separators, StringSplitOptions.RemoveEmptyEntries))
        {
            // Drive letters ("C:") and NTFS alternate data streams ("file:stream") are not portable.
            if (segment.Contains(':'))
                throw new ArgumentException($"Path '{path}' contains ':', which is not allowed.", paramName);

            switch (segment)
            {
                case ".":
                    break;

                case "..":
                    if (segments.Count == 0)
                        throw new ArgumentException($"Path '{path}' resolves outside the resource root.", paramName);

                    segments.RemoveAt(segments.Count - 1);
                    break;

                default:
                    segments.Add(segment);
                    break;
            }
        }

        return segments.Count == 0 ? null : string.Join('/', segments);
    }
}
