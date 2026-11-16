using kajarlabs.Framework.Numerics;

namespace kajarlabs.Framework.Scene;

/// <summary>
/// Stores accumulated color and alpha state inherited through a <see cref="Drawable"/> hierarchy.
/// </summary>
public readonly struct DrawColorInfo : IEquatable<DrawColorInfo>
{
    /// <summary>
    /// Gets an opaque white <see cref="DrawColorInfo"/>.
    /// </summary>
    public static readonly DrawColorInfo WHITE = new(Color4.WHITE);

    /// <summary>
    /// Initializes a new instance of the <see cref="DrawColorInfo"/> struct.
    /// </summary>
    public DrawColorInfo(Color4 color)
    {
        Color = color;
    }

    /// <summary>
    /// Gets the accumulated RGBA color.
    /// </summary>
    public Color4 Color { get; }

    /// <summary>
    /// Determines whether two <see cref="DrawColorInfo"/> values are equal.
    /// </summary>
    public static bool operator ==(DrawColorInfo left, DrawColorInfo right) => left.Equals(right);

    /// <summary>
    /// Determines whether two <see cref="DrawColorInfo"/> values are not equal.
    /// </summary>
    public static bool operator !=(DrawColorInfo left, DrawColorInfo right) => !left.Equals(right);

    /// <summary>
    /// Returns a new <see cref="DrawColorInfo"/> combining this color with a child's local <paramref name="localColor"/>
    /// and <paramref name="localAlpha"/>.
    /// </summary>
    public DrawColorInfo Multiply(Color4 localColor, float localAlpha)
        => new(Color * localColor.MultiplyAlpha(localAlpha));

    /// <inheritdoc />
    public bool Equals(DrawColorInfo other) => Color.Equals(other.Color);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is DrawColorInfo other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => Color.GetHashCode();
}
