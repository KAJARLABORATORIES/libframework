using SDL3;

namespace kajarlabs.Framework.IO;

/// <summary>
/// Resolves the per-user writable data directory for the application.
/// </summary>
internal static class UserDataLocation
{
    /// <summary>
    /// Resolves the user data directory for <paramref name="organization"/> and <paramref name="application"/>.
    /// When <paramref name="overridePath"/> is provided, it is created if missing and returned without querying SDL.
    /// </summary>
    public static string Resolve(string organization, string application, string? overridePath = null)
        => Resolve(organization, application, overridePath, SDL.GetPrefPath);

    internal static string Resolve(
        string organization,
        string application,
        string? overridePath,
        Func<string, string, string?> getPrefPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(organization);
        ArgumentException.ThrowIfNullOrWhiteSpace(application);
        ArgumentNullException.ThrowIfNull(getPrefPath);

        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            var resolved = Path.TrimEndingDirectorySeparator(Path.GetFullPath(overridePath));
            Directory.CreateDirectory(resolved);
            return resolved;
        }

        var prefPath = getPrefPath(organization, application);

        if (string.IsNullOrEmpty(prefPath))
            throw new InvalidOperationException($"Failed to resolve user data location for '{organization}/{application}'.");

        return Path.TrimEndingDirectorySeparator(prefPath);
    }
}
