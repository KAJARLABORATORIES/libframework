using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;

namespace kajarlabs.Framework.Numerics;

/// <summary>
/// Represents a four-component RGBA color with single-precision floating-point channels.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public readonly struct Color4 : IEquatable<Color4>
{
    /// <summary>
    /// Gets an opaque white color (<c>(1, 1, 1, 1)</c>).
    /// </summary>
    public static readonly Color4 WHITE = new(1f, 1f, 1f, 1f);

    /// <summary>
    /// Gets an opaque black color (<c>(0, 0, 0, 1)</c>).
    /// </summary>
    public static readonly Color4 BLACK = new(0f, 0f, 0f, 1f);

    /// <summary>
    /// Gets a fully transparent black color (<c>(0, 0, 0, 0)</c>).
    /// </summary>
    public static readonly Color4 TRANSPARENT = new(0f, 0f, 0f, 0f);

    /// <summary>
    /// Gets an opaque red color (<c>(1, 0, 0, 1)</c>).
    /// </summary>
    public static readonly Color4 RED = new(1f, 0f, 0f, 1f);

    /// <summary>
    /// Gets an opaque green color (<c>(0, 1, 0, 1)</c>).
    /// </summary>
    public static readonly Color4 GREEN = new(0f, 1f, 0f, 1f);

    /// <summary>
    /// Gets an opaque blue color (<c>(0, 0, 1, 1)</c>).
    /// </summary>
    public static readonly Color4 BLUE = new(0f, 0f, 1f, 1f);

    /// <summary>
    /// Gets an opaque yellow color (<c>(1, 1, 0, 1)</c>).
    /// </summary>
    public static readonly Color4 YELLOW = new(1f, 1f, 0f, 1f);

    /// <summary>
    /// Gets an opaque cyan color (<c>(0, 1, 1, 1)</c>).
    /// </summary>
    public static readonly Color4 CYAN = new(0f, 1f, 1f, 1f);

    /// <summary>
    /// Gets an opaque magenta color (<c>(1, 0, 1, 1)</c>).
    /// </summary>
    public static readonly Color4 MAGENTA = new(1f, 0f, 1f, 1f);

    /// <summary>
    /// Gets an opaque mid-gray color (<c>(0.5, 0.5, 0.5, 1)</c>).
    /// </summary>
    public static readonly Color4 GRAY = new(0.5f, 0.5f, 0.5f, 1f);

    /// <summary>
    /// The red component.
    /// </summary>
    public readonly float R;

    /// <summary>
    /// The green component.
    /// </summary>
    public readonly float G;

    /// <summary>
    /// The blue component.
    /// </summary>
    public readonly float B;

    /// <summary>
    /// The alpha component.
    /// </summary>
    public readonly float A;

    /// <summary>
    /// Initializes a new instance of the <see cref="Color4"/> struct.
    /// </summary>
    /// <param name="r">The red component, typically in <c>[0, 1]</c>.</param>
    /// <param name="g">The green component, typically in <c>[0, 1]</c>.</param>
    /// <param name="b">The blue component, typically in <c>[0, 1]</c>.</param>
    /// <param name="a">The alpha component, typically in <c>[0, 1]</c>.</param>
    public Color4(float r, float g, float b, float a = 1f)
    {
        R = r;
        G = g;
        B = b;
        A = a;
    }

    /// <summary>
    /// Creates a <see cref="Color4"/> from 8-bit unsigned channel values in <c>[0, 255]</c>.
    /// </summary>
    public static Color4 FromBytes(byte r, byte g, byte b, byte a = 255)
        => new(r / 255f, g / 255f, b / 255f, a / 255f);

    /// <summary>
    /// Creates a <see cref="Color4"/> from a packed <c>0xRRGGBBAA</c> 32-bit unsigned integer.
    /// </summary>
    public static Color4 FromRgba(uint rgba)
    {
        var r = (byte)((rgba >> 24) & 0xFF);
        var g = (byte)((rgba >> 16) & 0xFF);
        var b = (byte)((rgba >> 8) & 0xFF);
        var a = (byte)(rgba & 0xFF);
        return FromBytes(r, g, b, a);
    }

    /// <summary>
    /// Creates a <see cref="Color4"/> from a <see cref="Vector4"/> (<c>(X, Y, Z, W)</c> → <c>(R, G, B, A)</c>).
    /// </summary>
    public static Color4 FromVector4(Vector4 vector)
        => new(vector.X, vector.Y, vector.Z, vector.W);

    /// <summary>
    /// Linearly interpolates between two colors component-wise without clamping <paramref name="t"/>.
    /// </summary>
    public static Color4 Lerp(Color4 a, Color4 b, float t)
        => new(
            a.R + ((b.R - a.R) * t),
            a.G + ((b.G - a.G) * t),
            a.B + ((b.B - a.B) * t),
            a.A + ((b.A - a.A) * t));

    /// <summary>
    /// Multiplies two colors component-wise.
    /// </summary>
    public static Color4 operator *(Color4 left, Color4 right)
        => new(left.R * right.R, left.G * right.G, left.B * right.B, left.A * right.A);

    /// <summary>
    /// Scales all four channels of a color by a scalar factor.
    /// </summary>
    public static Color4 operator *(Color4 color, float scalar)
        => new(color.R * scalar, color.G * scalar, color.B * scalar, color.A * scalar);

    /// <summary>
    /// Scales all four channels of a color by a scalar factor.
    /// </summary>
    public static Color4 operator *(float scalar, Color4 color) => color * scalar;

    /// <summary>
    /// Determines whether two <see cref="Color4"/> values are equal.
    /// </summary>
    public static bool operator ==(Color4 left, Color4 right) => left.Equals(right);

    /// <summary>
    /// Determines whether two <see cref="Color4"/> values are not equal.
    /// </summary>
    public static bool operator !=(Color4 left, Color4 right) => !left.Equals(right);

    /// <summary>
    /// Converts this color to a <see cref="Vector4"/> (<c>(R, G, B, A)</c>).
    /// </summary>
    public Vector4 ToVector4() => new(R, G, B, A);

    /// <summary>
    /// Returns a copy of this color with its alpha channel replaced by <paramref name="alpha"/>.
    /// </summary>
    public Color4 WithAlpha(float alpha) => new(R, G, B, alpha);

    /// <summary>
    /// Returns a copy of this color with its alpha channel multiplied by <paramref name="factor"/>.
    /// </summary>
    public Color4 MultiplyAlpha(float factor) => new(R, G, B, A * factor);

    /// <summary>
    /// Returns a copy of this color with RGB channels premultiplied by <see cref="A"/>.
    /// </summary>
    public Color4 Premultiply() => new(R * A, G * A, B * A, A);

    /// <inheritdoc />
    public bool Equals(Color4 other)
        => normalizeZero(R).Equals(normalizeZero(other.R))
            && normalizeZero(G).Equals(normalizeZero(other.G))
            && normalizeZero(B).Equals(normalizeZero(other.B))
            && normalizeZero(A).Equals(normalizeZero(other.A));

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Color4 other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
        => HashCode.Combine(normalizeZero(R), normalizeZero(G), normalizeZero(B), normalizeZero(A));

    /// <inheritdoc />
    public override string ToString()
        => string.Create(CultureInfo.InvariantCulture, $"({R:G9}, {G:G9}, {B:G9}, {A:G9})");

    private static float normalizeZero(float value) => value == 0f ? 0f : value;
}
