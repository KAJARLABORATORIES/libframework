using System.Numerics;
using System.Runtime.InteropServices;

namespace kajarlabs.Framework.Input;

/// <summary>
/// Immutable value-type snapshot of pointer position, motion/wheel deltas, and mouse button state/deltas.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public readonly struct MouseState : IEquatable<MouseState>
{
    private const int max_button_count = 8;

    /// <summary>
    /// Gets an empty mouse state at the origin with no pressed buttons or deltas.
    /// </summary>
    public static readonly MouseState EMPTY = default;

    private readonly Vector2 position;
    private readonly Vector2 delta;
    private readonly Vector2 scrollDelta;
    private readonly byte pressedButtons;
    private readonly byte justPressedButtons;
    private readonly byte justReleasedButtons;
    private readonly bool isPositionValid;

    private MouseState(
        Vector2 position,
        Vector2 delta,
        Vector2 scrollDelta,
        byte pressedButtons,
        byte justPressedButtons,
        byte justReleasedButtons,
        bool isPositionValid)
    {
        this.position = position;
        this.delta = delta;
        this.scrollDelta = scrollDelta;
        this.pressedButtons = pressedButtons;
        this.justPressedButtons = justPressedButtons;
        this.justReleasedButtons = justReleasedButtons;
        this.isPositionValid = isPositionValid;
    }

    /// <summary>
    /// Gets the pointer position in viewport/screen-space pixel coordinates.
    /// </summary>
    public Vector2 Position => position;

    /// <summary>
    /// Gets the pointer movement delta in viewport/screen-space pixels since the previous update.
    /// </summary>
    public Vector2 Delta => delta;

    /// <summary>
    /// Gets the accumulated scroll wheel delta (<c>X</c> horizontal, <c>Y</c> vertical).
    /// </summary>
    public Vector2 ScrollDelta => scrollDelta;

    /// <summary>
    /// Gets a value indicating whether <see cref="Position"/> reflects a known pointer location inside the window.
    /// </summary>
    public bool IsPositionValid => isPositionValid;

    /// <summary>
    /// Gets a value indicating whether any mouse button is currently pressed.
    /// </summary>
    public bool AnyButtonDown => pressedButtons != 0;

    /// <summary>
    /// Gets the number of mouse buttons currently pressed.
    /// </summary>
    public int PressedButtonCount => BitOperations.PopCount(pressedButtons);

    /// <summary>
    /// Gets a value indicating whether <see cref="MouseButton.Left"/> is currently pressed.
    /// </summary>
    public bool LeftButton => IsButtonDown(MouseButton.Left);

    /// <summary>
    /// Gets a value indicating whether <see cref="MouseButton.Middle"/> is currently pressed.
    /// </summary>
    public bool MiddleButton => IsButtonDown(MouseButton.Middle);

    /// <summary>
    /// Gets a value indicating whether <see cref="MouseButton.Right"/> is currently pressed.
    /// </summary>
    public bool RightButton => IsButtonDown(MouseButton.Right);

    /// <summary>
    /// Returns <see langword="true"/> if <paramref name="button"/> is currently pressed.
    /// </summary>
    public bool IsButtonDown(MouseButton button)
    {
        var index = (int)button;
        return index >= 0 && index < max_button_count && (pressedButtons & (1 << index)) != 0;
    }

    /// <summary>
    /// Returns <see langword="true"/> if <paramref name="button"/> is currently released.
    /// </summary>
    public bool IsButtonUp(MouseButton button) => !IsButtonDown(button);

    /// <summary>
    /// Returns <see langword="true"/> if <paramref name="button"/> transitioned from released to pressed in this snapshot.
    /// </summary>
    public bool IsButtonJustPressed(MouseButton button)
    {
        var index = (int)button;
        return index >= 0 && index < max_button_count && (justPressedButtons & (1 << index)) != 0;
    }

    /// <summary>
    /// Returns <see langword="true"/> if <paramref name="button"/> transitioned from pressed to released in this snapshot.
    /// </summary>
    public bool IsButtonJustReleased(MouseButton button)
    {
        var index = (int)button;
        return index >= 0 && index < max_button_count && (justReleasedButtons & (1 << index)) != 0;
    }

    /// <summary>
    /// Copies currently pressed buttons in ascending <see cref="MouseButton"/> order into <paramref name="destination"/>.
    /// </summary>
    /// <returns>The number of buttons written to <paramref name="destination"/>.</returns>
    public int GetPressedButtons(Span<MouseButton> destination)
    {
        var word = (uint)pressedButtons;
        var count = 0;

        while (word != 0 && count < destination.Length)
        {
            var bit = BitOperations.TrailingZeroCount(word);
            destination[count++] = (MouseButton)bit;
            word &= word - 1;
        }

        return count;
    }

    /// <summary>
    /// Returns a newly allocated array containing all currently pressed buttons in ascending order.
    /// </summary>
    public MouseButton[] GetPressedButtons()
    {
        var count = PressedButtonCount;

        if (count == 0)
            return [];

        var result = new MouseButton[count];
        GetPressedButtons(result.AsSpan());
        return result;
    }

    /// <summary>
    /// Returns a new <see cref="MouseState"/> with <see cref="Position"/> set to <paramref name="newPosition"/>
    /// and <see cref="IsPositionValid"/> set to <see langword="true"/>.
    /// </summary>
    public MouseState WithPosition(Vector2 newPosition, bool updateDelta = true)
    {
        var nextDelta = updateDelta && isPositionValid ? newPosition - position : Vector2.Zero;
        return new MouseState(newPosition, nextDelta, scrollDelta, pressedButtons, justPressedButtons, justReleasedButtons, true);
    }

    /// <summary>
    /// Returns a new <see cref="MouseState"/> with <see cref="Delta"/> replaced by <paramref name="newDelta"/>.
    /// </summary>
    public MouseState WithDelta(Vector2 newDelta)
        => new(position, newDelta, scrollDelta, pressedButtons, justPressedButtons, justReleasedButtons, isPositionValid);

    /// <summary>
    /// Returns a new <see cref="MouseState"/> with <see cref="ScrollDelta"/> replaced by <paramref name="newScrollDelta"/>.
    /// </summary>
    public MouseState WithScrollDelta(Vector2 newScrollDelta)
        => new(position, delta, newScrollDelta, pressedButtons, justPressedButtons, justReleasedButtons, isPositionValid);

    /// <summary>
    /// Returns a new <see cref="MouseState"/> with <paramref name="button"/> marked as pressed,
    /// recording a press transition delta if it was previously released.
    /// </summary>
    public MouseState WithButtonDown(MouseButton button)
    {
        var index = (int)button;

        if (index < 0 || index >= max_button_count)
            return this;

        var mask = (byte)(1 << index);
        var nextPressed = (byte)(pressedButtons | mask);
        var nextJustPressed = (pressedButtons & mask) == 0 ? (byte)(justPressedButtons | mask) : justPressedButtons;
        var nextJustReleased = (byte)(justReleasedButtons & ~mask);
        return new MouseState(position, delta, scrollDelta, nextPressed, nextJustPressed, nextJustReleased, isPositionValid);
    }

    /// <summary>
    /// Returns a new <see cref="MouseState"/> with <paramref name="button"/> marked as released,
    /// recording a release transition delta if it was previously pressed.
    /// </summary>
    public MouseState WithButtonUp(MouseButton button)
    {
        var index = (int)button;

        if (index < 0 || index >= max_button_count)
            return this;

        var mask = (byte)(1 << index);
        var nextPressed = (byte)(pressedButtons & ~mask);
        var nextJustPressed = (byte)(justPressedButtons & ~mask);
        var nextJustReleased = (pressedButtons & mask) != 0 ? (byte)(justReleasedButtons | mask) : justReleasedButtons;
        return new MouseState(position, delta, scrollDelta, nextPressed, nextJustPressed, nextJustReleased, isPositionValid);
    }

    /// <summary>
    /// Returns a new <see cref="MouseState"/> with <paramref name="button"/> set to <paramref name="isDown"/>.
    /// </summary>
    public MouseState WithButton(MouseButton button, bool isDown)
        => isDown ? WithButtonDown(button) : WithButtonUp(button);

    /// <summary>
    /// Returns a new <see cref="MouseState"/> with motion, scroll, and button transition deltas cleared.
    /// </summary>
    public MouseState WithClearedDeltas()
        => new(position, Vector2.Zero, Vector2.Zero, pressedButtons, 0, 0, isPositionValid);

    /// <summary>
    /// Returns a new <see cref="MouseState"/> with <see cref="IsPositionValid"/> set to <see langword="false"/>
    /// and <see cref="Delta"/> cleared.
    /// </summary>
    public MouseState WithPositionInvalid()
        => new(position, Vector2.Zero, scrollDelta, pressedButtons, justPressedButtons, justReleasedButtons, false);

    /// <summary>
    /// Determines whether two <see cref="MouseState"/> snapshots are equal.
    /// </summary>
    public static bool operator ==(MouseState left, MouseState right) => left.Equals(right);

    /// <summary>
    /// Determines whether two <see cref="MouseState"/> snapshots are not equal.
    /// </summary>
    public static bool operator !=(MouseState left, MouseState right) => !left.Equals(right);

    /// <inheritdoc />
    public bool Equals(MouseState other)
        => position == other.position
            && delta == other.delta
            && scrollDelta == other.scrollDelta
            && pressedButtons == other.pressedButtons
            && justPressedButtons == other.justPressedButtons
            && justReleasedButtons == other.justReleasedButtons
            && isPositionValid == other.isPositionValid;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is MouseState other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(normalizeZero(position.X));
        hash.Add(normalizeZero(position.Y));
        hash.Add(normalizeZero(delta.X));
        hash.Add(normalizeZero(delta.Y));
        hash.Add(normalizeZero(scrollDelta.X));
        hash.Add(normalizeZero(scrollDelta.Y));
        hash.Add(pressedButtons);
        hash.Add(justPressedButtons);
        hash.Add(justReleasedButtons);
        hash.Add(isPositionValid);
        return hash.ToHashCode();
    }

    /// <inheritdoc />
    public override string ToString()
        => $"MouseState(Pos={position}, Delta={delta}, Scroll={scrollDelta}, Pressed={PressedButtonCount}, Valid={isPositionValid})";

    private static float normalizeZero(float value) => value == 0f ? 0f : value;
}
