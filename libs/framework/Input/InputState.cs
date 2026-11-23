using System.Runtime.InteropServices;

namespace kajarlabs.Framework.Input;

/// <summary>
/// Immutable value-type snapshot combining keyboard, mouse, and window focus state at a point in time.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public readonly struct InputState : IEquatable<InputState>
{
    /// <summary>
    /// Gets an empty input state with no pressed keys/buttons and no window focus.
    /// </summary>
    public static readonly InputState EMPTY;

    private readonly KeyboardState keyboard;
    private readonly MouseState mouse;
    private readonly bool windowHasFocus;

    /// <summary>
    /// Initializes a new instance of the <see cref="InputState"/> struct.
    /// </summary>
    public InputState(KeyboardState keyboard, MouseState mouse, bool windowHasFocus = false)
    {
        this.keyboard = keyboard;
        this.mouse = mouse;
        this.windowHasFocus = windowHasFocus;
    }

    /// <summary>
    /// Gets the keyboard snapshot.
    /// </summary>
    public KeyboardState Keyboard => keyboard;

    /// <summary>
    /// Gets the mouse snapshot.
    /// </summary>
    public MouseState Mouse => mouse;

    /// <summary>
    /// Gets a value indicating whether the host window currently has input focus.
    /// </summary>
    public bool WindowHasFocus => windowHasFocus;

    /// <summary>
    /// Gets the active keyboard modifier flags from <see cref="Keyboard"/>.
    /// </summary>
    public KeyModifiers Modifiers => keyboard.Modifiers;

    /// <summary>
    /// Returns a new <see cref="InputState"/> with <see cref="Keyboard"/> replaced by <paramref name="newKeyboard"/>.
    /// </summary>
    public InputState WithKeyboard(KeyboardState newKeyboard) => new(newKeyboard, mouse, windowHasFocus);

    /// <summary>
    /// Returns a new <see cref="InputState"/> with <see cref="Mouse"/> replaced by <paramref name="newMouse"/>.
    /// </summary>
    public InputState WithMouse(MouseState newMouse) => new(keyboard, newMouse, windowHasFocus);

    /// <summary>
    /// Returns a new <see cref="InputState"/> with <see cref="WindowHasFocus"/> set to <paramref name="hasFocus"/>.
    /// </summary>
    public InputState WithWindowHasFocus(bool hasFocus) => new(keyboard, mouse, hasFocus);

    /// <summary>
    /// Determines whether two <see cref="InputState"/> snapshots are equal.
    /// </summary>
    public static bool operator ==(InputState left, InputState right) => left.Equals(right);

    /// <summary>
    /// Determines whether two <see cref="InputState"/> snapshots are not equal.
    /// </summary>
    public static bool operator !=(InputState left, InputState right) => !left.Equals(right);

    /// <inheritdoc />
    public bool Equals(InputState other)
        => keyboard.Equals(other.keyboard)
            && mouse.Equals(other.mouse)
            && windowHasFocus == other.windowHasFocus;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is InputState other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(keyboard, mouse, windowHasFocus);

    /// <inheritdoc />
    public override string ToString() => $"InputState(Focus={windowHasFocus}, {keyboard}, {mouse})";
}
