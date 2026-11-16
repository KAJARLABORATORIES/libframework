using System.Numerics;
using kajarlabs.Framework.Numerics;
using kajarlabs.Framework.Timing;

namespace kajarlabs.Framework.Scene;

/// <summary>
/// A <see cref="Drawable"/> that manages a depth-sorted collection of child drawables,
/// interior <see cref="Padding"/>, <see cref="AutoSizeAxes"/>, and scissor <see cref="Masking"/>.
/// </summary>
public class CompositeDrawable : Drawable
{
    private static readonly ChildDepthComparer depth_comparer = new();

    private readonly List<Drawable> internalChildren = [];
    private readonly List<Drawable> updateBuffer = [];

    private long nextChildId;
    private long childrenVersion;
    private bool childrenSortDirty;
    private bool isComputingAutoSize;
    private Vector2 inProgressAutoSize;
    private Vector2 lastObservedAutoSize;

    /// <summary>
    /// Gets or sets the interior padding applied around <see cref="InternalChildren"/>.
    /// </summary>
    public MarginPadding Padding
    {
        get => field;
        set
        {
            if (field == value)
                return;

            field = value;
            Invalidate(Invalidation.Layout | Invalidation.Geometry | Invalidation.DrawInfo | Invalidation.MiscGeometry);
        }
    } = MarginPadding.ZERO;

    /// <summary>
    /// Gets or sets which axes automatically size this composite to enclose its present children.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when an axis is both auto-sized and relatively sized, or when a child uses relative sizing/positioning
    /// or non-zero-origin anchoring on an auto-sized axis.
    /// </exception>
    public Axes AutoSizeAxes
    {
        get => field;
        set
        {
            if (field == value)
                return;

            validateAutoSizeConfiguration(value, RelativeSizeAxes);
            field = value;
            Invalidate(Invalidation.Geometry | Invalidation.Layout | Invalidation.DrawInfo | Invalidation.MiscGeometry);
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether child rendering should be clipped to this composite's screen-space bounds.
    /// </summary>
    public bool Masking
    {
        get => field;
        set
        {
            if (field == value)
                return;

            field = value;
            Invalidate(Invalidation.DrawNode);
        }
    }

    /// <summary>
    /// Gets the size available to children inside this composite after subtracting <see cref="Padding"/>.
    /// </summary>
    protected internal override Vector2 ChildSize => Vector2.Max(Vector2.Zero, DrawSize - Padding.Total);

    /// <summary>
    /// Gets the top-left offset applied to children inside this composite from <see cref="Padding"/>.
    /// </summary>
    protected internal override Vector2 ChildOffset => Padding.TopLeftOffset;

    /// <summary>
    /// Gets the direct internal children of this composite sorted deterministically by <see cref="Drawable.Depth"/>.
    /// </summary>
    protected internal IReadOnlyList<Drawable> InternalChildren
    {
        get
        {
            EnsureChildrenSorted();
            return internalChildren;
        }
    }

    /// <summary>
    /// Gets a monotonically incremented version number that changes whenever <see cref="InternalChildren"/> membership or order changes.
    /// </summary>
    protected internal long ChildrenVersion
    {
        get
        {
            EnsureChildrenSorted();
            return childrenVersion;
        }
    }

    /// <summary>
    /// Computes the integer screen-space scissor rectangle enclosing this composite's <see cref="Drawable.ScreenSpaceDrawQuad"/>.
    /// </summary>
    public RectangleI ComputeScreenSpaceScissor()
    {
        var aabb = ScreenSpaceDrawQuad.AABB;

        if (aabb.IsEmpty)
            return RectangleI.EMPTY;

        var left = (int)MathF.Floor(aabb.Left);
        var top = (int)MathF.Floor(aabb.Top);
        var right = (int)MathF.Ceiling(aabb.Right);
        var bottom = (int)MathF.Ceiling(aabb.Bottom);

        if (right <= left || bottom <= top)
            return RectangleI.EMPTY;

        return new RectangleI(left, top, right - left, bottom - top);
    }

    /// <summary>
    /// Computes the effective screen-space scissor rectangle by combining <paramref name="parentScissor"/> with
    /// <see cref="ComputeScreenSpaceScissor"/> when <see cref="Masking"/> is enabled.
    /// </summary>
    public RectangleI? ComputeEffectiveScissor(RectangleI? parentScissor)
    {
        if (!Masking)
            return parentScissor;

        var ownScissor = ComputeScreenSpaceScissor();
        return parentScissor is { } parentRect
            ? RectangleI.Intersect(parentRect, ownScissor)
            : ownScissor;
    }

    /// <summary>
    /// Computes the effective screen-space scissor rectangle by walking ancestor composites,
    /// returning <see langword="null"/> when neither this composite nor any ancestor has <see cref="Masking"/> enabled.
    /// </summary>
    public RectangleI? ComputeEffectiveScissor()
    {
        RectangleI? parentScissor = null;

        for (var ancestor = Parent; ancestor is not null; ancestor = ancestor.Parent)
        {
            if (ancestor is CompositeDrawable composite && composite.Masking)
            {
                parentScissor = composite.ComputeEffectiveScissor();
                break;
            }
        }

        return ComputeEffectiveScissor(parentScissor);
    }

    /// <summary>
    /// Adds a child to <see cref="InternalChildren"/>.
    /// </summary>
    protected void AddInternal(Drawable child)
    {
        ArgumentNullException.ThrowIfNull(child);
        ObjectDisposedException.ThrowIf(IsDisposed, this);

        validateNoAncestorCycle(child);

        if (AutoSizeAxes != Axes.None)
            validateAutoSizeChild(AutoSizeAxes, child);

        child.ChildID = ++nextChildId;
        child.SetParent(this);

        internalChildren.Add(child);
        childrenSortDirty = true;
        childrenVersion++;
        OnChildrenChanged();

        if (AutoSizeAxes != Axes.None)
            notifyAutoSizeInvalidated();
        else
            Invalidate(Invalidation.DrawNode, InvalidationSource.Child);
    }

    /// <summary>
    /// Adds multiple children to <see cref="InternalChildren"/>.
    /// </summary>
    protected void AddRangeInternal(IEnumerable<Drawable> children)
    {
        ArgumentNullException.ThrowIfNull(children);

        foreach (var child in children)
            AddInternal(child);
    }

    /// <summary>
    /// Removes <paramref name="child"/> from <see cref="InternalChildren"/> and optionally disposes it.
    /// </summary>
    /// <returns><see langword="true"/> if <paramref name="child"/> was present and removed; otherwise <see langword="false"/>.</returns>
    protected bool RemoveInternal(Drawable child, bool disposeImmediately = false)
    {
        ArgumentNullException.ThrowIfNull(child);

        if (!internalChildren.Remove(child))
            return false;

        child.SetParent(null);
        childrenVersion++;
        OnChildrenChanged();

        if (disposeImmediately)
            child.Dispose();

        if (AutoSizeAxes != Axes.None)
            notifyAutoSizeInvalidated();
        else
            Invalidate(Invalidation.DrawNode, InvalidationSource.Child);

        return true;
    }

    /// <summary>
    /// Removes all children from <see cref="InternalChildren"/> and optionally disposes them.
    /// </summary>
    protected void ClearInternal(bool disposeImmediately = true)
    {
        if (internalChildren.Count == 0)
            return;

        for (var i = internalChildren.Count - 1; i >= 0; i--)
        {
            var child = internalChildren[i];
            internalChildren.RemoveAt(i);
            child.SetParent(null);

            if (disposeImmediately)
                child.Dispose();
        }

        childrenSortDirty = false;
        childrenVersion++;
        OnChildrenChanged();

        if (AutoSizeAxes != Axes.None)
            notifyAutoSizeInvalidated();
        else
            Invalidate(Invalidation.DrawNode, InvalidationSource.Child);
    }

    /// <summary>
    /// Ensures <see cref="internalChildren"/> is sorted deterministically by <see cref="Drawable.Depth"/>.
    /// </summary>
    protected void EnsureChildrenSorted()
    {
        if (!childrenSortDirty)
            return;

        internalChildren.Sort(depth_comparer);
        childrenSortDirty = false;
        childrenVersion++;
    }

    /// <summary>
    /// Invoked whenever <see cref="InternalChildren"/> membership changes.
    /// </summary>
    protected virtual void OnChildrenChanged()
    {
    }

    /// <inheritdoc />
    protected override Vector2 ComputeDrawSize()
    {
        if (isComputingAutoSize)
            return inProgressAutoSize;

        validateAutoSizeConfiguration(AutoSizeAxes, RelativeSizeAxes);

        var baseDrawSize = base.ComputeDrawSize();

        if (AutoSizeAxes == Axes.None)
            return baseDrawSize;

        EnsureChildrenSorted();

        inProgressAutoSize = baseDrawSize;
        isComputingAutoSize = true;

        try
        {
            var maxRight = 0f;
            var maxBottom = 0f;
            var hasPresentChild = false;

            for (var i = 0; i < internalChildren.Count; i++)
            {
                var child = internalChildren[i];
                validateAutoSizeChild(AutoSizeAxes, child);

                if (!child.IsPresent)
                    continue;

                var bounds = (new Quad(child.LayoutRectangle) * child.LocalTransform).AABB;

                if (!hasPresentChild)
                {
                    maxRight = bounds.Right;
                    maxBottom = bounds.Bottom;
                    hasPresentChild = true;
                }
                else
                {
                    maxRight = MathF.Max(maxRight, bounds.Right);
                    maxBottom = MathF.Max(maxBottom, bounds.Bottom);
                }
            }

            var autoWidth = hasPresentChild
                ? MathF.Max(0f, maxRight - Padding.Left) + Padding.TotalHorizontal
                : MathF.Max(0f, Padding.TotalHorizontal);

            var autoHeight = hasPresentChild
                ? MathF.Max(0f, maxBottom - Padding.Top) + Padding.TotalVertical
                : MathF.Max(0f, Padding.TotalVertical);

            var resolved = new Vector2(
                AutoSizeAxes.HasFlag(Axes.X) ? autoWidth : baseDrawSize.X,
                AutoSizeAxes.HasFlag(Axes.Y) ? autoHeight : baseDrawSize.Y);

            lastObservedAutoSize = resolved;
            return resolved;
        }
        finally
        {
            isComputingAutoSize = false;
        }
    }

    /// <inheritdoc />
    protected override bool UpdateChildren()
    {
        EnsureChildrenSorted();

        if (internalChildren.Count == 0)
            return true;

        updateBuffer.Clear();
        updateBuffer.AddRange(internalChildren);

        for (var i = 0; i < updateBuffer.Count; i++)
        {
            var child = updateBuffer[i];

            if (ReferenceEquals(child.Parent, this))
                child.UpdateSubTree();
        }

        updateBuffer.Clear();
        return true;
    }

    /// <inheritdoc />
    protected override void UpdateAfterChildren()
    {
        if (AutoSizeAxes == Axes.None)
            return;

        var previous = lastObservedAutoSize;
        var current = DrawSize;

        if (previous != current)
            Invalidate(Invalidation.DrawInfo, InvalidationSource.Child);
    }

    /// <inheritdoc />
    protected override void OnInvalidate(Invalidation invalidation, InvalidationSource source)
    {
        if (source == InvalidationSource.Self && (invalidation & Invalidation.Geometry) != Invalidation.None)
            validateAutoSizeConfiguration(AutoSizeAxes, RelativeSizeAxes);

        if (internalChildren.Count == 0)
            return;

        var childInvalidation = Invalidation.None;

        if (source == InvalidationSource.Child)
        {
            // Parent origin/bounds changed due to auto-sizing; children only need their draw transforms updated.
            if ((invalidation & Invalidation.DrawInfo) != Invalidation.None)
                childInvalidation = Invalidation.DrawInfo;
        }
        else
        {
            if ((invalidation & Invalidation.DrawInfo) != Invalidation.None)
                childInvalidation |= Invalidation.DrawInfo;

            if ((invalidation & Invalidation.Color) != Invalidation.None)
                childInvalidation |= Invalidation.Color;

            if ((invalidation & (Invalidation.Geometry | Invalidation.Layout)) != Invalidation.None)
                childInvalidation |= Invalidation.Layout | Invalidation.Geometry | Invalidation.DrawInfo;
        }

        if (childInvalidation == Invalidation.None)
            return;

        for (var i = 0; i < internalChildren.Count; i++)
            internalChildren[i].Invalidate(childInvalidation, InvalidationSource.Parent);
    }

    /// <inheritdoc />
    protected internal override void OnChildInvalidated(Drawable child, Invalidation invalidation)
    {
        if (AutoSizeAxes == Axes.None)
            return;

        if ((invalidation & (Invalidation.Layout | Invalidation.Geometry | Invalidation.Presence | Invalidation.MiscGeometry)) == Invalidation.None)
            return;

        validateAutoSizeChild(AutoSizeAxes, child);
        notifyAutoSizeInvalidated();
    }

    /// <inheritdoc />
    protected internal override void OnChildDepthChanged(Drawable child)
    {
        childrenSortDirty = true;
        childrenVersion++;
        Invalidate(Invalidation.DrawNode, InvalidationSource.Child);
    }

    /// <inheritdoc />
    protected internal override void UpdateClock(IFrameBasedClock? clock)
    {
        base.UpdateClock(clock);
        var effectiveClock = Clock;

        for (var i = 0; i < internalChildren.Count; i++)
            internalChildren[i].UpdateClock(effectiveClock);
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (!disposing || IsDisposed)
            return;

        for (var i = internalChildren.Count - 1; i >= 0; i--)
        {
            var child = internalChildren[i];
            internalChildren.RemoveAt(i);
            child.SetParent(null);
            child.Dispose();
        }

        updateBuffer.Clear();
        childrenSortDirty = false;
        childrenVersion++;
        base.Dispose(disposing);
    }

    private void notifyAutoSizeInvalidated()
    {
        const Invalidation auto_size_invalidation = Invalidation.Geometry | Invalidation.MiscGeometry | Invalidation.DrawInfo;

        Invalidate(auto_size_invalidation, InvalidationSource.Child);
        Parent?.OnChildInvalidated(this, auto_size_invalidation);
    }

    private void validateNoAncestorCycle(Drawable child)
    {
        for (Drawable? ancestor = this; ancestor is not null; ancestor = ancestor.Parent)
        {
            if (ReferenceEquals(ancestor, child))
                throw new InvalidOperationException("Cannot add a Drawable to itself or one of its descendants.");
        }
    }

    private void validateAutoSizeConfiguration(Axes autoSizeAxes, Axes relativeSizeAxes)
    {
        if ((autoSizeAxes & relativeSizeAxes) != Axes.None)
        {
            throw new InvalidOperationException(
                $"CompositeDrawable cannot enable {nameof(AutoSizeAxes)} ({autoSizeAxes}) and {nameof(RelativeSizeAxes)} ({relativeSizeAxes}) on the same axis.");
        }

        if (autoSizeAxes == Axes.None)
            return;

        for (var i = 0; i < internalChildren.Count; i++)
            validateAutoSizeChild(autoSizeAxes, internalChildren[i]);
    }

    private static void validateAutoSizeChild(Axes autoSizeAxes, Drawable child)
    {
        if (autoSizeAxes.HasFlag(Axes.X))
        {
            if (child.RelativeSizeAxes.HasFlag(Axes.X))
                throw new InvalidOperationException("A child inside an X-auto-sized container cannot use RelativeSizeAxes.X.");

            if (child.RelativePositionAxes.HasFlag(Axes.X))
                throw new InvalidOperationException("A child inside an X-auto-sized container cannot use RelativePositionAxes.X.");

            if ((child.Anchor & (Anchor.HorizontalCentre | Anchor.Right)) != 0)
                throw new InvalidOperationException("A child inside an X-auto-sized container cannot anchor to HorizontalCentre or Right.");
        }

        if (autoSizeAxes.HasFlag(Axes.Y))
        {
            if (child.RelativeSizeAxes.HasFlag(Axes.Y))
                throw new InvalidOperationException("A child inside a Y-auto-sized container cannot use RelativeSizeAxes.Y.");

            if (child.RelativePositionAxes.HasFlag(Axes.Y))
                throw new InvalidOperationException("A child inside a Y-auto-sized container cannot use RelativePositionAxes.Y.");

            if ((child.Anchor & (Anchor.VerticalCentre | Anchor.Bottom)) != 0)
                throw new InvalidOperationException("A child inside a Y-auto-sized container cannot anchor to VerticalCentre or Bottom.");
        }
    }

    private sealed class ChildDepthComparer : IComparer<Drawable>
    {
        public int Compare(Drawable? x, Drawable? y)
        {
            if (ReferenceEquals(x, y))
                return 0;

            if (x is null)
                return -1;

            if (y is null)
                return 1;

            // Higher depth draws behind (earlier in list); lower depth draws in front (later in list).
            var depthComparison = y.Depth.CompareTo(x.Depth);
            return depthComparison != 0 ? depthComparison : x.ChildID.CompareTo(y.ChildID);
        }
    }
}
