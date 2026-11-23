using System.Runtime.CompilerServices;
using kajarlabs.Framework.Scene.Transforms;

namespace kajarlabs.Framework.Scene.Transforms.Tests;

public sealed class CubicBezierEasingFunctionTests
{
    public static TheoryData<CubicBezierEasingFunction> PresetAndCustomCurves() =>
    [
        CubicBezierEasingFunction.LINEAR,
        CubicBezierEasingFunction.EASE,
        CubicBezierEasingFunction.EASE_IN,
        CubicBezierEasingFunction.EASE_OUT,
        CubicBezierEasingFunction.EASE_IN_OUT,
        new CubicBezierEasingFunction(0.0, 1.0, 1.0, 0.0),
        new CubicBezierEasingFunction(1.0, 0.0, 0.0, 1.0),
        new CubicBezierEasingFunction(0.34, 1.56, 0.64, 1.0),
        new CubicBezierEasingFunction(0.68, -0.55, 0.27, 1.55),
    ];

    [Theory]
    [MemberData(nameof(PresetAndCustomCurves))]
    public void ApplyEasingMatchesParametricCurveAcrossDomain(CubicBezierEasingFunction curve)
    {
        Assert.Equal(0.0, curve.ApplyEasing(0.0));
        Assert.Equal(1.0, curve.ApplyEasing(1.0));
        Assert.Equal(0.0, curve.ApplyEasing(-0.5));
        Assert.Equal(1.0, curve.ApplyEasing(1.5));
        Assert.True(double.IsNaN(curve.ApplyEasing(double.NaN)));

        const int samples = 100;
        for (var i = 1; i < samples; i++)
        {
            var u = (double)i / samples;
            var expectedX = evalComponent(u, curve.X1, curve.X2);
            var expectedY = evalComponent(u, curve.Y1, curve.Y2);

            var actualY = curve.ApplyEasing(expectedX);
            Assert.Equal(expectedY, actualY, precision: 8);
        }
    }

    [Fact]
    public void DefaultValueBehavesAsLinearCurve()
    {
        CubicBezierEasingFunction def = default;

        Assert.Equal(0.0, def.ApplyEasing(0.0));
        Assert.Equal(0.25, def.ApplyEasing(0.25), precision: 14);
        Assert.Equal(0.75, def.ApplyEasing(0.75), precision: 14);
        Assert.Equal(1.0, def.ApplyEasing(1.0));
    }

    [Fact]
    public void OvershootCurvesExceedUnitRange()
    {
        var backOut = new CubicBezierEasingFunction(0.34, 1.56, 0.64, 1.0);
        var anticipate = new CubicBezierEasingFunction(0.6, -0.4, 0.4, 1.4);

        Assert.True(backOut.ApplyEasing(0.6) > 1.0);
        Assert.True(anticipate.ApplyEasing(0.15) < 0.0);
        Assert.True(anticipate.ApplyEasing(0.85) > 1.0);
    }

    [Fact]
    public void ConstructorRejectsInvalidControlPoints()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CubicBezierEasingFunction(-0.01, 0.0, 1.0, 1.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CubicBezierEasingFunction(1.01, 0.0, 1.0, 1.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CubicBezierEasingFunction(0.0, 0.0, -0.01, 1.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CubicBezierEasingFunction(0.0, 0.0, 1.01, 1.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CubicBezierEasingFunction(double.NaN, 0.0, 1.0, 1.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CubicBezierEasingFunction(0.0, double.PositiveInfinity, 1.0, 1.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CubicBezierEasingFunction(0.0, 0.0, double.NegativeInfinity, 1.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CubicBezierEasingFunction(0.0, 0.0, 1.0, double.NaN));
    }

    [Fact]
    public void EqualityNormalizationAndMemoryLayoutHold()
    {
        var posZero = new CubicBezierEasingFunction(0.0, 0.0, 1.0, 1.0);
        var negZero = new CubicBezierEasingFunction(-0.0, -0.0, 1.0, 1.0);

        Assert.Equal(posZero, negZero);
        Assert.True(posZero == negZero);
        Assert.False(posZero != negZero);
        Assert.Equal(posZero.GetHashCode(), negZero.GetHashCode());
        Assert.NotEqual(CubicBezierEasingFunction.EASE, CubicBezierEasingFunction.EASE_IN);
        Assert.Equal("(0.25, 0.1, 0.25, 1)", CubicBezierEasingFunction.EASE.ToString());

        Assert.Equal(sizeof(double) * 4, Unsafe.SizeOf<CubicBezierEasingFunction>());
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<CubicBezierEasingFunction>());
    }

    private static double evalComponent(double u, double p1, double p2)
    {
        var inv = 1.0 - u;
        return (3.0 * inv * inv * u * p1) + (3.0 * inv * u * u * p2) + (u * u * u);
    }
}
