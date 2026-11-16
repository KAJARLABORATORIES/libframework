using System.Numerics;
using kajarlabs.Framework.Numerics;
using kajarlabs.Framework.Scene;

namespace kajarlabs.Framework.Tests.Scene;

public sealed class DrawableTransformTests
{
    [Theory]
    [InlineData(Anchor.TopLeft, 0f, 0f)]
    [InlineData(Anchor.TopCentre, 100f, 0f)]
    [InlineData(Anchor.TopRight, 200f, 0f)]
    [InlineData(Anchor.CentreLeft, 0f, 50f)]
    [InlineData(Anchor.Centre, 100f, 50f)]
    [InlineData(Anchor.CentreRight, 200f, 50f)]
    [InlineData(Anchor.BottomLeft, 0f, 100f)]
    [InlineData(Anchor.BottomCentre, 100f, 100f)]
    [InlineData(Anchor.BottomRight, 200f, 100f)]
    public void AnchorAndMatchingOriginPlaceChildAcrossParentBounds(Anchor alignment, float expectedX, float expectedY)
    {
        var parent = new TestContainer { Size = new Vector2(200f, 100f) };
        var child = new Drawable
        {
            Size = new Vector2(40f, 20f),
            Anchor = alignment,
            Origin = alignment,
        };
        parent.Add(child);

        var originWorld = child.ToScreenSpace(child.OriginPosition);
        assertVectorClose(new Vector2(expectedX, expectedY), originWorld);

        var quad = child.ScreenSpaceDrawQuad;
        Assert.True(quad.TopLeft.X >= -1e-4f && quad.TopRight.X <= 200.0001f);
        Assert.True(quad.TopLeft.Y >= -1e-4f && quad.BottomLeft.Y <= 100.0001f);
    }

    [Fact]
    public void CustomOriginPositionSwitchesOriginToCustomAndPivotsAroundExplicitPoint()
    {
        var drawable = new Drawable
        {
            Size = new Vector2(100f, 60f),
            Position = new Vector2(300f, 200f),
            Rotation = 90f,
            OriginPosition = new Vector2(25f, 10f),
        };

        Assert.Equal(Anchor.Custom, drawable.Origin);
        Assert.Equal(new Vector2(25f, 10f), drawable.OriginPosition);
        assertVectorClose(new Vector2(300f, 200f), drawable.ToScreenSpace(new Vector2(25f, 10f)));

        // Point (35, 10) is +10 on local X from the pivot; rotated 90° CW -> +10 on screen Y.
        assertVectorClose(new Vector2(300f, 210f), drawable.ToScreenSpace(new Vector2(35f, 10f)));

        // Assigning a standard Origin overrides the custom pivot.
        drawable.Origin = Anchor.Centre;
        Assert.Equal(new Vector2(50f, 30f), drawable.OriginPosition);
    }

    [Fact]
    public void InvalidConflictingAnchorOrOriginThrowsArgumentException()
    {
        var drawable = new Drawable();

        Assert.Throws<ArgumentException>(() => drawable.Anchor = Anchor.Left | Anchor.Right);
        Assert.Throws<ArgumentException>(() => drawable.Origin = Anchor.Top | Anchor.Bottom);
    }

    [Fact]
    public void RelativeSizeAndPositionAxesScaleWithParentChildAreaAndMargin()
    {
        var parent = new TestContainer
        {
            Size = new Vector2(400f, 200f),
            Padding = new MarginPadding(20f, 10f, 20f, 10f),
        };

        var child = new Drawable
        {
            RelativeSizeAxes = Axes.Both,
            RelativePositionAxes = Axes.X,
            Size = new Vector2(0.5f, 0.25f),
            Position = new Vector2(0.25f, 15f),
            Margin = new MarginPadding(10f, 5f, 30f, 15f),
        };
        parent.Add(child);

        // Parent ChildSize = (360, 180), ChildOffset = (20, 10).
        // Deflated by Margin (L:10, T:5, R:30, B:15) -> available size (320, 160), origin (30, 15).
        assertVectorClose(new Vector2(160f, 40f), child.DrawSize);
        assertVectorClose(new Vector2(30f, 15f), child.AnchorPosition);
        assertVectorClose(new Vector2(80f, 15f), child.DrawPosition);
        assertVectorClose(new Vector2(110f, 30f), child.ToParentSpace(Vector2.Zero));

        // LayoutSize / LayoutRectangle account for Margin around DrawSize.
        assertVectorClose(new Vector2(200f, 60f), child.LayoutSize);
        Assert.Equal(new RectangleF(-10f, -5f, 200f, 60f), child.LayoutRectangle);
    }

    [Fact]
    public void NestedRotationScaleShearRoundTripsScreenLocalAndParentSpaces()
    {
        var root = new TestContainer
        {
            Position = new Vector2(100f, 50f),
            Size = new Vector2(800f, 600f),
            Rotation = 25f,
            Scale = new Vector2(1.5f, 0.8f),
            Shear = new Vector2(0.2f, -0.05f),
            Origin = Anchor.Centre,
        };

        var middle = new TestContainer
        {
            Position = new Vector2(-40f, 30f),
            Size = new Vector2(200f, 120f),
            Anchor = Anchor.Centre,
            Origin = Anchor.BottomRight,
            Rotation = -42.5f,
            Scale = new Vector2(2f, 1.25f),
            Shear = new Vector2(-0.15f, 0.3f),
        };
        root.Add(middle);

        var leaf = new Drawable
        {
            Position = new Vector2(12f, -18f),
            Size = new Vector2(60f, 40f),
            Anchor = Anchor.TopRight,
            Origin = Anchor.CentreLeft,
            Rotation = 115f,
            Scale = new Vector2(0.75f, 1.8f),
            Shear = new Vector2(0.1f, 0.2f),
        };
        middle.Add(leaf);

        var localPoint = new Vector2(27.5f, 13.25f);
        var inMiddle = leaf.ToParentSpace(localPoint);
        var screenFromMiddle = middle.ToScreenSpace(inMiddle);
        var screenDirect = leaf.ToScreenSpace(localPoint);

        assertVectorClose(screenDirect, screenFromMiddle, precision: 3);

        Assert.True(leaf.TryToLocalSpace(screenDirect, out var roundTripped));
        assertVectorClose(localPoint, roundTripped, precision: 3);
        assertVectorClose(localPoint, leaf.ToLocalSpace(screenDirect), precision: 3);

        var quad = leaf.ScreenSpaceDrawQuad;
        assertVectorClose(leaf.ToScreenSpace(Vector2.Zero), quad.TopLeft, precision: 3);
        assertVectorClose(leaf.ToScreenSpace(new Vector2(60f, 0f)), quad.TopRight, precision: 3);
        assertVectorClose(leaf.ToScreenSpace(new Vector2(60f, 40f)), quad.BottomRight, precision: 3);
        assertVectorClose(leaf.ToScreenSpace(new Vector2(0f, 40f)), quad.BottomLeft, precision: 3);
    }

    [Fact]
    public void ContainsRespectsTransformedBoundsAndHandlesSingularMatrixSafely()
    {
        var drawable = new Drawable
        {
            Position = new Vector2(200f, 150f),
            Size = new Vector2(100f, 40f),
            Origin = Anchor.Centre,
            Rotation = 45f,
            Scale = new Vector2(2f, 1f),
        };

        var center = drawable.ToScreenSpace(new Vector2(50f, 20f));
        var insideNearCorner = drawable.ToScreenSpace(new Vector2(95f, 35f));
        var outside = drawable.ToScreenSpace(new Vector2(105f, 20f));

        Assert.True(drawable.Contains(center));
        Assert.True(drawable.Contains(insideNearCorner));
        Assert.False(drawable.Contains(outside));

        // Zero scale produces a singular matrix; conversions and Contains must not throw.
        drawable.Scale = Vector2.Zero;
        Assert.False(drawable.DrawInfo.IsInvertible);
        Assert.False(drawable.TryToLocalSpace(center, out var fallback));
        Assert.Equal(Vector2.Zero, fallback);
        Assert.Equal(Vector2.Zero, drawable.ToLocalSpace(center));
        Assert.False(drawable.Contains(center));
    }

    private static void assertVectorClose(Vector2 expected, Vector2 actual, int precision = 4)
    {
        Assert.Equal(expected.X, actual.X, precision);
        Assert.Equal(expected.Y, actual.Y, precision);
    }

    private sealed class TestContainer : Drawable
    {
        private readonly List<Drawable> children = [];

        public MarginPadding Padding { get; set; } = MarginPadding.ZERO;

        protected internal override Vector2 ChildSize => DrawSize - Padding.Total;

        protected internal override Vector2 ChildOffset => Padding.TopLeftOffset;

        public void Add(Drawable child)
        {
            child.SetParent(this);
            children.Add(child);
        }

        protected override void OnInvalidate(Invalidation invalidation, InvalidationSource source)
        {
            if (source == InvalidationSource.Child)
                return;

            var childInvalidation = invalidation & (Invalidation.DrawInfo | Invalidation.Color | Invalidation.Geometry | Invalidation.Layout);

            if (childInvalidation == Invalidation.None)
                return;

            foreach (var child in children)
                child.Invalidate(childInvalidation, InvalidationSource.Parent);
        }
    }
}
