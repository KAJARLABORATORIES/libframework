using System.Collections.Concurrent;
using kajarlabs.Framework.IO;
using kajarlabs.Framework.Platform;
using Microsoft.Extensions.Logging;
using SDL3;

namespace kajarlabs.Framework.Tests.Platform;

public sealed class GameHostTests : IDisposable
{
    private readonly string tempDir = Path.Combine(Path.GetTempPath(), $"kajarlabs-host-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(tempDir))
            Directory.Delete(tempDir, true);
    }

    [Fact]
    public void HostCreatesStorageAndFileLogsUsingUserDataPathOverride()
    {
        var overridePath = Path.Combine(tempDir, "custom-user-data");
        var backend = new FakePlatformBackend();

        using var host = new GameHost(new HostOptions
        {
            Organization = "Acme",
            Application = "TestGame",
            UserDataPath = overridePath,
            EnableConsoleLogging = false,
            EnableFileLogging = true,
        }, backend);

        Assert.IsType<FileSystemStorage>(host.Storage);
        Assert.Equal(Path.GetFullPath(overridePath), host.Storage.GetFullPath(""));
        Assert.True(Directory.Exists(overridePath));

        host.Run(new ImmediateExitGame());

        var logsStorage = host.Storage.GetStorageForDirectory("logs");
        var logFiles = logsStorage.GetFiles("").ToList();
        var logFile = Assert.Single(logFiles);

        var logContent = readAllTextShared(logsStorage.GetFullPath(logFile));
        Assert.Contains("Starting GameHost for Acme/TestGame", logContent);
        Assert.Contains("GameHost stopped", logContent);
    }

    [Fact]
    public void InstallsLogRouterBeforeInitializingVideoAndShutsDownInReverseOrder()
    {
        var backend = new FakePlatformBackend
        {
            OnInitializeVideo = router => router?.EmitLog("SDL.Video", LogLevel.Information, "dummy sdl init log"),
        };

        using var host = new GameHost(createTestOptions(), backend);
        using var provider = new CapturingProvider();
        host.LoggerFactory.AddProvider(provider);

        host.Run(new ImmediateExitGame());

        Assert.Equal(
            [
                "InstallLogRouter",
                "InitializeVideo",
                "CreateWindow",
                "DestroyWindow",
                "ShutdownVideo",
                "DisposeLogRouter",
            ],
            backend.LifecycleCalls);

        Assert.Contains(
            provider.Entries,
            e => e.Category == "SDL.Video" && e.Level == LogLevel.Information && e.Message == "dummy sdl init log");
    }

    [Fact]
    public void WindowCreationEventPumpingAndDestructionRunOnMainInputThread()
    {
        var callerThreadId = Environment.CurrentManagedThreadId;
        var backend = new FakePlatformBackend();
        var game = new RecordingGame(exitAfterUpdates: 3);

        using var host = new GameHost(createTestOptions(), backend);
        host.Run(game);

        Assert.Equal(callerThreadId, backend.CreateWindowThreadId);
        Assert.Equal(callerThreadId, backend.DestroyWindowThreadId);
        Assert.NotEmpty(backend.PollEventThreadIds);
        Assert.All(backend.PollEventThreadIds, id => Assert.Equal(callerThreadId, id));

        Assert.NotEqual(callerThreadId, game.UpdateThreadId);
        Assert.NotEqual(callerThreadId, game.DrawThreadId);
        Assert.NotEqual(game.UpdateThreadId, game.DrawThreadId);

        Assert.True(game.Loaded);
        Assert.True(game.ExitingCalled);
        Assert.True(game.UpdateCount >= 3);
        Assert.False(host.IsRunning);
        Assert.False(host.Window!.IsCreated);
    }

    [Fact]
    public void GameReceivesHostStorageAndLogger()
    {
        var backend = new FakePlatformBackend();
        var game = new LoggingProbeGame();

        using var host = new GameHost(createTestOptions(), backend);
        using var provider = new CapturingProvider();
        host.LoggerFactory.AddProvider(provider);

        host.Run(game);

        Assert.Same(host, game.Host);
        Assert.Same(host.Storage, game.Storage);
        Assert.Same(host.LoggerFactory, game.LoggerFactory);
        Assert.True(game.Storage.Exists("probe.txt"));
        Assert.Contains(provider.Entries, e => e.Message == "hello from game");
    }

    [Fact]
    public void WindowCloseRequestedEventStopsHostAndRaisesWindowEvent()
    {
        var backend = new FakePlatformBackend();
        using var host = new GameHost(createTestOptions(), backend);

        var closeEventCount = 0;
        var windowConfigured = false;
        var game = new CallbackGame(onLoad: g =>
        {
            g.Window!.CloseRequested += () => Interlocked.Increment(ref closeEventCount);
            windowConfigured = true;
            backend.EnqueueEvent(new PlatformEvent(PlatformEventType.WindowCloseRequested, backend.CreatedWindowId));
        });

        host.Run(game);

        Assert.True(windowConfigured);
        Assert.Equal(1, closeEventCount);
        Assert.False(host.IsRunning);
    }

    [Fact]
    public void QuitEventStopsHostCleanly()
    {
        var backend = new FakePlatformBackend();
        using var host = new GameHost(createTestOptions(), backend);

        var game = new CallbackGame(onLoad: _ =>
            backend.EnqueueEvent(new PlatformEvent(PlatformEventType.Quit)));

        host.Run(game);

        Assert.False(host.IsRunning);
    }

    [Fact]
    public void WindowResizedEventUpdatesDimensionsAndIgnoresForeignWindowIds()
    {
        var backend = new FakePlatformBackend();
        using var host = new GameHost(createTestOptions(), backend);

        var resizedEvents = new List<(int Width, int Height)>();
        var game = new CallbackGame(onLoad: g =>
        {
            g.Window!.Resized += (w, h) => resizedEvents.Add((w, h));
            backend.EnqueueEvent(new PlatformEvent(PlatformEventType.WindowResized, backend.CreatedWindowId + 999, 100, 100));
            backend.EnqueueEvent(new PlatformEvent(PlatformEventType.WindowResized, backend.CreatedWindowId, 1920, 1080));
            backend.EnqueueEvent(new PlatformEvent(PlatformEventType.Quit));
        });

        host.Run(game);

        Assert.Equal([(1920, 1080)], resizedEvents);
        Assert.Equal(1920, host.Window!.Width);
        Assert.Equal(1080, host.Window.Height);
    }

    [Fact]
    public void WindowMutationsFromOtherThreadAreMarshaledToInputThread()
    {
        var callerThreadId = Environment.CurrentManagedThreadId;
        var backend = new FakePlatformBackend();
        using var host = new GameHost(createTestOptions(), backend);

        var game = new CallbackGame(
            onLoad: g =>
            {
                g.Window!.Title = "Updated Title";
                g.Window.SetSize(800, 600);
            },
            onUpdate: g =>
            {
                if (!backend.TitleUpdates.IsEmpty && !backend.SizeUpdates.IsEmpty)
                    g.Exit();
            });

        host.Run(game);

        var (actualTitle, titleThreadId) = Assert.Single(backend.TitleUpdates);
        var (actualWidth, actualHeight, sizeThreadId) = Assert.Single(backend.SizeUpdates);

        Assert.Equal("Updated Title", actualTitle);
        Assert.Equal(callerThreadId, titleThreadId);
        Assert.Equal((800, 600), (actualWidth, actualHeight));
        Assert.Equal(callerThreadId, sizeThreadId);
    }

    [Fact]
    public async Task ExitFromExternalThreadIsIdempotentAndStopsHost()
    {
        var backend = new FakePlatformBackend();
        using var host = new GameHost(createTestOptions(), backend);
        using var started = new ManualResetEventSlim(false);

        var game = new CallbackGame(onLoad: _ => started.Set());

        var runTask = Task.Run(() => host.Run(game), TestContext.Current.CancellationToken);

        Assert.True(started.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));

        host.Exit();
        host.Exit();

        await runTask;

        Assert.False(host.IsRunning);
        Assert.Equal(["InstallLogRouter", "InitializeVideo", "CreateWindow", "DestroyWindow", "ShutdownVideo", "DisposeLogRouter"], backend.LifecycleCalls);
    }

    [Fact]
    public void CreateWindowFailureRollsBackVideoAndLogRouter()
    {
        var backend = new FakePlatformBackend { ThrowOnCreateWindow = true };
        using var host = new GameHost(createTestOptions(), backend);

        Assert.Throws<InvalidOperationException>(() => host.Run());

        Assert.False(host.IsRunning);
        Assert.Equal(["InstallLogRouter", "InitializeVideo", "ShutdownVideo", "DisposeLogRouter"], backend.LifecycleCalls);
    }

    [Fact]
    public void UnhandledExceptionOnUpdateThreadCleansUpAndPropagates()
    {
        var backend = new FakePlatformBackend();
        using var host = new GameHost(createTestOptions(), backend);

        var game = new CallbackGame(onUpdate: _ => throw new InvalidOperationException("boom"));

        var ex = Assert.Throws<InvalidOperationException>(() => host.Run(game));

        Assert.Equal("boom", ex.Message);
        Assert.False(host.IsRunning);
        Assert.Equal(["InstallLogRouter", "InitializeVideo", "CreateWindow", "DestroyWindow", "ShutdownVideo", "DisposeLogRouter"], backend.LifecycleCalls);
    }

    [Fact]
    public void PublicApiDoesNotExposeAnySdlTypes()
    {
        var sdlAssembly = typeof(SDL).Assembly;
        var frameworkAssembly = typeof(GameHost).Assembly;

        foreach (var type in frameworkAssembly.GetExportedTypes())
        {
            if (type.Namespace is not ("kajarlabs.Framework" or "kajarlabs.Framework.Platform"))
                continue;

            foreach (var prop in type.GetProperties())
                Assert.NotSame(sdlAssembly, prop.PropertyType.Assembly);

            foreach (var method in type.GetMethods())
            {
                Assert.NotSame(sdlAssembly, method.ReturnType.Assembly);

                foreach (var param in method.GetParameters())
                    Assert.NotSame(sdlAssembly, param.ParameterType.Assembly);
            }

            foreach (var ctor in type.GetConstructors())
            {
                foreach (var param in ctor.GetParameters())
                    Assert.NotSame(sdlAssembly, param.ParameterType.Assembly);
            }
        }
    }

    private static string readAllTextShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private HostOptions createTestOptions() => new()
    {
        Organization = "kajarlabs",
        Application = "HostTests",
        UserDataPath = tempDir,
        EnableConsoleLogging = false,
        EnableFileLogging = true,
    };

    private sealed class ImmediateExitGame : Game
    {
        protected override void Load() => Exit();
    }

    private sealed class RecordingGame(int exitAfterUpdates) : Game
    {
        public bool Loaded { get; private set; }
        public bool ExitingCalled { get; private set; }
        public int UpdateThreadId { get; private set; }
        public int DrawThreadId { get; private set; }
        public int UpdateCount { get; private set; }

        protected override void Load() => Loaded = true;

        protected override void Update()
        {
            UpdateThreadId = Environment.CurrentManagedThreadId;
            UpdateCount++;

            if (UpdateCount >= exitAfterUpdates && DrawThreadId != 0)
                Exit();
        }

        protected override void Draw() => DrawThreadId = Environment.CurrentManagedThreadId;

        protected override void Exiting() => ExitingCalled = true;
    }

    private sealed class LoggingProbeGame : Game
    {
        protected override void Load()
        {
            using (var stream = Storage.GetStream("probe.txt", FileAccess.Write, FileMode.Create))
            {
                stream.Write("ok"u8);
            }

            Logger.LogInformation("hello from game");
            Exit();
        }
    }

    private sealed class CallbackGame : Game
    {
        private readonly Action<Game>? onLoad;
        private readonly Action<Game>? onUpdate;

        public CallbackGame(Action<Game>? onLoad = null, Action<Game>? onUpdate = null)
        {
            this.onLoad = onLoad;
            this.onUpdate = onUpdate;
        }

        protected override void Load() => onLoad?.Invoke(this);

        protected override void Update() => onUpdate?.Invoke(this);
    }

    private sealed class FakePlatformBackend : IPlatformBackend
    {
        private readonly ConcurrentQueue<PlatformEvent> events = new();
        private FakeLogRouter? activeRouter;

        public List<string> LifecycleCalls { get; } = [];
        public ConcurrentQueue<int> PollEventThreadIds { get; } = new();
        public ConcurrentQueue<(string Title, int ThreadId)> TitleUpdates { get; } = new();
        public ConcurrentQueue<(int Width, int Height, int ThreadId)> SizeUpdates { get; } = new();

        public int CreateWindowThreadId { get; private set; }
        public int DestroyWindowThreadId { get; private set; }
        public uint CreatedWindowId { get; private set; } = 42;
        public bool ThrowOnCreateWindow { get; set; }
        public Action<FakeLogRouter?>? OnInitializeVideo { get; set; }

        public void EnqueueEvent(PlatformEvent platformEvent) => events.Enqueue(platformEvent);

        public IDisposable InstallLogRouter(ILoggerFactory loggerFactory)
        {
            LifecycleCalls.Add("InstallLogRouter");
            activeRouter = new FakeLogRouter(this, loggerFactory);
            return activeRouter;
        }

        public IDisposable InitializeVideo()
        {
            LifecycleCalls.Add("InitializeVideo");
            OnInitializeVideo?.Invoke(activeRouter);
            return new VideoToken(this);
        }

        public nint CreateWindow(string title, int width, int height, bool resizable)
        {
            CreateWindowThreadId = Environment.CurrentManagedThreadId;

            if (ThrowOnCreateWindow)
                throw new InvalidOperationException("Simulated CreateWindow failure.");

            LifecycleCalls.Add("CreateWindow");
            return new nint(100);
        }

        public void DestroyWindow(nint handle)
        {
            DestroyWindowThreadId = Environment.CurrentManagedThreadId;
            LifecycleCalls.Add("DestroyWindow");
        }

        public uint GetWindowId(nint handle) => CreatedWindowId;

        public void SetWindowTitle(nint handle, string title)
            => TitleUpdates.Enqueue((title, Environment.CurrentManagedThreadId));

        public void GetWindowSize(nint handle, out int width, out int height)
        {
            width = 1280;
            height = 720;
        }

        public void SetWindowSize(nint handle, int width, int height)
            => SizeUpdates.Enqueue((width, height, Environment.CurrentManagedThreadId));

        public bool PollEvent(out PlatformEvent platformEvent)
        {
            PollEventThreadIds.Enqueue(Environment.CurrentManagedThreadId);
            return events.TryDequeue(out platformEvent);
        }

        public sealed class FakeLogRouter(FakePlatformBackend owner, ILoggerFactory loggerFactory) : IDisposable
        {
            public void EmitLog(string category, LogLevel level, string message)
                => loggerFactory.CreateLogger(category).Log(level, "{Message}", message);

            public void Dispose()
            {
                owner.activeRouter = null;
                owner.LifecycleCalls.Add("DisposeLogRouter");
            }
        }

        private sealed class VideoToken(FakePlatformBackend owner) : IDisposable
        {
            public void Dispose() => owner.LifecycleCalls.Add("ShutdownVideo");
        }
    }

    private sealed class CapturingProvider : ILoggerProvider
    {
        public ConcurrentQueue<(string Category, LogLevel Level, string Message)> Entries { get; } = new();

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
                owner.Entries.Enqueue((categoryName, logLevel, formatter(state, exception)));
            }
        }
    }
}
