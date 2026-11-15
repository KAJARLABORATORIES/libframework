using System.Numerics;
using kajarlabs.Framework.Numerics;

namespace kajarlabs.Framework.Tests.Numerics;

public sealed class Matrix3x2ExtensionsTests
{
    [Fact]
    public void CreateLocalTransformMatchesSequentialMatrixProductAndClockwiseConvention()
    {
        var position = new Vector2(120f, 80f);
        var scale = new Vector2(2f, 0.5f);
        var shear = new Vector2(0.25f, -0.1f);
        const float rotation_degrees = 37.5f;
        var origin = new Vector2(15f, 25f);

        var actual = Matrix3x2Extensions.CreateLocalTransform(position, scale, shear, rotation_degrees, origin);

        var expected = Matrix3x2.CreateTranslation(-origin)
            * Matrix3x2.CreateScale(scale)
            * Matrix3x2Extensions.CreateShear(shear)
            * Matrix3x2.CreateRotation(float.DegreesToRadians(rotation_degrees))
            * Matrix3x2.CreateTranslation(position);

        assertMatricesClose(expected, actual);

        // Origin point maps directly to Position regardless of scale, shear, or rotation.
        var transformedOrigin = Vector2.Transform(origin, actual);
        Assert.Equal(position.X, transformedOrigin.X, precision: 4);
        Assert.Equal(position.Y, transformedOrigin.Y, precision: 4);

        // Positive 90-degree rotation maps +X to +Y (clockwise in top-left screen coordinates).
        var cw90 = Matrix3x2Extensions.CreateLocalTransform(Vector2.Zero, Vector2.One, Vector2.Zero, 90f, Vector2.Zero);
        var rotatedAxis = Vector2.Transform(Vector2.UnitX, cw90);
        Assert.Equal(0f, rotatedAxis.X, precision: 4);
        Assert.Equal(1f, rotatedAxis.Y, precision: 4);
    }

    [Fact]
    public void TryInvertAndInvertRoundTripNonSingularAndRejectSingularMatrices()
    {
        var matrix = Matrix3x2Extensions.CreateLocalTransform(
            new Vector2(300f, 150f),
            new Vector2(1.5f, 2.25f),
            new Vector2(0.3f, 0.1f),
            -60f,
            new Vector2(40f, 20f));

        Assert.True(matrix.TryInvert(out var inverse));

        var point = new Vector2(17.5f, -42f);
        var roundTripped = Vector2.Transform(Vector2.Transform(point, matrix), inverse);
        Assert.Equal(point.X, roundTripped.X, precision: 3);
        Assert.Equal(point.Y, roundTripped.Y, precision: 3);

        var product = matrix * inverse;
        assertMatricesClose(Matrix3x2.Identity, product);
        Assert.Equal(inverse, matrix.Invert());

        var singular = Matrix3x2.CreateScale(0f, 1f);
        Assert.False(singular.TryInvert(out _));
        Assert.Throws<InvalidOperationException>(() => singular.Invert());
    }

    [Fact]
    public void ExtractScaleRecoversRotatedAxisMagnitudes()
    {
        var transform = Matrix3x2Extensions.CreateLocalTransform(
            new Vector2(50f, 60f),
            new Vector2(3f, 4f),
            Vector2.Zero,
            28f,
            Vector2.Zero);

        var extracted = transform.ExtractScale();
        Assert.Equal(3f, extracted.X, precision: 4);
        Assert.Equal(4f, extracted.Y, precision: 4);
    }

    private static void assertMatricesClose(Matrix3x2 expected, Matrix3x2 actual)
    {
        Assert.Equal(expected.M11, actual.M11, precision: 4);
        Assert.Equal(expected.M12, actual.M12, precision: 4);
        Assert.Equal(expected.M21, actual.M21, precision: 4);
        Assert.Equal(expected.M22, actual.M22, precision: 4);
        Assert.Equal(expected.M31, actual.M31, precision: 4);
        Assert.Equal(expected.M32, actual.M32, precision: 4);
    }
}
