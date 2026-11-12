using kajarlabs.Framework.Logging;
using Microsoft.Extensions.Logging;

namespace kajarlabs.Framework.Tests.Logging;

public sealed partial class FrameworkLoggerFactoryTests
{
    [Fact]
    public void LogsReachRegisteredProvidersWithExpectedCategoryAndPayload()
    {
        using var factory = new FrameworkLoggerFactory();
        using var providerA = new RecordingProvider();
        using var providerB = new RecordingProvider();

        factory.AddProvider(providerA);
        var logger = factory.CreateLogger("kajarlabs.Tests.Sample");
        factory.AddProvider(providerB);

        logger.LogInformation(new EventId(42, "Started"), "Hello {Name}", "world");

        var entryA = Assert.Single(providerA.Entries);
        var entryB = Assert.Single(providerB.Entries);

        Assert.Equal("kajarlabs.Tests.Sample", entryA.Category);
        Assert.Equal(LogLevel.Information, entryA.Level);
        Assert.Equal(42, entryA.EventId.Id);
        Assert.Equal("Hello world", entryA.Message);

        Assert.Equal(entryA, entryB);
    }

    [Fact]
    public void TypedLoggerUsesTypeFullNameAsCategory()
    {
        using var factory = new FrameworkLoggerFactory();
        using var provider = new RecordingProvider();
        factory.AddProvider(provider);

        ILogger<SampleComponent> logger = factory.CreateLogger<SampleComponent>();
        LogSampleStarted(logger, "test-host");

        var entry = Assert.Single(provider.Entries);
        Assert.Equal("kajarlabs.Framework.Tests.Logging.FrameworkLoggerFactoryTests.SampleComponent", entry.Category);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Equal("Started test-host", entry.Message);
    }

    [Fact]
    public void IsEnabledRespectsProviderMinimumLevel()
    {
        using var factory = new FrameworkLoggerFactory();
        using var provider = new RecordingProvider(LogLevel.Warning);
        factory.AddProvider(provider);

        var logger = factory.CreateLogger("Filtered");

        Assert.False(logger.IsEnabled(LogLevel.Information));
        Assert.True(logger.IsEnabled(LogLevel.Warning));

        logger.LogInformation("ignored");
        logger.LogWarning("recorded");

        var entry = Assert.Single(provider.Entries);
        Assert.Equal("recorded", entry.Message);
    }

    [Fact]
    public void ScopesAreForwardedAndDisposedInReverseOrder()
    {
        using var factory = new FrameworkLoggerFactory();
        using var providerA = new RecordingProvider();
        using var providerB = new RecordingProvider();
        factory.AddProvider(providerA);
        factory.AddProvider(providerB);

        var logger = factory.CreateLogger("Scoped");

        using (logger.BeginScope("request-1"))
        {
            Assert.Equal(["request-1"], providerA.ActiveScopes);
            Assert.Equal(["request-1"], providerB.ActiveScopes);
        }

        Assert.Empty(providerA.ActiveScopes);
        Assert.Empty(providerB.ActiveScopes);
    }

    [Fact]
    public void DisposingFactoryDisposesOwnedProvidersAndRejectsFurtherUse()
    {
        var providerA = new RecordingProvider();
        var providerB = new RecordingProvider();
        var factory = new FrameworkLoggerFactory();

        factory.AddProvider(providerA);
        factory.AddProvider(providerB);

        factory.Dispose();
        factory.Dispose();

        Assert.Equal(1, providerA.DisposeCount);
        Assert.Equal(1, providerB.DisposeCount);
        Assert.Throws<ObjectDisposedException>(() => factory.CreateLogger("AfterDispose"));
        Assert.Throws<ObjectDisposedException>(() => factory.AddProvider(new RecordingProvider()));
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Started {HostName}")]
    private static partial void LogSampleStarted(ILogger logger, string hostName);

    private sealed class SampleComponent;

    private sealed record LogEntry(string Category, LogLevel Level, EventId EventId, string Message);

    private sealed class RecordingProvider(LogLevel minimumLevel = LogLevel.Trace) : ILoggerProvider
    {
        public List<LogEntry> Entries { get; } = [];
        public List<object> ActiveScopes { get; } = [];
        public int DisposeCount { get; private set; }

        public ILogger CreateLogger(string categoryName) => new RecordingLogger(this, categoryName, minimumLevel);

        public void Dispose() => DisposeCount++;

        private sealed class RecordingLogger(RecordingProvider owner, string categoryName, LogLevel minimumLevel) : ILogger
        {
            public IDisposable BeginScope<TState>(TState state) where TState : notnull
            {
                owner.ActiveScopes.Add(state);
                return new ScopeToken(owner.ActiveScopes, state);
            }

            public bool IsEnabled(LogLevel logLevel) => logLevel >= minimumLevel && logLevel != LogLevel.None;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                owner.Entries.Add(new LogEntry(categoryName, logLevel, eventId, formatter(state, exception)));
            }
        }

        private sealed class ScopeToken(List<object> scopes, object state) : IDisposable
        {
            public void Dispose() => scopes.Remove(state);
        }
    }
}
