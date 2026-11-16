using System.Numerics;
using kajarlabs.Framework.Numerics;

namespace kajarlabs.Framework.Scene;

/// <summary>
/// Stores a cached draw-space 2D affine transform matrix and its inverse.
/// </summary>
public readonly struct DrawInfo : IEquatable<DrawInfo>
{
    /// <summary>
    /// Gets an identity <see cref="DrawInfo"/> with identity matrix and inverse.
    /// </summary>
    public static readonly DrawInfo IDENTITY = new(Matrix3x2.Identity, Matrix3x2.Identity, isInvertible: true);

    /// <summary>
    /// Initializes a new instance of the <see cref="DrawInfo"/> struct.
    /// </summary>
    public DrawInfo(Matrix3x2 matrix, Matrix3x2 matrixInverse, bool isInvertible)
    {
        Matrix = matrix;
        MatrixInverse = matrixInverse;
        IsInvertible = isInvertible;
    }

    /// <summary>
    /// Gets the local-to-draw-space row-vector affine matrix.
    /// </summary>
    public Matrix3x2 Matrix { get; }

    /// <summary>
    /// Gets the cached draw-space-to-local inverse matrix, or <see cref="Matrix3x2.Identity"/> when <see cref="IsInvertible"/> is <see langword="false"/>.
    /// </summary>
    public Matrix3x2 MatrixInverse { get; }

    /// <summary>
    /// Gets a value indicating whether <see cref="Matrix"/> is finite and non-singular.
    /// </summary>
    public bool IsInvertible { get; }

    /// <summary>
    /// Creates a <see cref="DrawInfo"/> from <paramref name="matrix"/>, computing its inverse when non-singular.
    /// </summary>
    public static DrawInfo Create(Matrix3x2 matrix)
    {
        var isInvertible = matrix.TryInvert(out var inverse);
        return new DrawInfo(matrix, isInvertible ? inverse : Matrix3x2.Identity, isInvertible);
    }

    /// <summary>
    /// Determines whether two <see cref="DrawInfo"/> values are equal.
    /// </summary>
    public static bool operator ==(DrawInfo left, DrawInfo right) => left.Equals(right);

    /// <summary>
    /// Determines whether two <see cref="DrawInfo"/> values are not equal.
    /// </summary>
    public static bool operator !=(DrawInfo left, DrawInfo right) => !left.Equals(right);

    /// <summary>
    /// Composes <paramref name="localTransform"/> onto this parent <see cref="DrawInfo"/> in row-vector order
    /// (<c>localTransform * Matrix</c>).
    /// </summary>
    public DrawInfo ApplyLocal(Matrix3x2 localTransform)
        => Create(localTransform * Matrix);

    /// <inheritdoc />
    public bool Equals(DrawInfo other)
        => IsInvertible == other.IsInvertible
            && Matrix.Equals(other.Matrix)
            && MatrixInverse.Equals(other.MatrixInverse);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is DrawInfo other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Matrix, MatrixInverse, IsInvertible);
}
