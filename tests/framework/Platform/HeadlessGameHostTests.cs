using System.Collections.Concurrent;
using kajarlabs.Framework.Platform;
using Microsoft.Extensions.Logging;
using SDL3;

namespace kajarlabs.Framework.Tests.Platform;

[Collection("SDL")]
public sealed class HeadlessGameHostTests
{
    [Fact]
    public void UsesDummyVideoDriverAndCreatesNoWindow()
    {
        using var host = new HeadlessGameHost();
        var game = new DriverProbeGame();

        host.RunFrames(game, 3);

        Assert.Null(host.Window);
        Assert.False(host.IsRunning);
        Assert.Equal("dummy", game.ObservedVideoDriver);
        Assert.Null(game.ObservedWindow);
        Assert.Equal(3, game.UpdateCount);
        Assert.Equal(3, host.CompletedFrames);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(10)]
    public void RunFramesExecutesExactFrameCountAndStops(int frameCount)
    {
        using var host = new HeadlessGameHost();
        var game = new CountingGame();

        host.RunFrames(game, frameCount);

        Assert.Equal(1, game.LoadCount);
        Assert.Equal(frameCount, game.UpdateCount);
        Assert.Equal(frameCount, host.CompletedFrames);
        Assert.True(game.ExitingCalled);
        Assert.False(host.IsRunning);
    }

    [Fact]
    public void RunFramesWithoutGameStopsAfterBoundedFrames()
    {
        using var host = new HeadlessGameHost();

        host.RunFrames(5);

        Assert.Equal(5, host.CompletedFrames);
        Assert.False(host.IsRunning);
    }

    [Fact]
    public void EarlyExitStopsBeforeFrameLimit()
    {
        using var host = new HeadlessGameHost();
        var game = new CountingGame(exitAfterUpdates: 2);

        host.RunFrames(game, 20);

        Assert.Equal(2, game.UpdateCount);
        Assert.Equal(2, host.CompletedFrames);
        Assert.False(host.IsRunning);
    }

    [Fact]
    public void SupportsSequentialRunFramesCycles()
    {
        using var host = new HeadlessGameHost();

        var firstGame = new CountingGame();
        host.RunFrames(firstGame, 4);

        var secondGame = new CountingGame();
        host.RunFrames(secondGame, 3);

        Assert.Equal(4, firstGame.UpdateCount);
        Assert.Equal(3, secondGame.UpdateCount);
        Assert.Equal(3, host.CompletedFrames);
        Assert.False(host.IsRunning);
    }

    [Fact]
    public async Task TwoHeadlessHostsRunInParallelWithIsolatedLogs()
    {
        using var hostA = new HeadlessGameHost();
        using var hostB = new HeadlessGameHost();
        using var providerA = new CapturingProvider();
        using var providerB = new CapturingProvider();

        hostA.LoggerFactory.AddProvider(providerA);
        hostB.LoggerFactory.AddProvider(providerB);

        using var barrier = new Barrier(2);
        var gameA = new ParallelLoggingGame("ALPHA", barrier);
        var gameB = new ParallelLoggingGame("BRAVO", barrier);

        var taskA = Task.Run(() => hostA.RunFrames(gameA, 15), TestContext.Current.CancellationToken);
        var taskB = Task.Run(() => hostB.RunFrames(gameB, 15), TestContext.Current.CancellationToken);

        await Task.WhenAll(taskA, taskB);

        Assert.Equal(15, gameA.UpdateCount);
        Assert.Equal(15, gameB.UpdateCount);

        var entriesA = providerA.Entries.ToArray();
        var entriesB = providerB.Entries.ToArray();

        Assert.Contains(entriesA, e => e.Contains("ALPHA-load", StringComparison.Ordinal));
        Assert.Contains(entriesA, e => e.Contains("ALPHA-update-15", StringComparison.Ordinal));
        Assert.DoesNotContain(entriesA, e => e.Contains("BRAVO", StringComparison.Ordinal));

        Assert.Contains(entriesB, e => e.Contains("BRAVO-load", StringComparison.Ordinal));
        Assert.Contains(entriesB, e => e.Contains("BRAVO-update-15", StringComparison.Ordinal));
        Assert.DoesNotContain(entriesB, e => e.Contains("ALPHA", StringComparison.Ordinal));

        var logContentA = readHostLogFile(hostA);
        var logContentB = readHostLogFile(hostB);

        Assert.Contains("ALPHA-load", logContentA);
        Assert.Contains("ALPHA-update-15", logContentA);
        Assert.DoesNotContain("BRAVO", logContentA);

        Assert.Contains("BRAVO-load", logContentB);
        Assert.Contains("BRAVO-update-15", logContentB);
        Assert.DoesNotContain("ALPHA", logContentB);
    }

    [Fact]
    public void RunFramesRejectsNegativeFrameCount()
    {
        using var host = new HeadlessGameHost();

        Assert.Throws<ArgumentOutOfRangeException>(() => host.RunFrames(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => host.RunFrames(new CountingGame(), -1));
    }

    [Fact]
    public void PublicApiDoesNotExposeAnySdlTypes()
    {
        var sdlAssembly = typeof(SDL).Assembly;
        var headlessType = typeof(HeadlessGameHost);

        foreach (var prop in headlessType.GetProperties())
            Assert.NotSame(sdlAssembly, prop.PropertyType.Assembly);

        foreach (var method in headlessType.GetMethods())
        {
            Assert.NotSame(sdlAssembly, method.ReturnType.Assembly);

            foreach (var param in method.GetParameters())
                Assert.NotSame(sdlAssembly, param.ParameterType.Assembly);
        }

        foreach (var ctor in headlessType.GetConstructors())
        {
            foreach (var param in ctor.GetParameters())
                Assert.NotSame(sdlAssembly, param.ParameterType.Assembly);
        }
    }

    private static string readHostLogFile(GameHost host)
    {
        var logsStorage = host.Storage.GetStorageForDirectory("logs");
        var logFile = Assert.Single(logsStorage.GetFiles(""));
        var fullPath = logsStorage.GetFullPath(logFile);

        using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private sealed class DriverProbeGame : Game
    {
        public string? ObservedVideoDriver { get; private set; }
        public GameWindow? ObservedWindow { get; private set; }
        public int UpdateCount { get; private set; }

        protected override void Load()
        {
            ObservedVideoDriver = SDL.GetCurrentVideoDriver();
            ObservedWindow = Window;
        }

        protected override void Update() => UpdateCount++;
    }

    private sealed class CountingGame : Game
    {
        private readonly int? exitAfterUpdates;

        public CountingGame(int? exitAfterUpdates = null)
        {
            this.exitAfterUpdates = exitAfterUpdates;
        }

        public int LoadCount { get; private set; }
        public int UpdateCount { get; private set; }
        public bool ExitingCalled { get; private set; }

        protected override void Load() => LoadCount++;

        protected override void Update()
        {
            UpdateCount++;

            if (exitAfterUpdates is int limit && UpdateCount >= limit)
                Exit();
        }

        protected override void Exiting() => ExitingCalled = true;
    }

    private sealed class ParallelLoggingGame : Game
    {
        private readonly string tag;
        private readonly Barrier barrier;

        public ParallelLoggingGame(string tag, Barrier barrier)
        {
            this.tag = tag;
            this.barrier = barrier;
        }

        public int UpdateCount { get; private set; }

        protected override void Load()
        {
            barrier.SignalAndWait(TimeSpan.FromSeconds(5));
            Logger.LogInformation("{Tag}-load", tag);
        }

        protected override void Update()
        {
            UpdateCount++;
            Logger.LogInformation("{Tag}-update-{Frame}", tag, UpdateCount);
        }
    }

    private sealed class CapturingProvider : ILoggerProvider
    {
        public ConcurrentQueue<string> Entries { get; } = new();

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(this);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(CapturingProvider owner) : ILogger
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
                owner.Entries.Enqueue(formatter(state, exception));
            }
        }
    }
}
