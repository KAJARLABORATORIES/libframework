using System.Numerics;
using System.Runtime.CompilerServices;
using kajarlabs.Framework.Numerics;

namespace kajarlabs.Framework.Tests.Numerics;

public sealed class QuadTests
{
    [Fact]
    public void ConstructorsAndGeometryPropertiesWork()
    {
        var rect = new RectangleF(10f, 20f, 100f, 50f);
        Quad implicitQuad = rect;

        Assert.Equal(new Quad(10f, 20f, 100f, 50f), implicitQuad);
        Assert.Equal(Quad.FromRectangle(rect), implicitQuad);
        Assert.Equal(new Vector2(10f, 20f), implicitQuad.TopLeft);
        Assert.Equal(new Vector2(110f, 20f), implicitQuad.TopRight);
        Assert.Equal(new Vector2(110f, 70f), implicitQuad.BottomRight);
        Assert.Equal(new Vector2(10f, 70f), implicitQuad.BottomLeft);
        Assert.Equal(100f, implicitQuad.Width, precision: 5);
        Assert.Equal(50f, implicitQuad.Height, precision: 5);
        Assert.Equal(new Vector2(100f, 50f), implicitQuad.Size);
        Assert.Equal(new Vector2(60f, 45f), implicitQuad.Center);
        Assert.Equal(5000f, implicitQuad.Area, precision: 3);
        Assert.Equal(rect, implicitQuad.AABB);
        Assert.Equal(4 * Unsafe.SizeOf<Vector2>(), Unsafe.SizeOf<Quad>());
    }

    [Fact]
    public void TransformRotationScaleAndShearComputesExpectedCornersAndAabb()
    {
        var unit = new Quad(-10f, -10f, 20f, 20f);
        var rotated = unit * Matrix3x2.CreateRotation(float.DegreesToRadians(45f));

        var expectedHalfDiagonal = 10f * MathF.Sqrt(2f);
        Assert.Equal(0f, rotated.TopLeft.X, precision: 4);
        Assert.Equal(-expectedHalfDiagonal, rotated.TopLeft.Y, precision: 4);
        Assert.Equal(expectedHalfDiagonal, rotated.TopRight.X, precision: 4);
        Assert.Equal(0f, rotated.TopRight.Y, precision: 4);
        Assert.Equal(0f, rotated.BottomRight.X, precision: 4);
        Assert.Equal(expectedHalfDiagonal, rotated.BottomRight.Y, precision: 4);
        Assert.Equal(-expectedHalfDiagonal, rotated.BottomLeft.X, precision: 4);
        Assert.Equal(0f, rotated.BottomLeft.Y, precision: 4);

        var aabb = rotated.AABB;
        Assert.Equal(-expectedHalfDiagonal, aabb.Left, precision: 4);
        Assert.Equal(-expectedHalfDiagonal, aabb.Top, precision: 4);
        Assert.Equal(expectedHalfDiagonal * 2f, aabb.Width, precision: 4);
        Assert.Equal(expectedHalfDiagonal * 2f, aabb.Height, precision: 4);

        var sheared = new Quad(0f, 0f, 10f, 20f).Transformed(Matrix3x2Extensions.CreateShear(0.5f, 0f));
        Assert.Equal(new Vector2(10f, 20f), sheared.BottomLeft);
        Assert.Equal(new Vector2(20f, 20f), sheared.BottomRight);
    }

    [Fact]
    public void ContainsHandlesAxisAlignedRotatedMirroredAndDegenerateQuads()
    {
        var rotated = new Quad(-10f, -10f, 20f, 20f) * Matrix3x2.CreateRotation(float.DegreesToRadians(45f));

        Assert.True(rotated.Contains(Vector2.Zero));
        Assert.True(rotated.Contains(0f, 13.5f));
        Assert.False(rotated.Contains(9.9f, 9.9f));

        var mirrored = new Quad(0f, 0f, 40f, 20f) * Matrix3x2.CreateScale(-1f, 1f);
        Assert.True(mirrored.Contains(-20f, 10f));
        Assert.False(mirrored.Contains(20f, 10f));

        Assert.False(Quad.EMPTY.Contains(Vector2.Zero));
        Assert.False(new Quad(0f, 0f, 100f, 0f).Contains(50f, 0f));
    }

    [Fact]
    public void EqualityAndNegativeZeroNormalizationWork()
    {
        var positiveZero = new Quad(new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f));
        var negativeZero = new Quad(new Vector2(-0f, -0f), new Vector2(1f, -0f), new Vector2(1f, 1f), new Vector2(-0f, 1f));

        Assert.Equal(positiveZero, negativeZero);
        Assert.True(positiveZero == negativeZero);
        Assert.False(positiveZero != negativeZero);
        Assert.Equal(positiveZero.GetHashCode(), negativeZero.GetHashCode());

        var shifted = positiveZero * Matrix3x2.CreateTranslation(1f, 0f);
        Assert.NotEqual(positiveZero, shifted);
    }
}
