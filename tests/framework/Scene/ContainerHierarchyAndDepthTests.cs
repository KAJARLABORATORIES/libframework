using System.Numerics;
using kajarlabs.Framework.Numerics;
using kajarlabs.Framework.Scene;
using kajarlabs.Framework.Tests.Timing;
using kajarlabs.Framework.Timing;

namespace kajarlabs.Framework.Tests.Scene;

public sealed class ContainerHierarchyAndDepthTests
{
    [Fact]
    public void AddAddRangeRemoveClearAndTypedContainerManageParentAndDisposal()
    {
        var container = new Container<TestItem>();
        var a = new TestItem();
        var b = new TestItem();
        var c = new TestItem();

        container.Add(a);
        container.AddRange([b, c]);

        Assert.Equal(3, container.Children.Count);
        Assert.Same(container, a.Parent);
        Assert.Same(container, b.Parent);
        Assert.Same(container, c.Parent);

        // Removing with disposeImmediately: false allows reparenting without disposing.
        Assert.True(container.Remove(b, disposeImmediately: false));
        Assert.Null(b.Parent);
        Assert.False(b.IsDisposed);
        Assert.Equal([a, c], container.Children);

        var second = new Container();
        second.Add(b);
        Assert.Same(second, b.Parent);

        // Removing with disposeImmediately: true disposes immediately.
        Assert.True(container.Remove(c, disposeImmediately: true));
        Assert.Null(c.Parent);
        Assert.True(c.IsDisposed);
        Assert.False(container.Remove(c));

        container.Clear(disposeImmediately: true);
        Assert.Empty(container.Children);
        Assert.True(a.IsDisposed);
    }

    [Fact]
    public void OwnershipSelfAndAncestorCyclesAreRejected()
    {
        var root = new Container();
        var child = new Container();
        var leaf = new Drawable();

        root.Add(child);
        child.Add(leaf);

        var otherParent = new Container();
        Assert.Throws<InvalidOperationException>(() => otherParent.Add(leaf));
        Assert.Throws<InvalidOperationException>(() => root.Add(root));
        Assert.Throws<InvalidOperationException>(() => child.Add(root));
    }

    [Fact]
    public void ContentRedirectionSeparatesInternalChildrenFromUserChildren()
    {
        var frame = new FramedContainer
        {
            Size = new Vector2(200f, 100f),
            Padding = MarginPadding.All(10f),
        };

        var userChild = new Drawable { Size = new Vector2(40f, 20f) };
        frame.Add(userChild);

        Assert.Equal(2, frame.InternalChildren.Count);
        Assert.Same(frame.Background, frame.InternalChildren[0]);
        Assert.Same(frame.InnerContent, frame.InternalChildren[1]);

        Assert.Single(frame.Children);
        Assert.Same(userChild, frame.Children[0]);
        Assert.Same(frame.InnerContent, userChild.Parent);
        Assert.Equal(new Vector2(10f, 10f), userChild.ToScreenSpace(Vector2.Zero));

        frame.Clear();
        Assert.Empty(frame.Children);
        Assert.Equal(2, frame.InternalChildren.Count);
        Assert.True(userChild.IsDisposed);
    }

    [Fact]
    public void DepthSortingIsDeterministicAndHandlesDynamicDepthChanges()
    {
        var container = new Container();
        var first = new Drawable { Depth = 0f };
        var second = new Drawable { Depth = 0f };
        var back = new Drawable { Depth = 10f };
        var front = new Drawable { Depth = -5f };

        container.AddRange([first, front, second, back]);

        // Higher depth draws first (behind); ties preserve insertion order.
        Assert.Equal([back, first, second, front], container.Children);

        // Moving 'first' behind 'back' updates order deterministically.
        first.Depth = 20f;
        Assert.Equal([first, back, second, front], container.Children);

        // Equalizing 'front' depth to 'second' preserves ChildID order (first < front < second).
        front.Depth = 0f;
        Assert.Equal([first, back, front, second], container.Children);
    }

    [Fact]
    public void UpdateSubTreePropagatesClockAndSupportsMutationDuringUpdate()
    {
        var manual = new ManualClock(start: true, initialTime: 250);
        var framedClock = new FramedClock(manual);

        var root = new Container { Clock = framedClock };
        var selfRemoving = new SelfRemovingChild(root);
        var survivor = new TestItem();

        root.AddRange([selfRemoving, survivor]);

        Assert.Same(framedClock, selfRemoving.Clock);
        Assert.Same(framedClock, survivor.Clock);

        manual.Advance(25);
        Assert.True(root.UpdateSubTree());

        Assert.Equal(1, selfRemoving.UpdateCount);
        Assert.Equal(1, survivor.UpdateCount);
        Assert.Equal(275, survivor.Time, precision: 4);
        Assert.Equal(25, survivor.ElapsedFrameTime, precision: 4);
        Assert.DoesNotContain(selfRemoving, root.Children);
    }

    [Fact]
    public void DisposeRecursivelyDisposesInternalChildren()
    {
        var root = new Container();
        var nested = new Container();
        var leaf = new Drawable();

        root.Add(nested);
        nested.Add(leaf);

        root.Dispose();

        Assert.True(root.IsDisposed);
        Assert.True(nested.IsDisposed);
        Assert.True(leaf.IsDisposed);
        Assert.Null(nested.Parent);
        Assert.Null(leaf.Parent);
        Assert.Throws<ObjectDisposedException>(() => root.Add(new Drawable()));
    }

    private sealed class TestItem : Drawable
    {
        public int UpdateCount { get; private set; }

        protected override void Update() => UpdateCount++;
    }

    private sealed class SelfRemovingChild : Drawable
    {
        private readonly Container owner;

        public SelfRemovingChild(Container owner)
        {
            this.owner = owner;
        }

        public int UpdateCount { get; private set; }

        protected override void Update()
        {
            UpdateCount++;
            owner.Remove(this, disposeImmediately: false);
        }
    }

    private sealed class FramedContainer : Container
    {
        public Drawable Background { get; }

        public Container InnerContent { get; }

        public FramedContainer()
        {
            Background = new Drawable { RelativeSizeAxes = Axes.Both, Size = Vector2.One };
            InnerContent = new Container { RelativeSizeAxes = Axes.Both, Size = Vector2.One };

            AddInternal(Background);
            AddInternal(InnerContent);
        }

        protected override Container<Drawable> Content => InnerContent;
    }
}
