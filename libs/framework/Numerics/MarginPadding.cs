using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;

namespace kajarlabs.Framework.Numerics;

/// <summary>
/// Represents spacing insets around the four edges of a 2D box.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public readonly struct MarginPadding : IEquatable<MarginPadding>
{
    /// <summary>
    /// Gets a <see cref="MarginPadding"/> with zero spacing on all four sides.
    /// </summary>
    public static readonly MarginPadding ZERO = new(0f);

    /// <summary>
    /// Initializes a new instance of the <see cref="MarginPadding"/> struct with uniform spacing on all sides.
    /// </summary>
    public MarginPadding(float all)
        : this(all, all, all, all)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="MarginPadding"/> struct with explicit edge values.
    /// </summary>
    public MarginPadding(float left, float top, float right, float bottom)
    {
        Left = left;
        Top = top;
        Right = right;
        Bottom = bottom;
    }

    /// <summary>
    /// Gets the spacing on the top edge.
    /// </summary>
    public float Top { get; init; }

    /// <summary>
    /// Gets the spacing on the bottom edge.
    /// </summary>
    public float Bottom { get; init; }

    /// <summary>
    /// Gets the spacing on the left edge.
    /// </summary>
    public float Left { get; init; }

    /// <summary>
    /// Gets the spacing on the right edge.
    /// </summary>
    public float Right { get; init; }

    /// <summary>
    /// Gets the combined horizontal spacing (<see cref="Left"/> + <see cref="Right"/>).
    /// </summary>
    public float TotalHorizontal => Left + Right;

    /// <summary>
    /// Gets the combined vertical spacing (<see cref="Top"/> + <see cref="Bottom"/>).
    /// </summary>
    public float TotalVertical => Top + Bottom;

    /// <summary>
    /// Gets the combined horizontal and vertical spacing as a vector.
    /// </summary>
    public Vector2 Total => new(TotalHorizontal, TotalVertical);

    /// <summary>
    /// Gets the top-left inset offset (<see cref="Left"/>, <see cref="Top"/>).
    /// </summary>
    public Vector2 TopLeftOffset => new(Left, Top);

    /// <summary>
    /// Creates a <see cref="MarginPadding"/> with uniform spacing on all four sides.
    /// </summary>
    public static MarginPadding All(float all) => new(all);

    /// <summary>
    /// Creates a <see cref="MarginPadding"/> with symmetric horizontal and vertical edge values.
    /// </summary>
    public static MarginPadding FromHorizontalVertical(float horizontal, float vertical)
        => new(horizontal, vertical, horizontal, vertical);

    /// <summary>
    /// Returns the component-wise sum of two <see cref="MarginPadding"/> values.
    /// </summary>
    public static MarginPadding operator +(MarginPadding left, MarginPadding right)
        => new(left.Left + right.Left, left.Top + right.Top, left.Right + right.Right, left.Bottom + right.Bottom);

    /// <summary>
    /// Returns the component-wise difference of two <see cref="MarginPadding"/> values.
    /// </summary>
    public static MarginPadding operator -(MarginPadding left, MarginPadding right)
        => new(left.Left - right.Left, left.Top - right.Top, left.Right - right.Right, left.Bottom - right.Bottom);

    /// <summary>
    /// Negates all four edges of <paramref name="value"/>.
    /// </summary>
    public static MarginPadding operator -(MarginPadding value)
        => new(-value.Left, -value.Top, -value.Right, -value.Bottom);

    /// <summary>
    /// Scales all four edges of <paramref name="value"/> by <paramref name="scale"/>.
    /// </summary>
    public static MarginPadding operator *(MarginPadding value, float scale)
        => new(value.Left * scale, value.Top * scale, value.Right * scale, value.Bottom * scale);

    /// <summary>
    /// Scales all four edges of <paramref name="value"/> by <paramref name="scale"/>.
    /// </summary>
    public static MarginPadding operator *(float scale, MarginPadding value)
        => value * scale;

    /// <summary>
    /// Determines whether two <see cref="MarginPadding"/> values are equal.
    /// </summary>
    public static bool operator ==(MarginPadding left, MarginPadding right) => left.Equals(right);

    /// <summary>
    /// Determines whether two <see cref="MarginPadding"/> values are not equal.
    /// </summary>
    public static bool operator !=(MarginPadding left, MarginPadding right) => !left.Equals(right);

    /// <summary>
    /// Shrinks <paramref name="rectangle"/> inward by these edge insets.
    /// </summary>
    public RectangleF Deflate(RectangleF rectangle)
        => new(rectangle.X + Left, rectangle.Y + Top, rectangle.Width - TotalHorizontal, rectangle.Height - TotalVertical);

    /// <summary>
    /// Expands <paramref name="rectangle"/> outward by these edge insets.
    /// </summary>
    public RectangleF Inflate(RectangleF rectangle)
        => new(rectangle.X - Left, rectangle.Y - Top, rectangle.Width + TotalHorizontal, rectangle.Height + TotalVertical);

    /// <inheritdoc />
    public bool Equals(MarginPadding other)
        => normalizeZero(Left).Equals(normalizeZero(other.Left))
            && normalizeZero(Top).Equals(normalizeZero(other.Top))
            && normalizeZero(Right).Equals(normalizeZero(other.Right))
            && normalizeZero(Bottom).Equals(normalizeZero(other.Bottom));

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is MarginPadding other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
        => HashCode.Combine(normalizeZero(Left), normalizeZero(Top), normalizeZero(Right), normalizeZero(Bottom));

    /// <inheritdoc />
    public override string ToString()
        => string.Create(CultureInfo.InvariantCulture, $"(L:{Left:G9}, T:{Top:G9}, R:{Right:G9}, B:{Bottom:G9})");

    private static float normalizeZero(float value) => value == 0f ? 0f : value;
}
