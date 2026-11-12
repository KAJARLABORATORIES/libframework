using Microsoft.Extensions.Logging;
using SDL3;

namespace kajarlabs.Framework.Logging;

/// <summary>
/// Routes SDL log output to an <see cref="ILoggerFactory"/> and restores the previous SDL log output
/// function when disposed.
/// </summary>
internal sealed partial class SdlLogRouter : IDisposable
{
    private readonly ILoggerFactory loggerFactory;
    private readonly SDL.LogOutputFunction callback;
    private readonly SDL.LogOutputFunction? previousCallback;
    private readonly nint previousUserdata;
    private int disposed;

    public SdlLogRouter(ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);
        this.loggerFactory = loggerFactory;

        // Keep a strong reference to the delegate so GC cannot collect it while SDL holds the function pointer.
        callback = onSdlLog;

        SDL.GetLogOutputFunction(out previousCallback, out previousUserdata);
        SDL.SetLogOutputFunction(callback, nint.Zero);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
            return;

        SDL.SetLogOutputFunction(previousCallback!, previousUserdata);
    }

    internal static LogLevel MapPriority(SDL.LogPriority priority) => priority switch
    {
        SDL.LogPriority.Trace => LogLevel.Trace,
        SDL.LogPriority.Verbose => LogLevel.Trace,
        SDL.LogPriority.Debug => LogLevel.Debug,
        SDL.LogPriority.Info => LogLevel.Information,
        SDL.LogPriority.Warn => LogLevel.Warning,
        SDL.LogPriority.Error => LogLevel.Error,
        SDL.LogPriority.Critical => LogLevel.Critical,
        _ => LogLevel.Information
    };

    internal static string MapCategory(SDL.LogCategory category) => category switch
    {
        SDL.LogCategory.Application => "SDL.Application",
        SDL.LogCategory.Error => "SDL.Error",
        SDL.LogCategory.Assert => "SDL.Assert",
        SDL.LogCategory.System => "SDL.System",
        SDL.LogCategory.Audio => "SDL.Audio",
        SDL.LogCategory.Video => "SDL.Video",
        SDL.LogCategory.Render => "SDL.Render",
        SDL.LogCategory.Input => "SDL.Input",
        SDL.LogCategory.Test => "SDL.Test",
        SDL.LogCategory.GPU => "SDL.Gpu",
        _ => "SDL"
    };

    private void onSdlLog(nint userdata, SDL.LogCategory category, SDL.LogPriority priority, string message)
    {
        if (Volatile.Read(ref disposed) != 0)
            return;

        var logger = loggerFactory.CreateLogger(MapCategory(category));
        logSdlMessage(logger, MapPriority(priority), message);
    }

    [LoggerMessage(Message = "{Message}")]
    private static partial void logSdlMessage(ILogger logger, LogLevel level, string message);
}
