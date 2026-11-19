using kajarlabs.Framework.Platform;
using Microsoft.Extensions.Logging;
using SDL3;

namespace kajarlabs.Framework.Tests.Platform;

[Collection("SDL")]
public sealed class GameHostSdlTests : IDisposable
{
    private readonly string tempDir = Path.Combine(Path.GetTempPath(), $"kajarlabs-sdl-host-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(tempDir))
            Directory.Delete(tempDir, true);
    }

    [Fact]
    public void RealSdlHostInitializesDummyVideoCreatesWindowRoutesLogsAndExitsCleanly()
    {
        using var host = new DummySdlGameHost(new HostOptions
        {
            Organization = "kajarlabs",
            Application = "SdlIntegrationTest",
            UserDataPath = tempDir,
            WindowWidth = 640,
            WindowHeight = 360,
            EnableConsoleLogging = false,
            EnableFileLogging = true,
        });

        var game = new SdlLogProbeGame();
        host.Run(game);

        Assert.True(game.SawCreatedWindow);
        Assert.Equal(640, game.ObservedWidth);
        Assert.Equal(360, game.ObservedHeight);

        var logsStorage = host.Storage.GetStorageForDirectory("logs");
        var logFile = Assert.Single(logsStorage.GetFiles(""));
        var content = readAllTextShared(logsStorage.GetFullPath(logFile));

        Assert.Contains("Starting GameHost for kajarlabs/SdlIntegrationTest", content);
        Assert.Contains("SDL.Application: sdl log routed during host run", content);
        Assert.Contains("GameHost stopped", content);
    }

    [Fact]
    public void RealSdlEventWatchReceivesWindowExposeAndPixelSizeChangedAndUnregistersOnDispose()
    {
        using var videoSession = SdlPlatformBackend.INSTANCE.InitializeVideo("dummy");
        var received = new List<PlatformEvent>();

        using (var watch = SdlPlatformBackend.INSTANCE.AddEventWatch((in PlatformEvent e) => received.Add(e)))
        {
            var resizeEvent = new SDL.Event
            {
                Type = (uint)SDL.EventType.WindowPixelSizeChanged,
                Window = new SDL.WindowEvent
                {
                    Type = SDL.EventType.WindowPixelSizeChanged,
                    WindowID = 7,
                    Data1 = 1024,
                    Data2 = 768,
                },
            };
            Assert.True(SDL.PushEvent(ref resizeEvent));

            var exposeEvent = new SDL.Event
            {
                Type = (uint)SDL.EventType.WindowExposed,
                Window = new SDL.WindowEvent
                {
                    Type = SDL.EventType.WindowExposed,
                    WindowID = 7,
                    Data1 = 1,
                },
            };
            Assert.True(SDL.PushEvent(ref exposeEvent));

            var mouseEvent = new SDL.Event
            {
                Type = (uint)SDL.EventType.MouseMotion,
                Motion = new SDL.MouseMotionEvent
                {
                    Type = SDL.EventType.MouseMotion,
                    WindowID = 7,
                },
            };
            Assert.True(SDL.PushEvent(ref mouseEvent));
        }

        // After disposing watch, subsequent pushed events must not invoke the callback.
        var afterDisposeEvent = new SDL.Event
        {
            Type = (uint)SDL.EventType.WindowExposed,
            Window = new SDL.WindowEvent
            {
                Type = SDL.EventType.WindowExposed,
                WindowID = 7,
                Data1 = 1,
            },
        };
        Assert.True(SDL.PushEvent(ref afterDisposeEvent));

        while (SdlPlatformBackend.INSTANCE.PollEvent(out _))
        {
        }

        Assert.Equal(2, received.Count);
        Assert.Equal(PlatformEventType.WindowPixelSizeChanged, received[0].Type);
        Assert.Equal(7u, received[0].WindowId);
        Assert.Equal(1024, received[0].Data1);
        Assert.Equal(768, received[0].Data2);
        Assert.Equal(PlatformEventType.WindowExposed, received[1].Type);
        Assert.Equal(7u, received[1].WindowId);
        Assert.Equal(1, received[1].Data1);
    }

    private static string readAllTextShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private sealed class DummySdlGameHost : GameHost
    {
        public DummySdlGameHost(HostOptions options)
            : base(options)
        {
        }

        protected override void ConfigureSdlVideo()
            => SDL.SetHint("SDL_VIDEO_DRIVER", "dummy");
    }

    private sealed class SdlLogProbeGame : Game
    {
        public bool SawCreatedWindow { get; private set; }
        public int ObservedWidth { get; private set; }
        public int ObservedHeight { get; private set; }

        protected override void Load()
        {
            SawCreatedWindow = Window?.IsCreated == true;
            ObservedWidth = Window?.Width ?? 0;
            ObservedHeight = Window?.Height ?? 0;

            SDL.SetLogPriority(SDL.LogCategory.Application, SDL.LogPriority.Info);
            SDL.LogInfo(SDL.LogCategory.Application, "sdl log routed during host run");

            Exit();
        }
    }
}
