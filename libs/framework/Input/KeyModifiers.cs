namespace kajarlabs.Framework.Input;

/// <summary>
/// Normalized keyboard modifier flags.
/// </summary>
[Flags]
public enum KeyModifiers
{
    /// <summary>
    /// No modifier keys pressed.
    /// </summary>
    None = 0,

    /// <summary>
    /// Either left or right Shift key pressed.
    /// </summary>
    Shift = 1 << 0,

    /// <summary>
    /// Either left or right Control key pressed.
    /// </summary>
    Control = 1 << 1,

    /// <summary>
    /// Either left or right Alt/Option key pressed.
    /// </summary>
    Alt = 1 << 2,

    /// <summary>
    /// Either left or right Super/Windows/Command key pressed.
    /// </summary>
    Super = 1 << 3,

    /// <summary>
    /// All modifier flags combined.
    /// </summary>
    All = Shift | Control | Alt | Super,
}

/// <summary>
/// Provides extension members for <see cref="KeyModifiers"/>.
/// </summary>
public static class KeyModifiersExtensions
{
    extension(KeyModifiers modifiers)
    {
        /// <summary>
        /// Gets a value indicating whether <see cref="KeyModifiers.Shift"/> is set.
        /// </summary>
        public bool HasShift => (modifiers & KeyModifiers.Shift) != 0;

        /// <summary>
        /// Gets a value indicating whether <see cref="KeyModifiers.Control"/> is set.
        /// </summary>
        public bool HasControl => (modifiers & KeyModifiers.Control) != 0;

        /// <summary>
        /// Gets a value indicating whether <see cref="KeyModifiers.Alt"/> is set.
        /// </summary>
        public bool HasAlt => (modifiers & KeyModifiers.Alt) != 0;

        /// <summary>
        /// Gets a value indicating whether <see cref="KeyModifiers.Super"/> is set.
        /// </summary>
        public bool HasSuper => (modifiers & KeyModifiers.Super) != 0;

        /// <summary>
        /// Returns the <see cref="KeyModifiers"/> flag corresponding to <paramref name="key"/>,
        /// or <see cref="KeyModifiers.None"/> if <paramref name="key"/> is not a modifier key.
        /// </summary>
        public static KeyModifiers FromKey(Key key) => key.ToKeyModifiers();
    }
}
