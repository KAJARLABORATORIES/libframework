using kajarlabs.Framework.Graphics;
using kajarlabs.Framework.Graphics.Rendering;
using kajarlabs.Framework.Numerics;
using kajarlabs.Framework.Platform;
using kajarlabs.Framework.Tests.TestUtilities;
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
        using var providerA = new CapturingLoggerProvider();
        using var providerB = new CapturingLoggerProvider();

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

        Assert.Contains(entriesA, e => e.Message.Contains("ALPHA-load", StringComparison.Ordinal));
        Assert.Contains(entriesA, e => e.Message.Contains("ALPHA-update-15", StringComparison.Ordinal));
        Assert.DoesNotContain(entriesA, e => e.Message.Contains("BRAVO", StringComparison.Ordinal));

        Assert.Contains(entriesB, e => e.Message.Contains("BRAVO-load", StringComparison.Ordinal));
        Assert.Contains(entriesB, e => e.Message.Contains("BRAVO-update-15", StringComparison.Ordinal));
        Assert.DoesNotContain(entriesB, e => e.Message.Contains("ALPHA", StringComparison.Ordinal));

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

        var rendererType = typeof(IRenderer);

        foreach (var prop in rendererType.GetProperties())
            Assert.NotSame(sdlAssembly, prop.PropertyType.Assembly);

        foreach (var method in rendererType.GetMethods())
        {
            Assert.NotSame(sdlAssembly, method.ReturnType.Assembly);

            foreach (var param in method.GetParameters())
                Assert.NotSame(sdlAssembly, param.ParameterType.Assembly);
        }
    }

    [Fact]
    public void ExposesNullRendererExecutesDrawAndForwardsResize()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"kajarlabs-headless-render-{Guid.NewGuid():N}");

        try
        {
            using var host = new HeadlessGameHost(new HostOptions
            {
                UserDataPath = tempPath,
                WindowWidth = 800,
                WindowHeight = 600,
                EnableConsoleLogging = false,
                EnableFileLogging = false,
            });

            Assert.IsType<NullRenderer>(host.Renderer);
            Assert.Equal(new Viewport(800, 600), host.Renderer.Viewport);

            var drawCountAtResize = 0;
            var game = new RendererProbeGame(onUpdate: (probe, frame) =>
            {
                if (frame == 2)
                {
                    host.ForwardResize(1920, 1080);
                    drawCountAtResize = probe.DrawCount;
                }

                if (frame >= 4)
                    probe.WaitForDrawAfter(drawCountAtResize);
            });

            host.RunFrames(game, 5);

            Assert.Same(host.Renderer, game.ObservedRenderer);
            Assert.IsType<NullRenderer>(game.ObservedRenderer);
            Assert.True(game.DrawCount > 0);
            Assert.Equal(new Viewport(1920, 1080), host.Renderer.Viewport);
            Assert.Equal(Color4.CYAN, ((NullRenderer)host.Renderer).LastClearColor);
            Assert.Equal(1, ((NullRenderer)host.Renderer).ResizeCount);
        }
        finally
        {
            if (Directory.Exists(tempPath))
                Directory.Delete(tempPath, recursive: true);
        }
    }

    [Fact]
    public async Task ParallelHeadlessHostsIsolateRendererState()
    {
        var tempPathA = Path.Combine(Path.GetTempPath(), $"kajarlabs-headless-iso-a-{Guid.NewGuid():N}");
        var tempPathB = Path.Combine(Path.GetTempPath(), $"kajarlabs-headless-iso-b-{Guid.NewGuid():N}");

        try
        {
            using var hostA = new HeadlessGameHost(new HostOptions
            {
                UserDataPath = tempPathA,
                WindowWidth = 640,
                WindowHeight = 360,
                EnableConsoleLogging = false,
                EnableFileLogging = false,
            });
            using var hostB = new HeadlessGameHost(new HostOptions
            {
                UserDataPath = tempPathB,
                WindowWidth = 1024,
                WindowHeight = 768,
                EnableConsoleLogging = false,
                EnableFileLogging = false,
            });

            using var gameA = new RendererColorGame(Color4.RED, onFirstUpdate: () => hostA.ForwardResize(1280, 720));
            using var gameB = new RendererColorGame(Color4.GREEN, onFirstUpdate: () => hostB.ForwardResize(1600, 900));

            var taskA = Task.Run(() => hostA.RunFrames(gameA, 10), TestContext.Current.CancellationToken);
            var taskB = Task.Run(() => hostB.RunFrames(gameB, 10), TestContext.Current.CancellationToken);

            await Task.WhenAll(taskA, taskB);

            Assert.NotSame(hostA.Renderer, hostB.Renderer);
            Assert.Equal(new Viewport(1280, 720), hostA.Renderer.Viewport);
            Assert.Equal(new Viewport(1600, 900), hostB.Renderer.Viewport);
            Assert.Equal(Color4.RED, ((NullRenderer)hostA.Renderer).LastClearColor);
            Assert.Equal(Color4.GREEN, ((NullRenderer)hostB.Renderer).LastClearColor);
        }
        finally
        {
            if (Directory.Exists(tempPathA))
                Directory.Delete(tempPathA, recursive: true);

            if (Directory.Exists(tempPathB))
                Directory.Delete(tempPathB, recursive: true);
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

    private sealed class RendererProbeGame : Game
    {
        private readonly Action<RendererProbeGame, int>? onUpdate;
        private readonly ManualResetEventSlim drawObserved = new(false);
        private int updateFrame;
        private int drawCount;

        public RendererProbeGame(Action<RendererProbeGame, int>? onUpdate = null)
        {
            this.onUpdate = onUpdate;
        }

        public IRenderer? ObservedRenderer { get; private set; }
        public int DrawCount => Volatile.Read(ref drawCount);

        public void WaitForDrawAfter(int baselineDrawCount)
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);

            while (Volatile.Read(ref drawCount) <= baselineDrawCount)
            {
                var remaining = deadline - DateTime.UtcNow;
                Assert.True(remaining > TimeSpan.Zero, "Timed out waiting for draw after resize.");
                drawObserved.Reset();

                if (Volatile.Read(ref drawCount) > baselineDrawCount)
                    return;

                drawObserved.Wait(remaining);
            }
        }

        protected override void Load() => ObservedRenderer = Renderer;

        protected override void Update()
        {
            updateFrame++;
            onUpdate?.Invoke(this, updateFrame);
        }

        protected override void Draw()
        {
            Renderer.Clear(Color4.CYAN);
            Interlocked.Increment(ref drawCount);
            drawObserved.Set();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                drawObserved.Dispose();

            base.Dispose(disposing);
        }
    }

    private sealed class RendererColorGame : Game
    {
        private readonly Color4 clearColor;
        private readonly Action? onFirstUpdate;
        private readonly ManualResetEventSlim drawObserved = new(false);
        private int updates;

        public RendererColorGame(Color4 clearColor, Action? onFirstUpdate = null)
        {
            this.clearColor = clearColor;
            this.onFirstUpdate = onFirstUpdate;
        }

        protected override void Update()
        {
            updates++;

            if (updates == 1)
                onFirstUpdate?.Invoke();

            if (updates >= 5)
                Assert.True(drawObserved.Wait(TimeSpan.FromSeconds(5)));
        }

        protected override void Draw()
        {
            Renderer.Clear(clearColor);
            drawObserved.Set();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                drawObserved.Dispose();

            base.Dispose(disposing);
        }
    }
}
