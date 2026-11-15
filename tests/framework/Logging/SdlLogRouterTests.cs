using kajarlabs.Framework.Logging;
using kajarlabs.Framework.Tests.TestUtilities;
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
        using var provider = new CapturingLoggerProvider();
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

        var entries = provider.Entries.ToArray();
        Assert.Equal(3, entries.Length);

        Assert.Equal(("SDL.Application", LogLevel.Information, "sdl info test"), entries[0]);
        Assert.Equal(("SDL.Application", LogLevel.Warning, "sdl warn test"), entries[1]);
        Assert.Equal(("SDL.Application", LogLevel.Error, "sdl error test"), entries[2]);
    }
}
