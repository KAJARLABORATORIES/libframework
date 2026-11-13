using Microsoft.Extensions.Logging;

namespace kajarlabs.Framework.Platform;

/// <summary>
/// Configuration options used to initialize and run a <see cref="GameHost"/>.
/// </summary>
public sealed class HostOptions
{
    /// <summary>
    /// The default organization name used when resolving user data storage.
    /// </summary>
    public const string DEFAULT_ORGANIZATION = "kajarlabs";

    /// <summary>
    /// The default application name used when resolving user data storage and the default window title.
    /// </summary>
    public const string DEFAULT_APPLICATION = "kajarlabs.Framework";

    /// <summary>
    /// The default initial window width in screen coordinates.
    /// </summary>
    public const int DEFAULT_WINDOW_WIDTH = 1280;

    /// <summary>
    /// The default initial window height in screen coordinates.
    /// </summary>
    public const int DEFAULT_WINDOW_HEIGHT = 720;

    /// <summary>
    /// The default maximum number of session log files retained in user data storage.
    /// </summary>
    public const int DEFAULT_MAX_RETAINED_LOG_FILES = 10;

    /// <summary>
    /// Gets the organization name used when resolving the default user data location.
    /// </summary>
    public string Organization { get; init; } = DEFAULT_ORGANIZATION;

    /// <summary>
    /// Gets the application name used when resolving the default user data location and default window title.
    /// </summary>
    public string Application { get; init; } = DEFAULT_APPLICATION;

    /// <summary>
    /// Gets an optional override path for user data storage. When set, it is used instead of the platform default location.
    /// </summary>
    public string? UserDataPath { get; init; }

    /// <summary>
    /// Gets the initial window title. When <see langword="null"/> or whitespace, <see cref="Application"/> is used.
    /// </summary>
    public string? WindowTitle { get; init; }

    /// <summary>
    /// Gets the initial window width in screen coordinates.
    /// </summary>
    public int WindowWidth { get; init; } = DEFAULT_WINDOW_WIDTH;

    /// <summary>
    /// Gets the initial window height in screen coordinates.
    /// </summary>
    public int WindowHeight { get; init; } = DEFAULT_WINDOW_HEIGHT;

    /// <summary>
    /// Gets a value indicating whether the window can be resized by the user.
    /// </summary>
    public bool Resizable { get; init; } = true;

    /// <summary>
    /// Gets the minimum log level recorded by the host logger providers.
    /// </summary>
    public LogLevel MinimumLogLevel { get; init; } = LogLevel.Information;

    /// <summary>
    /// Gets the maximum number of session log files retained in the logs directory.
    /// </summary>
    public int MaxRetainedLogFiles { get; init; } = DEFAULT_MAX_RETAINED_LOG_FILES;

    /// <summary>
    /// Gets a value indicating whether log messages are written to standard output.
    /// </summary>
    public bool EnableConsoleLogging { get; init; } = true;

    /// <summary>
    /// Gets a value indicating whether log messages are written to a session file in user data storage.
    /// </summary>
    public bool EnableFileLogging { get; init; } = true;

    internal void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Organization);
        ArgumentException.ThrowIfNullOrWhiteSpace(Application);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(WindowWidth, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(WindowHeight, 0);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxRetainedLogFiles, 1);
    }

    internal string ResolveWindowTitle()
        => string.IsNullOrWhiteSpace(WindowTitle) ? Application : WindowTitle;
}
