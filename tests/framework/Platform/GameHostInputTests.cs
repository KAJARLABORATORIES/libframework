using System.Collections.Concurrent;
using System.Numerics;
using kajarlabs.Framework.Input;
using kajarlabs.Framework.Platform;
using Microsoft.Extensions.Logging;

namespace kajarlabs.Framework.Tests.Platform;

public sealed class GameHostInputTests : IDisposable
{
    private readonly string tempDir = Path.Combine(Path.GetTempPath(), $"kajarlabs-input-host-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(tempDir))
            Directory.Delete(tempDir, true);
    }

    [Fact]
    public void BridgesInputThreadEventsInFifoOrderToUpdateThreadBeforeGameUpdate()
    {
        var mainThreadId = Environment.CurrentManagedThreadId;
        var backend = new InputTestPlatformBackend(logicalWidth: 800, logicalHeight: 600, pixelWidth: 800, pixelHeight: 600);
        using var host = new GameHost(createTestOptions(), backend);

        var game = new RecordingInputGame(onUpdate: (g, frame) =>
        {
            if (g.ObservedEvents.Count >= 8)
                g.Exit();
        });

        backend.EnqueueEvent(new PlatformEvent(
            PlatformEventType.WindowFocusGained,
            InputTestPlatformBackend.CreatedWindowId,
            Input: PlatformInputEvent.WindowFocus(true)));
        backend.EnqueueEvent(new PlatformEvent(
            PlatformEventType.MouseMotion,
            InputTestPlatformBackend.CreatedWindowId,
            Input: PlatformInputEvent.MouseMotion(new Vector2(100f, 50f), new Vector2(10f, -5f))));
        backend.EnqueueEvent(new PlatformEvent(
            PlatformEventType.MouseButtonDown,
            InputTestPlatformBackend.CreatedWindowId,
            Input: PlatformInputEvent.MouseButtonEvent(MouseButton.Left, isDown: true, new Vector2(100f, 50f))));
        backend.EnqueueEvent(new PlatformEvent(
            PlatformEventType.KeyDown,
            InputTestPlatformBackend.CreatedWindowId,
            Input: PlatformInputEvent.KeyEvent(Key.A, isDown: true, KeyModifiers.Shift)));
        backend.EnqueueEvent(new PlatformEvent(
            PlatformEventType.TextInput,
            InputTestPlatformBackend.CreatedWindowId,
            Input: PlatformInputEvent.TextInput("A")));
        backend.EnqueueEvent(new PlatformEvent(
            PlatformEventType.MouseWheel,
            InputTestPlatformBackend.CreatedWindowId,
            Input: PlatformInputEvent.MouseWheel(new Vector2(0f, 1.5f), new Vector2(100f, 50f), isPrecise: true)));
        backend.EnqueueEvent(new PlatformEvent(
            PlatformEventType.MouseButtonUp,
            InputTestPlatformBackend.CreatedWindowId,
            Input: PlatformInputEvent.MouseButtonEvent(MouseButton.Left, isDown: false, new Vector2(100f, 50f))));
        backend.EnqueueEvent(new PlatformEvent(
            PlatformEventType.WindowMouseLeave,
            InputTestPlatformBackend.CreatedWindowId));

        host.Run(game);

        Assert.Equal(8, game.ObservedEvents.Count);
        Assert.NotEmpty(game.ProcessThreads);
        Assert.All(game.ProcessThreads, id => Assert.NotEqual(mainThreadId, id));
        Assert.True(game.ProcessedBeforeUpdate);

        Assert.Equal(
            [
                PlatformInputEventType.WindowFocusGained,
                PlatformInputEventType.MouseMotion,
                PlatformInputEventType.MouseButtonDown,
                PlatformInputEventType.KeyDown,
                PlatformInputEventType.TextInput,
                PlatformInputEventType.MouseWheel,
                PlatformInputEventType.MouseButtonUp,
                PlatformInputEventType.WindowMouseLeave,
            ],
            game.ObservedEvents.Select(e => e.Type).ToArray());

        Assert.Equal("A", game.ObservedEvents[4].Text);
        Assert.True(game.ObservedEvents[5].IsPrecise);
    }

    [Fact]
    public void ScalesPointerCoordinatesByWindowPixelRatioAndUpdatesOnPixelSizeChanged()
    {
        var backend = new InputTestPlatformBackend(logicalWidth: 800, logicalHeight: 600, pixelWidth: 1600, pixelHeight: 1200);
        using var host = new GameHost(createTestOptions(), backend);

        var pixelSizeChangedEvents = new List<(int Width, int Height)>();
        var step = 0;

        var game = new RecordingInputGame(
            onLoad: g =>
            {
                Assert.Equal(800, g.Window!.Width);
                Assert.Equal(600, g.Window.Height);
                Assert.Equal(1600, g.Window.PixelWidth);
                Assert.Equal(1200, g.Window.PixelHeight);

                g.Window.PixelSizeChanged += (pw, ph) => pixelSizeChangedEvents.Add((pw, ph));

                backend.EnqueueEvent(new PlatformEvent(
                    PlatformEventType.MouseMotion,
                    InputTestPlatformBackend.CreatedWindowId,
                    Input: PlatformInputEvent.MouseMotion(new Vector2(100f, 150f), new Vector2(5f, -3f))));
                backend.EnqueueEvent(new PlatformEvent(
                    PlatformEventType.MouseWheel,
                    InputTestPlatformBackend.CreatedWindowId,
                    Input: PlatformInputEvent.MouseWheel(new Vector2(0f, 2f), new Vector2(50f, 25f))));
            },
            onUpdate: (g, _) =>
            {
                if (step == 0 && g.ObservedEvents.Count >= 2)
                {
                    step = 1;
                    backend.EnqueueEvent(new PlatformEvent(PlatformEventType.WindowPixelSizeChanged, InputTestPlatformBackend.CreatedWindowId, 2400, 1800));
                    backend.EnqueueEvent(new PlatformEvent(
                        PlatformEventType.MouseButtonDown,
                        InputTestPlatformBackend.CreatedWindowId,
                        Input: PlatformInputEvent.MouseButtonEvent(MouseButton.Right, isDown: true, new Vector2(100f, 100f))));
                }
                else if (step == 1 && g.ObservedEvents.Count >= 3)
                {
                    g.Exit();
                }
            });

        host.Run(game);

        Assert.Equal(3, game.ObservedEvents.Count);

        // Initial 2x DPI scale
        Assert.Equal(new Vector2(200f, 300f), game.ObservedEvents[0].Position);
        Assert.Equal(new Vector2(10f, -6f), game.ObservedEvents[0].Delta);

        // Wheel pointer scaled by 2x, wheel delta unscaled
        Assert.Equal(new Vector2(100f, 50f), game.ObservedEvents[1].Position);
        Assert.Equal(new Vector2(0f, 2f), game.ObservedEvents[1].ScrollDelta);

        // Updated 3x DPI scale after WindowPixelSizeChanged
        Assert.Equal([(2400, 1800)], pixelSizeChangedEvents);
        Assert.Equal(2400, host.Window!.PixelWidth);
        Assert.Equal(1800, host.Window.PixelHeight);
        Assert.Equal(new Vector2(300f, 300f), game.ObservedEvents[2].Position);
    }

    [Fact]
    public void FiltersForeignWindowIdsWhenWindowExistsAndTracksWindowFocus()
    {
        var backend = new InputTestPlatformBackend(logicalWidth: 640, logicalHeight: 480, pixelWidth: 640, pixelHeight: 480);
        using var host = new GameHost(createTestOptions(), backend);
        var focusNotifications = new List<bool>();

        var game = new RecordingInputGame(
            onLoad: g =>
            {
                g.Window!.FocusChanged += focusNotifications.Add;

                backend.EnqueueEvent(new PlatformEvent(
                    PlatformEventType.WindowFocusGained,
                    InputTestPlatformBackend.CreatedWindowId + 10));
                backend.EnqueueEvent(new PlatformEvent(
                    PlatformEventType.KeyDown,
                    InputTestPlatformBackend.CreatedWindowId + 10,
                    Input: PlatformInputEvent.KeyEvent(Key.Escape, isDown: true)));

                backend.EnqueueEvent(new PlatformEvent(
                    PlatformEventType.WindowFocusGained,
                    InputTestPlatformBackend.CreatedWindowId));
                backend.EnqueueEvent(new PlatformEvent(
                    PlatformEventType.KeyDown,
                    InputTestPlatformBackend.CreatedWindowId,
                    Input: PlatformInputEvent.KeyEvent(Key.Enter, isDown: true)));
                backend.EnqueueEvent(new PlatformEvent(
                    PlatformEventType.WindowFocusLost,
                    InputTestPlatformBackend.CreatedWindowId));
            },
            onUpdate: (g, _) =>
            {
                if (g.ObservedEvents.Count >= 3)
                    g.Exit();
            });

        host.Run(game);

        Assert.Equal(
            [PlatformInputEventType.WindowFocusGained, PlatformInputEventType.KeyDown, PlatformInputEventType.WindowFocusLost],
            game.ObservedEvents.Select(e => e.Type).ToArray());
        Assert.Equal(Key.Enter, game.ObservedEvents[1].Key);
        Assert.Equal([true, false], focusNotifications);
        Assert.False(host.Window!.HasFocus);
    }

    [Fact]
    public void GameWindowTextInputMarshalsToInputThreadAndStopsOnDestroy()
    {
        var mainThreadId = Environment.CurrentManagedThreadId;
        var backend = new InputTestPlatformBackend(logicalWidth: 640, logicalHeight: 480, pixelWidth: 640, pixelHeight: 480);
        using var host = new GameHost(createTestOptions(), backend);
        var stage = 0;

        var game = new RecordingInputGame(
            onLoad: g =>
            {
                Assert.False(g.Window!.IsTextInputActive);
                g.Window.StartTextInput();
                g.Window.StartTextInput(); // idempotent
            },
            onUpdate: (g, _) =>
            {
                if (stage == 0 && g.Window!.IsTextInputActive)
                {
                    stage = 1;
                    g.Window.StopTextInput();
                }
                else if (stage == 1 && !g.Window!.IsTextInputActive)
                {
                    stage = 2;
                    g.Window.StartTextInput();
                }
                else if (stage == 2 && g.Window!.IsTextInputActive)
                {
                    g.Exit();
                }
            });

        host.Run(game);

        // Expect: Start (1), Stop (1), Start (1), plus automatic Stop on Destroy().
        Assert.Equal(
            [
                ("Start", mainThreadId),
                ("Stop", mainThreadId),
                ("Start", mainThreadId),
                ("Stop", mainThreadId),
            ],
            backend.TextInputCalls.ToArray());

        Assert.False(host.Window!.IsTextInputActive);
    }

    [Fact]
    public void HeadlessHostDeliversPreInjectedEventsOnFrame1AndMidUpdateInjectedEventsOnNextFrame()
    {
        using var host = new HeadlessGameHost();
        var eventsByFrame = new Dictionary<int, PlatformInputEvent[]>();

        host.InjectWindowFocus(true);
        host.InjectMouseMove(new Vector2(200f, 100f), new Vector2(5f, 10f));
        host.InjectMouseDown(MouseButton.Left, new Vector2(200f, 100f));

        var game = new RecordingInputGame(onUpdate: (g, frame) =>
        {
            eventsByFrame[frame] = [.. g.CurrentFrameInputEvents];

            if (frame == 1)
            {
                host.InjectKeyDown(Key.A, KeyModifiers.Control, repeat: false);
                host.InjectTextInput(string.Empty); // ignored
                host.InjectTextInput("ok");
                host.InjectMouseScroll(new Vector2(0f, -1f), new Vector2(200f, 100f), isPrecise: true);
                host.InjectMouseUp(MouseButton.Left);
                host.InjectKeyUp(Key.A, KeyModifiers.Control);
                host.InjectWindowMouseLeave();
                host.InjectWindowFocus(false);
            }
        });

        host.RunFrames(game, 3);

        Assert.Equal(3, eventsByFrame.Count);

        Assert.Equal(
            [PlatformInputEventType.WindowFocusGained, PlatformInputEventType.MouseMotion, PlatformInputEventType.MouseButtonDown],
            eventsByFrame[1].Select(e => e.Type).ToArray());

        Assert.Equal(
            [
                PlatformInputEventType.KeyDown,
                PlatformInputEventType.TextInput,
                PlatformInputEventType.MouseWheel,
                PlatformInputEventType.MouseButtonUp,
                PlatformInputEventType.KeyUp,
                PlatformInputEventType.WindowMouseLeave,
                PlatformInputEventType.WindowFocusLost,
            ],
            eventsByFrame[2].Select(e => e.Type).ToArray());

        Assert.Equal(Key.A, eventsByFrame[2][0].Key);
        Assert.Equal(KeyModifiers.Control, eventsByFrame[2][0].Modifiers);
        Assert.Equal("ok", eventsByFrame[2][1].Text);
        Assert.False(eventsByFrame[2][3].HasPosition);
        Assert.Empty(eventsByFrame[3]);

        Assert.Throws<ArgumentNullException>(() => host.InjectTextInput(null!));
    }

    [Fact]
    public void HeadlessHostWithoutWindowBypassesWindowIdFilteringWhenBackendProvidesEvents()
    {
        var backend = new InputTestPlatformBackend(logicalWidth: 640, logicalHeight: 480, pixelWidth: 1280, pixelHeight: 960);
        using var host = new HeadlessGameHost(createTestOptions(), ownsUserDataPath: false, useRealTimeThrottling: false, backend);

        var game = new RecordingInputGame(onUpdate: (g, _) =>
        {
            if (g.ObservedEvents.Count >= 1)
                g.Exit();
        });

        backend.EnqueueEvent(new PlatformEvent(
            PlatformEventType.MouseMotion,
            WindowId: 999,
            Input: PlatformInputEvent.MouseMotion(new Vector2(30f, 40f), new Vector2(3f, 4f))));

        host.Run(game);

        var evt = Assert.Single(game.ObservedEvents);
        Assert.Equal(new Vector2(30f, 40f), evt.Position);
        Assert.Equal(new Vector2(3f, 4f), evt.Delta);
    }

    [Fact]
    public async Task ParallelHeadlessHostsIsolateInjectedInputQueues()
    {
        using var hostA = new HeadlessGameHost();
        using var hostB = new HeadlessGameHost();

        hostA.InjectTextInput("alpha");
        hostB.InjectTextInput("bravo");

        var gameA = new RecordingInputGame();
        var gameB = new RecordingInputGame();

        var taskA = Task.Run(() => hostA.RunFrames(gameA, 2), TestContext.Current.CancellationToken);
        var taskB = Task.Run(() => hostB.RunFrames(gameB, 2), TestContext.Current.CancellationToken);

        await Task.WhenAll(taskA, taskB);

        Assert.Equal("alpha", Assert.Single(gameA.ObservedEvents).Text);
        Assert.Equal("bravo", Assert.Single(gameB.ObservedEvents).Text);
    }

    private HostOptions createTestOptions() => new()
    {
        Organization = "kajarlabs",
        Application = "HostInputTests",
        UserDataPath = tempDir,
        EnableConsoleLogging = false,
        EnableFileLogging = false,
    };

    private sealed class RecordingInputGame : Game
    {
        private readonly Action<RecordingInputGame>? onLoad;
        private readonly Action<RecordingInputGame, int>? onUpdate;
        private int updateFrame;
        private int sequence;
        private int lastProcessedSequence = -1;

        public RecordingInputGame(
            Action<RecordingInputGame>? onLoad = null,
            Action<RecordingInputGame, int>? onUpdate = null)
        {
            this.onLoad = onLoad;
            this.onUpdate = onUpdate;
        }

        public List<PlatformInputEvent> ObservedEvents { get; } = [];

        public List<int> ProcessThreads { get; } = [];

        public bool ProcessedBeforeUpdate { get; private set; } = true;

        internal override void ProcessInputEvents(IReadOnlyList<PlatformInputEvent> events)
        {
            lastProcessedSequence = ++sequence;
            ProcessThreads.Add(Environment.CurrentManagedThreadId);
            ObservedEvents.AddRange(events);
        }

        protected override void Load() => onLoad?.Invoke(this);

        protected override void Update()
        {
            updateFrame++;
            var updateSequence = ++sequence;

            if (CurrentFrameInputEvents.Count > 0 && lastProcessedSequence >= updateSequence)
                ProcessedBeforeUpdate = false;

            onUpdate?.Invoke(this, updateFrame);
        }
    }

    private sealed class InputTestPlatformBackend : IPlatformBackend
    {
        private readonly ConcurrentQueue<PlatformEvent> events = new();
        private readonly int logicalWidth;
        private readonly int logicalHeight;
        private readonly int pixelWidth;
        private readonly int pixelHeight;

        public InputTestPlatformBackend(int logicalWidth, int logicalHeight, int pixelWidth, int pixelHeight)
        {
            this.logicalWidth = logicalWidth;
            this.logicalHeight = logicalHeight;
            this.pixelWidth = pixelWidth;
            this.pixelHeight = pixelHeight;
        }

        public static uint CreatedWindowId => 77;

        public ConcurrentQueue<(string Action, int ThreadId)> TextInputCalls { get; } = new();

        public void EnqueueEvent(PlatformEvent platformEvent) => events.Enqueue(platformEvent);

        public IDisposable InstallLogRouter(ILoggerFactory loggerFactory) => new NoopDisposable();

        public IDisposable InitializeVideo(string? videoDriverHint = null) => new NoopDisposable();

        public bool IsDummyVideoDriver() => false;

        public nint CreateWindow(string title, int width, int height, bool resizable) => new(100);

        public void DestroyWindow(nint handle)
        {
        }

        public uint GetWindowId(nint handle) => CreatedWindowId;

        public void SetWindowTitle(nint handle, string title)
        {
        }

        public void GetWindowSize(nint handle, out int width, out int height)
        {
            width = logicalWidth;
            height = logicalHeight;
        }

        public bool GetWindowPixelSize(nint handle, out int width, out int height)
        {
            width = pixelWidth;
            height = pixelHeight;
            return true;
        }

        public void SetWindowSize(nint handle, int width, int height)
        {
        }

        public void StartTextInput(nint handle)
            => TextInputCalls.Enqueue(("Start", Environment.CurrentManagedThreadId));

        public void StopTextInput(nint handle)
            => TextInputCalls.Enqueue(("Stop", Environment.CurrentManagedThreadId));

        public bool PollEvent(out PlatformEvent platformEvent) => events.TryDequeue(out platformEvent);

        public IDisposable AddEventWatch(PlatformEventWatchCallback callback) => new NoopDisposable();

        private sealed class NoopDisposable : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }
}
