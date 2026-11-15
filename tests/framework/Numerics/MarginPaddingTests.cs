using System.Numerics;
using System.Runtime.CompilerServices;
using kajarlabs.Framework.Numerics;

namespace kajarlabs.Framework.Tests.Numerics;

public sealed class MarginPaddingTests
{
    [Fact]
    public void ConstructorsAndTotalsWork()
    {
        var uniform = MarginPadding.All(10f);
        Assert.Equal(10f, uniform.Left);
        Assert.Equal(10f, uniform.Top);
        Assert.Equal(10f, uniform.Right);
        Assert.Equal(10f, uniform.Bottom);
        Assert.Equal(new Vector2(20f, 20f), uniform.Total);
        Assert.Equal(new Vector2(10f, 10f), uniform.TopLeftOffset);

        var asymmetric = new MarginPadding(4f, 6f, 8f, 12f);
        Assert.Equal(12f, asymmetric.TotalHorizontal);
        Assert.Equal(18f, asymmetric.TotalVertical);

        var symmetric = MarginPadding.FromHorizontalVertical(16f, 8f);
        Assert.Equal(new MarginPadding { Left = 16f, Top = 8f, Right = 16f, Bottom = 8f }, symmetric);
        Assert.Equal(4 * sizeof(float), Unsafe.SizeOf<MarginPadding>());
    }

    [Fact]
    public void DeflateInflateAndArithmeticWork()
    {
        var rect = new RectangleF(100f, 50f, 400f, 200f);
        var padding = new MarginPadding(10f, 20f, 30f, 40f);

        var inner = padding.Deflate(rect);
        Assert.Equal(new RectangleF(110f, 70f, 360f, 140f), inner);
        Assert.Equal(rect, padding.Inflate(inner));

        var sum = new MarginPadding(1f, 2f, 3f, 4f) + new MarginPadding(5f, 6f, 7f, 8f);
        Assert.Equal(new MarginPadding(6f, 8f, 10f, 12f), sum);
        Assert.Equal(new MarginPadding(4f, 4f, 4f, 4f), new MarginPadding(5f, 6f, 7f, 8f) - new MarginPadding(1f, 2f, 3f, 4f));
        Assert.Equal(new MarginPadding(-2f, -4f, -6f, -8f), -new MarginPadding(2f, 4f, 6f, 8f));
        Assert.Equal(new MarginPadding(3f, 6f, 9f, 12f), new MarginPadding(1f, 2f, 3f, 4f) * 3f);
        Assert.Equal(new MarginPadding(3f, 6f, 9f, 12f), 3f * new MarginPadding(1f, 2f, 3f, 4f));
    }

    [Fact]
    public void EqualityAndNegativeZeroNormalizationWork()
    {
        var a = MarginPadding.ZERO;
        var b = new MarginPadding(-0f, -0f, -0f, -0f);

        Assert.Equal(a, b);
        Assert.True(a == b);
        Assert.False(a != b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotEqual(a, new MarginPadding(1f));
    }
}
