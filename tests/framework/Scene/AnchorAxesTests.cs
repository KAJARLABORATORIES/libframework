using System.Numerics;
using kajarlabs.Framework.Scene;

namespace kajarlabs.Framework.Tests.Scene;

public sealed class AnchorAxesTests
{
    [Theory]
    [InlineData(Anchor.TopLeft, 0f, 0f)]
    [InlineData(Anchor.TopCentre, 0.5f, 0f)]
    [InlineData(Anchor.TopRight, 1f, 0f)]
    [InlineData(Anchor.CentreLeft, 0f, 0.5f)]
    [InlineData(Anchor.Centre, 0.5f, 0.5f)]
    [InlineData(Anchor.CentreRight, 1f, 0.5f)]
    [InlineData(Anchor.BottomLeft, 0f, 1f)]
    [InlineData(Anchor.BottomCentre, 0.5f, 1f)]
    [InlineData(Anchor.BottomRight, 1f, 1f)]
    [InlineData(Anchor.Custom, 0f, 0f)]
    public void CompositeAndCustomAnchorsProduceExpectedOffsets(Anchor anchor, float expectedX, float expectedY)
    {
        Assert.Equal(new Vector2(expectedX, expectedY), anchor.GetRelativeOffset());
    }

    [Fact]
    public void DirectionalFlagsCombineAndConflictingFlagsThrow()
    {
        Assert.Equal(new Vector2(1f, 0.5f), (Anchor.VerticalCentre | Anchor.Right).GetRelativeOffset());
        Assert.Throws<ArgumentException>(() => (Anchor.Left | Anchor.Right).GetRelativeOffset());
        Assert.Throws<ArgumentException>(() => (Anchor.Top | Anchor.Bottom).GetRelativeOffset());
    }

    [Fact]
    public void AxesFlagsCoverSingleAndCombinedAxes()
    {
        Assert.Equal(Axes.None, Axes.Both & ~Axes.X & ~Axes.Y);
        Assert.True(Axes.Both.HasFlag(Axes.X));
        Assert.True(Axes.Both.HasFlag(Axes.Y));
        Assert.False(Axes.X.HasFlag(Axes.Y));
    }
}
