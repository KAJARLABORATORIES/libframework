using Microsoft.Extensions.Logging;

namespace kajarlabs.Framework.Logging;

/// <summary>
/// A lightweight, NativeAOT-compatible <see cref="ILoggerFactory"/> owned by the host.
/// </summary>
internal sealed class FrameworkLoggerFactory : ILoggerFactory
{
    private readonly Lock gate = new();
    private readonly List<ILoggerProvider> providers = [];
    private readonly Dictionary<string, MultiplexingLogger> loggers = new(StringComparer.Ordinal);
    private bool disposed;

    public void AddProvider(ILoggerProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);

            providers.Add(provider);

            foreach (var (categoryName, logger) in loggers)
                logger.AddLogger(provider.CreateLogger(categoryName));
        }
    }

    public ILogger CreateLogger(string categoryName)
    {
        ArgumentNullException.ThrowIfNull(categoryName);

        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);

            if (!loggers.TryGetValue(categoryName, out var logger))
            {
                var inner = new ILogger[providers.Count];

                for (var i = 0; i < providers.Count; i++)
                    inner[i] = providers[i].CreateLogger(categoryName);

                logger = new MultiplexingLogger(inner);
                loggers.Add(categoryName, logger);
            }

            return logger;
        }
    }

    public void Dispose()
    {
        ILoggerProvider[] snapshot;

        lock (gate)
        {
            if (disposed)
                return;

            disposed = true;
            snapshot = [.. providers];
            providers.Clear();
            loggers.Clear();
        }

        foreach (var provider in snapshot)
            provider.Dispose();
    }

    private sealed class MultiplexingLogger(ILogger[] initialLoggers) : ILogger
    {
        private volatile ILogger[] targets = initialLoggers;

        public void AddLogger(ILogger logger)
        {
            var current = targets;
            var updated = new ILogger[current.Length + 1];
            Array.Copy(current, updated, current.Length);
            updated[current.Length] = logger;
            targets = updated;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            var snapshot = targets;

            if (snapshot.Length == 0)
                return NullScope.Instance;

            if (snapshot.Length == 1)
                return snapshot[0].BeginScope(state) ?? NullScope.Instance;

            var scopes = new IDisposable?[snapshot.Length];

            for (var i = 0; i < snapshot.Length; i++)
                scopes[i] = snapshot[i].BeginScope(state);

            return new CompositeScope(scopes);
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            if (logLevel == LogLevel.None)
                return false;

            foreach (var logger in targets)
            {
                if (logger.IsEnabled(logLevel))
                    return true;
            }

            return false;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.None)
                return;

            foreach (var logger in targets)
            {
                if (logger.IsEnabled(logLevel))
                    logger.Log(logLevel, eventId, state, exception, formatter);
            }
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose()
        {
        }
    }

    private sealed class CompositeScope(IDisposable?[] scopes) : IDisposable
    {
        public void Dispose()
        {
            for (var i = scopes.Length - 1; i >= 0; i--)
                scopes[i]?.Dispose();
        }
    }
}
