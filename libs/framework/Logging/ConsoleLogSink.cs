namespace kajarlabs.Framework.Logging;

/// <summary>
/// An <see cref="ILogSink"/> that writes formatted log entries to a <see cref="TextWriter"/> (stdout by default).
/// </summary>
internal sealed class ConsoleLogSink(TextWriter? output = null) : ILogSink
{
    private readonly TextWriter writer = output ?? Console.Out;

    public void Write(in LogMessage message) => LogMessageFormatter.WriteTo(writer, in message);

    public void Flush() => writer.Flush();

    public void Dispose()
    {
    }
}
