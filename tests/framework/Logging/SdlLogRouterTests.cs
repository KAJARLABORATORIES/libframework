using kajarlabs.Framework.Logging;
using Microsoft.Extensions.Logging;
using SDL3;

namespace kajarlabs.Framework.Tests.Logging;

[Collection("SDL")]
public sealed class SdlLogRouterTests
{
    [Theory]
    [InlineData(SDL.LogPriority.Trace, LogLevel.Trace)]
    [InlineData(SDL.LogPriority.Verbose, LogLevel.Trace)]
    [InlineData(SDL.LogPriority.Debug, LogLevel.Debug)]
    [InlineData(SDL.LogPriority.Info, LogLevel.Information)]
    [InlineData(SDL.LogPriority.Warn, LogLevel.Warning)]
    [InlineData(SDL.LogPriority.Error, LogLevel.Error)]
    [InlineData(SDL.LogPriority.Critical, LogLevel.Critical)]
    public void MapPriorityMapsSdlPrioritiesToLogLevels(SDL.LogPriority sdlPriority, LogLevel expected)
    {
        Assert.Equal(expected, SdlLogRouter.MapPriority(sdlPriority));
    }

    [Fact]
    public void RoutesSdlLogCallsToHostLoggerAndRestoresPreviousHandlerOnDispose()
    {
        using var factory = new FrameworkLoggerFactory();
        using var provider = new CapturingProvider();
        factory.AddProvider(provider);

        using (var router = new SdlLogRouter(factory))
        {
            SDL.SetLogPriority(SDL.LogCategory.Application, SDL.LogPriority.Info);
            SDL.LogInfo(SDL.LogCategory.Application, "sdl info test");
            SDL.LogWarn(SDL.LogCategory.Application, "sdl warn test");
            SDL.LogError(SDL.LogCategory.Application, "sdl error test");
        }

        // After disposal, SDL logs must no longer reach the router's logger.
        SDL.LogInfo(SDL.LogCategory.Application, "after dispose");

        Assert.Equal(3, provider.Entries.Count);

        Assert.Equal(("SDL.Application", LogLevel.Information, "sdl info test"), provider.Entries[0]);
        Assert.Equal(("SDL.Application", LogLevel.Warning, "sdl warn test"), provider.Entries[1]);
        Assert.Equal(("SDL.Application", LogLevel.Error, "sdl error test"), provider.Entries[2]);
    }

    private sealed class CapturingProvider : ILoggerProvider
    {
        public List<(string Category, LogLevel Level, string Message)> Entries { get; } = [];

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(this, categoryName);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(CapturingProvider owner, string categoryName) : ILogger
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
                owner.Entries.Add((categoryName, logLevel, formatter(state, exception)));
            }
        }
    }
}
