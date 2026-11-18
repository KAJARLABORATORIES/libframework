using kajarlabs.Framework.Scene;

namespace kajarlabs.Framework.Input;

/// <summary>
/// Raised when a bound <see cref="KeyCombination"/> activates an action on an <see cref="IKeyBindingHandler{TAction}"/>.
/// </summary>
/// <typeparam name="TAction">The action type associated with the key binding.</typeparam>
public sealed class KeyBindingPressEvent<TAction> : UIEvent
{
    /// <summary>
    /// Initializes a new instance of the <see cref="KeyBindingPressEvent{TAction}"/> class.
    /// </summary>
    public KeyBindingPressEvent(
        InputState currentState,
        TAction action,
        KeyCombination keyCombination,
        bool repeat = false,
        Drawable? target = null)
        : base(currentState, target)
    {
        Action = action;
        KeyCombination = keyCombination;
        Repeat = repeat;
    }

    /// <summary>
    /// Gets the strongly-typed action triggered by the binding.
    /// </summary>
    public TAction Action { get; }

    /// <summary>
    /// Gets the <see cref="Input.KeyCombination"/> that triggered this event.
    /// </summary>
    public KeyCombination KeyCombination { get; }

    /// <summary>
    /// Gets a value indicating whether this event originated from automatic key repeat.
    /// </summary>
    public bool Repeat { get; }
}

/// <summary>
/// Raised when a previously pressed key binding is released.
/// </summary>
/// <typeparam name="TAction">The action type associated with the key binding.</typeparam>
public sealed class KeyBindingReleaseEvent<TAction> : UIEvent
{
    /// <summary>
    /// Initializes a new instance of the <see cref="KeyBindingReleaseEvent{TAction}"/> class.
    /// </summary>
    public KeyBindingReleaseEvent(
        InputState currentState,
        TAction action,
        KeyCombination keyCombination,
        Drawable? target = null)
        : base(currentState, target)
    {
        Action = action;
        KeyCombination = keyCombination;
    }

    /// <summary>
    /// Gets the strongly-typed action whose binding was released.
    /// </summary>
    public TAction Action { get; }

    /// <summary>
    /// Gets the <see cref="Input.KeyCombination"/> that was released.
    /// </summary>
    public KeyCombination KeyCombination { get; }
}
