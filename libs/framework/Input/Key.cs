namespace kajarlabs.Framework.Input;

/// <summary>
/// Platform-agnostic keyboard key identifiers.
/// </summary>
public enum Key
{
    /// <summary>
    /// An unmapped or unknown key.
    /// </summary>
    Unknown = 0,

    // Letters
    A,
    B,
    C,
    D,
    E,
    F,
    G,
    H,
    I,
    J,
    K,
    L,
    M,
    N,
    O,
    P,
    Q,
    R,
    S,
    T,
    U,
    V,
    W,
    X,
    Y,
    Z,

    // Top-row digits
    Number0,
    Number1,
    Number2,
    Number3,
    Number4,
    Number5,
    Number6,
    Number7,
    Number8,
    Number9,

    // Function keys
    F1,
    F2,
    F3,
    F4,
    F5,
    F6,
    F7,
    F8,
    F9,
    F10,
    F11,
    F12,
    F13,
    F14,
    F15,
    F16,
    F17,
    F18,
    F19,
    F20,
    F21,
    F22,
    F23,
    F24,

    // Modifiers
    ShiftLeft,
    ShiftRight,
    ControlLeft,
    ControlRight,
    AltLeft,
    AltRight,
    SuperLeft,
    SuperRight,

    // Navigation
    Left,
    Right,
    Up,
    Down,
    Home,
    End,
    PageUp,
    PageDown,
    Insert,
    Delete,

    // Editing & whitespace
    Backspace,
    Tab,
    Enter,
    Space,
    Escape,

    // Punctuation & symbols
    Grave,
    Minus,
    Equals,
    BracketLeft,
    BracketRight,
    Backslash,
    Semicolon,
    Apostrophe,
    Comma,
    Period,
    Slash,

    // Lock keys
    CapsLock,
    NumLock,
    ScrollLock,

    // System
    PrintScreen,
    Pause,
    Menu,

    // Numpad
    Keypad0,
    Keypad1,
    Keypad2,
    Keypad3,
    Keypad4,
    Keypad5,
    Keypad6,
    Keypad7,
    Keypad8,
    Keypad9,
    KeypadDecimal,
    KeypadDivide,
    KeypadMultiply,
    KeypadSubtract,
    KeypadAdd,
    KeypadEnter,
    KeypadEquals,

    // Media
    Mute,
    VolumeDown,
    VolumeUp,
    MediaPlayPause,
    MediaStop,
    MediaNext,
    MediaPrevious,
}

/// <summary>
/// Provides extension members for <see cref="Key"/>.
/// </summary>
public static class KeyExtensions
{
    extension(Key key)
    {
        /// <summary>
        /// Gets a value indicating whether this key is a modifier key (<c>Shift</c>, <c>Control</c>, <c>Alt</c>, or <c>Super</c>).
        /// </summary>
        public bool IsModifier => key.ToKeyModifiers() != KeyModifiers.None;

        /// <summary>
        /// Converts this key to its corresponding <see cref="KeyModifiers"/> flag, or <see cref="KeyModifiers.None"/> if not a modifier.
        /// </summary>
        public KeyModifiers ToKeyModifiers() => key switch
        {
            Key.ShiftLeft or Key.ShiftRight => KeyModifiers.Shift,
            Key.ControlLeft or Key.ControlRight => KeyModifiers.Control,
            Key.AltLeft or Key.AltRight => KeyModifiers.Alt,
            Key.SuperLeft or Key.SuperRight => KeyModifiers.Super,
            _ => KeyModifiers.None,
        };
    }
}
