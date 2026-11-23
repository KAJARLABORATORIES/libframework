using System.Runtime.InteropServices;

namespace kajarlabs.Framework.Input;

/// <summary>
/// Identifies the category of input represented by an <see cref="InputKey"/>.
/// </summary>
public enum InputKeyKind
{
    /// <summary>
    /// No input key.
    /// </summary>
    None = 0,

    /// <summary>
    /// A specific physical keyboard <see cref="Key"/>.
    /// </summary>
    Key = 1,

    /// <summary>
    /// A normalized modifier flag (<c>Shift</c>, <c>Control</c>, <c>Alt</c>, or <c>Super</c>) matching either left or right physical keys.
    /// </summary>
    Modifier = 2,

    /// <summary>
    /// A <see cref="Input.MouseButton"/>.
    /// </summary>
    MouseButton = 3,

    /// <summary>
    /// A mouse wheel <see cref="Input.ScrollDirection"/>.
    /// </summary>
    ScrollDirection = 4,
}

/// <summary>
/// Identifies a discrete mouse wheel or trackpad scroll direction for key bindings.
/// </summary>
public enum ScrollDirection
{
    /// <summary>
    /// Vertical scroll up / away from the user (<c>ScrollDelta.Y &gt; 0</c>).
    /// </summary>
    Up = 0,

    /// <summary>
    /// Vertical scroll down / toward the user (<c>ScrollDelta.Y &lt; 0</c>).
    /// </summary>
    Down = 1,

    /// <summary>
    /// Horizontal scroll left (<c>ScrollDelta.X &lt; 0</c>).
    /// </summary>
    Left = 2,

    /// <summary>
    /// Horizontal scroll right (<c>ScrollDelta.X &gt; 0</c>).
    /// </summary>
    Right = 3,
}

/// <summary>
/// Lightweight, AOT-safe value type unifying keyboard keys, normalized modifiers,
/// mouse buttons, and scroll wheel directions.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public readonly struct InputKey : IEquatable<InputKey>, IComparable<InputKey>
{
    private readonly InputKeyKind kind;
    private readonly int rawValue;

    private InputKey(InputKeyKind kind, int rawValue)
    {
        this.kind = kind;
        this.rawValue = rawValue;
    }

    /// <summary>
    /// Gets an empty <see cref="InputKey"/> representing no input.
    /// </summary>
    public static InputKey None => default;

    /// <summary>
    /// Gets a normalized Shift modifier key matching either <see cref="Key.ShiftLeft"/> or <see cref="Key.ShiftRight"/>.
    /// </summary>
    public static InputKey Shift => FromModifier(KeyModifiers.Shift);

    /// <summary>
    /// Gets a normalized Control modifier key matching either <see cref="Key.ControlLeft"/> or <see cref="Key.ControlRight"/>.
    /// </summary>
    public static InputKey Control => FromModifier(KeyModifiers.Control);

    /// <summary>
    /// Gets a normalized Alt/Option modifier key matching either <see cref="Key.AltLeft"/> or <see cref="Key.AltRight"/>.
    /// </summary>
    public static InputKey Alt => FromModifier(KeyModifiers.Alt);

    /// <summary>
    /// Gets a normalized Super/Windows/Command modifier key matching either <see cref="Key.SuperLeft"/> or <see cref="Key.SuperRight"/>.
    /// </summary>
    public static InputKey Super => FromModifier(KeyModifiers.Super);

    /// <summary>
    /// Gets the <see cref="InputKey"/> for <see cref="Input.MouseButton.Left"/>.
    /// </summary>
    public static InputKey MouseLeft => FromMouseButton(Input.MouseButton.Left);

    /// <summary>
    /// Gets the <see cref="InputKey"/> for <see cref="Input.MouseButton.Middle"/>.
    /// </summary>
    public static InputKey MouseMiddle => FromMouseButton(Input.MouseButton.Middle);

    /// <summary>
    /// Gets the <see cref="InputKey"/> for <see cref="Input.MouseButton.Right"/>.
    /// </summary>
    public static InputKey MouseRight => FromMouseButton(Input.MouseButton.Right);

    /// <summary>
    /// Gets the <see cref="InputKey"/> for <see cref="Input.MouseButton.Button4"/>.
    /// </summary>
    public static InputKey MouseButton4 => FromMouseButton(Input.MouseButton.Button4);

    /// <summary>
    /// Gets the <see cref="InputKey"/> for <see cref="Input.MouseButton.Button5"/>.
    /// </summary>
    public static InputKey MouseButton5 => FromMouseButton(Input.MouseButton.Button5);

    /// <summary>
    /// Gets the <see cref="InputKey"/> for scrolling up.
    /// </summary>
    public static InputKey MouseWheelUp => FromScrollDirection(Input.ScrollDirection.Up);

    /// <summary>
    /// Gets the <see cref="InputKey"/> for scrolling down.
    /// </summary>
    public static InputKey MouseWheelDown => FromScrollDirection(Input.ScrollDirection.Down);

    /// <summary>
    /// Gets the <see cref="InputKey"/> for scrolling left.
    /// </summary>
    public static InputKey MouseWheelLeft => FromScrollDirection(Input.ScrollDirection.Left);

    /// <summary>
    /// Gets the <see cref="InputKey"/> for scrolling right.
    /// </summary>
    public static InputKey MouseWheelRight => FromScrollDirection(Input.ScrollDirection.Right);

    /// <summary>
    /// Gets the category of input represented by this <see cref="InputKey"/>.
    /// </summary>
    public InputKeyKind Kind => kind;

    /// <summary>
    /// Gets a value indicating whether this key represents a normalized modifier (<see cref="Shift"/>, <see cref="Control"/>, <see cref="Alt"/>, or <see cref="Super"/>)
    /// or a physical modifier <see cref="Input.Key"/>.
    /// </summary>
    public bool IsModifier => kind == InputKeyKind.Modifier || (kind == InputKeyKind.Key && ((Key)rawValue).IsModifier);

    /// <summary>
    /// Gets a value indicating whether this key represents a keyboard input (<see cref="InputKeyKind.Key"/> or <see cref="InputKeyKind.Modifier"/>).
    /// </summary>
    public bool IsKeyboard => kind is InputKeyKind.Key or InputKeyKind.Modifier;

    /// <summary>
    /// Gets a value indicating whether this key represents a mouse button.
    /// </summary>
    public bool IsMouse => kind == InputKeyKind.MouseButton;

    /// <summary>
    /// Gets a value indicating whether this key represents a scroll wheel direction.
    /// </summary>
    public bool IsScroll => kind == InputKeyKind.ScrollDirection;

    /// <summary>
    /// Gets the underlying <see cref="Input.Key"/> when <see cref="Kind"/> is <see cref="InputKeyKind.Key"/>, or <see cref="Key.Unknown"/> otherwise.
    /// </summary>
    public Key Key => kind == InputKeyKind.Key ? (Key)rawValue : Key.Unknown;

    /// <summary>
    /// Gets the underlying <see cref="KeyModifiers"/> flag when <see cref="Kind"/> is <see cref="InputKeyKind.Modifier"/> or a physical modifier key.
    /// </summary>
    public KeyModifiers Modifier => kind switch
    {
        InputKeyKind.Modifier => (KeyModifiers)rawValue,
        InputKeyKind.Key => ((Key)rawValue).ToKeyModifiers(),
        _ => KeyModifiers.None,
    };

    /// <summary>
    /// Gets the underlying <see cref="Input.MouseButton"/> when <see cref="Kind"/> is <see cref="InputKeyKind.MouseButton"/>, or <see langword="null"/> otherwise.
    /// </summary>
    public MouseButton? MouseButton => kind == InputKeyKind.MouseButton ? (MouseButton)rawValue : null;

    /// <summary>
    /// Gets the underlying <see cref="Input.ScrollDirection"/> when <see cref="Kind"/> is <see cref="InputKeyKind.ScrollDirection"/>, or <see langword="null"/> otherwise.
    /// </summary>
    public ScrollDirection? ScrollDirection => kind == InputKeyKind.ScrollDirection ? (ScrollDirection)rawValue : null;

    /// <summary>
    /// Creates an <see cref="InputKey"/> from a physical keyboard <see cref="Input.Key"/>.
    /// </summary>
    public static InputKey FromKey(Key key)
        => key == Key.Unknown ? None : new InputKey(InputKeyKind.Key, (int)key);

    /// <summary>
    /// Creates a normalized modifier <see cref="InputKey"/> from a single <see cref="KeyModifiers"/> flag.
    /// </summary>
    public static InputKey FromModifier(KeyModifiers modifier) => modifier switch
    {
        KeyModifiers.None => None,
        KeyModifiers.Shift or KeyModifiers.Control or KeyModifiers.Alt or KeyModifiers.Super
            => new InputKey(InputKeyKind.Modifier, (int)modifier),
        _ => throw new ArgumentOutOfRangeException(
            nameof(modifier),
            modifier,
            "Normalized modifier InputKey must specify a single modifier flag."),
    };

    /// <summary>
    /// Creates an <see cref="InputKey"/> from a <see cref="Input.MouseButton"/>.
    /// </summary>
    public static InputKey FromMouseButton(MouseButton button)
        => new(InputKeyKind.MouseButton, (int)button);

    /// <summary>
    /// Creates an <see cref="InputKey"/> from a <see cref="Input.ScrollDirection"/>.
    /// </summary>
    public static InputKey FromScrollDirection(ScrollDirection direction)
        => new(InputKeyKind.ScrollDirection, (int)direction);

    /// <summary>
    /// Implicitly converts a <see cref="Input.Key"/> to an <see cref="InputKey"/>.
    /// </summary>
    public static implicit operator InputKey(Key key) => FromKey(key);

    /// <summary>
    /// Implicitly converts a <see cref="Input.MouseButton"/> to an <see cref="InputKey"/>.
    /// </summary>
    public static implicit operator InputKey(MouseButton button) => FromMouseButton(button);

    /// <summary>
    /// Implicitly converts a <see cref="Input.ScrollDirection"/> to an <see cref="InputKey"/>.
    /// </summary>
    public static implicit operator InputKey(ScrollDirection direction) => FromScrollDirection(direction);

    /// <summary>
    /// Determines whether this <see cref="InputKey"/> matches <paramref name="triggeredKey"/>
    /// (including normalized modifiers matching left or right physical modifier keys).
    /// </summary>
    public bool Matches(InputKey triggeredKey)
    {
        if (Equals(triggeredKey))
            return true;

        if (kind == InputKeyKind.Modifier && triggeredKey.kind == InputKeyKind.Key)
            return ((Key)triggeredKey.rawValue).ToKeyModifiers() == (KeyModifiers)rawValue;

        return false;
    }

    /// <summary>
    /// Returns <see langword="true"/> if this key or button is currently held down in <paramref name="state"/>.
    /// Scroll directions are instantaneous and always return <see langword="false"/>.
    /// </summary>
    public bool IsPressed(in InputState state) => kind switch
    {
        InputKeyKind.Key => state.Keyboard.IsKeyDown((Key)rawValue),
        InputKeyKind.Modifier => (state.Keyboard.Modifiers & (KeyModifiers)rawValue) != 0,
        InputKeyKind.MouseButton => state.Mouse.IsButtonDown((MouseButton)rawValue),
        _ => false,
    };

    /// <summary>
    /// Determines whether two <see cref="InputKey"/> values are equal.
    /// </summary>
    public static bool operator ==(InputKey left, InputKey right) => left.Equals(right);

    /// <summary>
    /// Determines whether two <see cref="InputKey"/> values are not equal.
    /// </summary>
    public static bool operator !=(InputKey left, InputKey right) => !left.Equals(right);

    /// <summary>
    /// Determines whether <paramref name="left"/> precedes <paramref name="right"/> in canonical sort order.
    /// </summary>
    public static bool operator <(InputKey left, InputKey right) => left.CompareTo(right) < 0;

    /// <summary>
    /// Determines whether <paramref name="left"/> precedes or equals <paramref name="right"/> in canonical sort order.
    /// </summary>
    public static bool operator <=(InputKey left, InputKey right) => left.CompareTo(right) <= 0;

    /// <summary>
    /// Determines whether <paramref name="left"/> follows <paramref name="right"/> in canonical sort order.
    /// </summary>
    public static bool operator >(InputKey left, InputKey right) => left.CompareTo(right) > 0;

    /// <summary>
    /// Determines whether <paramref name="left"/> follows or equals <paramref name="right"/> in canonical sort order.
    /// </summary>
    public static bool operator >=(InputKey left, InputKey right) => left.CompareTo(right) >= 0;

    /// <inheritdoc />
    public bool Equals(InputKey other) => kind == other.kind && rawValue == other.rawValue;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is InputKey other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(kind, rawValue);

    /// <inheritdoc />
    public int CompareTo(InputKey other)
    {
        var kindComparison = getSortRank(kind).CompareTo(getSortRank(other.kind));
        return kindComparison != 0 ? kindComparison : rawValue.CompareTo(other.rawValue);
    }

    /// <inheritdoc />
    public override string ToString() => kind switch
    {
        InputKeyKind.None => "None",
        InputKeyKind.Modifier => ((KeyModifiers)rawValue).ToString(),
        InputKeyKind.Key => ((Key)rawValue).ToString(),
        InputKeyKind.MouseButton => $"Mouse{(MouseButton)rawValue}",
        InputKeyKind.ScrollDirection => $"Wheel{(ScrollDirection)rawValue}",
        _ => "Unknown",
    };

    private static int getSortRank(InputKeyKind value) => value switch
    {
        InputKeyKind.Modifier => 0,
        InputKeyKind.Key => 1,
        InputKeyKind.MouseButton => 2,
        InputKeyKind.ScrollDirection => 3,
        _ => 4,
    };
}
