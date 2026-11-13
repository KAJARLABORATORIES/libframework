using System.Globalization;
using System.Runtime.InteropServices;

namespace kajarlabs.Framework.Numerics;

/// <summary>
/// Represents an axis-aligned 2D rectangle with integer coordinates and dimensions.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public readonly struct RectangleI : IEquatable<RectangleI>
{
    /// <summary>
    /// Gets an empty rectangle at the origin (<c>(0, 0, 0, 0)</c>).
    /// </summary>
    public static readonly RectangleI EMPTY = new(0, 0, 0, 0);

    /// <summary>
    /// Initializes a new instance of the <see cref="RectangleI"/> struct.
    /// </summary>
    public RectangleI(int x, int y, int width, int height)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    /// <summary>
    /// Gets the X coordinate of the left edge.
    /// </summary>
    public int X { get; }

    /// <summary>
    /// Gets the Y coordinate of the top edge.
    /// </summary>
    public int Y { get; }

    /// <summary>
    /// Gets the width of the rectangle.
    /// </summary>
    public int Width { get; }

    /// <summary>
    /// Gets the height of the rectangle.
    /// </summary>
    public int Height { get; }

    /// <summary>
    /// Gets the X coordinate of the left edge.
    /// </summary>
    public int Left => X;

    /// <summary>
    /// Gets the Y coordinate of the top edge.
    /// </summary>
    public int Top => Y;

    /// <summary>
    /// Gets the X coordinate of the right edge (<c>X + Width</c>).
    /// </summary>
    public int Right => X + Width;

    /// <summary>
    /// Gets the Y coordinate of the bottom edge (<c>Y + Height</c>).
    /// </summary>
    public int Bottom => Y + Height;

    /// <summary>
    /// Gets a value indicating whether this rectangle has non-positive width or height.
    /// </summary>
    public bool IsEmpty => Width <= 0 || Height <= 0;

    /// <summary>
    /// Computes the intersection of two rectangles, or <see cref="EMPTY"/> if they do not overlap.
    /// </summary>
    public static RectangleI Intersect(RectangleI a, RectangleI b)
    {
        if (a.IsEmpty || b.IsEmpty)
            return EMPTY;

        var left = Math.Max(a.Left, b.Left);
        var top = Math.Max(a.Top, b.Top);
        var right = Math.Min(a.Right, b.Right);
        var bottom = Math.Min(a.Bottom, b.Bottom);

        if (right <= left || bottom <= top)
            return EMPTY;

        return new RectangleI(left, top, right - left, bottom - top);
    }

    /// <summary>
    /// Computes the smallest rectangle containing both <paramref name="a"/> and <paramref name="b"/>.
    /// </summary>
    public static RectangleI Union(RectangleI a, RectangleI b)
    {
        if (a.IsEmpty)
            return b.IsEmpty ? EMPTY : b;

        if (b.IsEmpty)
            return a;

        var left = Math.Min(a.Left, b.Left);
        var top = Math.Min(a.Top, b.Top);
        var right = Math.Max(a.Right, b.Right);
        var bottom = Math.Max(a.Bottom, b.Bottom);
        return new RectangleI(left, top, right - left, bottom - top);
    }

    /// <summary>
    /// Determines whether two <see cref="RectangleI"/> values are equal.
    /// </summary>
    public static bool operator ==(RectangleI left, RectangleI right) => left.Equals(right);

    /// <summary>
    /// Determines whether two <see cref="RectangleI"/> values are not equal.
    /// </summary>
    public static bool operator !=(RectangleI left, RectangleI right) => !left.Equals(right);

    /// <summary>
    /// Determines whether the point (<paramref name="x"/>, <paramref name="y"/>) lies inside this rectangle
    /// (inclusive of top/left, exclusive of bottom/right).
    /// </summary>
    public bool Contains(int x, int y)
        => !IsEmpty && x >= Left && x < Right && y >= Top && y < Bottom;

    /// <summary>
    /// Determines whether this rectangle overlaps with <paramref name="other"/>.
    /// </summary>
    public bool Intersects(RectangleI other)
        => !IsEmpty && !other.IsEmpty
            && Left < other.Right && Right > other.Left
            && Top < other.Bottom && Bottom > other.Top;

    /// <summary>
    /// Returns a copy of this rectangle translated by (<paramref name="dx"/>, <paramref name="dy"/>).
    /// </summary>
    public RectangleI Offset(int dx, int dy) => new(X + dx, Y + dy, Width, Height);

    /// <summary>
    /// Clamps this rectangle to the interior of <paramref name="bounds"/>.
    /// </summary>
    public RectangleI Clamp(RectangleI bounds) => Intersect(this, bounds);

    /// <summary>
    /// Converts this integer rectangle to a <see cref="RectangleF"/>.
    /// </summary>
    public RectangleF ToRectangleF() => new(X, Y, Width, Height);

    /// <inheritdoc />
    public bool Equals(RectangleI other)
        => X == other.X && Y == other.Y && Width == other.Width && Height == other.Height;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is RectangleI other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(X, Y, Width, Height);

    /// <inheritdoc />
    public override string ToString()
        => string.Create(CultureInfo.InvariantCulture, $"({X}, {Y}, {Width}, {Height})");
}
