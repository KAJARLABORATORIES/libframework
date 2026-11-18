using kajarlabs.Framework.Scene;

namespace kajarlabs.Framework.Input;

/// <summary>
/// Raised when a <see cref="Drawable"/> acquires input focus.
/// </summary>
public sealed class FocusEvent : UIEvent
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FocusEvent"/> class.
    /// </summary>
    public FocusEvent(InputState currentState, Drawable? previousFocused = null, Drawable? target = null)
        : base(currentState, target)
    {
        PreviousFocused = previousFocused;
    }

    /// <summary>
    /// Gets the drawable that held focus immediately before this transition, or <see langword="null"/> if none.
    /// </summary>
    public Drawable? PreviousFocused { get; }
}

/// <summary>
/// Raised when a <see cref="Drawable"/> loses input focus.
/// </summary>
public sealed class FocusLostEvent : UIEvent
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FocusLostEvent"/> class.
    /// </summary>
    public FocusLostEvent(InputState currentState, Drawable? nextFocused = null, Drawable? target = null)
        : base(currentState, target)
    {
        NextFocused = nextFocused;
    }

    /// <summary>
    /// Gets the drawable receiving focus after this transition, or <see langword="null"/> if focus was cleared.
    /// </summary>
    public Drawable? NextFocused { get; }
}
