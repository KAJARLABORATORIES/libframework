using System.Numerics;
using kajarlabs.Framework.Numerics;
using kajarlabs.Framework.Scene;

namespace kajarlabs.Framework.Tests.Scene;

public sealed class ContainerLayoutAndMaskingTests
{
    [Fact]
    public void PaddingClampsChildSizeAtZeroAndShiftsChildOffset()
    {
        var container = new Container
        {
            Size = new Vector2(100f, 60f),
            Padding = new MarginPadding(15f, 10f, 25f, 20f),
        };

        var child = new Drawable
        {
            RelativeSizeAxes = Axes.Both,
            Size = new Vector2(1f, 0.5f),
        };
        container.Add(child);

        Assert.Equal(new Vector2(15f, 10f), container.ChildOffset);
        Assert.Equal(new Vector2(60f, 30f), container.ChildSize);
        Assert.Equal(new Vector2(60f, 15f), child.DrawSize);
        Assert.Equal(new Vector2(15f, 10f), child.ToScreenSpace(Vector2.Zero));

        // Padding larger than DrawSize clamps ChildSize to zero instead of going negative.
        container.Size = new Vector2(20f, 10f);
        Assert.Equal(Vector2.Zero, container.ChildSize);
        Assert.Equal(Vector2.Zero, child.DrawSize);
    }

    [Fact]
    public void DeeplyNestedRelativeSizeAndPositionPropagateOnRootResize()
    {
        var root = new Container
        {
            Size = new Vector2(1000f, 800f),
            Padding = MarginPadding.All(50f),
        };

        var level1 = new Container
        {
            RelativeSizeAxes = Axes.Both,
            Size = new Vector2(0.5f, 0.5f),
            Padding = new MarginPadding(20f, 10f, 20f, 10f),
        };

        var level2 = new Container
        {
            RelativeSizeAxes = Axes.Both,
            RelativePositionAxes = Axes.Both,
            Size = new Vector2(0.5f, 0.5f),
            Position = new Vector2(0.25f, 0.5f),
        };

        var leaf = new Drawable
        {
            RelativeSizeAxes = Axes.Both,
            Anchor = Anchor.BottomRight,
            Origin = Anchor.BottomRight,
            Size = new Vector2(0.5f, 0.25f),
        };

        root.Add(level1);
        level1.Add(level2);
        level2.Add(leaf);

        // Root ChildSize = (900, 700), offset = (50, 50)
        // level1 DrawSize = (450, 350), ChildSize = (410, 330), offset = (20, 10)
        // level2 DrawSize = (205, 165), screen TopLeft = (50+20+0.25*410, 50+10+0.5*330) = (172.5, 225)
        // leaf DrawSize = (102.5, 41.25)
        assertVectorClose(new Vector2(450f, 350f), level1.DrawSize);
        assertVectorClose(new Vector2(205f, 165f), level2.DrawSize);
        assertVectorClose(new Vector2(172.5f, 225f), level2.ToScreenSpace(Vector2.Zero));
        assertVectorClose(new Vector2(102.5f, 41.25f), leaf.DrawSize);

        // Resize root and verify full cascade updates lazily.
        root.Size = new Vector2(500f, 400f);

        // Root ChildSize = (400, 300) -> level1 = (200, 150), ChildSize = (160, 130)
        // level2 = (80, 65) -> leaf = (40, 16.25)
        assertVectorClose(new Vector2(200f, 150f), level1.DrawSize);
        assertVectorClose(new Vector2(80f, 65f), level2.DrawSize);
        assertVectorClose(new Vector2(40f, 16.25f), leaf.DrawSize);
    }

    [Fact]
    public void AutoSizeAxesEnclosesPresentChildrenWithPaddingMarginAndNestedAutoSize()
    {
        var outer = new Container
        {
            AutoSizeAxes = Axes.Both,
            Padding = new MarginPadding(10f, 20f, 30f, 40f),
        };

        var inner = new Container
        {
            AutoSizeAxes = Axes.Both,
            Position = new Vector2(5f, 10f),
            Padding = MarginPadding.All(4f),
        };

        var child = new Drawable
        {
            Size = new Vector2(80f, 30f),
            Margin = new MarginPadding(2f, 3f, 4f, 5f),
        };

        var hiddenChild = new Drawable
        {
            Position = new Vector2(500f, 500f),
            Size = new Vector2(100f, 100f),
            Alpha = 0f,
        };

        inner.AddRange([child, hiddenChild]);
        outer.Add(inner);

        // inner width = 4 (padL) + 2 (margL) + 80 + 4 (margR) + 4 (padR) = 94
        // inner height = 4 (padT) + 3 (margT) + 30 + 5 (margB) + 4 (padB) = 46
        assertVectorClose(new Vector2(94f, 46f), inner.DrawSize);

        // outer width = 10 (padL) + 5 (inner.X) + 94 + 30 (padR) = 139
        // outer height = 20 (padT) + 10 (inner.Y) + 46 + 40 (padB) = 116
        assertVectorClose(new Vector2(139f, 116f), outer.DrawSize);

        // Mutating child size propagates upward to outer.
        child.Width = 100f;
        assertVectorClose(new Vector2(114f, 46f), inner.DrawSize);
        assertVectorClose(new Vector2(159f, 116f), outer.DrawSize);

        // Making hiddenChild AlwaysPresent expands outer/inner immediately.
        hiddenChild.AlwaysPresent = true;
        assertVectorClose(new Vector2(608f, 608f), inner.DrawSize);
    }

    [Fact]
    public void AutoSizeMixedWithFixedAxisAllowsAnchoringOnNonAutoAxisAndUpdatesChildTransformsOnOriginShift()
    {
        var container = new Container
        {
            AutoSizeAxes = Axes.X,
            Height = 200f,
            Origin = Anchor.Centre,
            Position = new Vector2(300f, 200f),
            Padding = MarginPadding.All(10f),
        };

        var child = new Drawable
        {
            Anchor = Anchor.BottomLeft,
            Origin = Anchor.BottomLeft,
            Position = new Vector2(20f, 0f),
            Size = new Vector2(60f, 40f),
        };
        container.Add(child);

        // Auto width = 10 + 20 + 60 + 10 = 100; height remains 200.
        assertVectorClose(new Vector2(100f, 200f), container.DrawSize);
        // Container Origin.Centre is (50, 100) placed at (300, 200) -> container TopLeft is (250, 100).
        // Child at local (30, 190) bottom-left -> screen (280, 290).
        assertVectorClose(new Vector2(280f, 290f), child.ToScreenSpace(new Vector2(0f, 40f)));

        // Growing child updates container DrawSize, shifts container OriginPosition, and updates child screen quad.
        child.Width = 100f;
        assertVectorClose(new Vector2(140f, 200f), container.DrawSize);
        // Container TopLeft is now (300 - 70, 100) = (230, 100); child bottom-left is (260, 290).
        assertVectorClose(new Vector2(260f, 290f), child.ToScreenSpace(new Vector2(0f, 40f)));
    }

    [Fact]
    public void CircularAutoSizeConfigurationsThrowInvalidOperationException()
    {
        var selfCircular = new Container { RelativeSizeAxes = Axes.X };
        Assert.Throws<InvalidOperationException>(() => selfCircular.AutoSizeAxes = Axes.X);

        var reverseCircular = new Container { AutoSizeAxes = Axes.Y };
        Assert.Throws<InvalidOperationException>(() => reverseCircular.RelativeSizeAxes = Axes.Both);

        var autoX = new Container { AutoSizeAxes = Axes.X };
        Assert.Throws<InvalidOperationException>(() => autoX.Add(new Drawable { RelativeSizeAxes = Axes.X }));
        Assert.Throws<InvalidOperationException>(() => autoX.Add(new Drawable { RelativePositionAxes = Axes.X }));
        Assert.Throws<InvalidOperationException>(() => autoX.Add(new Drawable { Anchor = Anchor.TopRight }));
        Assert.Throws<InvalidOperationException>(() => autoX.Add(new Drawable { Anchor = Anchor.Centre }));

        var validChild = new Drawable();
        autoX.Add(validChild);
        Assert.Throws<InvalidOperationException>(() => validChild.RelativeSizeAxes = Axes.X);

        var autoY = new Container { AutoSizeAxes = Axes.Y };
        Assert.Throws<InvalidOperationException>(() => autoY.Add(new Drawable { RelativeSizeAxes = Axes.Y }));
        Assert.Throws<InvalidOperationException>(() => autoY.Add(new Drawable { RelativePositionAxes = Axes.Y }));
        Assert.Throws<InvalidOperationException>(() => autoY.Add(new Drawable { Anchor = Anchor.BottomLeft }));
    }

    [Fact]
    public void MaskingComputesScreenSpaceScissorAndIntersectsNestedMaskedContainers()
    {
        var root = new Container
        {
            Position = new Vector2(10.2f, 20.8f),
            Size = new Vector2(200.5f, 100.4f),
            Masking = true,
        };

        var unmaskedMiddle = new Container
        {
            Position = new Vector2(50f, 10f),
            Size = new Vector2(300f, 300f),
            Masking = false,
        };

        var innerMasked = new Container
        {
            Position = new Vector2(100f, 30f),
            Size = new Vector2(150f, 120f),
            Masking = true,
        };

        var rotatedMasked = new Container
        {
            Position = new Vector2(100f, 100f),
            Size = new Vector2(100f, 100f),
            Origin = Anchor.Centre,
            Rotation = 45f,
            Masking = true,
        };

        root.Add(unmaskedMiddle);
        unmaskedMiddle.Add(innerMasked);

        // Root AABB: [10.2, 20.8] to [210.7, 121.2] -> Floor(10, 20) to Ceiling(211, 122) => (10, 20, 201, 102)
        Assert.Equal(new RectangleI(10, 20, 201, 102), root.ComputeScreenSpaceScissor());
        Assert.Equal(new RectangleI(10, 20, 201, 102), root.ComputeEffectiveScissor());

        // Unmasked middle inherits root's scissor unchanged.
        Assert.Equal(new RectangleI(10, 20, 201, 102), unmaskedMiddle.ComputeEffectiveScissor());

        // Inner masked top-left is (10.2+50+100, 20.8+10+30) = (160.2, 60.8) -> (160, 60, 151, 121)
        // Intersected with root (10, 20, 201, 102) -> X:[160..211] W=51, Y:[60..122] H=62
        Assert.Equal(new RectangleI(160, 60, 51, 62), innerMasked.ComputeEffectiveScissor());

        // Disjoint nested mask yields RectangleI.EMPTY.
        innerMasked.Position = new Vector2(500f, 500f);
        Assert.Equal(RectangleI.EMPTY, innerMasked.ComputeEffectiveScissor());

        // Rotated container scissor uses transformed AABB.
        var rotatedScissor = rotatedMasked.ComputeScreenSpaceScissor();
        Assert.True(rotatedScissor.Width > 140 && rotatedScissor.Height > 140);
        Assert.True(rotatedScissor.Contains(100, 100));
    }

    private static void assertVectorClose(Vector2 expected, Vector2 actual, int precision = 3)
    {
        Assert.Equal(expected.X, actual.X, precision);
        Assert.Equal(expected.Y, actual.Y, precision);
    }
}
