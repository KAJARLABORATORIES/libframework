using System.Globalization;
using System.Runtime.InteropServices;
using kajarlabs.Framework.Numerics;

namespace kajarlabs.Framework.Graphics;

/// <summary>
/// Represents a render target viewport region and normalized depth bounds.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public readonly struct Viewport : IEquatable<Viewport>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Viewport"/> struct.
    /// </summary>
    public Viewport(
        float x,
        float y,
        float width,
        float height,
        float minDepth = 0f,
        float maxDepth = 1f)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(width);
        ArgumentOutOfRangeException.ThrowIfNegative(height);

        X = x;
        Y = y;
        Width = width;
        Height = height;
        MinDepth = minDepth;
        MaxDepth = maxDepth;
    }

    /// <summary>
    /// Initializes a new origin-aligned <see cref="Viewport"/> with default depth range <c>[0, 1]</c>.
    /// </summary>
    public Viewport(float width, float height)
        : this(0f, 0f, width, height)
    {
    }

    /// <summary>
    /// Initializes a new <see cref="Viewport"/> from integer pixel bounds.
    /// </summary>
    public Viewport(RectangleI bounds, float minDepth = 0f, float maxDepth = 1f)
        : this(bounds.X, bounds.Y, bounds.Width, bounds.Height, minDepth, maxDepth)
    {
    }

    /// <summary>
    /// Gets the X coordinate of the left edge in pixels.
    /// </summary>
    public float X { get; }

    /// <summary>
    /// Gets the Y coordinate of the top edge in pixels.
    /// </summary>
    public float Y { get; }

    /// <summary>
    /// Gets the width of the viewport in pixels.
    /// </summary>
    public float Width { get; }

    /// <summary>
    /// Gets the height of the viewport in pixels.
    /// </summary>
    public float Height { get; }

    /// <summary>
    /// Gets the minimum depth value of the viewport.
    /// </summary>
    public float MinDepth { get; }

    /// <summary>
    /// Gets the maximum depth value of the viewport.
    /// </summary>
    public float MaxDepth { get; }

    /// <summary>
    /// Gets the width-to-height aspect ratio, or <c>0</c> when <see cref="Height"/> is zero.
    /// </summary>
    public float AspectRatio => Height > 0f ? Width / Height : 0f;

    /// <summary>
    /// Gets the integer pixel bounds of this viewport.
    /// </summary>
    public RectangleI Bounds => new((int)X, (int)Y, (int)Width, (int)Height);

    /// <summary>
    /// Determines whether two <see cref="Viewport"/> values are equal.
    /// </summary>
    public static bool operator ==(Viewport left, Viewport right) => left.Equals(right);

    /// <summary>
    /// Determines whether two <see cref="Viewport"/> values are not equal.
    /// </summary>
    public static bool operator !=(Viewport left, Viewport right) => !left.Equals(right);

    /// <inheritdoc />
    public bool Equals(Viewport other)
        => normalizeZero(X).Equals(normalizeZero(other.X))
            && normalizeZero(Y).Equals(normalizeZero(other.Y))
            && normalizeZero(Width).Equals(normalizeZero(other.Width))
            && normalizeZero(Height).Equals(normalizeZero(other.Height))
            && normalizeZero(MinDepth).Equals(normalizeZero(other.MinDepth))
            && normalizeZero(MaxDepth).Equals(normalizeZero(other.MaxDepth));

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Viewport other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
        => HashCode.Combine(
            normalizeZero(X),
            normalizeZero(Y),
            normalizeZero(Width),
            normalizeZero(Height),
            normalizeZero(MinDepth),
            normalizeZero(MaxDepth));

    /// <inheritdoc />
    public override string ToString()
        => string.Create(CultureInfo.InvariantCulture, $"({X:G9}, {Y:G9}, {Width:G9}, {Height:G9})");

    private static float normalizeZero(float value) => value == 0f ? 0f : value;
}
