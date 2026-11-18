using System.Numerics;
using System.Runtime.InteropServices;

namespace kajarlabs.Framework.Input;

/// <summary>
/// Immutable value-type snapshot of pressed keyboard keys and active <see cref="KeyModifiers"/>.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public readonly struct KeyboardState : IEquatable<KeyboardState>
{
    private const int max_key_count = 256;

    /// <summary>
    /// Gets an empty keyboard state with no pressed keys or modifiers.
    /// </summary>
    public static readonly KeyboardState EMPTY = default;

    private readonly ulong bits0;
    private readonly ulong bits1;
    private readonly ulong bits2;
    private readonly ulong bits3;
    private readonly KeyModifiers modifiers;

    private KeyboardState(ulong bits0, ulong bits1, ulong bits2, ulong bits3, KeyModifiers modifiers)
    {
        this.bits0 = bits0;
        this.bits1 = bits1;
        this.bits2 = bits2;
        this.bits3 = bits3;
        this.modifiers = modifiers;
    }

    /// <summary>
    /// Gets the active keyboard modifier flags for this snapshot.
    /// </summary>
    public KeyModifiers Modifiers => modifiers;

    /// <summary>
    /// Gets a value indicating whether either Shift modifier is active.
    /// </summary>
    public bool ShiftPressed => modifiers.HasShift;

    /// <summary>
    /// Gets a value indicating whether either Control modifier is active.
    /// </summary>
    public bool ControlPressed => modifiers.HasControl;

    /// <summary>
    /// Gets a value indicating whether either Alt/Option modifier is active.
    /// </summary>
    public bool AltPressed => modifiers.HasAlt;

    /// <summary>
    /// Gets a value indicating whether either Super/Windows/Command modifier is active.
    /// </summary>
    public bool SuperPressed => modifiers.HasSuper;

    /// <summary>
    /// Gets a value indicating whether any key is currently pressed.
    /// </summary>
    public bool AnyKeyDown => (bits0 | bits1 | bits2 | bits3) != 0;

    /// <summary>
    /// Gets the number of keys currently pressed.
    /// </summary>
    public int PressedKeyCount
        => BitOperations.PopCount(bits0)
            + BitOperations.PopCount(bits1)
            + BitOperations.PopCount(bits2)
            + BitOperations.PopCount(bits3);

    /// <summary>
    /// Returns <see langword="true"/> if <paramref name="key"/> is currently pressed.
    /// </summary>
    public bool IsKeyDown(Key key)
    {
        var index = (int)key;

        if (index <= 0 || index >= max_key_count)
            return false;

        return (index >> 6) switch
        {
            0 => (bits0 & (1UL << (index & 63))) != 0,
            1 => (bits1 & (1UL << (index & 63))) != 0,
            2 => (bits2 & (1UL << (index & 63))) != 0,
            _ => (bits3 & (1UL << (index & 63))) != 0,
        };
    }

    /// <summary>
    /// Returns <see langword="true"/> if <paramref name="key"/> is currently released.
    /// </summary>
    public bool IsKeyUp(Key key) => !IsKeyDown(key);

    /// <summary>
    /// Copies currently pressed keys in ascending <see cref="Key"/> order into <paramref name="destination"/>.
    /// </summary>
    /// <returns>The number of keys written to <paramref name="destination"/>.</returns>
    public int GetPressedKeys(Span<Key> destination)
    {
        var written = 0;
        written += writeBits(bits0, 0, destination[written..]);
        written += writeBits(bits1, 64, destination[written..]);
        written += writeBits(bits2, 128, destination[written..]);
        written += writeBits(bits3, 192, destination[written..]);
        return written;
    }

    /// <summary>
    /// Returns a newly allocated array containing all currently pressed keys in ascending order.
    /// </summary>
    public Key[] GetPressedKeys()
    {
        var count = PressedKeyCount;

        if (count == 0)
            return [];

        var result = new Key[count];
        GetPressedKeys(result.AsSpan());
        return result;
    }

    /// <summary>
    /// Returns a new <see cref="KeyboardState"/> with <paramref name="key"/> marked as pressed
    /// and modifier flags updated accordingly.
    /// </summary>
    public KeyboardState WithKeyDown(Key key)
    {
        var index = (int)key;

        if (index <= 0 || index >= max_key_count)
            return this;

        var b0 = bits0;
        var b1 = bits1;
        var b2 = bits2;
        var b3 = bits3;
        var mask = 1UL << (index & 63);

        switch (index >> 6)
        {
            case 0:
                b0 |= mask;
                break;

            case 1:
                b1 |= mask;
                break;

            case 2:
                b2 |= mask;
                break;

            default:
                b3 |= mask;
                break;
        }

        var nextModifiers = modifiers | key.ToKeyModifiers();
        return new KeyboardState(b0, b1, b2, b3, nextModifiers);
    }

    /// <summary>
    /// Returns a new <see cref="KeyboardState"/> with <paramref name="key"/> marked as released
    /// and modifier flags updated if no corresponding physical modifier key remains pressed.
    /// </summary>
    public KeyboardState WithKeyUp(Key key)
    {
        var index = (int)key;

        if (index <= 0 || index >= max_key_count)
            return this;

        var b0 = bits0;
        var b1 = bits1;
        var b2 = bits2;
        var b3 = bits3;
        var mask = ~(1UL << (index & 63));

        switch (index >> 6)
        {
            case 0:
                b0 &= mask;
                break;

            case 1:
                b1 &= mask;
                break;

            case 2:
                b2 &= mask;
                break;

            default:
                b3 &= mask;
                break;
        }

        var updated = new KeyboardState(b0, b1, b2, b3, modifiers);
        var keyModifier = key.ToKeyModifiers();

        if (keyModifier == KeyModifiers.None)
            return updated;

        var keepModifier = keyModifier switch
        {
            KeyModifiers.Shift => updated.IsKeyDown(Key.ShiftLeft) || updated.IsKeyDown(Key.ShiftRight),
            KeyModifiers.Control => updated.IsKeyDown(Key.ControlLeft) || updated.IsKeyDown(Key.ControlRight),
            KeyModifiers.Alt => updated.IsKeyDown(Key.AltLeft) || updated.IsKeyDown(Key.AltRight),
            KeyModifiers.Super => updated.IsKeyDown(Key.SuperLeft) || updated.IsKeyDown(Key.SuperRight),
            _ => false,
        };

        return keepModifier ? updated : new KeyboardState(b0, b1, b2, b3, modifiers & ~keyModifier);
    }

    /// <summary>
    /// Returns a new <see cref="KeyboardState"/> with <paramref name="key"/> set to <paramref name="isDown"/>.
    /// </summary>
    public KeyboardState WithKey(Key key, bool isDown)
        => isDown ? WithKeyDown(key) : WithKeyUp(key);

    /// <summary>
    /// Returns a new <see cref="KeyboardState"/> with <see cref="Modifiers"/> replaced by <paramref name="newModifiers"/>.
    /// </summary>
    public KeyboardState WithModifiers(KeyModifiers newModifiers)
        => new(bits0, bits1, bits2, bits3, newModifiers);

    /// <summary>
    /// Determines whether two <see cref="KeyboardState"/> snapshots are equal.
    /// </summary>
    public static bool operator ==(KeyboardState left, KeyboardState right) => left.Equals(right);

    /// <summary>
    /// Determines whether two <see cref="KeyboardState"/> snapshots are not equal.
    /// </summary>
    public static bool operator !=(KeyboardState left, KeyboardState right) => !left.Equals(right);

    /// <inheritdoc />
    public bool Equals(KeyboardState other)
        => bits0 == other.bits0
            && bits1 == other.bits1
            && bits2 == other.bits2
            && bits3 == other.bits3
            && modifiers == other.modifiers;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is KeyboardState other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(bits0, bits1, bits2, bits3, modifiers);

    /// <inheritdoc />
    public override string ToString() => $"KeyboardState(Pressed={PressedKeyCount}, Modifiers={modifiers})";

    private static int writeBits(ulong word, int baseIndex, Span<Key> destination)
    {
        var count = 0;

        while (word != 0 && count < destination.Length)
        {
            var bit = BitOperations.TrailingZeroCount(word);
            destination[count++] = (Key)(baseIndex + bit);
            word &= word - 1;
        }

        return count;
    }
}
