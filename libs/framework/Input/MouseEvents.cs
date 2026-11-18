using System.Numerics;
using kajarlabs.Framework.Scene;

namespace kajarlabs.Framework.Input;

/// <summary>
/// Base payload for pointer-related <see cref="UIEvent"/> notifications.
/// </summary>
public abstract class MouseEvent : UIEvent
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MouseEvent"/> class.
    /// </summary>
    protected MouseEvent(InputState currentState, Drawable? target = null)
        : base(currentState, target)
    {
    }

    /// <summary>
    /// Gets the pointer position in viewport/screen-space pixel coordinates.
    /// </summary>
    public Vector2 ScreenSpaceMousePosition => CurrentState.Mouse.Position;

    /// <summary>
    /// Gets the pointer position transformed into <see cref="UIEvent.Target"/>'s local coordinate space.
    /// </summary>
    public Vector2 MousePosition => ToLocalSpace(ScreenSpaceMousePosition);

    /// <summary>
    /// Gets a value indicating whether any mouse button is currently pressed.
    /// </summary>
    public bool HasAnyButtonPressed => CurrentState.Mouse.AnyButtonDown;

    /// <summary>
    /// Returns <see langword="true"/> if <paramref name="button"/> is currently pressed in <see cref="UIEvent.CurrentState"/>.
    /// </summary>
    public bool IsButtonDown(MouseButton button) => CurrentState.Mouse.IsButtonDown(button);
}

/// <summary>
/// Base payload for pointer events associated with a specific <see cref="MouseButton"/>.
/// </summary>
public abstract class MouseButtonEvent : MouseEvent
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MouseButtonEvent"/> class.
    /// </summary>
    protected MouseButtonEvent(InputState currentState, MouseButton button, Drawable? target = null)
        : base(currentState, target)
    {
        Button = button;
    }

    /// <summary>
    /// Gets the mouse button associated with this event.
    /// </summary>
    public MouseButton Button { get; }
}

/// <summary>
/// Raised when the pointer moves within the viewport.
/// </summary>
public sealed class MouseMoveEvent : MouseEvent
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MouseMoveEvent"/> class.
    /// </summary>
    public MouseMoveEvent(InputState currentState, Vector2? screenSpaceDelta = null, Drawable? target = null)
        : base(currentState, target)
    {
        ScreenSpaceDelta = screenSpaceDelta ?? currentState.Mouse.Delta;
    }

    /// <summary>
    /// Gets the pointer movement delta in screen-space pixel coordinates.
    /// </summary>
    public Vector2 ScreenSpaceDelta { get; }

    /// <summary>
    /// Gets the pointer movement delta in <see cref="UIEvent.Target"/>'s local coordinate space.
    /// </summary>
    public Vector2 Delta => MousePosition - LastMousePosition;

    /// <summary>
    /// Gets the previous pointer position in screen-space pixel coordinates.
    /// </summary>
    public Vector2 ScreenSpaceLastMousePosition => ScreenSpaceMousePosition - ScreenSpaceDelta;

    /// <summary>
    /// Gets the previous pointer position in <see cref="UIEvent.Target"/>'s local coordinate space.
    /// </summary>
    public Vector2 LastMousePosition => ToLocalSpace(ScreenSpaceLastMousePosition);
}

/// <summary>
/// Raised when a mouse button is pressed down over a drawable.
/// </summary>
public sealed class MouseDownEvent : MouseButtonEvent
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MouseDownEvent"/> class.
    /// </summary>
    public MouseDownEvent(InputState currentState, MouseButton button, Drawable? target = null)
        : base(currentState, button, target)
    {
    }
}

/// <summary>
/// Raised when a mouse button is released.
/// </summary>
public sealed class MouseUpEvent : MouseButtonEvent
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MouseUpEvent"/> class.
    /// </summary>
    public MouseUpEvent(
        InputState currentState,
        MouseButton button,
        Vector2? screenSpaceMouseDownPosition = null,
        Drawable? target = null)
        : base(currentState, button, target)
    {
        ScreenSpaceMouseDownPosition = screenSpaceMouseDownPosition;
    }

    /// <summary>
    /// Gets the screen-space position where the matching mouse button press occurred, if known.
    /// </summary>
    public Vector2? ScreenSpaceMouseDownPosition { get; }

    /// <summary>
    /// Gets the local-space position where the matching mouse button press occurred, if known.
    /// </summary>
    public Vector2? MouseDownPosition
        => ScreenSpaceMouseDownPosition is { } downPos ? ToLocalSpace(downPos) : null;
}

/// <summary>
/// Raised when a mouse button press and release complete on a valid target without dragging.
/// </summary>
public sealed class ClickEvent : MouseButtonEvent
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ClickEvent"/> class.
    /// </summary>
    public ClickEvent(
        InputState currentState,
        MouseButton button,
        Vector2? screenSpaceMouseDownPosition = null,
        Drawable? target = null)
        : base(currentState, button, target)
    {
        ScreenSpaceMouseDownPosition = screenSpaceMouseDownPosition ?? currentState.Mouse.Position;
    }

    /// <summary>
    /// Gets the screen-space position where the click's initial button press occurred.
    /// </summary>
    public Vector2 ScreenSpaceMouseDownPosition { get; }

    /// <summary>
    /// Gets the local-space position where the click's initial button press occurred.
    /// </summary>
    public Vector2 MouseDownPosition => ToLocalSpace(ScreenSpaceMouseDownPosition);
}

/// <summary>
/// Raised when two consecutive clicks occur within the double-click time and distance thresholds.
/// </summary>
public sealed class DoubleClickEvent : MouseButtonEvent
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DoubleClickEvent"/> class.
    /// </summary>
    public DoubleClickEvent(
        InputState currentState,
        MouseButton button,
        Vector2? screenSpaceMouseDownPosition = null,
        Drawable? target = null)
        : base(currentState, button, target)
    {
        ScreenSpaceMouseDownPosition = screenSpaceMouseDownPosition ?? currentState.Mouse.Position;
    }

    /// <summary>
    /// Gets the screen-space position where the second click's initial button press occurred.
    /// </summary>
    public Vector2 ScreenSpaceMouseDownPosition { get; }

    /// <summary>
    /// Gets the local-space position where the second click's initial button press occurred.
    /// </summary>
    public Vector2 MouseDownPosition => ToLocalSpace(ScreenSpaceMouseDownPosition);
}

/// <summary>
/// Raised when the pointer enters or hovers over a drawable.
/// </summary>
public sealed class HoverEvent : MouseEvent
{
    /// <summary>
    /// Initializes a new instance of the <see cref="HoverEvent"/> class.
    /// </summary>
    public HoverEvent(InputState currentState, Drawable? target = null)
        : base(currentState, target)
    {
    }
}

/// <summary>
/// Raised when a previously hovered drawable ceases to be hovered.
/// </summary>
public sealed class HoverLostEvent : MouseEvent
{
    /// <summary>
    /// Initializes a new instance of the <see cref="HoverLostEvent"/> class.
    /// </summary>
    public HoverLostEvent(InputState currentState, Drawable? target = null)
        : base(currentState, target)
    {
    }
}

/// <summary>
/// Raised when the mouse wheel or trackpad scrolls over a drawable.
/// </summary>
public sealed class ScrollEvent : MouseEvent
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ScrollEvent"/> class.
    /// </summary>
    public ScrollEvent(
        InputState currentState,
        Vector2? scrollDelta = null,
        bool isPrecise = false,
        Drawable? target = null)
        : base(currentState, target)
    {
        ScrollDelta = scrollDelta ?? currentState.Mouse.ScrollDelta;
        IsPrecise = isPrecise;
    }

    /// <summary>
    /// Gets the scroll delta (<c>X</c> horizontal, <c>Y</c> vertical).
    /// </summary>
    public Vector2 ScrollDelta { get; }

    /// <summary>
    /// Gets a value indicating whether the scroll originated from a high-precision scrolling device.
    /// </summary>
    public bool IsPrecise { get; }
}

/// <summary>
/// Raised when pointer movement with a pressed button exceeds the drag threshold.
/// </summary>
public sealed class DragStartEvent : MouseButtonEvent
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DragStartEvent"/> class.
    /// </summary>
    public DragStartEvent(
        InputState currentState,
        MouseButton button,
        Vector2 screenSpaceMouseDownPosition,
        Drawable? target = null)
        : base(currentState, button, target)
    {
        ScreenSpaceMouseDownPosition = screenSpaceMouseDownPosition;
    }

    /// <summary>
    /// Gets the screen-space position where the drag's initial mouse down occurred.
    /// </summary>
    public Vector2 ScreenSpaceMouseDownPosition { get; }

    /// <summary>
    /// Gets the local-space position where the drag's initial mouse down occurred.
    /// </summary>
    public Vector2 MouseDownPosition => ToLocalSpace(ScreenSpaceMouseDownPosition);

    /// <summary>
    /// Gets the screen-space displacement from <see cref="ScreenSpaceMouseDownPosition"/> to <see cref="MouseEvent.ScreenSpaceMousePosition"/>.
    /// </summary>
    public Vector2 ScreenSpaceDelta => ScreenSpaceMousePosition - ScreenSpaceMouseDownPosition;

    /// <summary>
    /// Gets the local-space displacement from <see cref="MouseDownPosition"/> to <see cref="MouseEvent.MousePosition"/>.
    /// </summary>
    public Vector2 Delta => MousePosition - MouseDownPosition;
}

/// <summary>
/// Raised while an active drag operation continues moving.
/// </summary>
public sealed class DragEvent : MouseButtonEvent
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DragEvent"/> class.
    /// </summary>
    public DragEvent(
        InputState currentState,
        MouseButton button,
        Vector2 screenSpaceMouseDownPosition,
        Vector2? screenSpaceDelta = null,
        Drawable? target = null)
        : base(currentState, button, target)
    {
        ScreenSpaceMouseDownPosition = screenSpaceMouseDownPosition;
        ScreenSpaceDelta = screenSpaceDelta ?? currentState.Mouse.Delta;
    }

    /// <summary>
    /// Gets the screen-space position where the drag's initial mouse down occurred.
    /// </summary>
    public Vector2 ScreenSpaceMouseDownPosition { get; }

    /// <summary>
    /// Gets the local-space position where the drag's initial mouse down occurred.
    /// </summary>
    public Vector2 MouseDownPosition => ToLocalSpace(ScreenSpaceMouseDownPosition);

    /// <summary>
    /// Gets the screen-space pointer delta for this drag step.
    /// </summary>
    public Vector2 ScreenSpaceDelta { get; }

    /// <summary>
    /// Gets the local-space pointer delta for this drag step.
    /// </summary>
    public Vector2 Delta => MousePosition - LastMousePosition;

    /// <summary>
    /// Gets the previous pointer position in screen-space pixel coordinates.
    /// </summary>
    public Vector2 ScreenSpaceLastMousePosition => ScreenSpaceMousePosition - ScreenSpaceDelta;

    /// <summary>
    /// Gets the previous pointer position in <see cref="UIEvent.Target"/>'s local coordinate space.
    /// </summary>
    public Vector2 LastMousePosition => ToLocalSpace(ScreenSpaceLastMousePosition);
}

/// <summary>
/// Raised when an active drag operation finishes (button release, removal, disposal, or focus loss).
/// </summary>
public sealed class DragEndEvent : MouseButtonEvent
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DragEndEvent"/> class.
    /// </summary>
    public DragEndEvent(
        InputState currentState,
        MouseButton button,
        Vector2 screenSpaceMouseDownPosition,
        Drawable? target = null)
        : base(currentState, button, target)
    {
        ScreenSpaceMouseDownPosition = screenSpaceMouseDownPosition;
    }

    /// <summary>
    /// Gets the screen-space position where the drag's initial mouse down occurred.
    /// </summary>
    public Vector2 ScreenSpaceMouseDownPosition { get; }

    /// <summary>
    /// Gets the local-space position where the drag's initial mouse down occurred.
    /// </summary>
    public Vector2 MouseDownPosition => ToLocalSpace(ScreenSpaceMouseDownPosition);
}
