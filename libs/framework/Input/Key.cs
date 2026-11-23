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
    /// <summary>The A key.</summary>
    A,
    /// <summary>The B key.</summary>
    B,
    /// <summary>The C key.</summary>
    C,
    /// <summary>The D key.</summary>
    D,
    /// <summary>The E key.</summary>
    E,
    /// <summary>The F key.</summary>
    F,
    /// <summary>The G key.</summary>
    G,
    /// <summary>The H key.</summary>
    H,
    /// <summary>The I key.</summary>
    I,
    /// <summary>The J key.</summary>
    J,
    /// <summary>The K key.</summary>
    K,
    /// <summary>The L key.</summary>
    L,
    /// <summary>The M key.</summary>
    M,
    /// <summary>The N key.</summary>
    N,
    /// <summary>The O key.</summary>
    O,
    /// <summary>The P key.</summary>
    P,
    /// <summary>The Q key.</summary>
    Q,
    /// <summary>The R key.</summary>
    R,
    /// <summary>The S key.</summary>
    S,
    /// <summary>The T key.</summary>
    T,
    /// <summary>The U key.</summary>
    U,
    /// <summary>The V key.</summary>
    V,
    /// <summary>The W key.</summary>
    W,
    /// <summary>The X key.</summary>
    X,
    /// <summary>The Y key.</summary>
    Y,
    /// <summary>The Z key.</summary>
    Z,

    // Top-row digits
    /// <summary>The top-row 0 key.</summary>
    Number0,
    /// <summary>The top-row 1 key.</summary>
    Number1,
    /// <summary>The top-row 2 key.</summary>
    Number2,
    /// <summary>The top-row 3 key.</summary>
    Number3,
    /// <summary>The top-row 4 key.</summary>
    Number4,
    /// <summary>The top-row 5 key.</summary>
    Number5,
    /// <summary>The top-row 6 key.</summary>
    Number6,
    /// <summary>The top-row 7 key.</summary>
    Number7,
    /// <summary>The top-row 8 key.</summary>
    Number8,
    /// <summary>The top-row 9 key.</summary>
    Number9,

    // Function keys
    /// <summary>The F1 function key.</summary>
    F1,
    /// <summary>The F2 function key.</summary>
    F2,
    /// <summary>The F3 function key.</summary>
    F3,
    /// <summary>The F4 function key.</summary>
    F4,
    /// <summary>The F5 function key.</summary>
    F5,
    /// <summary>The F6 function key.</summary>
    F6,
    /// <summary>The F7 function key.</summary>
    F7,
    /// <summary>The F8 function key.</summary>
    F8,
    /// <summary>The F9 function key.</summary>
    F9,
    /// <summary>The F10 function key.</summary>
    F10,
    /// <summary>The F11 function key.</summary>
    F11,
    /// <summary>The F12 function key.</summary>
    F12,
    /// <summary>The F13 function key.</summary>
    F13,
    /// <summary>The F14 function key.</summary>
    F14,
    /// <summary>The F15 function key.</summary>
    F15,
    /// <summary>The F16 function key.</summary>
    F16,
    /// <summary>The F17 function key.</summary>
    F17,
    /// <summary>The F18 function key.</summary>
    F18,
    /// <summary>The F19 function key.</summary>
    F19,
    /// <summary>The F20 function key.</summary>
    F20,
    /// <summary>The F21 function key.</summary>
    F21,
    /// <summary>The F22 function key.</summary>
    F22,
    /// <summary>The F23 function key.</summary>
    F23,
    /// <summary>The F24 function key.</summary>
    F24,

    // Modifiers
    /// <summary>The left Shift modifier key.</summary>
    ShiftLeft,
    /// <summary>The right Shift modifier key.</summary>
    ShiftRight,
    /// <summary>The left Control modifier key.</summary>
    ControlLeft,
    /// <summary>The right Control modifier key.</summary>
    ControlRight,
    /// <summary>The left Alt/Option modifier key.</summary>
    AltLeft,
    /// <summary>The right Alt/Option modifier key.</summary>
    AltRight,
    /// <summary>The left Super/Windows/Command modifier key.</summary>
    SuperLeft,
    /// <summary>The right Super/Windows/Command modifier key.</summary>
    SuperRight,

    // Navigation
    /// <summary>The Left Arrow key.</summary>
    Left,
    /// <summary>The Right Arrow key.</summary>
    Right,
    /// <summary>The Up Arrow key.</summary>
    Up,
    /// <summary>The Down Arrow key.</summary>
    Down,
    /// <summary>The Home key.</summary>
    Home,
    /// <summary>The End key.</summary>
    End,
    /// <summary>The Page Up key.</summary>
    PageUp,
    /// <summary>The Page Down key.</summary>
    PageDown,
    /// <summary>The Insert key.</summary>
    Insert,
    /// <summary>The Delete key.</summary>
    Delete,

    // Editing & whitespace
    /// <summary>The Backspace key.</summary>
    Backspace,
    /// <summary>The Tab key.</summary>
    Tab,
    /// <summary>The Enter/Return key.</summary>
    Enter,
    /// <summary>The Spacebar key.</summary>
    Space,
    /// <summary>The Escape key.</summary>
    Escape,

    // Punctuation & symbols
    /// <summary>The Grave/Tilde (<c>`</c>) key.</summary>
    Grave,
    /// <summary>The Minus/Hyphen (<c>-</c>) key.</summary>
    Minus,
    /// <summary>The Equals (<c>=</c>) key.</summary>
    Equals,
    /// <summary>The Left Bracket (<c>[</c>) key.</summary>
    BracketLeft,
    /// <summary>The Right Bracket (<c>]</c>) key.</summary>
    BracketRight,
    /// <summary>The Backslash (<c>\</c>) key.</summary>
    Backslash,
    /// <summary>The Semicolon (<c>;</c>) key.</summary>
    Semicolon,
    /// <summary>The Apostrophe (<c>'</c>) key.</summary>
    Apostrophe,
    /// <summary>The Comma (<c>,</c>) key.</summary>
    Comma,
    /// <summary>The Period (<c>.</c>) key.</summary>
    Period,
    /// <summary>The Forward Slash (<c>/</c>) key.</summary>
    Slash,

    // Lock keys
    /// <summary>The Caps Lock key.</summary>
    CapsLock,
    /// <summary>The Num Lock key.</summary>
    NumLock,
    /// <summary>The Scroll Lock key.</summary>
    ScrollLock,

    // System
    /// <summary>The Print Screen key.</summary>
    PrintScreen,
    /// <summary>The Pause/Break key.</summary>
    Pause,
    /// <summary>The Menu/Application key.</summary>
    Menu,

    // Numpad
    /// <summary>The numeric keypad 0 key.</summary>
    Keypad0,
    /// <summary>The numeric keypad 1 key.</summary>
    Keypad1,
    /// <summary>The numeric keypad 2 key.</summary>
    Keypad2,
    /// <summary>The numeric keypad 3 key.</summary>
    Keypad3,
    /// <summary>The numeric keypad 4 key.</summary>
    Keypad4,
    /// <summary>The numeric keypad 5 key.</summary>
    Keypad5,
    /// <summary>The numeric keypad 6 key.</summary>
    Keypad6,
    /// <summary>The numeric keypad 7 key.</summary>
    Keypad7,
    /// <summary>The numeric keypad 8 key.</summary>
    Keypad8,
    /// <summary>The numeric keypad 9 key.</summary>
    Keypad9,
    /// <summary>The numeric keypad decimal separator key.</summary>
    KeypadDecimal,
    /// <summary>The numeric keypad divide (<c>/</c>) key.</summary>
    KeypadDivide,
    /// <summary>The numeric keypad multiply (<c>*</c>) key.</summary>
    KeypadMultiply,
    /// <summary>The numeric keypad subtract (<c>-</c>) key.</summary>
    KeypadSubtract,
    /// <summary>The numeric keypad add (<c>+</c>) key.</summary>
    KeypadAdd,
    /// <summary>The numeric keypad Enter key.</summary>
    KeypadEnter,
    /// <summary>The numeric keypad equals (<c>=</c>) key.</summary>
    KeypadEquals,

    // Media
    /// <summary>The audio Mute media key.</summary>
    Mute,
    /// <summary>The Volume Down media key.</summary>
    VolumeDown,
    /// <summary>The Volume Up media key.</summary>
    VolumeUp,
    /// <summary>The Play/Pause media key.</summary>
    MediaPlayPause,
    /// <summary>The Stop media key.</summary>
    MediaStop,
    /// <summary>The Next Track media key.</summary>
    MediaNext,
    /// <summary>The Previous Track media key.</summary>
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
