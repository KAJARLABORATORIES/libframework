using System.Numerics;
using System.Runtime.CompilerServices;
using kajarlabs.Framework.Input;
using kajarlabs.Framework.Platform;
using kajarlabs.Framework.Scene;

namespace kajarlabs.Framework.Tests.Input;

[Collection("SDL")]
public sealed class KeyBindingTests
{
    private enum TestAction
    {
        MoveLeft,
        MoveRight,
        Jump,
        Save,
        SaveAs,
        StepDown,
        ZoomIn,
        SecondaryClick,
    }

    [Fact]
    public void InputKeyHasZeroManagedReferencesAndNormalizesModifiers()
    {
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<InputKey>());

        InputKey keyA = Key.A;
        InputKey mouseLeft = MouseButton.Left;
        InputKey wheelUp = ScrollDirection.Up;

        Assert.Equal(InputKeyKind.Key, keyA.Kind);
        Assert.True(keyA.IsKeyboard);
        Assert.False(keyA.IsModifier);

        Assert.Equal(InputKeyKind.MouseButton, mouseLeft.Kind);
        Assert.True(mouseLeft.IsMouse);
        Assert.Equal(MouseButton.Left, mouseLeft.MouseButton);

        Assert.Equal(InputKeyKind.ScrollDirection, wheelUp.Kind);
        Assert.True(wheelUp.IsScroll);
        Assert.Equal(ScrollDirection.Up, wheelUp.ScrollDirection);

        Assert.True(InputKey.Shift.IsModifier);
        Assert.True(InputKey.Shift.Matches(Key.ShiftLeft));
        Assert.True(InputKey.Shift.Matches(Key.ShiftRight));
        Assert.False(InputKey.Shift.Matches(Key.ControlLeft));

        Assert.True(InputKey.Control.Matches(Key.ControlLeft));
        Assert.True(InputKey.Alt.Matches(Key.AltRight));
        Assert.True(InputKey.Super.Matches(Key.SuperLeft));

        var specificLeftShift = InputKey.FromKey(Key.ShiftLeft);
        Assert.True(specificLeftShift.Matches(Key.ShiftLeft));
        Assert.False(specificLeftShift.Matches(Key.ShiftRight));

        Assert.Throws<ArgumentOutOfRangeException>(() => InputKey.FromModifier(KeyModifiers.Shift | KeyModifiers.Control));

        var syntheticState = InputState.EMPTY.WithKeyboard(
            KeyboardState.EMPTY.WithModifiers(KeyModifiers.Control).WithKeyDown(Key.S));
        Assert.True(InputKey.Control.IsPressed(in syntheticState));
        Assert.True(InputKey.FromKey(Key.S).IsPressed(in syntheticState));
    }

    [Fact]
    public void KeyCombinationCanonicalizesEqualityAndEvaluatesExactMatch()
    {
        var combo1 = new KeyCombination(Key.S, InputKey.Control, Key.S, InputKey.None);
        var combo2 = new KeyCombination(InputKey.Control, Key.S);

        Assert.Equal(combo1, combo2);
        Assert.Equal(combo1.GetHashCode(), combo2.GetHashCode());
        Assert.Equal(2, combo1.Count);
        Assert.Equal([InputKey.Control, InputKey.FromKey(Key.S)], combo1.Keys);

        var exactState = InputState.EMPTY.WithKeyboard(
            KeyboardState.EMPTY.WithKeyDown(Key.ControlLeft).WithKeyDown(Key.S));
        Assert.True(combo1.Matches(Key.S, in exactState, requireExact: true));

        var extraShiftState = exactState.WithKeyboard(exactState.Keyboard.WithKeyDown(Key.ShiftLeft));
        Assert.True(combo1.Matches(Key.S, in extraShiftState, requireExact: false));
        Assert.False(combo1.Matches(Key.S, in extraShiftState, requireExact: true));

        var extraMouseState = exactState.WithMouse(MouseState.EMPTY.WithButtonDown(MouseButton.Right));
        Assert.False(combo1.Matches(Key.S, in extraMouseState, requireExact: true));
    }

    [Theory]
    [InlineData(SimultaneousBindingMode.None, 1, false, true)]
    [InlineData(SimultaneousBindingMode.All, 0, true, true)]
    public void SimultaneousBindingModeNoneVsAllHandlesOverlappingKeys(
        SimultaneousBindingMode mode,
        int expectedLeftReleasesAfterPressRight,
        bool expectedLeftStillActive,
        bool expectedRightActive)
    {
        using var manager = new ManualInputManager { Size = new Vector2(200f, 200f) };
        using var container = new KeyBindingContainer<TestAction>(mode)
        {
            KeyBindings =
            [
                new(Key.A, TestAction.MoveLeft),
                new(Key.D, TestAction.MoveRight),
            ],
        };
        using var handler = new RecordingBindingHandler();
        container.Add(handler);
        manager.Add(container);

        manager.PressKey(Key.A);
        Assert.Equal([TestAction.MoveLeft], handler.ActiveActions);

        manager.PressKey(Key.D);
        Assert.Equal(expectedLeftReleasesAfterPressRight, handler.Releases.Count(a => a == TestAction.MoveLeft));
        Assert.Equal(expectedLeftStillActive, handler.ActiveActions.Contains(TestAction.MoveLeft));
        Assert.Equal(expectedRightActive, handler.ActiveActions.Contains(TestAction.MoveRight));

        manager.ReleaseKey(Key.D);
        manager.ReleaseKey(Key.A);
        Assert.Empty(handler.ActiveActions);
    }

    [Fact]
    public void UniqueModeRejectsExtraHeldKeysAndMatchesExactChords()
    {
        using var manager = new ManualInputManager { Size = new Vector2(200f, 200f) };
        using var container = new KeyBindingContainer<TestAction>(SimultaneousBindingMode.Unique)
        {
            KeyBindings =
            [
                new(new KeyCombination(InputKey.Control, Key.S), TestAction.Save),
                new(new KeyCombination(InputKey.Control, InputKey.Shift, Key.S), TestAction.SaveAs),
            ],
        };
        using var handler = new RecordingBindingHandler();
        container.Add(handler);
        manager.Add(container);

        // Holding an unrelated key prevents Unique Save from firing.
        manager.PressKey(Key.W);
        manager.PressKey(Key.ControlLeft);
        manager.PressKey(Key.S);
        Assert.Empty(handler.Presses);

        manager.ReleaseKey(Key.S);
        manager.ReleaseKey(Key.W);

        // Now only Ctrl + S is held -> triggers Save.
        manager.PressKey(Key.S);
        Assert.Equal([TestAction.Save], handler.Presses);
        manager.ReleaseKey(Key.S);

        // Ctrl + Shift + S triggers SaveAs, not Save.
        manager.PressKey(Key.ShiftRight);
        manager.PressKey(Key.S);
        Assert.Equal([TestAction.Save, TestAction.SaveAs], handler.Presses);
    }

    [Fact]
    public void ChordedBindingsTakePrecedenceIgnoreRepeatsAndReleaseWhenModifierLifts()
    {
        using var manager = new ManualInputManager { Size = new Vector2(200f, 200f) };
        using var container = new SubclassedKeyBindingContainer();
        using var handler = new RecordingBindingHandler();
        container.Add(handler);
        manager.Add(container);

        Assert.False(container.HandlePositionalInput);

        manager.PressKey(Key.ControlLeft);
        manager.PressKey(Key.S);
        Assert.Equal([TestAction.Save], handler.Presses);

        // Key repeat does not re-trigger OnPressed.
        manager.PressKey(Key.S, repeat: true);
        Assert.Equal([TestAction.Save], handler.Presses);

        // Releasing Control while S stays held releases Save because the chord is broken.
        manager.ReleaseKey(Key.ControlLeft);
        Assert.Equal([TestAction.Save], handler.Releases);
        Assert.Empty(handler.ActiveActions);

        // Pressing S alone triggers StepDown.
        manager.ReleaseKey(Key.S);
        manager.PressKey(Key.S);
        Assert.Equal([TestAction.Save, TestAction.StepDown], handler.Presses);
    }

    [Fact]
    public void MouseAndScrollBindingsRespectMaskingAndReleaseCleanly()
    {
        using var manager = new ManualInputManager { Size = new Vector2(400f, 300f) };
        using var container = new KeyBindingContainer<TestAction>(SimultaneousBindingMode.All)
        {
            Position = new Vector2(20f, 20f),
            Size = new Vector2(100f, 100f),
            Masking = true,
            KeyBindings =
            [
                new(MouseButton.Right, TestAction.SecondaryClick),
                new(new KeyCombination(InputKey.Control, InputKey.MouseWheelUp), TestAction.ZoomIn),
            ],
        };
        using var handler = new RecordingBindingHandler
        {
            Size = new Vector2(200f, 100f),
        };
        container.Add(handler);
        manager.Add(container);

        Assert.True(container.HandlePositionalInput);

        // Outside masked container -> no mouse/scroll bindings trigger.
        manager.MoveMouseTo(new Vector2(160f, 60f));
        manager.PressButton(MouseButton.Right);
        manager.ReleaseButton(MouseButton.Right);
        Assert.Empty(handler.Presses);

        // Inside container -> mouse button triggers OnPressed and releases on MouseUp.
        manager.MoveMouseTo(new Vector2(60f, 60f));
        manager.PressButton(MouseButton.Right);
        Assert.Equal([TestAction.SecondaryClick], handler.Presses);
        Assert.Equal([TestAction.SecondaryClick], handler.ActiveActions);

        manager.ReleaseButton(MouseButton.Right);
        Assert.Equal([TestAction.SecondaryClick], handler.Releases);

        // Ctrl + WheelUp triggers ZoomIn and immediately fires OnReleased.
        manager.PressKey(Key.ControlLeft);
        manager.Scroll(new Vector2(0f, 1f));
        Assert.Equal([TestAction.SecondaryClick, TestAction.ZoomIn], handler.Presses);
        Assert.Equal([TestAction.SecondaryClick, TestAction.ZoomIn], handler.Releases);
        Assert.Empty(handler.ActiveActions);
    }

    [Fact]
    public void NestedKeyBindingContainersRespectFrontToBackPrecedence()
    {
        using var manager = new ManualInputManager { Size = new Vector2(300f, 300f) };
        using var outer = new KeyBindingContainer<TestAction>
        {
            KeyBindings = [new(Key.Space, TestAction.Jump)],
        };
        using var inner = new KeyBindingContainer<TestAction>
        {
            KeyBindings = [new(Key.Space, TestAction.Jump)],
        };
        using var outerHandler = new RecordingBindingHandler();
        using var innerHandler = new RecordingBindingHandler();

        inner.Add(innerHandler);
        outer.AddRange([outerHandler, inner]);
        manager.Add(outer);

        // Inner consumes Space -> outer does not trigger.
        manager.PressKey(Key.Space);
        Assert.Equal([TestAction.Jump], innerHandler.Presses);
        Assert.Empty(outerHandler.Presses);

        manager.ReleaseKey(Key.Space);
        Assert.Equal([TestAction.Jump], innerHandler.Releases);

        // Inner declines Space -> bubbles to outer.
        innerHandler.ConsumePressed = false;
        manager.PressKey(Key.Space);
        Assert.Equal([TestAction.Jump], outerHandler.Presses);

        // Inner does not swallow KeyUp -> outer releases cleanly.
        manager.ReleaseKey(Key.Space);
        Assert.Equal([TestAction.Jump], outerHandler.Releases);
    }

    [Fact]
    public void ReleasesActiveBindingsOnSwallowedKeyUpWindowFocusLossRemovalAndHiding()
    {
        using var manager = new ManualInputManager { Size = new Vector2(300f, 300f) };
        using var container = new KeyBindingContainer<TestAction>
        {
            KeyBindings = [new(Key.Space, TestAction.Jump)],
        };
        using var handler = new RecordingBindingHandler();
        using var focusedControl = new KeyUpSwallowingFocusedDrawable();
        container.Add(handler);
        manager.AddRange([container, focusedControl]);

        // Focused control does not consume KeyDown, so container activates Jump; then focused control swallows KeyUp.
        manager.ChangeFocus(focusedControl);
        manager.PressKey(Key.Space);
        Assert.Equal([TestAction.Jump], handler.ActiveActions);

        manager.ReleaseKey(Key.Space);
        // UpdateAfterChildren detects key release via InputManager.CurrentState even though OnKeyUp was swallowed.
        manager.UpdateSubTree();
        Assert.Empty(handler.ActiveActions);
        Assert.Equal([TestAction.Jump], handler.Releases);

        // WindowFocusLost releases active binding.
        manager.SetWindowFocus(true);
        manager.ChangeFocus(null);
        manager.PressKey(Key.Space);
        Assert.Equal([TestAction.Jump], handler.ActiveActions);

        manager.SetWindowFocus(false);
        Assert.Empty(handler.ActiveActions);
        Assert.Equal(2, handler.Releases.Count);

        // Hiding handler (!IsPresent) releases active binding on UpdateSubTree.
        manager.SetWindowFocus(true);
        manager.PressKey(Key.Space);
        Assert.Equal([TestAction.Jump], handler.ActiveActions);

        handler.Alpha = 0f;
        manager.UpdateSubTree();
        Assert.Empty(handler.ActiveActions);
        Assert.Equal(3, handler.Releases.Count);

        // Removing handler releases active binding on UpdateSubTree.
        handler.Alpha = 1f;
        manager.ReleaseKey(Key.Space);
        manager.PressKey(Key.Space);
        Assert.Equal([TestAction.Jump], handler.ActiveActions);

        container.Remove(handler, disposeImmediately: false);
        manager.UpdateSubTree();
        Assert.Empty(handler.ActiveActions);
        Assert.Equal(4, handler.Releases.Count);
    }

    [Fact]
    public void HeadlessGameHostDeliversKeyBindingsEndToEnd()
    {
        using var host = new HeadlessGameHost();
        using var game = new KeyBindingProbeGame(host);

        host.InjectWindowFocus(true);
        host.InjectKeyDown(Key.ControlLeft);
        host.InjectKeyDown(Key.S);

        host.RunFrames(game, 4);

        Assert.Equal([TestAction.Save, TestAction.ZoomIn], game.Handler.Presses);
        Assert.Equal([TestAction.ZoomIn, TestAction.Save], game.Handler.Releases);
        Assert.Empty(game.Handler.ActiveActions);
    }

    private sealed class SubclassedKeyBindingContainer : KeyBindingContainer<TestAction>
    {
        public override IEnumerable<KeyBinding<TestAction>> DefaultKeyBindings =>
        [
            new(Key.S, TestAction.StepDown),
            new(new KeyCombination(InputKey.Control, Key.S), TestAction.Save),
        ];
    }

    private sealed class RecordingBindingHandler : Drawable, IKeyBindingHandler<TestAction>
    {
        public bool ConsumePressed { get; set; } = true;

        public List<TestAction> Presses { get; } = [];

        public List<TestAction> Releases { get; } = [];

        public HashSet<TestAction> ActiveActions { get; } = [];

        public bool OnPressed(KeyBindingPressEvent<TestAction> e)
        {
            if (!ConsumePressed)
                return false;

            Presses.Add(e.Action);
            ActiveActions.Add(e.Action);
            return true;
        }

        public void OnReleased(KeyBindingReleaseEvent<TestAction> e)
        {
            Releases.Add(e.Action);
            ActiveActions.Remove(e.Action);
        }
    }

    private sealed class KeyUpSwallowingFocusedDrawable : Drawable
    {
        public override bool AcceptsFocus => true;

        protected override bool OnKeyUp(KeyUpEvent e) => true;
    }

    private sealed class KeyBindingProbeGame(HeadlessGameHost headlessHost) : Game
    {
        private int frame;

        public RecordingBindingHandler Handler { get; } = new()
        {
            RelativeSizeAxes = Axes.Both,
            Size = Vector2.One,
        };

        protected override void Load()
        {
            var container = new KeyBindingContainer<TestAction>(SimultaneousBindingMode.All)
            {
                RelativeSizeAxes = Axes.Both,
                Size = Vector2.One,
                KeyBindings =
                [
                    new(new KeyCombination(InputKey.Control, Key.S), TestAction.Save),
                    new(new KeyCombination(InputKey.Control, InputKey.MouseWheelUp), TestAction.ZoomIn),
                ],
            };
            container.Add(Handler);
            Root.Add(container);
        }

        protected override void Update()
        {
            frame++;

            if (frame == 1)
            {
                headlessHost.InjectMouseMove(new Vector2(100f, 100f));
                headlessHost.InjectMouseScroll(new Vector2(0f, 1f), new Vector2(100f, 100f));
            }
            else if (frame == 2)
            {
                headlessHost.InjectWindowFocus(false);
            }
        }
    }
}
