using System.Numerics;
using kajarlabs.Framework.Numerics;
using kajarlabs.Framework.Scene.Transforms;

namespace kajarlabs.Framework.Scene.Transforms.Tests;

public sealed class InterpolationTests
{
    [Fact]
    public void LerpInterpolatesAndExtrapolatesAllSupportedTypes()
    {
        Assert.Equal(15.0, Interpolation.Lerp(10.0, 20.0, 0.5), precision: 14);
        Assert.Equal(30.0, Interpolation.Lerp(10.0, 20.0, 2.0), precision: 14);
        Assert.Equal(5.0, Interpolation.Lerp(10.0, 20.0, -0.5), precision: 14);

        Assert.Equal(12.5f, Interpolation.Lerp(10f, 20f, 0.25), precision: 6);
        Assert.Equal(5f, Interpolation.Lerp(20f, 0f, 0.75), precision: 6);

        Assert.Equal(0, Interpolation.Lerp(0, 100, 0.0));
        Assert.Equal(25, Interpolation.Lerp(0, 100, 0.254));
        Assert.Equal(26, Interpolation.Lerp(0, 100, 0.256));
        Assert.Equal(100, Interpolation.Lerp(0, 100, 1.0));
        Assert.Equal(-25, Interpolation.Lerp(0, 100, -0.25));

        var vec = Interpolation.Lerp(new Vector2(10f, -20f), new Vector2(30f, 20f), 0.25);
        Assert.Equal(15f, vec.X, precision: 6);
        Assert.Equal(-10f, vec.Y, precision: 6);

        var color = Interpolation.Lerp(new Color4(0f, 0.5f, 1f, 0f), new Color4(1f, 0.5f, 0f, 1f), 0.4);
        Assert.Equal(0.4f, color.R, precision: 6);
        Assert.Equal(0.5f, color.G, precision: 6);
        Assert.Equal(0.6f, color.B, precision: 6);
        Assert.Equal(0.4f, color.A, precision: 6);

        var margin = Interpolation.Lerp(new MarginPadding(0f, 10f, 20f, 30f), new MarginPadding(40f, 50f, 60f, 70f), 0.5);
        Assert.Equal(new MarginPadding(20f, 30f, 40f, 50f), margin);

        var rect = Interpolation.Lerp(new RectangleF(0f, 10f, 100f, 200f), new RectangleF(20f, 30f, 200f, 400f), 0.5);
        Assert.Equal(new RectangleF(10f, 20f, 150f, 300f), rect);
    }

    [Fact]
    public void ValueAtClampsTimeBoundsAndHandlesZeroDuration()
    {
        const double start = 100.0;
        const double end = 200.0;

        Assert.Equal(10.0, Interpolation.ValueAt(50.0, 10.0, 20.0, start, end));
        Assert.Equal(10.0, Interpolation.ValueAt(100.0, 10.0, 20.0, start, end));
        Assert.Equal(15.0, Interpolation.ValueAt(150.0, 10.0, 20.0, start, end), precision: 14);
        Assert.Equal(20.0, Interpolation.ValueAt(200.0, 10.0, 20.0, start, end));
        Assert.Equal(20.0, Interpolation.ValueAt(250.0, 10.0, 20.0, start, end));

        // Zero-duration transforms stay at startValue before startTime and apply endValue at/after startTime.
        Assert.Equal(10.0, Interpolation.ValueAt(99.0, 10.0, 20.0, 100.0, 100.0));
        Assert.Equal(20.0, Interpolation.ValueAt(100.0, 10.0, 20.0, 100.0, 100.0));
        Assert.Equal(20.0, Interpolation.ValueAt(101.0, 10.0, 20.0, 100.0, 100.0));

        Assert.Equal(10f, Interpolation.ValueAt(99.0, 10f, 20f, 100.0, 100.0));
        Assert.Equal(20f, Interpolation.ValueAt(100.0, 10f, 20f, 100.0, 100.0));

        Assert.Equal(10, Interpolation.ValueAt(99.0, 10, 20, 100.0, 100.0));
        Assert.Equal(20, Interpolation.ValueAt(100.0, 10, 20, 100.0, 100.0));

        Assert.Equal(new Vector2(1f, 2f), Interpolation.ValueAt(99.0, new Vector2(1f, 2f), new Vector2(3f, 4f), 100.0, 100.0));
        Assert.Equal(new Vector2(3f, 4f), Interpolation.ValueAt(100.0, new Vector2(1f, 2f), new Vector2(3f, 4f), 100.0, 100.0));

        Assert.Equal(Color4.BLACK, Interpolation.ValueAt(99.0, Color4.BLACK, Color4.WHITE, 100.0, 100.0));
        Assert.Equal(Color4.WHITE, Interpolation.ValueAt(100.0, Color4.BLACK, Color4.WHITE, 100.0, 100.0));

        Assert.Equal(MarginPadding.ZERO, Interpolation.ValueAt(99.0, MarginPadding.ZERO, new MarginPadding(8f), 100.0, 100.0));
        Assert.Equal(new MarginPadding(8f), Interpolation.ValueAt(100.0, MarginPadding.ZERO, new MarginPadding(8f), 100.0, 100.0));

        Assert.Equal(RectangleF.EMPTY, Interpolation.ValueAt(99.0, RectangleF.EMPTY, new RectangleF(1f, 2f, 3f, 4f), 100.0, 100.0));
        Assert.Equal(new RectangleF(1f, 2f, 3f, 4f), Interpolation.ValueAt(100.0, RectangleF.EMPTY, new RectangleF(1f, 2f, 3f, 4f), 100.0, 100.0));
    }

    [Fact]
    public void ValueAtAppliesEasingsAndGenericStructEasingsAccurately()
    {
        const double start_time = 10.0;
        const double end_time = 30.0;
        const double time = 15.0; // normalized t = 0.25

        var quadEased = 0.25 * 0.25;
        Assert.Equal(100.0 + (200.0 * quadEased), Interpolation.ValueAt(time, 100.0, 300.0, start_time, end_time, Easing.InQuad), precision: 12);
        Assert.Equal((float)(100.0 + (200.0 * quadEased)), Interpolation.ValueAt(time, 100f, 300f, start_time, end_time, Easing.InQuad), precision: 5);
        // 100 + 200 * 0.0625 = 112.5, which rounds to even (112) via Math.Round.
        Assert.Equal(112, Interpolation.ValueAt(time, 100, 300, start_time, end_time, Easing.InQuad));

        var custom = new HalfStepEasing();
        Assert.Equal(new Vector2(50f, 100f), Interpolation.ValueAt(time, Vector2.Zero, new Vector2(100f, 200f), start_time, end_time, custom));
        Assert.Equal(new Color4(0.5f, 0.5f, 0.5f, 1f), Interpolation.ValueAt(time, Color4.BLACK, Color4.WHITE, start_time, end_time, custom));
        Assert.Equal(new MarginPadding(5f, 10f, 15f, 20f), Interpolation.ValueAt(time, MarginPadding.ZERO, new MarginPadding(10f, 20f, 30f, 40f), start_time, end_time, custom));
        Assert.Equal(new RectangleF(5f, 10f, 50f, 100f), Interpolation.ValueAt(time, RectangleF.EMPTY, new RectangleF(10f, 20f, 100f, 200f), start_time, end_time, custom));

        var bezier = CubicBezierEasingFunction.EASE_IN_OUT;
        var expectedBezier = bezier.ApplyEasing(0.25);
        Assert.Equal(expectedBezier, Interpolation.ValueAt(time, 0.0, 1.0, start_time, end_time, bezier), precision: 14);
    }

    [Fact]
    public void ValueAtThrowsOnInvalidTimeBounds()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Interpolation.ValueAt(0.0, 0.0, 1.0, 10.0, 5.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Interpolation.ValueAt(double.NaN, 0.0, 1.0, 0.0, 1.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Interpolation.ValueAt(0.5, 0.0, 1.0, double.NaN, 1.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Interpolation.ValueAt(0.5, 0.0, 1.0, 0.0, double.NaN));

        Assert.Throws<ArgumentOutOfRangeException>(() => Interpolation.ValueAt(0.0, 0f, 1f, 10.0, 5.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Interpolation.ValueAt(0.0, 0, 1, 10.0, 5.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Interpolation.ValueAt(0.0, Vector2.Zero, Vector2.One, 10.0, 5.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Interpolation.ValueAt(0.0, Color4.BLACK, Color4.WHITE, 10.0, 5.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Interpolation.ValueAt(0.0, MarginPadding.ZERO, new MarginPadding(1f), 10.0, 5.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Interpolation.ValueAt(0.0, RectangleF.EMPTY, new RectangleF(0f, 0f, 1f, 1f), 10.0, 5.0));
    }

    [Fact]
    public void ValueAtAllocatesZeroHeapBytesAcrossAllSupportedTypes()
    {
        var bezier = CubicBezierEasingFunction.EASE;
        var defaultEasing = new DefaultEasingFunction(Easing.InOutCubic);

        // Warm up JIT for all overloads and generic instantiations.
        _ = Interpolation.ValueAt(0.35, 0.0, 100.0, 0.0, 1.0, defaultEasing);
        _ = Interpolation.ValueAt(0.35, 0f, 100f, 0.0, 1.0, bezier);
        _ = Interpolation.ValueAt(0.35, 0, 100, 0.0, 1.0, defaultEasing);
        _ = Interpolation.ValueAt(0.35, Vector2.Zero, Vector2.One, 0.0, 1.0, bezier);
        _ = Interpolation.ValueAt(0.35, Color4.BLACK, Color4.WHITE, 0.0, 1.0, defaultEasing);
        _ = Interpolation.ValueAt(0.35, MarginPadding.ZERO, new MarginPadding(10f), 0.0, 1.0, bezier);
        _ = Interpolation.ValueAt(0.35, RectangleF.EMPTY, new RectangleF(1f, 2f, 3f, 4f), 0.0, 1.0, defaultEasing);

        var startAllocated = GC.GetAllocatedBytesForCurrentThread();

        var d = 0.0;
        var f = 0f;
        var n = 0;
        var v = Vector2.Zero;
        var c = Color4.BLACK;
        var m = MarginPadding.ZERO;
        var r = RectangleF.EMPTY;

        for (var i = 0; i < 100; i++)
        {
            var t = i * 0.01;
            d += Interpolation.ValueAt(t, 0.0, 100.0, 0.0, 1.0, defaultEasing);
            f += Interpolation.ValueAt(t, 0f, 100f, 0.0, 1.0, bezier);
            n += Interpolation.ValueAt(t, 0, 100, 0.0, 1.0, defaultEasing);
            v += Interpolation.ValueAt(t, Vector2.Zero, Vector2.One, 0.0, 1.0, bezier);
            c = Interpolation.ValueAt(t, c, Color4.WHITE, 0.0, 1.0, defaultEasing);
            m = Interpolation.ValueAt(t, m, new MarginPadding(10f), 0.0, 1.0, bezier);
            r = Interpolation.ValueAt(t, r, new RectangleF(1f, 2f, 3f, 4f), 0.0, 1.0, defaultEasing);
        }

        var endAllocated = GC.GetAllocatedBytesForCurrentThread();

        Assert.True(d > 0);
        Assert.True(f > 0);
        Assert.True(n > 0);
        Assert.True(v.X > 0);
        Assert.Equal(0, endAllocated - startAllocated);
    }

    private readonly struct HalfStepEasing : IEasingFunction
    {
        public double ApplyEasing(double time) => time < 0.0 ? 0.0 : 0.5;
    }
}
