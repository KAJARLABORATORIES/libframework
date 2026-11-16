using System.Numerics;
using kajarlabs.Framework.Numerics;
using kajarlabs.Framework.Scene;
using kajarlabs.Framework.Tests.Timing;
using kajarlabs.Framework.Timing;

namespace kajarlabs.Framework.Tests.Scene;

public sealed class DrawableInvalidationAndClockTests
{
    [Fact]
    public void PropertyAssignmentsWithUnchangedValuesDoNotIncrementInvalidationId()
    {
        var drawable = new Drawable
        {
            Position = new Vector2(10f, 20f),
            Size = new Vector2(100f, 50f),
            Color = Color4.RED,
            Alpha = 0.5f,
            Depth = 2f,
        };

        var baselineId = drawable.InvalidationID;

        drawable.Position = new Vector2(10f, 20f);
        drawable.X = 10f;
        drawable.Y = 20f;
        drawable.Size = new Vector2(100f, 50f);
        drawable.Width = 100f;
        drawable.Height = 50f;
        drawable.Rotation = 0f;
        drawable.Scale = Vector2.One;
        drawable.Shear = Vector2.Zero;
        drawable.Anchor = Anchor.TopLeft;
        drawable.Origin = Anchor.TopLeft;
        drawable.RelativePositionAxes = Axes.None;
        drawable.RelativeSizeAxes = Axes.None;
        drawable.Margin = MarginPadding.ZERO;
        drawable.Color = Color4.RED;
        drawable.Alpha = 0.5f;
        drawable.Depth = 2f;
        drawable.AlwaysPresent = false;

        Assert.Equal(baselineId, drawable.InvalidationID);
        Assert.False(drawable.Invalidate(Invalidation.None));
        Assert.Equal(baselineId, drawable.InvalidationID);
    }

    [Fact]
    public void InvalidationPropagatesFromParentToChildWithoutCircularLoopAndPreservesColorCacheIsolation()
    {
        var parent = new TrackingParent { Size = new Vector2(200f, 200f) };
        var child = new TrackingDrawable { Size = new Vector2(50f, 50f) };
        parent.Add(child);

        // Prime both DrawInfo and DrawColorInfo caches.
        var initialQuad = child.ScreenSpaceDrawQuad;
        var initialColor = child.DrawColorInfo;
        Assert.Equal(new Vector2(50f, 50f), initialQuad.BottomRight);
        Assert.Equal(Color4.WHITE, initialColor.Color);

        var parentIdBefore = parent.InvalidationID;
        var childNotificationsBefore = parent.ChildInvalidationCount;

        // Changing parent Color propagates Color only to child and does not bounce back to parent.
        parent.Color = new Color4(0.5f, 0.25f, 1f, 1f);
        Assert.Equal(parentIdBefore + 1, parent.InvalidationID);
        Assert.Equal(childNotificationsBefore, parent.ChildInvalidationCount);
        Assert.Equal(Invalidation.Color, child.LastInvalidationFlags);
        Assert.Equal(InvalidationSource.Parent, child.LastInvalidationSource);
        Assert.Equal(new Color4(0.5f, 0.25f, 1f, 1f), child.DrawColorInfo.Color);

        // Changing child Position notifies parent once with Self source without parent bouncing back.
        var childIdBefore = child.InvalidationID;
        child.Position = new Vector2(15f, 25f);
        Assert.Equal(childIdBefore + 1, child.InvalidationID);
        Assert.Equal(childNotificationsBefore + 1, parent.ChildInvalidationCount);
        Assert.Equal(new Vector2(15f, 25f), child.ScreenSpaceDrawQuad.TopLeft);
    }

    [Fact]
    public void ColorAndAlphaInheritMultiplicativelyAndControlPresence()
    {
        var grandparent = new TrackingParent
        {
            Color = new Color4(0.8f, 0.5f, 0.4f, 1f),
            Alpha = 0.5f,
        };
        var parent = new TrackingParent
        {
            Color = new Color4(0.5f, 0.8f, 1f, 0.5f),
            Alpha = 0.5f,
        };
        var child = new TrackingDrawable
        {
            Color = new Color4(1f, 0.5f, 0.5f, 1f),
            Alpha = 0.8f,
        };

        grandparent.Add(parent);
        parent.Add(child);

        var actual = child.DrawColorInfo.Color;
        Assert.Equal(0.4f, actual.R, precision: 4);
        Assert.Equal(0.2f, actual.G, precision: 4);
        Assert.Equal(0.2f, actual.B, precision: 4);
        Assert.Equal(0.1f, actual.A, precision: 4);

        Assert.True(child.IsPresent);
        child.Alpha = 0f;
        Assert.False(child.IsPresent);
        Assert.True(child.LastInvalidationFlags.HasFlag(Invalidation.Presence));

        child.AlwaysPresent = true;
        Assert.True(child.IsPresent);
        Assert.True(child.LastInvalidationFlags.HasFlag(Invalidation.Presence));
    }

    [Fact]
    public void UpdateSubTreePropagatesClockToInheritedChildrenAndRespectsCustomChildClock()
    {
        var manual = new ManualClock(start: true, initialTime: 100);
        var rootFramedClock = new FramedClock(manual);

        var childManual = new ManualClock(start: true, initialTime: 500);
        var customChildClock = new FramedClock(childManual);

        var root = new TrackingParent { Clock = rootFramedClock };
        var inheritedChild = new TrackingDrawable();
        var customClockChild = new TrackingDrawable { Clock = customChildClock };

        root.Add(inheritedChild);
        root.Add(customClockChild);

        Assert.Same(rootFramedClock, inheritedChild.Clock);
        Assert.Same(customChildClock, customClockChild.Clock);

        manual.Advance(16);
        childManual.Advance(33);

        Assert.True(root.UpdateSubTree());

        Assert.Equal(1, root.UpdateCount);
        Assert.Equal(1, inheritedChild.UpdateCount);
        Assert.Equal(1, customClockChild.UpdateCount);

        Assert.Equal(116, inheritedChild.Time, precision: 4);
        Assert.Equal(16, inheritedChild.ElapsedFrameTime, precision: 4);

        Assert.Equal(533, customClockChild.Time, precision: 4);
        Assert.Equal(33, customClockChild.ElapsedFrameTime, precision: 4);

        // Clearing custom clock falls back to parent's inherited clock.
        customClockChild.Clock = null;
        Assert.Same(rootFramedClock, customClockChild.Clock);
    }

    [Fact]
    public void DepthChangeNotifiesParentDuplicateParentThrowsAndDisposeStopsUpdates()
    {
        var firstParent = new TrackingParent();
        var secondParent = new TrackingParent();
        var child = new TrackingDrawable();

        firstParent.Add(child);
        Assert.Same(firstParent, child.Parent);

        child.Depth = -5f;
        Assert.Equal(1, firstParent.ChildDepthChangedCount);

        Assert.Throws<InvalidOperationException>(() => secondParent.Add(child));

        firstParent.Remove(child);
        Assert.Null(child.Parent);
        secondParent.Add(child);
        Assert.Same(secondParent, child.Parent);

        child.Dispose();
        Assert.True(child.IsDisposed);
        Assert.Null(child.Parent);
        Assert.False(child.UpdateSubTree());

        // Idempotent second Dispose and rejecting attachment after disposal.
        child.Dispose();
        Assert.Throws<ObjectDisposedException>(() => firstParent.Add(child));
    }

    private sealed class TrackingDrawable : Drawable
    {
        public Invalidation LastInvalidationFlags { get; private set; }

        public InvalidationSource LastInvalidationSource { get; private set; }

        public int UpdateCount { get; private set; }

        protected override void OnInvalidate(Invalidation invalidation, InvalidationSource source)
        {
            LastInvalidationFlags = invalidation;
            LastInvalidationSource = source;
        }

        protected override void Update()
        {
            UpdateCount++;
        }
    }

    private sealed class TrackingParent : Drawable
    {
        private readonly List<Drawable> children = [];

        public int UpdateCount { get; private set; }

        public int ChildInvalidationCount { get; private set; }

        public int ChildDepthChangedCount { get; private set; }

        public void Add(Drawable child)
        {
            child.SetParent(this);
            children.Add(child);
        }

        public void Remove(Drawable child)
        {
            if (children.Remove(child))
                child.SetParent(null);
        }

        protected override void Update()
        {
            UpdateCount++;
        }

        protected override bool UpdateChildren()
        {
            foreach (var child in children)
                child.UpdateSubTree();

            return true;
        }

        protected internal override void UpdateClock(IFrameBasedClock? clock)
        {
            base.UpdateClock(clock);
            var effective = Clock;

            foreach (var child in children)
                child.UpdateClock(effective);
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

        protected internal override void OnChildInvalidated(Drawable child, Invalidation invalidation)
        {
            ChildInvalidationCount++;
        }

        protected internal override void OnChildDepthChanged(Drawable child)
        {
            ChildDepthChangedCount++;
        }
    }
}
