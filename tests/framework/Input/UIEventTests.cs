using System.Numerics;
using kajarlabs.Framework.Input;
using kajarlabs.Framework.Scene;

namespace kajarlabs.Framework.Tests.Input;

public sealed class UIEventTests
{
    [Fact]
    public void UIEventForwardsModifiersAndFallsBackWithoutTarget()
    {
        var state = InputState.EMPTY
            .WithKeyboard(KeyboardState.EMPTY
                .WithKeyDown(Key.ShiftLeft)
                .WithKeyDown(Key.ControlRight))
            .WithMouse(MouseState.EMPTY.WithPosition(new Vector2(120f, 80f)));

        var evt = new MouseDownEvent(state, MouseButton.Left);

        Assert.Null(evt.Target);
        Assert.Equal(KeyModifiers.Shift | KeyModifiers.Control, evt.Modifiers);
        Assert.True(evt.ShiftPressed);
        Assert.True(evt.ControlPressed);
        Assert.False(evt.AltPressed);
        Assert.False(evt.SuperPressed);
        Assert.Equal(new Vector2(120f, 80f), evt.ScreenSpaceMousePosition);
        Assert.Equal(new Vector2(120f, 80f), evt.MousePosition);
        Assert.True(evt.TryToLocalSpace(new Vector2(10f, 20f), out var local));
        Assert.Equal(new Vector2(10f, 20f), local);
    }

    [Fact]
    public void MouseEventsTransformScreenCoordinatesIntoTargetLocalSpace()
    {
        using var target = new Drawable
        {
            Position = new Vector2(100f, 50f),
            Size = new Vector2(40f, 20f),
            Scale = new Vector2(2f, 2f),
            Rotation = 90f,
        };

        var screenPos = target.ToScreenSpace(new Vector2(10f, 5f));
        var screenDownPos = target.ToScreenSpace(new Vector2(4f, 2f));
        var prevScreenPos = target.ToScreenSpace(new Vector2(8f, 4f));

        var mouse = MouseState.EMPTY
            .WithPosition(prevScreenPos)
            .WithPosition(screenPos)
            .WithButtonDown(MouseButton.Left);

        var state = InputState.EMPTY.WithMouse(mouse).WithWindowHasFocus(true);

        var move = new MouseMoveEvent(state, target: target);
        Assert.Equal(10f, move.MousePosition.X, precision: 4);
        Assert.Equal(5f, move.MousePosition.Y, precision: 4);
        Assert.Equal(8f, move.LastMousePosition.X, precision: 4);
        Assert.Equal(4f, move.LastMousePosition.Y, precision: 4);
        Assert.Equal(2f, move.Delta.X, precision: 4);
        Assert.Equal(1f, move.Delta.Y, precision: 4);
        Assert.True(move.IsButtonDown(MouseButton.Left));
        Assert.True(move.HasAnyButtonPressed);

        var drag = new DragEvent(state, MouseButton.Left, screenDownPos, target: target);
        Assert.Equal(MouseButton.Left, drag.Button);
        Assert.Equal(4f, drag.MouseDownPosition.X, precision: 4);
        Assert.Equal(2f, drag.MouseDownPosition.Y, precision: 4);
        Assert.Equal(2f, drag.Delta.X, precision: 4);
        Assert.Equal(1f, drag.Delta.Y, precision: 4);

        var dragStart = new DragStartEvent(state, MouseButton.Left, screenDownPos, target);
        Assert.Equal(6f, dragStart.Delta.X, precision: 4);
        Assert.Equal(3f, dragStart.Delta.Y, precision: 4);

        var click = new ClickEvent(state, MouseButton.Left, screenDownPos, target);
        var doubleClick = new DoubleClickEvent(state, MouseButton.Left, screenDownPos, target);
        var mouseUp = new MouseUpEvent(state, MouseButton.Left, screenDownPos, target);
        var dragEnd = new DragEndEvent(state, MouseButton.Left, screenDownPos, target);

        Assert.Equal(4f, click.MouseDownPosition.X, precision: 4);
        Assert.Equal(2f, doubleClick.MouseDownPosition.Y, precision: 4);
        Assert.NotNull(mouseUp.MouseDownPosition);
        Assert.Equal(4f, mouseUp.MouseDownPosition.Value.X, precision: 4);
        Assert.Equal(4f, dragEnd.MouseDownPosition.X, precision: 4);
    }

    [Fact]
    public void NonInvertibleTargetReportsFailureFromTryToLocalSpace()
    {
        using var zeroScaleTarget = new Drawable
        {
            Size = new Vector2(50f, 50f),
            Scale = Vector2.Zero,
        };

        var state = InputState.EMPTY.WithMouse(MouseState.EMPTY.WithPosition(new Vector2(10f, 10f)));
        var hover = new HoverEvent(state, zeroScaleTarget);

        Assert.False(hover.TryToLocalSpace(new Vector2(10f, 10f), out var local));
        Assert.Equal(Vector2.Zero, local);
        Assert.Equal(Vector2.Zero, hover.MousePosition);
    }

    [Fact]
    public void ScrollKeyboardTextAndFocusEventsExposeExpectedPayloads()
    {
        using var first = new Drawable();
        using var second = new Drawable();

        var state = InputState.EMPTY
            .WithKeyboard(KeyboardState.EMPTY.WithKeyDown(Key.Enter))
            .WithMouse(MouseState.EMPTY.WithScrollDelta(new Vector2(1.5f, -3f)))
            .WithWindowHasFocus(true);

        var hoverLost = new HoverLostEvent(state, first);
        Assert.Same(first, hoverLost.Target);

        var scroll = new ScrollEvent(state, isPrecise: true, target: first);
        Assert.Equal(new Vector2(1.5f, -3f), scroll.ScrollDelta);
        Assert.True(scroll.IsPrecise);

        var keyDown = new KeyDownEvent(state, Key.Enter, repeat: true, target: second);
        Assert.Equal(Key.Enter, keyDown.Key);
        Assert.True(keyDown.Repeat);

        var keyUp = new KeyUpEvent(state, Key.Enter, target: second);
        Assert.Equal(Key.Enter, keyUp.Key);

        var text = new TextInputEvent(state, "abc", target: second);
        Assert.Equal("abc", text.Text);
        Assert.Throws<ArgumentNullException>(() => new TextInputEvent(state, null!));

        var focus = new FocusEvent(state, previousFocused: first, target: second);
        Assert.Same(first, focus.PreviousFocused);
        Assert.Same(second, focus.Target);

        var focusLost = new FocusLostEvent(state, nextFocused: second, target: first);
        Assert.Same(second, focusLost.NextFocused);
        Assert.Same(first, focusLost.Target);
    }
}
