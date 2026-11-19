using System.Collections.Concurrent;
using System.Numerics;
using kajarlabs.Framework.DependencyInjection;
using kajarlabs.Framework.Input;
using kajarlabs.Framework.Platform;
using kajarlabs.Framework.Scene;
using Microsoft.Extensions.Logging;

namespace kajarlabs.Framework.Tests.Input;

[Collection("SDL")]
public sealed partial class InteractiveInputIntegrationTests : IDisposable
{
    private readonly string tempDir = Path.Combine(Path.GetTempPath(), $"kajarlabs-interactive-input-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(tempDir))
            Directory.Delete(tempDir, recursive: true);
    }

    [Fact]
    public void MultiFrameRotatedAndMaskedContainerHitTestingAndHoverReevaluation()
    {
        using var host = new HeadlessGameHost();
        using var game = new RotatedMaskedSceneGame(host);

        host.RunFrames(game, 8);

        // Frame 2: hit rotated child via transformed screen-space center.
        Assert.True(game.Frame2RotatedTileHovered);
        Assert.Equal(1, game.Frame2RotatedTileClicks);
        Assert.True(game.Frame2RotatedTileFocused);

        // Frame 3: clicking outside rotated quad misses and clears hover.
        Assert.False(game.Frame3RotatedTileHovered);
        Assert.Equal(1, game.Frame3RotatedTileClicks);
        Assert.Equal(1, game.RotatedTile.HoverLostCount);

        // Frame 4: visible half of overflowing child inside masked panel receives input.
        Assert.True(game.Frame4OverflowHovered);
        Assert.Equal(1, game.Frame4OverflowClicks);

        // Frame 5: clipped half of overflowing child outside masked panel does not receive input.
        Assert.False(game.Frame5OverflowHovered);
        Assert.Equal(1, game.Frame5OverflowClicks);

        // Frame 6: turning Masking off without moving mouse re-evaluates hover dynamically.
        Assert.True(game.Frame6OverflowHoveredAfterUnmask);

        // Frame 7: clicking the previously clipped position now succeeds.
        Assert.Equal(2, game.OverflowTile.ClickCount);
    }

    [Fact]
    public void MultiFrameClickDoubleClickDragAndScrollPropagation()
    {
        using var host = new HeadlessGameHost();
        using var game = new DragAndScrollSceneGame(host);

        host.RunFrames(game, 9);

        Assert.Equal(1, game.Frame2CardClicks);
        Assert.Equal(0, game.Frame2CardDoubleClicks);

        Assert.Equal(1, game.Frame3CardClicks);
        Assert.Equal(1, game.Frame3CardDoubleClicks);

        // Sub-threshold motion does not start a drag.
        Assert.False(game.Frame4IsDragged);
        Assert.Equal(0, game.Frame4DragStarts);

        // Exceeding DragDistanceThreshold starts the drag and moves the card.
        Assert.True(game.Frame5IsDragged);
        Assert.Equal(1, game.Frame5DragStarts);

        // Continuing drag far outside card bounds maintains drag capture and updates position.
        Assert.True(game.Frame6IsDragged);
        Assert.True(game.Card.DragCount >= 1);
        Assert.True(game.Card.Position.X > 150f);
        Assert.True(game.Card.Position.Y > 90f);

        // Releasing after drag ends drag without emitting a click.
        Assert.False(game.Card.IsDragged);
        Assert.Equal(1, game.Card.DragEndCount);
        Assert.Equal(1, game.Card.ClickCount);

        // Scroll unhandled by the card bubbles to the scroll panel behind it.
        Assert.Equal(1, game.ScrollPanel.ScrollCount);
        Assert.Equal(new Vector2(0f, -3f), game.ScrollPanel.TotalScrollDelta);
    }

    [Fact]
    public void MultiFrameFocusSwitchingTextInputAndWindowFocusLossCleanup()
    {
        using var host = new HeadlessGameHost();
        using var game = new FocusAndWindowLossGame(host);

        host.RunFrames(game, 7);

        // Frame 2: clicking TargetA gives it focus and routes key events to it.
        Assert.True(game.Frame2TargetAFocused);
        Assert.Equal([Key.A], game.TargetA.ReceivedKeys);

        // Frame 3: switching focus via InputManager.ChangeFocus transitions focus and routes text to TargetB.
        Assert.False(game.Frame3TargetAFocused);
        Assert.True(game.Frame3TargetBFocused);
        Assert.Equal(1, game.Frame3TargetAFocusLostCount);
        Assert.Equal("hello", game.TargetB.ReceivedText);

        // Frame 4: active key binding and drag are live prior to window focus loss.
        Assert.Equal(1, game.Frame4HoldPressedCount);
        Assert.Equal(0, game.Frame4HoldReleasedCount);
        Assert.True(game.Frame4TargetADragged);

        // Frame 5: window focus loss clears focus, ends active drag, releases held keys and active bindings.
        Assert.Null(game.InputManager.FocusedDrawable);
        Assert.False(game.TargetA.HasFocus);
        Assert.False(game.TargetB.HasFocus);
        Assert.Equal(2, game.TargetA.FocusLostCount);
        Assert.False(game.TargetA.IsDragged);
        Assert.Equal(1, game.TargetA.DragEndCount);
        Assert.Equal(1, game.HoldHandler.ReleasedCount);
        Assert.False(game.InputManager.CurrentState.Keyboard.AnyKeyDown);
    }

    [Fact]
    public void MultiFrameKeyBindingPrecedencePositionalBindingsAndNestedContainers()
    {
        using var host = new HeadlessGameHost();
        using var game = new KeyBindingIntegrationGame(host);

        host.RunFrames(game, 8);

        // Frame 2: Ctrl+R triggers Reset chord and does not fire plain R Rotate.
        Assert.Equal(1, game.Frame2OuterResetPressed);
        Assert.Equal(0, game.Frame2OuterResetReleased);
        Assert.Equal(0, game.Frame2InnerRotatePressed);
        Assert.Equal(0, game.Frame2OuterRotatePressed);

        // Frame 3: releasing ControlLeft releases the active Ctrl+R binding.
        Assert.Equal(1, game.OuterHandler.GetReleasedCount(IntegrationAction.Reset));

        // Frame 4: plain R goes to the inner nested KeyBindingContainer before the outer one.
        Assert.Equal(1, game.InnerHandler.GetPressedCount(IntegrationAction.Rotate));
        Assert.Equal(1, game.InnerHandler.GetReleasedCount(IntegrationAction.Rotate));
        Assert.Equal(0, game.OuterHandler.GetPressedCount(IntegrationAction.Rotate));

        // Frame 5: Ctrl+WheelUp inside masked inner panel hits inner positional handler.
        Assert.Equal(1, game.InnerHandler.GetPressedCount(IntegrationAction.ZoomIn));
        Assert.Equal(1, game.InnerHandler.GetReleasedCount(IntegrationAction.ZoomIn));
        Assert.Equal(0, game.Frame5OuterZoomPressed);

        // Frame 6: Ctrl+WheelUp outside masked inner panel skips inner and hits outer positional handler.
        Assert.Equal(1, game.InnerHandler.GetPressedCount(IntegrationAction.ZoomIn));
        Assert.Equal(1, game.OuterHandler.GetPressedCount(IntegrationAction.ZoomIn));
        Assert.Equal(1, game.OuterHandler.GetReleasedCount(IntegrationAction.ZoomIn));
    }

    [Fact]
    public void SyntheticPlatformBackendPropagatesSdlLikeEventsThroughGameHostToInputManager()
    {
        var backend = new SyntheticPlatformBackend();
        using var host = new GameHost(new HostOptions
        {
            Organization = "kajarlabs",
            Application = "SyntheticPlatformInputIntegration",
            UserDataPath = tempDir,
            EnableConsoleLogging = false,
            EnableFileLogging = false,
        }, backend);

        using var game = new SyntheticPlatformBackendGame(backend);
        host.Run(game);

        Assert.True(game.CompletedAllSteps);
        Assert.Equal(1, game.Button.ClickCount);
        Assert.True(game.Button.HasFocus);
        Assert.Equal("sdl", game.Button.ReceivedText);
        Assert.Equal(1, game.ActionHandler.GetPressedCount(IntegrationAction.Reset));
        Assert.Equal(1, game.ActionHandler.GetReleasedCount(IntegrationAction.Reset));
    }

    [Fact]
    public void PassThroughAndManualInputManagersRespectUseParentInputUnderHeadlessHost()
    {
        using var host = new HeadlessGameHost();
        using var game = new NestedInputManagersGame(host);

        host.RunFrames(game, 5);

        // Host-injected events pass through PassThroughInputManager (UseParentInput == true).
        Assert.Equal(1, game.PassThroughTarget.ClickCount);
        Assert.True(game.PassThroughTarget.IsHovered);
        Assert.Equal(1, game.PassThroughHandler.GetPressedCount(IntegrationAction.Rotate));
        Assert.Equal(1, game.PassThroughHandler.GetReleasedCount(IntegrationAction.Rotate));

        // ManualInputManager (UseParentInput == false) ignores host events...
        Assert.Equal(0, game.Frame2ManualTargetClicksFromHost);

        // ...and responds deterministically to its own manual input helpers without affecting the parent.
        Assert.Equal(1, game.ManualTarget.ClickCount);
        Assert.True(game.ManualTarget.HasFocus);
        Assert.Equal(1, game.ManualHandler.GetPressedCount(IntegrationAction.Rotate));
        Assert.Equal(1, game.ManualHandler.GetReleasedCount(IntegrationAction.Rotate));
        Assert.Equal(1, game.PassThroughTarget.ClickCount);
    }

    private enum IntegrationAction
    {
        Rotate,
        Reset,
        Hold,
        ZoomIn,
    }

    private sealed partial class RotatedMaskedSceneGame : Game
    {
        private readonly HeadlessGameHost headlessHost;
        private int frame;

        public RotatedMaskedSceneGame(HeadlessGameHost headlessHost)
        {
            this.headlessHost = headlessHost;
        }

        public Container MaskedPanel { get; } = new()
        {
            Position = new Vector2(100f, 100f),
            Size = new Vector2(240f, 240f),
            Masking = true,
        };

        public Container RotatedContainer { get; } = new()
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            Size = new Vector2(160f, 160f),
            Rotation = 45f,
        };

        public InteractiveProbe RotatedTile { get; } = new()
        {
            Position = new Vector2(10f, 10f),
            Size = new Vector2(60f, 60f),
            AcceptsFocusFlag = true,
        };

        // Starts inside MaskedPanel (local x=200 => screen x=300..420, while panel right edge is x=340).
        public InteractiveProbe OverflowTile { get; } = new()
        {
            Position = new Vector2(200f, 180f),
            Size = new Vector2(120f, 40f),
            AcceptsFocusFlag = true,
        };

        public bool Frame2RotatedTileHovered { get; private set; }

        public int Frame2RotatedTileClicks { get; private set; }

        public bool Frame2RotatedTileFocused { get; private set; }

        public bool Frame3RotatedTileHovered { get; private set; }

        public int Frame3RotatedTileClicks { get; private set; }

        public bool Frame4OverflowHovered { get; private set; }

        public int Frame4OverflowClicks { get; private set; }

        public bool Frame5OverflowHovered { get; private set; }

        public int Frame5OverflowClicks { get; private set; }

        public bool Frame6OverflowHoveredAfterUnmask { get; private set; }

        protected override void Load()
        {
            RotatedContainer.Add(RotatedTile);
            MaskedPanel.AddRange([RotatedContainer, OverflowTile]);
            Root.Add(MaskedPanel);
        }

        protected override void Update()
        {
            frame++;

            switch (frame)
            {
                case 1:
                {
                    var rotatedCenter = RotatedTile.ToScreenSpace(new Vector2(30f, 30f));
                    headlessHost.InjectWindowFocus(true);
                    headlessHost.InjectMouseMove(rotatedCenter);
                    headlessHost.InjectMouseDown(MouseButton.Left, rotatedCenter);
                    headlessHost.InjectMouseUp(MouseButton.Left, rotatedCenter);
                    break;
                }

                case 2:
                {
                    Frame2RotatedTileHovered = RotatedTile.IsHovered;
                    Frame2RotatedTileClicks = RotatedTile.ClickCount;
                    Frame2RotatedTileFocused = RotatedTile.HasFocus;

                    // Move to a point outside the rotated tile (top-left of MaskedPanel).
                    var outsideRotated = new Vector2(110f, 110f);
                    headlessHost.InjectMouseMove(outsideRotated);
                    headlessHost.InjectMouseDown(MouseButton.Left, outsideRotated);
                    headlessHost.InjectMouseUp(MouseButton.Left, outsideRotated);
                    break;
                }

                case 3:
                {
                    Frame3RotatedTileHovered = RotatedTile.IsHovered;
                    Frame3RotatedTileClicks = RotatedTile.ClickCount;

                    // Visible half of OverflowTile (screen x=320, y=300 is inside MaskedPanel [100..340, 100..340]).
                    var visibleOverflow = new Vector2(320f, 300f);
                    headlessHost.InjectMouseMove(visibleOverflow);
                    headlessHost.InjectMouseDown(MouseButton.Left, visibleOverflow);
                    headlessHost.InjectMouseUp(MouseButton.Left, visibleOverflow);
                    break;
                }

                case 4:
                {
                    Frame4OverflowHovered = OverflowTile.IsHovered;
                    Frame4OverflowClicks = OverflowTile.ClickCount;

                    // Clipped half of OverflowTile (screen x=380, y=300 is outside MaskedPanel right edge 340).
                    var clippedOverflow = new Vector2(380f, 300f);
                    headlessHost.InjectMouseMove(clippedOverflow);
                    headlessHost.InjectMouseDown(MouseButton.Left, clippedOverflow);
                    headlessHost.InjectMouseUp(MouseButton.Left, clippedOverflow);
                    break;
                }

                case 5:
                {
                    Frame5OverflowHovered = OverflowTile.IsHovered;
                    Frame5OverflowClicks = OverflowTile.ClickCount;

                    // Disable masking without moving pointer; InputManager re-evaluates hover on UpdateSubTree.
                    MaskedPanel.Masking = false;
                    break;
                }

                case 6:
                {
                    Frame6OverflowHoveredAfterUnmask = OverflowTile.IsHovered;

                    var previouslyClipped = new Vector2(380f, 300f);
                    headlessHost.InjectMouseDown(MouseButton.Left, previouslyClipped);
                    headlessHost.InjectMouseUp(MouseButton.Left, previouslyClipped);
                    break;
                }
            }
        }
    }

    private sealed class DragAndScrollSceneGame : Game
    {
        private readonly HeadlessGameHost headlessHost;
        private int frame;

        public DragAndScrollSceneGame(HeadlessGameHost headlessHost)
        {
            this.headlessHost = headlessHost;
        }

        public ScrollProbePanel ScrollPanel { get; } = new()
        {
            Position = new Vector2(20f, 20f),
            Size = new Vector2(500f, 360f),
        };

        public DraggableProbeCard Card { get; } = new()
        {
            Position = new Vector2(30f, 30f),
            Size = new Vector2(100f, 80f),
        };

        public int Frame2CardClicks { get; private set; }

        public int Frame2CardDoubleClicks { get; private set; }

        public int Frame3CardClicks { get; private set; }

        public int Frame3CardDoubleClicks { get; private set; }

        public bool Frame4IsDragged { get; private set; }

        public int Frame4DragStarts { get; private set; }

        public bool Frame5IsDragged { get; private set; }

        public int Frame5DragStarts { get; private set; }

        public bool Frame6IsDragged { get; private set; }

        protected override void Load()
        {
            InputManager.DoubleClickTimeThreshold = 5000.0;
            ScrollPanel.Add(Card);
            Root.Add(ScrollPanel);
        }

        protected override void Update()
        {
            frame++;

            switch (frame)
            {
                case 1:
                    // First click on Card (screen pos 90, 80 => inside Card [50..150, 50..130]).
                    headlessHost.InjectWindowFocus(true);
                    headlessHost.InjectMouseMove(new Vector2(90f, 80f));
                    headlessHost.InjectMouseDown(MouseButton.Left, new Vector2(90f, 80f));
                    headlessHost.InjectMouseUp(MouseButton.Left, new Vector2(90f, 80f));
                    break;

                case 2:
                    Frame2CardClicks = Card.ClickCount;
                    Frame2CardDoubleClicks = Card.DoubleClickCount;

                    // Second click immediately at the same position triggers DoubleClick.
                    headlessHost.InjectMouseDown(MouseButton.Left, new Vector2(90f, 80f));
                    headlessHost.InjectMouseUp(MouseButton.Left, new Vector2(90f, 80f));
                    break;

                case 3:
                    Frame3CardClicks = Card.ClickCount;
                    Frame3CardDoubleClicks = Card.DoubleClickCount;

                    // Begin press + sub-threshold move (+4px < 10px default threshold).
                    headlessHost.InjectMouseDown(MouseButton.Left, new Vector2(90f, 80f));
                    headlessHost.InjectMouseMove(new Vector2(94f, 80f));
                    break;

                case 4:
                    Frame4IsDragged = Card.IsDragged;
                    Frame4DragStarts = Card.DragStartCount;

                    // Cross DragDistanceThreshold (+25px from 90).
                    headlessHost.InjectMouseMove(new Vector2(115f, 80f));
                    break;

                case 5:
                    Frame5IsDragged = Card.IsDragged;
                    Frame5DragStarts = Card.DragStartCount;

                    // Drag well outside original card bounds.
                    headlessHost.InjectMouseMove(new Vector2(290f, 200f));
                    break;

                case 6:
                    Frame6IsDragged = Card.IsDragged;

                    // Release mouse button to end drag cleanly without emitting a click.
                    headlessHost.InjectMouseUp(MouseButton.Left, new Vector2(290f, 200f));
                    break;

                case 7:
                    // Scroll over the card's current position; card does not consume scroll so ScrollPanel receives it.
                    var cardCenter = Card.ToScreenSpace(new Vector2(50f, 40f));
                    headlessHost.InjectMouseScroll(new Vector2(0f, -3f), cardCenter);
                    break;
            }
        }
    }

    private sealed class FocusAndWindowLossGame : Game
    {
        private readonly HeadlessGameHost headlessHost;
        private int frame;

        public FocusAndWindowLossGame(HeadlessGameHost headlessHost)
        {
            this.headlessHost = headlessHost;
        }

        public InteractiveProbe TargetA { get; } = new()
        {
            Position = new Vector2(20f, 20f),
            Size = new Vector2(120f, 80f),
            AcceptsFocusFlag = true,
            AllowDrag = true,
        };

        public InteractiveProbe TargetB { get; } = new()
        {
            Position = new Vector2(180f, 20f),
            Size = new Vector2(120f, 80f),
            AcceptsFocusFlag = true,
            RequestsTextInputFlag = true,
        };

        public ActionCounterHandler HoldHandler { get; } = new();

        public bool Frame2TargetAFocused { get; private set; }

        public bool Frame3TargetAFocused { get; private set; }

        public int Frame3TargetAFocusLostCount { get; private set; }

        public bool Frame3TargetBFocused { get; private set; }

        public int Frame4HoldPressedCount { get; private set; }

        public int Frame4HoldReleasedCount { get; private set; }

        public bool Frame4TargetADragged { get; private set; }

        protected override void Load()
        {
            var bindingContainer = new KeyBindingContainer<IntegrationAction>(SimultaneousBindingMode.All)
            {
                RelativeSizeAxes = Axes.Both,
                Size = Vector2.One,
                KeyBindings =
                [
                    new(new KeyCombination(Key.H), IntegrationAction.Hold),
                ],
            };

            bindingContainer.AddRange([TargetA, TargetB, HoldHandler]);
            Root.Add(bindingContainer);
        }

        protected override void Update()
        {
            frame++;

            switch (frame)
            {
                case 1:
                    headlessHost.InjectWindowFocus(true);
                    headlessHost.InjectMouseMove(new Vector2(60f, 60f));
                    headlessHost.InjectMouseDown(MouseButton.Left, new Vector2(60f, 60f));
                    headlessHost.InjectMouseUp(MouseButton.Left, new Vector2(60f, 60f));
                    headlessHost.InjectKeyDown(Key.A);
                    headlessHost.InjectKeyUp(Key.A);
                    break;

                case 2:
                    Frame2TargetAFocused = TargetA.HasFocus;

                    InputManager.ChangeFocus(TargetB);
                    headlessHost.InjectTextInput("hello");
                    break;

                case 3:
                    Frame3TargetAFocused = TargetA.HasFocus;
                    Frame3TargetAFocusLostCount = TargetA.FocusLostCount;
                    Frame3TargetBFocused = TargetB.HasFocus;

                    // Hold a key binding and start dragging TargetA.
                    headlessHost.InjectKeyDown(Key.H);
                    headlessHost.InjectMouseDown(MouseButton.Left, new Vector2(60f, 60f));
                    headlessHost.InjectMouseMove(new Vector2(90f, 60f));
                    break;

                case 4:
                    Frame4HoldPressedCount = HoldHandler.GetPressedCount(IntegrationAction.Hold);
                    Frame4HoldReleasedCount = HoldHandler.GetReleasedCount(IntegrationAction.Hold);
                    Frame4TargetADragged = TargetA.IsDragged;

                    // Losing window focus must clear focus, end drag, release keys, and release active bindings.
                    headlessHost.InjectWindowFocus(false);
                    break;
            }
        }
    }

    private sealed class KeyBindingIntegrationGame : Game
    {
        private readonly HeadlessGameHost headlessHost;
        private int frame;

        public KeyBindingIntegrationGame(HeadlessGameHost headlessHost)
        {
            this.headlessHost = headlessHost;
        }

        public ActionCounterHandler OuterHandler { get; } = new()
        {
            RelativeSizeAxes = Axes.Both,
            Size = Vector2.One,
        };

        public ActionCounterHandler InnerHandler { get; } = new(IntegrationAction.Rotate, IntegrationAction.ZoomIn)
        {
            RelativeSizeAxes = Axes.Both,
            Size = Vector2.One,
        };

        public int Frame2OuterResetPressed { get; private set; }

        public int Frame2OuterResetReleased { get; private set; }

        public int Frame2InnerRotatePressed { get; private set; }

        public int Frame2OuterRotatePressed { get; private set; }

        public int Frame5OuterZoomPressed { get; private set; }

        protected override void Load()
        {
            var outerBindings = new KeyBindingContainer<IntegrationAction>(SimultaneousBindingMode.All)
            {
                RelativeSizeAxes = Axes.Both,
                Size = Vector2.One,
                KeyBindings =
                [
                    new(new KeyCombination(InputKey.Control, Key.R), IntegrationAction.Reset),
                    new(new KeyCombination(Key.R), IntegrationAction.Rotate),
                    new(new KeyCombination(InputKey.Control, InputKey.MouseWheelUp), IntegrationAction.ZoomIn),
                ],
            };

            var innerMaskedPanel = new Container
            {
                Position = new Vector2(40f, 40f),
                Size = new Vector2(140f, 140f),
                Masking = true,
            };

            var innerBindings = new KeyBindingContainer<IntegrationAction>(SimultaneousBindingMode.Unique)
            {
                RelativeSizeAxes = Axes.Both,
                Size = Vector2.One,
                KeyBindings =
                [
                    new(new KeyCombination(Key.R), IntegrationAction.Rotate),
                    new(new KeyCombination(InputKey.Control, InputKey.MouseWheelUp), IntegrationAction.ZoomIn),
                ],
            };

            innerBindings.Add(InnerHandler);
            innerMaskedPanel.Add(innerBindings);
            outerBindings.AddRange([OuterHandler, innerMaskedPanel]);
            Root.Add(outerBindings);
        }

        protected override void Update()
        {
            frame++;

            switch (frame)
            {
                case 1:
                    headlessHost.InjectWindowFocus(true);
                    headlessHost.InjectKeyDown(Key.ControlLeft, KeyModifiers.Control);
                    headlessHost.InjectKeyDown(Key.R, KeyModifiers.Control);
                    break;

                case 2:
                    Frame2OuterResetPressed = OuterHandler.GetPressedCount(IntegrationAction.Reset);
                    Frame2OuterResetReleased = OuterHandler.GetReleasedCount(IntegrationAction.Reset);
                    Frame2InnerRotatePressed = InnerHandler.GetPressedCount(IntegrationAction.Rotate);
                    Frame2OuterRotatePressed = OuterHandler.GetPressedCount(IntegrationAction.Rotate);

                    // Lifting modifier releases the Ctrl+R chord while R is still pressed.
                    headlessHost.InjectKeyUp(Key.ControlLeft);
                    headlessHost.InjectKeyUp(Key.R);
                    break;

                case 3:
                    // Plain R is consumed by the inner nested KeyBindingContainer first.
                    headlessHost.InjectKeyDown(Key.R);
                    headlessHost.InjectKeyUp(Key.R);
                    break;

                case 4:
                    // Hold Ctrl and scroll up inside masked inner panel (40..180, 40..180).
                    headlessHost.InjectKeyDown(Key.ControlLeft, KeyModifiers.Control);
                    headlessHost.InjectMouseScroll(new Vector2(0f, 1f), new Vector2(80f, 80f));
                    break;

                case 5:
                    Frame5OuterZoomPressed = OuterHandler.GetPressedCount(IntegrationAction.ZoomIn);

                    // Scroll up outside masked inner panel; outer positional handler receives it.
                    headlessHost.InjectMouseScroll(new Vector2(0f, 1f), new Vector2(300f, 300f));
                    headlessHost.InjectKeyUp(Key.ControlLeft);
                    break;
            }
        }
    }

    private sealed class SyntheticPlatformBackendGame : Game
    {
        private readonly SyntheticPlatformBackend backend;
        private int step;

        public SyntheticPlatformBackendGame(SyntheticPlatformBackend backend)
        {
            this.backend = backend;
        }

        public InteractiveProbe Button { get; } = new()
        {
            Position = new Vector2(20f, 20f),
            Size = new Vector2(120f, 60f),
            AcceptsFocusFlag = true,
            RequestsTextInputFlag = true,
        };

        public ActionCounterHandler ActionHandler { get; } = new();

        public bool CompletedAllSteps { get; private set; }

        protected override void Load()
        {
            var bindings = new KeyBindingContainer<IntegrationAction>(SimultaneousBindingMode.All)
            {
                RelativeSizeAxes = Axes.Both,
                Size = Vector2.One,
                KeyBindings =
                [
                    new(new KeyCombination(InputKey.Control, Key.R), IntegrationAction.Reset),
                ],
            };

            bindings.AddRange([Button, ActionHandler]);
            Root.Add(bindings);

            backend.Enqueue(PlatformEventType.WindowFocusGained, PlatformInputEvent.WindowFocus(true));
            backend.Enqueue(PlatformEventType.MouseMotion, PlatformInputEvent.MouseMotion(new Vector2(50f, 50f), Vector2.Zero));
            backend.Enqueue(PlatformEventType.MouseButtonDown, PlatformInputEvent.MouseButtonEvent(MouseButton.Left, isDown: true, new Vector2(50f, 50f)));
            backend.Enqueue(PlatformEventType.MouseButtonUp, PlatformInputEvent.MouseButtonEvent(MouseButton.Left, isDown: false, new Vector2(50f, 50f)));
        }

        protected override void Update()
        {
            if (step == 0 && Button.ClickCount == 1 && Button.HasFocus)
            {
                step = 1;
                backend.Enqueue(PlatformEventType.TextInput, PlatformInputEvent.TextInput("sdl"));
                backend.Enqueue(PlatformEventType.KeyDown, PlatformInputEvent.KeyEvent(Key.ControlLeft, isDown: true, KeyModifiers.Control));
                backend.Enqueue(PlatformEventType.KeyDown, PlatformInputEvent.KeyEvent(Key.R, isDown: true, KeyModifiers.Control));
                backend.Enqueue(PlatformEventType.KeyUp, PlatformInputEvent.KeyEvent(Key.R, isDown: false, KeyModifiers.Control));
                backend.Enqueue(PlatformEventType.KeyUp, PlatformInputEvent.KeyEvent(Key.ControlLeft, isDown: false));
            }
            else if (step == 1
                && Button.ReceivedText == "sdl"
                && ActionHandler.GetPressedCount(IntegrationAction.Reset) == 1
                && ActionHandler.GetReleasedCount(IntegrationAction.Reset) == 1)
            {
                CompletedAllSteps = true;
                Exit();
            }
        }
    }

    private sealed class NestedInputManagersGame : Game
    {
        private readonly HeadlessGameHost headlessHost;
        private int frame;

        public NestedInputManagersGame(HeadlessGameHost headlessHost)
        {
            this.headlessHost = headlessHost;
        }

        public PassThroughInputManager PassThrough { get; } = new()
        {
            Position = new Vector2(20f, 20f),
            Size = new Vector2(180f, 180f),
        };

        public InteractiveProbe PassThroughTarget { get; } = new()
        {
            Position = new Vector2(10f, 10f),
            Size = new Vector2(120f, 120f),
            AcceptsFocusFlag = true,
        };

        public ActionCounterHandler PassThroughHandler { get; } = new();

        public ManualInputManager Manual { get; } = new()
        {
            Position = new Vector2(240f, 20f),
            Size = new Vector2(180f, 180f),
        };

        public InteractiveProbe ManualTarget { get; } = new()
        {
            Position = new Vector2(10f, 10f),
            Size = new Vector2(120f, 120f),
            AcceptsFocusFlag = true,
        };

        public ActionCounterHandler ManualHandler { get; } = new();

        public int Frame2ManualTargetClicksFromHost { get; private set; }

        protected override void Load()
        {
            var passThroughBindings = new KeyBindingContainer<IntegrationAction>(SimultaneousBindingMode.All)
            {
                RelativeSizeAxes = Axes.Both,
                Size = Vector2.One,
                KeyBindings = [new(new KeyCombination(Key.R), IntegrationAction.Rotate)],
            };
            passThroughBindings.AddRange([PassThroughTarget, PassThroughHandler]);
            PassThrough.Add(passThroughBindings);

            var manualBindings = new KeyBindingContainer<IntegrationAction>(SimultaneousBindingMode.All)
            {
                RelativeSizeAxes = Axes.Both,
                Size = Vector2.One,
                KeyBindings = [new(new KeyCombination(Key.R), IntegrationAction.Rotate)],
            };
            manualBindings.AddRange([ManualTarget, ManualHandler]);
            Manual.Add(manualBindings);

            Root.AddRange([PassThrough, Manual]);
        }

        protected override void Update()
        {
            frame++;

            if (frame == 1)
            {
                // Inject host events over both PassThrough and Manual bounds.
                headlessHost.InjectWindowFocus(true);
                headlessHost.InjectMouseMove(new Vector2(290f, 70f));
                headlessHost.InjectMouseDown(MouseButton.Left, new Vector2(290f, 70f));
                headlessHost.InjectMouseUp(MouseButton.Left, new Vector2(290f, 70f));

                headlessHost.InjectMouseMove(new Vector2(70f, 70f));
                headlessHost.InjectMouseDown(MouseButton.Left, new Vector2(70f, 70f));
                headlessHost.InjectMouseUp(MouseButton.Left, new Vector2(70f, 70f));
                headlessHost.InjectKeyDown(Key.R);
                headlessHost.InjectKeyUp(Key.R);
            }
            else if (frame == 2)
            {
                Frame2ManualTargetClicksFromHost = ManualTarget.ClickCount;

                // Drive ManualInputManager directly with its own manual input helpers.
                Manual.SetWindowFocus(true);
                Manual.Click(MouseButton.Left, new Vector2(290f, 70f));
                Manual.PressKey(Key.R);
                Manual.ReleaseKey(Key.R);
            }
        }
    }

    private sealed partial class InteractiveProbe : Drawable
    {
        [Resolved]
        public InputManager ResolvedInputManager { get; private set; } = null!;

        public bool AcceptsFocusFlag { get; init; }

        public bool RequestsTextInputFlag { get; init; }

        public bool AllowDrag { get; init; }

        public int ClickCount { get; private set; }

        public int HoverLostCount { get; private set; }

        public int FocusCount { get; private set; }

        public int FocusLostCount { get; private set; }

        public int DragEndCount { get; private set; }

        public List<Key> ReceivedKeys { get; } = [];

        public string ReceivedText { get; private set; } = string.Empty;

        public override bool AcceptsFocus => AcceptsFocusFlag;

        public override bool RequestsTextInput => RequestsTextInputFlag;

        protected override bool OnHover(HoverEvent e) => true;

        protected override void OnHoverLost(HoverLostEvent e) => HoverLostCount++;

        protected override bool OnMouseDown(MouseDownEvent e) => e.Button == MouseButton.Left;

        protected override bool OnClick(ClickEvent e)
        {
            ClickCount++;
            return true;
        }

        protected override bool OnDragStart(DragStartEvent e) => AllowDrag && e.Button == MouseButton.Left;

        protected override bool OnDrag(DragEvent e) => AllowDrag;

        protected override void OnDragEnd(DragEndEvent e) => DragEndCount++;

        protected override void OnFocus(FocusEvent e) => FocusCount++;

        protected override void OnFocusLost(FocusLostEvent e) => FocusLostCount++;

        protected override bool OnKeyDown(KeyDownEvent e)
        {
            if (e.Key is Key.H or Key.ControlLeft or Key.R)
                return false;

            ReceivedKeys.Add(e.Key);
            return true;
        }

        protected override bool OnTextInput(TextInputEvent e)
        {
            ReceivedText += e.Text;
            return true;
        }
    }

    private sealed class DraggableProbeCard : Drawable
    {
        public int ClickCount { get; private set; }

        public int DoubleClickCount { get; private set; }

        public int DragStartCount { get; private set; }

        public int DragCount { get; private set; }

        public int DragEndCount { get; private set; }

        public override bool AcceptsFocus => true;

        protected override bool OnHover(HoverEvent e) => true;

        protected override bool OnMouseDown(MouseDownEvent e) => e.Button == MouseButton.Left;

        protected override bool OnClick(ClickEvent e)
        {
            ClickCount++;
            return true;
        }

        protected override bool OnDoubleClick(DoubleClickEvent e)
        {
            DoubleClickCount++;
            return true;
        }

        protected override bool OnDragStart(DragStartEvent e)
        {
            if (e.Button != MouseButton.Left)
                return false;

            DragStartCount++;

            if (Parent is not null)
                Position += Parent.ToLocalSpace(e.ScreenSpaceMousePosition) - Parent.ToLocalSpace(e.ScreenSpaceMouseDownPosition);

            return true;
        }

        protected override bool OnDrag(DragEvent e)
        {
            DragCount++;

            if (Parent is not null)
                Position += Parent.ToLocalSpace(e.ScreenSpaceMousePosition) - Parent.ToLocalSpace(e.ScreenSpaceLastMousePosition);

            return true;
        }

        protected override void OnDragEnd(DragEndEvent e) => DragEndCount++;
    }

    private sealed class ScrollProbePanel : Container
    {
        public int ScrollCount { get; private set; }

        public Vector2 TotalScrollDelta { get; private set; }

        public override bool HandlePositionalInput => true;

        protected override bool OnScroll(ScrollEvent e)
        {
            ScrollCount++;
            TotalScrollDelta += e.ScrollDelta;
            return true;
        }
    }

    private sealed class ActionCounterHandler : Drawable, IKeyBindingHandler<IntegrationAction>
    {
        private readonly HashSet<IntegrationAction>? handledActions;
        private readonly Dictionary<IntegrationAction, int> pressed = [];
        private readonly Dictionary<IntegrationAction, int> released = [];

        public ActionCounterHandler(params IntegrationAction[] handledActions)
        {
            if (handledActions.Length > 0)
                this.handledActions = [.. handledActions];
        }

        public int ReleasedCount => released.Values.Sum();

        public int GetPressedCount(IntegrationAction action) => pressed.GetValueOrDefault(action);

        public int GetReleasedCount(IntegrationAction action) => released.GetValueOrDefault(action);

        public bool OnPressed(KeyBindingPressEvent<IntegrationAction> e)
        {
            if (handledActions is not null && !handledActions.Contains(e.Action))
                return false;

            pressed[e.Action] = pressed.GetValueOrDefault(e.Action) + 1;
            return true;
        }

        public void OnReleased(KeyBindingReleaseEvent<IntegrationAction> e)
        {
            released[e.Action] = released.GetValueOrDefault(e.Action) + 1;
        }
    }

    private sealed class SyntheticPlatformBackend : IPlatformBackend
    {
        private readonly ConcurrentQueue<PlatformEvent> events = new();

        public uint CreatedWindowId => 77;

        public void Enqueue(PlatformEventType type, PlatformInputEvent input)
            => events.Enqueue(new PlatformEvent(type, CreatedWindowId, Input: input));

        public IDisposable InstallLogRouter(ILoggerFactory loggerFactory) => new NoopDisposable();

        public IDisposable InitializeVideo(string? videoDriverHint = null) => new NoopDisposable();

        public bool IsDummyVideoDriver() => false;

        public nint CreateWindow(string title, int width, int height, bool resizable) => new(202);

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

        public void StartTextInput(nint handle)
        {
        }

        public void StopTextInput(nint handle)
        {
        }

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
