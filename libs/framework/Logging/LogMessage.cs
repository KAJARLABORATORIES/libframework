using Microsoft.Extensions.Logging;

namespace kajarlabs.Framework.Logging;

/// <summary>
/// A formatted log entry queued for background dispatch to sinks.
/// </summary>
internal readonly record struct LogMessage(
    DateTimeOffset Timestamp,
    LogLevel Level,
    string Category,
    EventId EventId,
    string Message,
    Exception? Exception);
