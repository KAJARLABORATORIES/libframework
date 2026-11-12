using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace kajarlabs.Framework.Logging;

/// <summary>
/// An <see cref="ILoggerProvider"/> that enqueues log entries without blocking callers and writes them
/// to <see cref="ILogSink"/> instances on a single dedicated background thread.
/// </summary>
internal sealed class BackgroundLoggerProvider : ILoggerProvider
{
    private readonly Channel<LogMessage> channel = Channel.CreateUnbounded<LogMessage>(
        new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

    private readonly ILogSink[] sinks;
    private readonly LogLevel minimumLevel;
    private readonly Thread worker;
    private int disposed;

    public BackgroundLoggerProvider(IEnumerable<ILogSink> sinks, LogLevel minimumLevel = LogLevel.Trace)
    {
        ArgumentNullException.ThrowIfNull(sinks);

        this.sinks = [.. sinks];
        this.minimumLevel = minimumLevel;

        worker = new Thread(processQueue)
        {
            Name = "kajarlabs.Logging",
            IsBackground = true
        };
        worker.Start();
    }

    public ILogger CreateLogger(string categoryName)
    {
        ArgumentNullException.ThrowIfNull(categoryName);
        return new BackgroundLogger(this, categoryName);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
            return;

        channel.Writer.TryComplete();
        worker.Join();

        foreach (var sink in sinks)
        {
            try
            {
                sink.Flush();
            }
            catch
            {
                // Best-effort flush during shutdown.
            }

            sink.Dispose();
        }
    }

    private void enqueue(in LogMessage message)
    {
        if (Volatile.Read(ref disposed) != 0)
            return;

        channel.Writer.TryWrite(message);
    }

    private void processQueue()
    {
        var reader = channel.Reader;

        while (reader.WaitToReadAsync().AsTask().GetAwaiter().GetResult())
        {
            while (reader.TryRead(out var message))
                writeToSinks(in message);
        }
    }

    private void writeToSinks(in LogMessage message)
    {
        foreach (var sink in sinks)
        {
            try
            {
                sink.Write(in message);
            }
            catch
            {
                // Isolate sink failures so one broken sink does not drop logs for others.
            }
        }
    }

    private sealed class BackgroundLogger(BackgroundLoggerProvider owner, string categoryName) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel)
            => Volatile.Read(ref owner.disposed) == 0
               && logLevel != LogLevel.None
               && logLevel >= owner.minimumLevel;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;

            ArgumentNullException.ThrowIfNull(formatter);

            var text = formatter(state, exception);
            owner.enqueue(new LogMessage(DateTimeOffset.UtcNow, logLevel, categoryName, eventId, text, exception));
        }
    }
}
