namespace kajarlabs.Framework.Logging;

/// <summary>
/// A destination that writes <see cref="LogMessage"/> entries from the background logging thread.
/// </summary>
internal interface ILogSink : IDisposable
{
    void Write(in LogMessage message);

    void Flush();
}
