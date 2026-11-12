using System.Globalization;
using Microsoft.Extensions.Logging;

namespace kajarlabs.Framework.Logging;

/// <summary>
/// Formats <see cref="LogMessage"/> entries for text-based sinks.
/// </summary>
internal static class LogMessageFormatter
{
    public static void WriteTo(TextWriter writer, in LogMessage message)
    {
        var timestamp = message.Timestamp.ToLocalTime().ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);
        var level = formatLevel(message.Level);

        writer.WriteLine($"[{timestamp}] [{level}] {message.Category}: {message.Message}");

        if (message.Exception is not null)
            writer.WriteLine(message.Exception.ToString());
    }

    private static string formatLevel(LogLevel level) => level switch
    {
        LogLevel.Trace => "TRC",
        LogLevel.Debug => "DBG",
        LogLevel.Information => "INF",
        LogLevel.Warning => "WRN",
        LogLevel.Error => "ERR",
        LogLevel.Critical => "CRT",
        _ => "UNK"
    };
}
