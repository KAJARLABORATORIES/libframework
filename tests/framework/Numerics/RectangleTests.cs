using System.Numerics;
using kajarlabs.Framework.Numerics;

namespace kajarlabs.Framework.Tests.Numerics;

public sealed class RectangleTests
{
    [Fact]
    public void RectangleIPropertiesContainmentAndOperationsWork()
    {
        var rect = new RectangleI(10, 20, 100, 50);

        Assert.Equal(10, rect.Left);
        Assert.Equal(20, rect.Top);
        Assert.Equal(110, rect.Right);
        Assert.Equal(70, rect.Bottom);
        Assert.False(rect.IsEmpty);
        Assert.True(RectangleI.EMPTY.IsEmpty);
        Assert.True(new RectangleI(0, 0, 0, 10).IsEmpty);
        Assert.True(new RectangleI(0, 0, 10, -1).IsEmpty);

        Assert.True(rect.Contains(10, 20));
        Assert.True(rect.Contains(109, 69));
        Assert.False(rect.Contains(110, 69));
        Assert.False(rect.Contains(109, 70));
        Assert.False(RectangleI.EMPTY.Contains(0, 0));

        Assert.Equal(new RectangleI(15, 15, 100, 50), rect.Offset(5, -5));

        var other = new RectangleI(50, 40, 100, 60);
        Assert.True(rect.Intersects(other));
        Assert.Equal(new RectangleI(50, 40, 60, 30), RectangleI.Intersect(rect, other));
        Assert.Equal(new RectangleI(10, 20, 140, 80), RectangleI.Union(rect, other));
        Assert.Equal(new RectangleI(50, 40, 60, 30), rect.Clamp(other));

        var disjoint = new RectangleI(200, 200, 10, 10);
        Assert.False(rect.Intersects(disjoint));
        Assert.Equal(RectangleI.EMPTY, RectangleI.Intersect(rect, disjoint));
        Assert.Equal(rect, RectangleI.Union(rect, RectangleI.EMPTY));
        Assert.Equal(rect, RectangleI.Union(RectangleI.EMPTY, rect));
    }

    [Fact]
    public void RectangleFPropertiesContainmentAndOperationsWork()
    {
        var rect = new RectangleF(10f, 20f, 100f, 50f);

        Assert.Equal(new Vector2(10f, 20f), rect.Position);
        Assert.Equal(new Vector2(100f, 50f), rect.Size);
        Assert.Equal(new Vector2(10f, 20f), rect.TopLeft);
        Assert.Equal(new Vector2(110f, 20f), rect.TopRight);
        Assert.Equal(new Vector2(10f, 70f), rect.BottomLeft);
        Assert.Equal(new Vector2(110f, 70f), rect.BottomRight);
        Assert.Equal(new Vector2(60f, 45f), rect.Center);

        Assert.Equal(rect, RectangleF.FromPoints(10f, 20f, 110f, 70f));
        Assert.True(rect.Contains(new Vector2(10f, 20f)));
        Assert.False(rect.Contains(110f, 20f));

        Assert.Equal(new RectangleF(5f, 15f, 110f, 60f), rect.Inflate(5f));
        Assert.Equal(new RectangleF(15f, 10f, 100f, 50f), rect.Offset(new Vector2(5f, -10f)));

        var other = new RectangleF(60f, 30f, 80f, 50f);
        Assert.True(rect.Intersects(other));
        Assert.Equal(new RectangleF(60f, 30f, 50f, 40f), RectangleF.Intersect(rect, other));
        Assert.Equal(new RectangleF(10f, 20f, 130f, 60f), RectangleF.Union(rect, other));
    }

    [Fact]
    public void EqualityAndHashCodeAreConsistent()
    {
        var aI = new RectangleI(1, 2, 3, 4);
        var bI = new RectangleI(1, 2, 3, 4);
        Assert.True(aI == bI);
        Assert.Equal(aI.GetHashCode(), bI.GetHashCode());

        var aF = new RectangleF(0f, -0f, 10f, 20f);
        var bF = new RectangleF(-0f, 0f, 10f, 20f);
        Assert.True(aF == bF);
        Assert.Equal(aF, bF);
        Assert.Equal(aF.GetHashCode(), bF.GetHashCode());
    }
}
