using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;

namespace kajarlabs.Framework.Numerics;

/// <summary>
/// Represents a 2D quadrilateral defined by four corner vertices in top-left, top-right,
/// bottom-right, bottom-left order.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public readonly struct Quad : IEquatable<Quad>
{
    private const float area_epsilon = 1e-6f;

    /// <summary>
    /// Gets an empty quad with all four corners at the origin.
    /// </summary>
    public static readonly Quad EMPTY = new(Vector2.Zero, Vector2.Zero, Vector2.Zero, Vector2.Zero);

    /// <summary>
    /// Initializes a new instance of the <see cref="Quad"/> struct from explicit corner positions.
    /// </summary>
    public Quad(Vector2 topLeft, Vector2 topRight, Vector2 bottomRight, Vector2 bottomLeft)
    {
        TopLeft = topLeft;
        TopRight = topRight;
        BottomRight = bottomRight;
        BottomLeft = bottomLeft;
    }

    /// <summary>
    /// Initializes an axis-aligned <see cref="Quad"/> from position and dimensions.
    /// </summary>
    public Quad(float x, float y, float width, float height)
        : this(
            new Vector2(x, y),
            new Vector2(x + width, y),
            new Vector2(x + width, y + height),
            new Vector2(x, y + height))
    {
    }

    /// <summary>
    /// Initializes an axis-aligned <see cref="Quad"/> from a <see cref="RectangleF"/>.
    /// </summary>
    public Quad(RectangleF rectangle)
        : this(rectangle.TopLeft, rectangle.TopRight, rectangle.BottomRight, rectangle.BottomLeft)
    {
    }

    /// <summary>
    /// Gets the top-left corner position.
    /// </summary>
    public Vector2 TopLeft { get; }

    /// <summary>
    /// Gets the top-right corner position.
    /// </summary>
    public Vector2 TopRight { get; }

    /// <summary>
    /// Gets the bottom-right corner position.
    /// </summary>
    public Vector2 BottomRight { get; }

    /// <summary>
    /// Gets the bottom-left corner position.
    /// </summary>
    public Vector2 BottomLeft { get; }

    /// <summary>
    /// Gets the distance along the top edge from <see cref="TopLeft"/> to <see cref="TopRight"/>.
    /// </summary>
    public float Width => Vector2.Distance(TopLeft, TopRight);

    /// <summary>
    /// Gets the distance along the left edge from <see cref="TopLeft"/> to <see cref="BottomLeft"/>.
    /// </summary>
    public float Height => Vector2.Distance(TopLeft, BottomLeft);

    /// <summary>
    /// Gets the edge lengths (<see cref="Width"/>, <see cref="Height"/>) of the quad.
    /// </summary>
    public Vector2 Size => new(Width, Height);

    /// <summary>
    /// Gets the arithmetic center of the four corners.
    /// </summary>
    public Vector2 Center => (TopLeft + TopRight + BottomRight + BottomLeft) * 0.25f;

    /// <summary>
    /// Gets the unsigned area of the quad.
    /// </summary>
    public float Area => 0.5f * MathF.Abs(signedDoubleArea());

    /// <summary>
    /// Gets the axis-aligned bounding box enclosing all four corners of this quad.
    /// </summary>
    public RectangleF AABB
    {
        get
        {
            var minX = MathF.Min(MathF.Min(TopLeft.X, TopRight.X), MathF.Min(BottomRight.X, BottomLeft.X));
            var minY = MathF.Min(MathF.Min(TopLeft.Y, TopRight.Y), MathF.Min(BottomRight.Y, BottomLeft.Y));
            var maxX = MathF.Max(MathF.Max(TopLeft.X, TopRight.X), MathF.Max(BottomRight.X, BottomLeft.X));
            var maxY = MathF.Max(MathF.Max(TopLeft.Y, TopRight.Y), MathF.Max(BottomRight.Y, BottomLeft.Y));
            return RectangleF.FromPoints(minX, minY, maxX, maxY);
        }
    }

    /// <summary>
    /// Creates an axis-aligned <see cref="Quad"/> matching <paramref name="rectangle"/>.
    /// </summary>
    public static Quad FromRectangle(RectangleF rectangle) => new(rectangle);

    /// <summary>
    /// Implicitly converts a <see cref="RectangleF"/> to an axis-aligned <see cref="Quad"/>.
    /// </summary>
    public static implicit operator Quad(RectangleF rectangle) => FromRectangle(rectangle);

    /// <summary>
    /// Transforms all four corners of <paramref name="quad"/> by row-vector affine <paramref name="matrix"/>.
    /// </summary>
    public static Quad Transform(Quad quad, Matrix3x2 matrix)
        => new(
            Vector2.Transform(quad.TopLeft, matrix),
            Vector2.Transform(quad.TopRight, matrix),
            Vector2.Transform(quad.BottomRight, matrix),
            Vector2.Transform(quad.BottomLeft, matrix));

    /// <summary>
    /// Transforms <paramref name="quad"/> by <paramref name="matrix"/>.
    /// </summary>
    public static Quad operator *(Quad quad, Matrix3x2 matrix) => Transform(quad, matrix);

    /// <summary>
    /// Determines whether two <see cref="Quad"/> values are equal.
    /// </summary>
    public static bool operator ==(Quad left, Quad right) => left.Equals(right);

    /// <summary>
    /// Determines whether two <see cref="Quad"/> values are not equal.
    /// </summary>
    public static bool operator !=(Quad left, Quad right) => !left.Equals(right);

    /// <summary>
    /// Returns a copy of this quad transformed by <paramref name="matrix"/>.
    /// </summary>
    public Quad Transformed(Matrix3x2 matrix) => Transform(this, matrix);

    /// <summary>
    /// Determines whether <paramref name="point"/> lies within this quad
    /// (tested across triangles <c>(TopLeft, TopRight, BottomRight)</c> and <c>(TopLeft, BottomRight, BottomLeft)</c>).
    /// </summary>
    public bool Contains(Vector2 point)
    {
        if (MathF.Abs(signedDoubleArea()) <= area_epsilon)
            return false;

        return pointInTriangle(point, TopLeft, TopRight, BottomRight)
            || pointInTriangle(point, TopLeft, BottomRight, BottomLeft);
    }

    /// <summary>
    /// Determines whether the point (<paramref name="x"/>, <paramref name="y"/>) lies within this quad.
    /// </summary>
    public bool Contains(float x, float y) => Contains(new Vector2(x, y));

    /// <inheritdoc />
    public bool Equals(Quad other)
        => vectorsEqual(TopLeft, other.TopLeft)
            && vectorsEqual(TopRight, other.TopRight)
            && vectorsEqual(BottomRight, other.BottomRight)
            && vectorsEqual(BottomLeft, other.BottomLeft);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Quad other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(normalizeZero(TopLeft.X));
        hash.Add(normalizeZero(TopLeft.Y));
        hash.Add(normalizeZero(TopRight.X));
        hash.Add(normalizeZero(TopRight.Y));
        hash.Add(normalizeZero(BottomRight.X));
        hash.Add(normalizeZero(BottomRight.Y));
        hash.Add(normalizeZero(BottomLeft.X));
        hash.Add(normalizeZero(BottomLeft.Y));
        return hash.ToHashCode();
    }

    /// <inheritdoc />
    public override string ToString()
        => string.Create(
            CultureInfo.InvariantCulture,
            $"[{TopLeft.X:G9}, {TopLeft.Y:G9}; {TopRight.X:G9}, {TopRight.Y:G9}; {BottomRight.X:G9}, {BottomRight.Y:G9}; {BottomLeft.X:G9}, {BottomLeft.Y:G9}]");

    private float signedDoubleArea()
        => ((TopLeft.X * TopRight.Y) - (TopRight.X * TopLeft.Y))
            + ((TopRight.X * BottomRight.Y) - (BottomRight.X * TopRight.Y))
            + ((BottomRight.X * BottomLeft.Y) - (BottomLeft.X * BottomRight.Y))
            + ((BottomLeft.X * TopLeft.Y) - (TopLeft.X * BottomLeft.Y));

    private static bool pointInTriangle(Vector2 point, Vector2 a, Vector2 b, Vector2 c)
    {
        var twiceArea = cross(b - a, c - a);

        if (MathF.Abs(twiceArea) <= area_epsilon)
            return false;

        var d1 = cross(b - a, point - a);
        var d2 = cross(c - b, point - b);
        var d3 = cross(a - c, point - c);

        var hasNegative = d1 < 0f || d2 < 0f || d3 < 0f;
        var hasPositive = d1 > 0f || d2 > 0f || d3 > 0f;
        return !(hasNegative && hasPositive);
    }

    private static float cross(Vector2 a, Vector2 b) => (a.X * b.Y) - (a.Y * b.X);

    private static bool vectorsEqual(Vector2 left, Vector2 right)
        => normalizeZero(left.X).Equals(normalizeZero(right.X))
            && normalizeZero(left.Y).Equals(normalizeZero(right.Y));

    private static float normalizeZero(float value) => value == 0f ? 0f : value;
}
