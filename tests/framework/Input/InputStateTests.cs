using System.Numerics;
using System.Runtime.CompilerServices;
using kajarlabs.Framework.Input;

namespace kajarlabs.Framework.Tests.Input;

public sealed class InputStateTests
{
    [Fact]
    public void StateSnapshotsHaveZeroManagedReferences()
    {
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<KeyboardState>());
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<MouseState>());
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<InputState>());
    }

    [Fact]
    public void KeyboardStateTracksKeysModifiersAndImmutableSnapshots()
    {
        var initial = KeyboardState.EMPTY;
        Assert.False(initial.AnyKeyDown);
        Assert.Equal(0, initial.PressedKeyCount);
        Assert.Equal(KeyModifiers.None, initial.Modifiers);
        Assert.Empty(initial.GetPressedKeys());

        var withShiftAndA = initial
            .WithKeyDown(Key.ShiftLeft)
            .WithKeyDown(Key.A)
            .WithKeyDown(Key.MediaPrevious);

        // Original snapshot must remain untouched.
        Assert.False(initial.AnyKeyDown);
        Assert.True(initial.IsKeyUp(Key.A));

        Assert.True(withShiftAndA.AnyKeyDown);
        Assert.Equal(3, withShiftAndA.PressedKeyCount);
        Assert.True(withShiftAndA.IsKeyDown(Key.ShiftLeft));
        Assert.True(withShiftAndA.IsKeyDown(Key.A));
        Assert.True(withShiftAndA.IsKeyDown(Key.MediaPrevious));
        Assert.True(withShiftAndA.IsKeyUp(Key.B));
        Assert.True(withShiftAndA.ShiftPressed);
        Assert.Equal(KeyModifiers.Shift, withShiftAndA.Modifiers);
        Assert.Equal([Key.A, Key.ShiftLeft, Key.MediaPrevious], withShiftAndA.GetPressedKeys());

        Span<Key> span = stackalloc Key[2];
        var written = withShiftAndA.GetPressedKeys(span);
        Assert.Equal(2, written);
        Assert.Equal(Key.A, span[0]);
        Assert.Equal(Key.ShiftLeft, span[1]);

        // Holding both ShiftLeft and ShiftRight keeps Shift active when only ShiftLeft releases.
        var bothShifts = withShiftAndA.WithKeyDown(Key.ShiftRight);
        var onlyRightShift = bothShifts.WithKeyUp(Key.ShiftLeft);
        Assert.True(onlyRightShift.ShiftPressed);
        Assert.False(onlyRightShift.IsKeyDown(Key.ShiftLeft));
        Assert.True(onlyRightShift.IsKeyDown(Key.ShiftRight));

        var noShift = onlyRightShift.WithKey(Key.ShiftRight, false);
        Assert.False(noShift.ShiftPressed);
        Assert.Equal(KeyModifiers.None, noShift.Modifiers);

        // Unknown and out-of-range keys are ignored.
        Assert.Equal(noShift, noShift.WithKeyDown(Key.Unknown));
        Assert.False(noShift.WithKeyDown((Key)999).IsKeyDown((Key)999));
    }

    [Fact]
    public void MouseStateTracksPositionButtonsAndDeltasImmutably()
    {
        var initial = MouseState.EMPTY;
        Assert.False(initial.IsPositionValid);
        Assert.False(initial.AnyButtonDown);
        Assert.Equal(Vector2.Zero, initial.Position);
        Assert.Equal(Vector2.Zero, initial.Delta);
        Assert.Equal(Vector2.Zero, initial.ScrollDelta);

        // First position establishes validity with zero movement delta.
        var moved1 = initial.WithPosition(new Vector2(100f, 50f));
        Assert.True(moved1.IsPositionValid);
        Assert.Equal(new Vector2(100f, 50f), moved1.Position);
        Assert.Equal(Vector2.Zero, moved1.Delta);

        // Subsequent move computes delta from previous position.
        var moved2 = moved1.WithPosition(new Vector2(125f, 40f));
        Assert.Equal(new Vector2(125f, 40f), moved2.Position);
        Assert.Equal(new Vector2(25f, -10f), moved2.Delta);
        Assert.Equal(Vector2.Zero, moved1.Delta);

        // Button press/release deltas.
        var pressed = moved2
            .WithButtonDown(MouseButton.Left)
            .WithButtonDown(MouseButton.Button5)
            .WithScrollDelta(new Vector2(0f, 2f));

        Assert.True(pressed.LeftButton);
        Assert.True(pressed.IsButtonDown(MouseButton.Left));
        Assert.True(pressed.IsButtonDown(MouseButton.Button5));
        Assert.False(pressed.RightButton);
        Assert.True(pressed.IsButtonUp(MouseButton.Right));
        Assert.True(pressed.IsButtonJustPressed(MouseButton.Left));
        Assert.True(pressed.IsButtonJustPressed(MouseButton.Button5));
        Assert.False(pressed.IsButtonJustReleased(MouseButton.Left));
        Assert.Equal(2, pressed.PressedButtonCount);
        Assert.Equal([MouseButton.Left, MouseButton.Button5], pressed.GetPressedButtons());
        Assert.Equal(new Vector2(0f, 2f), pressed.ScrollDelta);

        var cleared = pressed.WithClearedDeltas();
        Assert.True(cleared.IsButtonDown(MouseButton.Left));
        Assert.False(cleared.IsButtonJustPressed(MouseButton.Left));
        Assert.Equal(Vector2.Zero, cleared.Delta);
        Assert.Equal(Vector2.Zero, cleared.ScrollDelta);

        var released = cleared.WithButton(MouseButton.Left, false);
        Assert.False(released.IsButtonDown(MouseButton.Left));
        Assert.True(released.IsButtonJustReleased(MouseButton.Left));
        Assert.True(released.IsButtonDown(MouseButton.Button5));

        var leftWindow = released.WithPositionInvalid();
        Assert.False(leftWindow.IsPositionValid);
        Assert.Equal(Vector2.Zero, leftWindow.Delta);
    }

    [Fact]
    public void InputStateAndMouseStateEqualityHandleNegativeZeroAndComponents()
    {
        var mousePosZero = MouseState.EMPTY.WithPosition(new Vector2(0f, 0f)).WithScrollDelta(new Vector2(0f, 0f));
        var mouseNegZero = MouseState.EMPTY.WithPosition(new Vector2(-0f, -0f)).WithScrollDelta(new Vector2(-0f, -0f));

        Assert.True(mousePosZero == mouseNegZero);
        Assert.Equal(mousePosZero, mouseNegZero);
        Assert.Equal(mousePosZero.GetHashCode(), mouseNegZero.GetHashCode());

        var kbA = KeyboardState.EMPTY.WithKeyDown(Key.ControlLeft).WithKeyDown(Key.C);
        var kbB = KeyboardState.EMPTY.WithModifiers(KeyModifiers.Control).WithKeyDown(Key.ControlLeft).WithKeyDown(Key.C);
        Assert.Equal(kbA, kbB);
        Assert.Equal(kbA.GetHashCode(), kbB.GetHashCode());

        var state1 = new InputState(kbA, mousePosZero, windowHasFocus: true);
        var state2 = InputState.EMPTY
            .WithKeyboard(kbB)
            .WithMouse(mouseNegZero)
            .WithWindowHasFocus(true);

        Assert.True(state1 == state2);
        Assert.Equal(state1, state2);
        Assert.Equal(state1.GetHashCode(), state2.GetHashCode());
        Assert.Equal(KeyModifiers.Control, state1.Modifiers);
        Assert.True(state1.WindowHasFocus);

        var unfocused = state1.WithWindowHasFocus(false);
        Assert.True(state1 != unfocused);
    }
}
