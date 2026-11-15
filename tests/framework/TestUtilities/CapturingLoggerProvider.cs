using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace kajarlabs.Framework.Tests.TestUtilities;

/// <summary>
/// Thread-safe <see cref="ILoggerProvider"/> that records log entries emitted during tests.
/// </summary>
internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    public ConcurrentQueue<(string Category, LogLevel Level, string Message)> Entries { get; } = new();

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(this, categoryName);

    public void Dispose()
    {
    }

    private sealed class CapturingLogger(CapturingLoggerProvider owner, string categoryName) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            owner.Entries.Enqueue((categoryName, logLevel, formatter(state, exception)));
        }
    }
}
