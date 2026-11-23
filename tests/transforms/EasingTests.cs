using System.Runtime.CompilerServices;
using kajarlabs.Framework.Scene.Transforms;

namespace kajarlabs.Framework.Scene.Transforms.Tests;

public sealed class EasingTests
{
    public static TheoryData<Easing> AllEasings()
    {
        var data = new TheoryData<Easing>();
        foreach (var easing in Enum.GetValues<Easing>())
            data.Add(easing);
        return data;
    }

    public static TheoryData<Easing> MonotonicEasings() =>
    [
        Easing.None,
        Easing.In,
        Easing.Out,
        Easing.InQuad,
        Easing.OutQuad,
        Easing.InOutQuad,
        Easing.InCubic,
        Easing.OutCubic,
        Easing.InOutCubic,
        Easing.InQuart,
        Easing.OutQuart,
        Easing.InOutQuart,
        Easing.InQuint,
        Easing.OutQuint,
        Easing.InOutQuint,
        Easing.InSine,
        Easing.OutSine,
        Easing.InOutSine,
        Easing.InExpo,
        Easing.OutExpo,
        Easing.InOutExpo,
        Easing.InCirc,
        Easing.OutCirc,
        Easing.InOutCirc,
        Easing.OutPow10,
    ];

    public static TheoryData<Easing, Easing, Easing> EasingFamilies() =>
    [
        (Easing.InQuad, Easing.OutQuad, Easing.InOutQuad),
        (Easing.InCubic, Easing.OutCubic, Easing.InOutCubic),
        (Easing.InQuart, Easing.OutQuart, Easing.InOutQuart),
        (Easing.InQuint, Easing.OutQuint, Easing.InOutQuint),
        (Easing.InSine, Easing.OutSine, Easing.InOutSine),
        (Easing.InExpo, Easing.OutExpo, Easing.InOutExpo),
        (Easing.InCirc, Easing.OutCirc, Easing.InOutCirc),
        (Easing.InElastic, Easing.OutElastic, Easing.InOutElastic),
        (Easing.InBack, Easing.OutBack, Easing.InOutBack),
        (Easing.InBounce, Easing.OutBounce, Easing.InOutBounce),
    ];

    [Theory]
    [MemberData(nameof(AllEasings))]
    public void AllCurvesHitExactZeroAndOneAtBoundaries(Easing easing)
    {
        Assert.Equal(0.0, DefaultEasingFunction.ApplyEasing(easing, 0.0));
        Assert.Equal(1.0, DefaultEasingFunction.ApplyEasing(easing, 1.0));
    }

    [Theory]
    [MemberData(nameof(MonotonicEasings))]
    public void MonotonicCurvesIncreaseAcrossUnitInterval(Easing easing)
    {
        const int steps = 200;
        var previous = DefaultEasingFunction.ApplyEasing(easing, 0.0);

        for (var i = 1; i <= steps; i++)
        {
            var t = (double)i / steps;
            var current = DefaultEasingFunction.ApplyEasing(easing, t);

            // OutPow10 saturates to 1.0 in IEEE-754 double precision for (1 - t)^10 < 2^-53 (t >= ~0.975).
            if (easing == Easing.OutPow10 && t > 0.96)
                Assert.True(current >= previous, $"Expected {easing} at t={t} ({current}) >= {previous}.");
            else
                Assert.True(current > previous, $"Expected {easing} at t={t} ({current}) > {previous}.");

            Assert.InRange(current, 0.0, 1.0);
            previous = current;
        }
    }

    [Theory]
    [MemberData(nameof(EasingFamilies))]
    public void FamiliesSatisfyInOutDualityAndInOutSymmetry(Easing easeIn, Easing easeOut, Easing easeInOut)
    {
        Assert.Equal(0.5, DefaultEasingFunction.ApplyEasing(easeInOut, 0.5), precision: 12);

        const int steps = 100;
        for (var i = 0; i <= steps; i++)
        {
            var t = (double)i / steps;

            var inVal = DefaultEasingFunction.ApplyEasing(easeIn, t);
            var outMirror = 1.0 - DefaultEasingFunction.ApplyEasing(easeOut, 1.0 - t);
            Assert.Equal(inVal, outMirror, precision: 11);

            var inOutVal = DefaultEasingFunction.ApplyEasing(easeInOut, t);
            var inOutMirror = 1.0 - DefaultEasingFunction.ApplyEasing(easeInOut, 1.0 - t);
            Assert.Equal(inOutVal, inOutMirror, precision: 11);
        }
    }

    [Fact]
    public void AliasesAndSpecialCurvesMatchExpectedFormulas()
    {
        for (var i = 0; i <= 40; i++)
        {
            var t = i / 40.0;

            Assert.Equal(t, DefaultEasingFunction.ApplyEasing(Easing.None, t), precision: 14);
            Assert.Equal(DefaultEasingFunction.ApplyEasing(Easing.InQuad, t), DefaultEasingFunction.ApplyEasing(Easing.In, t), precision: 14);
            Assert.Equal(DefaultEasingFunction.ApplyEasing(Easing.OutQuad, t), DefaultEasingFunction.ApplyEasing(Easing.Out, t), precision: 14);
            Assert.Equal(1.0 - Math.Pow(1.0 - t, 10.0), DefaultEasingFunction.ApplyEasing(Easing.OutPow10, t), precision: 14);
        }
    }

    [Fact]
    public void BackCurvesOvershootBoundsInExpectedRegions()
    {
        var minInBack = sampleMin(Easing.InBack, 0.0, 1.0);
        var maxOutBack = sampleMax(Easing.OutBack, 0.0, 1.0);
        var minInOutBack = sampleMin(Easing.InOutBack, 0.0, 0.5);
        var maxInOutBack = sampleMax(Easing.InOutBack, 0.5, 1.0);

        Assert.True(minInBack < -0.05);
        Assert.True(maxOutBack > 1.05);
        Assert.True(minInOutBack < -0.05);
        Assert.True(maxInOutBack > 1.05);
    }

    [Fact]
    public void ElasticCurvesOscillateOutsideUnitRange()
    {
        var minInElastic = sampleMin(Easing.InElastic, 0.0, 1.0);
        var maxInElastic = sampleMax(Easing.InElastic, 0.0, 0.999);
        var maxOutElastic = sampleMax(Easing.OutElastic, 0.0, 1.0);
        var minInOutElastic = sampleMin(Easing.InOutElastic, 0.0, 0.5);
        var maxInOutElastic = sampleMax(Easing.InOutElastic, 0.5, 1.0);

        Assert.True(minInElastic < -0.1);
        Assert.True(maxInElastic > 0.0);
        Assert.True(maxOutElastic > 1.1);
        Assert.True(minInOutElastic < -0.05);
        Assert.True(maxInOutElastic > 1.05);
    }

    [Fact]
    public void BounceCurvesStayInUnitRangeAndRebound()
    {
        foreach (var easing in new[] { Easing.InBounce, Easing.OutBounce, Easing.InOutBounce })
        {
            for (var i = 0; i <= 200; i++)
            {
                var value = DefaultEasingFunction.ApplyEasing(easing, i / 200.0);
                Assert.InRange(value, 0.0, 1.0);
            }
        }

        // OutBounce reaches 1.0 at 1/2.75 (~0.3636) and rebounds below 1.0 before rising again.
        Assert.Equal(1.0, DefaultEasingFunction.ApplyEasing(Easing.OutBounce, 1.0 / 2.75), precision: 12);
        Assert.True(DefaultEasingFunction.ApplyEasing(Easing.OutBounce, 1.5 / 2.75) < 0.8);
        Assert.Equal(1.0, DefaultEasingFunction.ApplyEasing(Easing.OutBounce, 2.0 / 2.75), precision: 12);
        Assert.True(DefaultEasingFunction.ApplyEasing(Easing.OutBounce, 2.25 / 2.75) < 0.96);
    }

    [Fact]
    public void DefaultEasingFunctionWrapperEqualityAndLayoutBehaveCorrectly()
    {
        Assert.Equal(Easing.None, DefaultEasingFunction.DEFAULT.Easing);

        DefaultEasingFunction implicitCubic = Easing.InOutCubic;
        var explicitCubic = DefaultEasingFunction.FromEasing(Easing.InOutCubic);
        var other = new DefaultEasingFunction(Easing.OutExpo);

        Assert.Equal(explicitCubic, implicitCubic);
        Assert.True(explicitCubic == implicitCubic);
        Assert.True(explicitCubic != other);
        Assert.Equal(explicitCubic.GetHashCode(), implicitCubic.GetHashCode());
        Assert.Equal("InOutCubic", explicitCubic.ToString());
        Assert.Equal(DefaultEasingFunction.ApplyEasing(Easing.InOutCubic, 0.35), explicitCubic.ApplyEasing(0.35));

        Assert.Equal(sizeof(Easing), Unsafe.SizeOf<DefaultEasingFunction>());
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<DefaultEasingFunction>());

        var invalid = (Easing)9999;
        Assert.Throws<ArgumentOutOfRangeException>(() => DefaultEasingFunction.ApplyEasing(invalid, 0.5));
    }

    private static double sampleMin(Easing easing, double start, double end)
    {
        var min = double.PositiveInfinity;
        for (var i = 0; i <= 200; i++)
        {
            var t = start + ((end - start) * (i / 200.0));
            min = Math.Min(min, DefaultEasingFunction.ApplyEasing(easing, t));
        }

        return min;
    }

    private static double sampleMax(Easing easing, double start, double end)
    {
        var max = double.NegativeInfinity;
        for (var i = 0; i <= 200; i++)
        {
            var t = start + ((end - start) * (i / 200.0));
            max = Math.Max(max, DefaultEasingFunction.ApplyEasing(easing, t));
        }

        return max;
    }
}
