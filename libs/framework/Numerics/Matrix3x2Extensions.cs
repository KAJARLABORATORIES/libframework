using System.Numerics;

namespace kajarlabs.Framework.Numerics;

/// <summary>
/// Provides allocation-free affine 2D transform composition and inversion helpers for
/// <see cref="Matrix3x2"/> using row-vector convention (<c>Vector2.Transform(v, m)</c>)
/// and clockwise screen-space degrees.
/// </summary>
public static class Matrix3x2Extensions
{
    private const float singular_determinant_epsilon = 1e-12f;

    /// <summary>
    /// Creates a 2D shear matrix in row-vector convention where
    /// <c>x' = x + y * shearX</c> and <c>y' = y + x * shearY</c>.
    /// </summary>
    public static Matrix3x2 CreateShear(float shearX, float shearY)
        => new(1f, shearY, shearX, 1f, 0f, 0f);

    /// <summary>
    /// Creates a 2D shear matrix from a <see cref="Vector2"/> shear factor.
    /// </summary>
    public static Matrix3x2 CreateShear(Vector2 shear)
        => CreateShear(shear.X, shear.Y);

    /// <summary>
    /// Composes a local-to-parent 2D affine transform in row-vector order:
    /// <c>T(-originPosition) * Scale(scale) * Shear(shear) * Rotation(rotationDegrees) * T(position)</c>.
    /// </summary>
    /// <remarks>
    /// Positive <paramref name="rotationDegrees"/> rotates clockwise in top-left screen coordinates
    /// (<c>+X</c> right, <c>+Y</c> down).
    /// </remarks>
    public static Matrix3x2 CreateLocalTransform(
        Vector2 position,
        Vector2 scale,
        Vector2 shear,
        float rotationDegrees,
        Vector2 originPosition)
    {
        var a11 = scale.X;
        var a12 = scale.X * shear.Y;
        var a21 = scale.Y * shear.X;
        var a22 = scale.Y;

        float m11, m12, m21, m22;

        if (rotationDegrees == 0f)
        {
            m11 = a11;
            m12 = a12;
            m21 = a21;
            m22 = a22;
        }
        else
        {
            var (sin, cos) = MathF.SinCos(float.DegreesToRadians(rotationDegrees));
            m11 = (a11 * cos) - (a12 * sin);
            m12 = (a11 * sin) + (a12 * cos);
            m21 = (a21 * cos) - (a22 * sin);
            m22 = (a21 * sin) + (a22 * cos);
        }

        var m31 = position.X - (originPosition.X * m11) - (originPosition.Y * m21);
        var m32 = position.Y - (originPosition.X * m12) - (originPosition.Y * m22);

        return new Matrix3x2(m11, m12, m21, m22, m31, m32);
    }

    /// <summary>
    /// Attempts to invert <paramref name="matrix"/>, returning <see langword="false"/> if the matrix
    /// is singular or non-finite.
    /// </summary>
    public static bool TryInvert(this Matrix3x2 matrix, out Matrix3x2 result)
    {
        var determinant = matrix.GetDeterminant();

        if (!float.IsFinite(determinant) || MathF.Abs(determinant) <= singular_determinant_epsilon)
        {
            result = default;
            return false;
        }

        if (!Matrix3x2.Invert(matrix, out result))
        {
            result = default;
            return false;
        }

        if (!float.IsFinite(result.M11)
            || !float.IsFinite(result.M12)
            || !float.IsFinite(result.M21)
            || !float.IsFinite(result.M22)
            || !float.IsFinite(result.M31)
            || !float.IsFinite(result.M32))
        {
            result = default;
            return false;
        }

        return true;
    }

    /// <summary>
    /// Returns the inverse of <paramref name="matrix"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when <paramref name="matrix"/> is singular.</exception>
    public static Matrix3x2 Invert(this Matrix3x2 matrix)
    {
        if (!TryInvert(matrix, out var result))
            throw new InvalidOperationException("Matrix is singular or non-finite and cannot be inverted.");

        return result;
    }

    /// <summary>
    /// Extracts the positive X and Y axis scale magnitudes encoded by <paramref name="matrix"/>.
    /// </summary>
    public static Vector2 ExtractScale(this Matrix3x2 matrix)
        => new(
            MathF.Sqrt((matrix.M11 * matrix.M11) + (matrix.M12 * matrix.M12)),
            MathF.Sqrt((matrix.M21 * matrix.M21) + (matrix.M22 * matrix.M22)));
}
