using System.Numerics;
using kajarlabs.Framework.Scene;

namespace kajarlabs.Framework.Input;

/// <summary>
/// Base payload for all scene-graph input and focus events.
/// </summary>
public abstract class UIEvent
{
    /// <summary>
    /// Initializes a new instance of the <see cref="UIEvent"/> class.
    /// </summary>
    protected UIEvent(InputState currentState, Drawable? target = null)
    {
        CurrentState = currentState;
        Target = target;
    }

    /// <summary>
    /// Gets the <see cref="InputState"/> snapshot captured when this event was dispatched.
    /// </summary>
    public InputState CurrentState { get; }

    /// <summary>
    /// Gets the <see cref="Drawable"/> currently receiving this event, or <see langword="null"/> if unassigned.
    /// </summary>
    public Drawable? Target { get; internal set; }

    /// <summary>
    /// Gets the active keyboard modifiers in <see cref="CurrentState"/>.
    /// </summary>
    public KeyModifiers Modifiers => CurrentState.Modifiers;

    /// <summary>
    /// Gets a value indicating whether either Shift modifier is pressed.
    /// </summary>
    public bool ShiftPressed => CurrentState.Keyboard.ShiftPressed;

    /// <summary>
    /// Gets a value indicating whether either Control modifier is pressed.
    /// </summary>
    public bool ControlPressed => CurrentState.Keyboard.ControlPressed;

    /// <summary>
    /// Gets a value indicating whether either Alt/Option modifier is pressed.
    /// </summary>
    public bool AltPressed => CurrentState.Keyboard.AltPressed;

    /// <summary>
    /// Gets a value indicating whether either Super/Windows/Command modifier is pressed.
    /// </summary>
    public bool SuperPressed => CurrentState.Keyboard.SuperPressed;

    /// <summary>
    /// Transforms <paramref name="screenSpacePosition"/> into <see cref="Target"/>'s local coordinate space,
    /// or returns <paramref name="screenSpacePosition"/> unchanged when <see cref="Target"/> is <see langword="null"/>.
    /// </summary>
    public Vector2 ToLocalSpace(Vector2 screenSpacePosition)
        => Target is not null ? Target.ToLocalSpace(screenSpacePosition) : screenSpacePosition;

    /// <summary>
    /// Attempts to transform <paramref name="screenSpacePosition"/> into <see cref="Target"/>'s local coordinate space.
    /// </summary>
    public bool TryToLocalSpace(Vector2 screenSpacePosition, out Vector2 localPosition)
    {
        if (Target is not null)
            return Target.TryToLocalSpace(screenSpacePosition, out localPosition);

        localPosition = screenSpacePosition;
        return true;
    }
}
