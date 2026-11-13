using System.Numerics;
using System.Runtime.InteropServices;
using kajarlabs.Framework.Numerics;

namespace kajarlabs.Framework.Graphics.Rendering.Vertices;

/// <summary>
/// Represents a 2D textured vertex with position, texture coordinates, and RGBA color,
/// laid out sequentially for direct GPU transfer buffer upload.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public readonly struct TexturedVertex2D : IEquatable<TexturedVertex2D>
{
    /// <summary>
    /// The 2D vertex position in coordinate space.
    /// </summary>
    public readonly Vector2 Position;

    /// <summary>
    /// The normalized UV texture coordinates.
    /// </summary>
    public readonly Vector2 TexCoord;

    /// <summary>
    /// The RGBA vertex tint color.
    /// </summary>
    public readonly Color4 Color;

    /// <summary>
    /// Initializes a new instance of the <see cref="TexturedVertex2D"/> struct.
    /// </summary>
    public TexturedVertex2D(Vector2 position, Vector2 texCoord, Color4 color)
    {
        Position = position;
        TexCoord = texCoord;
        Color = color;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TexturedVertex2D"/> struct from scalar coordinates.
    /// </summary>
    public TexturedVertex2D(float x, float y, float u, float v, Color4 color)
        : this(new Vector2(x, y), new Vector2(u, v), color)
    {
    }

    /// <summary>
    /// Determines whether two <see cref="TexturedVertex2D"/> values are equal.
    /// </summary>
    public static bool operator ==(TexturedVertex2D left, TexturedVertex2D right) => left.Equals(right);

    /// <summary>
    /// Determines whether two <see cref="TexturedVertex2D"/> values are not equal.
    /// </summary>
    public static bool operator !=(TexturedVertex2D left, TexturedVertex2D right) => !left.Equals(right);

    /// <inheritdoc />
    public bool Equals(TexturedVertex2D other)
        => Position.Equals(other.Position)
            && TexCoord.Equals(other.TexCoord)
            && Color.Equals(other.Color);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is TexturedVertex2D other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Position, TexCoord, Color);
}
