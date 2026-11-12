using System.Globalization;
using Microsoft.Extensions.Logging;

namespace kajarlabs.Framework.Logging;

/// <summary>
/// An <see cref="ILogSink"/> that writes formatted log entries to a <see cref="TextWriter"/> (stdout by default).
/// </summary>
internal sealed class ConsoleLogSink(TextWriter? output = null) : ILogSink
{
    private readonly TextWriter writer = output ?? Console.Out;

    public void Write(in LogMessage message)
    {
        var timestamp = message.Timestamp.ToLocalTime().ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);
        var level = formatLevel(message.Level);

        writer.WriteLine($"[{timestamp}] [{level}] {message.Category}: {message.Message}");

        if (message.Exception is not null)
            writer.WriteLine(message.Exception.ToString());
    }

    public void Flush() => writer.Flush();

    public void Dispose()
    {
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
