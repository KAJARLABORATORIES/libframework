using System.Collections.Concurrent;
using System.Numerics;
using kajarlabs.Framework.DependencyInjection;
using kajarlabs.Framework.Input;
using kajarlabs.Framework.Platform;
using kajarlabs.Framework.Scene;
using Microsoft.Extensions.Logging;

namespace kajarlabs.Framework.Tests.Input;

[Collection("SDL")]
public sealed partial class HeadlessInputIntegrationTests : IDisposable
{
    private readonly string tempDir = Path.Combine(Path.GetTempPath(), $"kajarlabs-input-int-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(tempDir))
            Directory.Delete(tempDir, true);
    }

    [Fact]
    public void HeadlessHostDrivesGameInputManagerHoverClickDragScrollAndFocusAcrossFrames()
    {
        using var host = new HeadlessGameHost();
        using var game = new InteractiveSceneGame(host);

        // Pre-frame 1: focus window, move over button and click it.
        host.InjectWindowFocus(true);
        host.InjectMouseMove(new Vector2(60f, 60f));
        host.InjectMouseDown(MouseButton.Left);
        host.InjectMouseUp(MouseButton.Left);

        host.RunFrames(game, 5);

        Assert.Same(game.InputManager, game.ResolvedInputManager);
        Assert.Same(game.InputManager, game.TextBox.ResolvedInputManager);

        // Frame 1 observed the pre-injected click in Game.Update().
        Assert.True(game.Frame1ObservedButtonClick);
        Assert.True(game.Frame1ObservedButtonHover);

        // Subsequent frames drove drag, scroll, focus switch, text input, and masked clipping.
        Assert.Equal(1, game.Button.ClickCount);
        Assert.Equal(1, game.Button.DragStartCount);
        Assert.True(game.Button.DragCount >= 1);
        Assert.Equal(1, game.Button.DragEndCount);
        Assert.Equal(new Vector2(0f, -2f), game.Button.LastScrollDelta);

        Assert.True(game.TextBox.HasFocus);
        Assert.Equal("hi", game.TextBox.ReceivedText);
        Assert.Equal(0, game.ClippedChild.ClickCount);
    }

    [Fact]
    public void FocusTransitionsDriveGameWindowStartAndStopTextInput()
    {
        var backend = new TextInputProbeBackend();
        using var host = new GameHost(new HostOptions
        {
            Organization = "kajarlabs",
            Application = "TextInputFocusIntegration",
            UserDataPath = tempDir,
            EnableConsoleLogging = false,
            EnableFileLogging = false,
        }, backend);

        using var game = new TextFocusGame(backend);
        host.Run(game);

        Assert.Equal(["Start", "Stop"], backend.TextInputEvents.ToArray());
    }

    private sealed partial class InteractiveSceneGame : Game
    {
        private readonly HeadlessGameHost headlessHost;
        private int frame;

        public InteractiveSceneGame(HeadlessGameHost headlessHost)
        {
            this.headlessHost = headlessHost;
        }

        [Resolved]
        public InputManager ResolvedInputManager { get; private set; } = null!;

        public ProbeControl Button { get; } = new()
        {
            Position = new Vector2(20f, 20f),
            Size = new Vector2(100f, 100f),
            AcceptsFocusFlag = true,
        };

        public ProbeControl TextBox { get; } = new()
        {
            Position = new Vector2(200f, 20f),
            Size = new Vector2(120f, 40f),
            AcceptsFocusFlag = true,
            RequestsTextInputFlag = true,
        };

        public Container MaskedPanel { get; } = new()
        {
            Position = new Vector2(20f, 200f),
            Size = new Vector2(80f, 80f),
            Masking = true,
        };

        public ProbeControl ClippedChild { get; } = new()
        {
            Position = new Vector2(0f, 0f),
            Size = new Vector2(200f, 80f),
            AcceptsFocusFlag = true,
        };

        public bool Frame1ObservedButtonClick { get; private set; }

        public bool Frame1ObservedButtonHover { get; private set; }

        protected override void Load()
        {
            MaskedPanel.Add(ClippedChild);
            Root.AddRange([Button, TextBox, MaskedPanel]);
        }

        protected override void Update()
        {
            frame++;

            if (frame == 1)
            {
                Frame1ObservedButtonClick = Button.ClickCount == 1;
                Frame1ObservedButtonHover = Button.IsHovered;

                // Frame 2: start dragging Button and scroll over it.
                headlessHost.InjectMouseScroll(new Vector2(0f, -2f), new Vector2(60f, 60f));
                headlessHost.InjectMouseDown(MouseButton.Left, new Vector2(60f, 60f));
                headlessHost.InjectMouseMove(new Vector2(80f, 60f));
            }
            else if (frame == 2)
            {
                // Frame 3: continue drag outside bounds and release, then try clicking clipped part of ClippedChild.
                headlessHost.InjectMouseMove(new Vector2(400f, 300f));
                headlessHost.InjectMouseUp(MouseButton.Left, new Vector2(400f, 300f));
                headlessHost.InjectMouseDown(MouseButton.Left, new Vector2(150f, 240f)); // outside MaskedPanel [20..100]
                headlessHost.InjectMouseUp(MouseButton.Left, new Vector2(150f, 240f));
            }
            else if (frame == 3)
            {
                // Frame 4: click TextBox and send text input.
                headlessHost.InjectMouseDown(MouseButton.Left, new Vector2(240f, 40f));
                headlessHost.InjectMouseUp(MouseButton.Left, new Vector2(240f, 40f));
                headlessHost.InjectTextInput("hi");
            }
        }
    }

    private sealed class TextFocusGame : Game
    {
        private readonly TextInputProbeBackend backend;
        private int step;

        public TextFocusGame(TextInputProbeBackend backend)
        {
            this.backend = backend;
        }

        public ProbeControl TextInputBox { get; } = new()
        {
            Position = new Vector2(20f, 20f),
            Size = new Vector2(200f, 50f),
            AcceptsFocusFlag = true,
            RequestsTextInputFlag = true,
        };

        protected override void Load()
        {
            Root.Add(TextInputBox);
            backend.EnqueueEvent(new PlatformEvent(
                PlatformEventType.MouseButtonDown,
                TextInputProbeBackend.CreatedWindowId,
                Input: PlatformInputEvent.MouseButtonEvent(MouseButton.Left, isDown: true, new Vector2(50f, 40f))));
        }

        protected override void Update()
        {
            if (step == 0 && TextInputBox.HasFocus && Window!.IsTextInputActive)
            {
                step = 1;
                backend.EnqueueEvent(new PlatformEvent(
                    PlatformEventType.MouseButtonDown,
                    TextInputProbeBackend.CreatedWindowId,
                    Input: PlatformInputEvent.MouseButtonEvent(MouseButton.Left, isDown: true, new Vector2(400f, 400f))));
            }
            else if (step == 1 && !TextInputBox.HasFocus && !Window!.IsTextInputActive)
            {
                Exit();
            }
        }
    }

    private sealed partial class ProbeControl : Drawable
    {
        [Resolved]
        public InputManager ResolvedInputManager { get; private set; } = null!;

        public bool AcceptsFocusFlag { get; init; }

        public bool RequestsTextInputFlag { get; init; }

        public int ClickCount { get; private set; }

        public int DragStartCount { get; private set; }

        public int DragCount { get; private set; }

        public int DragEndCount { get; private set; }

        public Vector2 LastScrollDelta { get; private set; }

        public string ReceivedText { get; private set; } = string.Empty;

        public override bool AcceptsFocus => AcceptsFocusFlag;

        public override bool RequestsTextInput => RequestsTextInputFlag;

        protected override bool OnHover(HoverEvent e) => true;

        protected override bool OnMouseDown(MouseDownEvent e) => true;

        protected override bool OnClick(ClickEvent e)
        {
            ClickCount++;
            return true;
        }

        protected override bool OnDragStart(DragStartEvent e)
        {
            DragStartCount++;
            return true;
        }

        protected override bool OnDrag(DragEvent e)
        {
            DragCount++;
            return true;
        }

        protected override void OnDragEnd(DragEndEvent e) => DragEndCount++;

        protected override bool OnScroll(ScrollEvent e)
        {
            LastScrollDelta = e.ScrollDelta;
            return true;
        }

        protected override bool OnTextInput(TextInputEvent e)
        {
            ReceivedText += e.Text;
            return true;
        }
    }

    private sealed class TextInputProbeBackend : IPlatformBackend
    {
        private readonly ConcurrentQueue<PlatformEvent> events = new();

        public static uint CreatedWindowId => 88;

        public ConcurrentQueue<string> TextInputEvents { get; } = new();

        public void EnqueueEvent(PlatformEvent platformEvent) => events.Enqueue(platformEvent);

        public IDisposable InstallLogRouter(ILoggerFactory loggerFactory) => new NoopDisposable();

        public IDisposable InitializeVideo(string? videoDriverHint = null) => new NoopDisposable();

        public bool IsDummyVideoDriver() => false;

        public nint CreateWindow(string title, int width, int height, bool resizable) => new(101);

        public void DestroyWindow(nint handle)
        {
        }

        public uint GetWindowId(nint handle) => CreatedWindowId;

        public void SetWindowTitle(nint handle, string title)
        {
        }

        public void GetWindowSize(nint handle, out int width, out int height)
        {
            width = 800;
            height = 600;
        }

        public bool GetWindowPixelSize(nint handle, out int width, out int height)
        {
            width = 800;
            height = 600;
            return true;
        }

        public void SetWindowSize(nint handle, int width, int height)
        {
        }

        public void StartTextInput(nint handle) => TextInputEvents.Enqueue("Start");

        public void StopTextInput(nint handle) => TextInputEvents.Enqueue("Stop");

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
