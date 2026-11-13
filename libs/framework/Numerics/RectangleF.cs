using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;

namespace kajarlabs.Framework.Numerics;

/// <summary>
/// Represents an axis-aligned 2D rectangle with single-precision floating-point coordinates and dimensions.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public readonly struct RectangleF : IEquatable<RectangleF>
{
    /// <summary>
    /// Gets an empty rectangle at the origin (<c>(0, 0, 0, 0)</c>).
    /// </summary>
    public static readonly RectangleF EMPTY = new(0f, 0f, 0f, 0f);

    /// <summary>
    /// Initializes a new instance of the <see cref="RectangleF"/> struct.
    /// </summary>
    public RectangleF(float x, float y, float width, float height)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RectangleF"/> struct from position and size vectors.
    /// </summary>
    public RectangleF(Vector2 position, Vector2 size)
        : this(position.X, position.Y, size.X, size.Y)
    {
    }

    /// <summary>
    /// Gets the X coordinate of the left edge.
    /// </summary>
    public float X { get; }

    /// <summary>
    /// Gets the Y coordinate of the top edge.
    /// </summary>
    public float Y { get; }

    /// <summary>
    /// Gets the width of the rectangle.
    /// </summary>
    public float Width { get; }

    /// <summary>
    /// Gets the height of the rectangle.
    /// </summary>
    public float Height { get; }

    /// <summary>
    /// Gets the X coordinate of the left edge.
    /// </summary>
    public float Left => X;

    /// <summary>
    /// Gets the Y coordinate of the top edge.
    /// </summary>
    public float Top => Y;

    /// <summary>
    /// Gets the X coordinate of the right edge (<c>X + Width</c>).
    /// </summary>
    public float Right => X + Width;

    /// <summary>
    /// Gets the Y coordinate of the bottom edge (<c>Y + Height</c>).
    /// </summary>
    public float Bottom => Y + Height;

    /// <summary>
    /// Gets the top-left position of the rectangle.
    /// </summary>
    public Vector2 Position => new(X, Y);

    /// <summary>
    /// Gets the width and height of the rectangle as a vector.
    /// </summary>
    public Vector2 Size => new(Width, Height);

    /// <summary>
    /// Gets the top-left corner of the rectangle.
    /// </summary>
    public Vector2 TopLeft => new(Left, Top);

    /// <summary>
    /// Gets the top-right corner of the rectangle.
    /// </summary>
    public Vector2 TopRight => new(Right, Top);

    /// <summary>
    /// Gets the bottom-left corner of the rectangle.
    /// </summary>
    public Vector2 BottomLeft => new(Left, Bottom);

    /// <summary>
    /// Gets the bottom-right corner of the rectangle.
    /// </summary>
    public Vector2 BottomRight => new(Right, Bottom);

    /// <summary>
    /// Gets the center point of the rectangle.
    /// </summary>
    public Vector2 Center => new(X + (Width * 0.5f), Y + (Height * 0.5f));

    /// <summary>
    /// Gets a value indicating whether this rectangle has non-positive width or height.
    /// </summary>
    public bool IsEmpty => Width <= 0f || Height <= 0f;

    /// <summary>
    /// Creates a <see cref="RectangleF"/> from minimum and maximum edge coordinates.
    /// </summary>
    public static RectangleF FromPoints(float left, float top, float right, float bottom)
        => new(left, top, right - left, bottom - top);

    /// <summary>
    /// Computes the intersection of two rectangles, or <see cref="EMPTY"/> if they do not overlap.
    /// </summary>
    public static RectangleF Intersect(RectangleF a, RectangleF b)
    {
        if (a.IsEmpty || b.IsEmpty)
            return EMPTY;

        var left = MathF.Max(a.Left, b.Left);
        var top = MathF.Max(a.Top, b.Top);
        var right = MathF.Min(a.Right, b.Right);
        var bottom = MathF.Min(a.Bottom, b.Bottom);

        if (right <= left || bottom <= top)
            return EMPTY;

        return new RectangleF(left, top, right - left, bottom - top);
    }

    /// <summary>
    /// Computes the smallest rectangle containing both <paramref name="a"/> and <paramref name="b"/>.
    /// </summary>
    public static RectangleF Union(RectangleF a, RectangleF b)
    {
        if (a.IsEmpty)
            return b.IsEmpty ? EMPTY : b;

        if (b.IsEmpty)
            return a;

        var left = MathF.Min(a.Left, b.Left);
        var top = MathF.Min(a.Top, b.Top);
        var right = MathF.Max(a.Right, b.Right);
        var bottom = MathF.Max(a.Bottom, b.Bottom);
        return new RectangleF(left, top, right - left, bottom - top);
    }

    /// <summary>
    /// Determines whether two <see cref="RectangleF"/> values are equal.
    /// </summary>
    public static bool operator ==(RectangleF left, RectangleF right) => left.Equals(right);

    /// <summary>
    /// Determines whether two <see cref="RectangleF"/> values are not equal.
    /// </summary>
    public static bool operator !=(RectangleF left, RectangleF right) => !left.Equals(right);

    /// <summary>
    /// Determines whether <paramref name="point"/> lies inside this rectangle
    /// (inclusive of top/left, exclusive of bottom/right).
    /// </summary>
    public bool Contains(Vector2 point)
        => !IsEmpty && point.X >= Left && point.X < Right && point.Y >= Top && point.Y < Bottom;

    /// <summary>
    /// Determines whether the point (<paramref name="x"/>, <paramref name="y"/>) lies inside this rectangle.
    /// </summary>
    public bool Contains(float x, float y) => Contains(new Vector2(x, y));

    /// <summary>
    /// Determines whether this rectangle overlaps with <paramref name="other"/>.
    /// </summary>
    public bool Intersects(RectangleF other)
        => !IsEmpty && !other.IsEmpty
            && Left < other.Right && Right > other.Left
            && Top < other.Bottom && Bottom > other.Top;

    /// <summary>
    /// Returns a copy of this rectangle translated by <paramref name="delta"/>.
    /// </summary>
    public RectangleF Offset(Vector2 delta) => new(X + delta.X, Y + delta.Y, Width, Height);

    /// <summary>
    /// Returns a copy of this rectangle expanded equally on all four sides by <paramref name="amount"/>.
    /// </summary>
    public RectangleF Inflate(float amount) => Inflate(amount, amount);

    /// <summary>
    /// Returns a copy of this rectangle expanded horizontally by <paramref name="horizontal"/> and
    /// vertically by <paramref name="vertical"/> on each side.
    /// </summary>
    public RectangleF Inflate(float horizontal, float vertical)
        => new(X - horizontal, Y - vertical, Width + (horizontal * 2f), Height + (vertical * 2f));

    /// <inheritdoc />
    public bool Equals(RectangleF other)
        => normalizeZero(X).Equals(normalizeZero(other.X))
            && normalizeZero(Y).Equals(normalizeZero(other.Y))
            && normalizeZero(Width).Equals(normalizeZero(other.Width))
            && normalizeZero(Height).Equals(normalizeZero(other.Height));

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is RectangleF other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
        => HashCode.Combine(normalizeZero(X), normalizeZero(Y), normalizeZero(Width), normalizeZero(Height));

    /// <inheritdoc />
    public override string ToString()
        => string.Create(CultureInfo.InvariantCulture, $"({X:G9}, {Y:G9}, {Width:G9}, {Height:G9})");

    private static float normalizeZero(float value) => value == 0f ? 0f : value;
}
