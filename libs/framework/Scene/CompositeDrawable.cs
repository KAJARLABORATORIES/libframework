using System.Numerics;
using kajarlabs.Framework.DependencyInjection;
using kajarlabs.Framework.Numerics;
using kajarlabs.Framework.Threading;
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
    private readonly Lock childrenLock = new();
    private readonly Lock asyncLoadLock = new();

    private HashSet<IPendingAsyncLoad>? pendingAsyncLoads;
    private long nextChildId;
    private long childrenVersion;
    private bool childrenSortDirty;
    private bool isComputingAutoSize;
    private bool isDisposingOrDisposed;
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
    /// Asynchronously executes <see cref="Drawable.Load()"/> for <paramref name="component"/> off the update thread
    /// using this composite's <see cref="Drawable.Dependencies"/>, then marshals <paramref name="onLoaded"/>
    /// back onto the update thread via <see cref="Drawable.Scheduler"/>.
    /// </summary>
    /// <typeparam name="TDrawable">The type of component to load.</typeparam>
    /// <param name="component">The component to load asynchronously.</param>
    /// <param name="onLoaded">Optional callback invoked on the update thread once loading completes (for example, <see cref="Container{T}.Add"/> or <see cref="AddInternal"/>).</param>
    /// <param name="cancellationToken">Optional token used to cancel loading before completion.</param>
    /// <returns>A task that completes on the update thread with <paramref name="component"/> after <paramref name="onLoaded"/> has executed.</returns>
    public Task<TDrawable> LoadComponentAsync<TDrawable>(
        TDrawable component,
        Action<TDrawable>? onLoaded = null,
        CancellationToken cancellationToken = default)
        where TDrawable : Drawable
    {
        ArgumentNullException.ThrowIfNull(component);
        ObjectDisposedException.ThrowIf(IsDisposed || Volatile.Read(ref isDisposingOrDisposed), this);
        ObjectDisposedException.ThrowIf(component.IsDisposed, component);

        if (component.LoadState == LoadState.Loading)
            throw new InvalidOperationException("Cannot load a Drawable while it is already loading.");

        if (cancellationToken.IsCancellationRequested)
        {
            component.Dispose();
            return Task.FromCanceled<TDrawable>(cancellationToken);
        }

        var items = new[] { component };
        Action? invokeOnLoaded = onLoaded is not null ? () => onLoaded(items[0]) : null;
        return loadComponentsCore(items, invokeOnLoaded, static loaded => loaded[0], cancellationToken);
    }

    /// <summary>
    /// Asynchronously executes <see cref="Drawable.Load()"/> for all <paramref name="components"/> off the update thread
    /// using this composite's <see cref="Drawable.Dependencies"/>, then marshals <paramref name="onLoaded"/>
    /// back onto the update thread via <see cref="Drawable.Scheduler"/> in the original order.
    /// </summary>
    /// <typeparam name="TDrawable">The element type of components to load.</typeparam>
    /// <param name="components">The components to load asynchronously.</param>
    /// <param name="onLoaded">Optional callback invoked on the update thread once all components finish loading (for example, <see cref="Container{T}.AddRange"/> or <see cref="AddRangeInternal"/>).</param>
    /// <param name="cancellationToken">Optional token used to cancel loading before completion.</param>
    /// <returns>A task that completes on the update thread with the loaded components after <paramref name="onLoaded"/> has executed.</returns>
    public Task<IReadOnlyList<TDrawable>> LoadComponentsAsync<TDrawable>(
        IEnumerable<TDrawable> components,
        Action<IEnumerable<TDrawable>>? onLoaded = null,
        CancellationToken cancellationToken = default)
        where TDrawable : Drawable
    {
        ArgumentNullException.ThrowIfNull(components);
        ObjectDisposedException.ThrowIf(IsDisposed || Volatile.Read(ref isDisposingOrDisposed), this);

        var items = components.ToArray();
        HashSet<Drawable>? seen = items.Length > 1 ? [] : null;

        for (var i = 0; i < items.Length; i++)
        {
            var item = items[i];
            ArgumentNullException.ThrowIfNull(item, nameof(components));
            ObjectDisposedException.ThrowIf(item.IsDisposed, item);

            if (item.LoadState == LoadState.Loading)
                throw new InvalidOperationException("Cannot load a Drawable while it is already loading.");

            if (seen is not null && !seen.Add(item))
                throw new ArgumentException("Cannot load the same component instance multiple times in a single batch.", nameof(components));
        }

        if (cancellationToken.IsCancellationRequested)
        {
            for (var i = 0; i < items.Length; i++)
                items[i].Dispose();

            return Task.FromCanceled<IReadOnlyList<TDrawable>>(cancellationToken);
        }

        if (items.Length == 0 && onLoaded is null)
            return Task.FromResult<IReadOnlyList<TDrawable>>(Array.Empty<TDrawable>());

        Action? invokeOnLoaded = onLoaded is not null ? () => onLoaded(items) : null;
        return loadComponentsCore<TDrawable, IReadOnlyList<TDrawable>>(items, invokeOnLoaded, static loaded => loaded, cancellationToken);
    }

    /// <summary>
    /// Adds a child to <see cref="InternalChildren"/>.
    /// </summary>
    protected void AddInternal(Drawable child)
    {
        ArgumentNullException.ThrowIfNull(child);
        ObjectDisposedException.ThrowIf(IsDisposed || Volatile.Read(ref isDisposingOrDisposed), this);

        if (child.LoadState == LoadState.Loading)
            throw new InvalidOperationException("Cannot add a Drawable while it is currently loading.");

        validateNoAncestorCycle(child);

        if (AutoSizeAxes != Axes.None)
            validateAutoSizeChild(AutoSizeAxes, child);

        child.SetParent(this);

        if (LoadState is LoadState.Ready or LoadState.Loaded && child.LoadState == LoadState.NotLoaded)
        {
            try
            {
                child.Load(Dependencies);

                if (AutoSizeAxes != Axes.None)
                    validateAutoSizeChild(AutoSizeAxes, child);
            }
            catch
            {
                if (ReferenceEquals(child.Parent, this))
                    child.SetParent(null);

                throw;
            }

            if (!ReferenceEquals(child.Parent, this) || child.IsDisposed)
                return;
        }

        lock (childrenLock)
        {
            if (isDisposingOrDisposed || IsDisposed)
            {
                if (ReferenceEquals(child.Parent, this))
                    child.SetParent(null);

                throw new ObjectDisposedException(GetType().FullName);
            }

            child.ChildID = ++nextChildId;
            internalChildren.Add(child);
            childrenSortDirty = true;
            childrenVersion++;
        }

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

        lock (childrenLock)
        {
            if (!internalChildren.Remove(child))
                return false;

            childrenVersion++;
        }

        child.SetParent(null);
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
        Drawable[]? removed;

        lock (childrenLock)
        {
            if (internalChildren.Count == 0)
                return;

            removed = [.. internalChildren];
            internalChildren.Clear();
            childrenSortDirty = false;
            childrenVersion++;
        }

        for (var i = removed.Length - 1; i >= 0; i--)
        {
            var child = removed[i];
            child.SetParent(null);

            if (disposeImmediately)
                child.Dispose();
        }

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

    internal override void LoadChildren(CancellationToken cancellationToken)
    {
        var pending = new List<Drawable>();

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            lock (childrenLock)
            {
                if (isDisposingOrDisposed || IsDisposed || internalChildren.Count == 0)
                    return;

                EnsureChildrenSorted();
                pending.Clear();

                for (var i = 0; i < internalChildren.Count; i++)
                {
                    var child = internalChildren[i];

                    if (child.LoadState == LoadState.NotLoaded)
                        pending.Add(child);
                }
            }

            if (pending.Count == 0)
                break;

            for (var i = 0; i < pending.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (Volatile.Read(ref isDisposingOrDisposed) || IsDisposed)
                    return;

                var child = pending[i];

                if (ReferenceEquals(child.Parent, this) && !child.IsDisposed && child.LoadState == LoadState.NotLoaded)
                    child.LoadCore(Dependencies, cancellationToken);
            }
        }
    }

    internal override DrawNode? GenerateDrawNodeSubtree(int treeIndex, RectangleI? parentScissor = null)
    {
        if (base.GenerateDrawNodeSubtree(treeIndex, parentScissor) is not CompositeDrawNode compositeNode)
            return null;

        var effectiveScissor = ComputeEffectiveScissor(parentScissor);

        if (compositeNode.SyncedSubTreeInvalidationID == SubTreeInvalidationID
            && compositeNode.HasSyncedParentScissor
            && compositeNode.SyncedParentScissor == parentScissor)
        {
            return compositeNode;
        }

        compositeNode.UpdateScissorState(Masking, effectiveScissor, parentScissor);

        EnsureChildrenSorted();
        var targetChildren = compositeNode.WritableChildren;
        targetChildren.Clear();

        for (var i = 0; i < internalChildren.Count; i++)
        {
            var childNode = internalChildren[i].GenerateDrawNodeSubtree(treeIndex, effectiveScissor);

            if (childNode is not null)
                targetChildren.Add(childNode);
        }

        compositeNode.SyncedSubTreeInvalidationID = SubTreeInvalidationID;
        compositeNode.SyncedParentScissor = parentScissor;
        compositeNode.HasSyncedParentScissor = true;
        return compositeNode;
    }

    /// <inheritdoc />
    protected override DrawNode? CreateDrawNode() => new CompositeDrawNode(this);

    /// <inheritdoc />
    protected internal override void OnChildInvalidated(Drawable child, Invalidation invalidation)
    {
        if (invalidation.HasFlag(Invalidation.Presence) && AutoSizeAxes == Axes.None)
            Invalidate(Invalidation.DrawNode, InvalidationSource.Child);

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

        IPendingAsyncLoad[]? loadsToCancel = null;

        lock (asyncLoadLock)
        {
            if (isDisposingOrDisposed)
                return;

            Volatile.Write(ref isDisposingOrDisposed, true);

            if (pendingAsyncLoads is { Count: > 0 })
            {
                loadsToCancel = [.. pendingAsyncLoads];
                pendingAsyncLoads.Clear();
            }
        }

        if (loadsToCancel is not null)
        {
            for (var i = 0; i < loadsToCancel.Length; i++)
                loadsToCancel[i].CancelFromParentDispose();
        }

        Drawable[]? childrenToDispose = null;

        lock (childrenLock)
        {
            if (internalChildren.Count > 0)
            {
                childrenToDispose = [.. internalChildren];
                internalChildren.Clear();
            }

            childrenSortDirty = false;
            childrenVersion++;
        }

        if (childrenToDispose is not null)
        {
            for (var i = childrenToDispose.Length - 1; i >= 0; i--)
            {
                var child = childrenToDispose[i];
                child.SetParent(null);
                child.Dispose();
            }
        }

        updateBuffer.Clear();
        base.Dispose(disposing);
    }

    private Task<TResult> loadComponentsCore<TDrawable, TResult>(
        TDrawable[] items,
        Action? invokeOnLoaded,
        Func<TDrawable[], TResult> resultSelector,
        CancellationToken cancellationToken)
        where TDrawable : Drawable
    {
        var pendingLoad = new PendingAsyncLoad<TDrawable, TResult>(
            this,
            items,
            invokeOnLoaded,
            resultSelector,
            Dependencies,
            cancellationToken);

        return pendingLoad.Start();
    }

    private bool tryRegisterPendingLoad(IPendingAsyncLoad pendingLoad)
    {
        lock (asyncLoadLock)
        {
            if (isDisposingOrDisposed || IsDisposed)
                return false;

            pendingAsyncLoads ??= [];
            pendingAsyncLoads.Add(pendingLoad);
            return true;
        }
    }

    private void unregisterPendingLoad(IPendingAsyncLoad pendingLoad)
    {
        lock (asyncLoadLock)
            pendingAsyncLoads?.Remove(pendingLoad);
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

    private interface IPendingAsyncLoad
    {
        void CancelFromParentDispose();
    }

    private enum AsyncLoadPhase
    {
        Loading,
        Scheduled,
        Completing,
        Done,
    }

    private sealed class PendingAsyncLoad<TDrawable, TResult> : IPendingAsyncLoad
        where TDrawable : Drawable
    {
        private readonly CompositeDrawable owner;
        private readonly TDrawable[] components;
        private readonly Action? invokeOnLoaded;
        private readonly Func<TDrawable[], TResult> resultSelector;
        private readonly IReadOnlyDependencyCollection parentDependencies;
        private readonly CancellationToken externalToken;
        private readonly CancellationTokenSource linkedCts;
        private readonly TaskCompletionSource<TResult> tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Lock stateLock = new();
        private readonly Action disposeCallback;

        private CancellationTokenRegistration externalRegistration;
        private ScheduledDelegate? scheduledDelegate;
        private AsyncLoadPhase phase = AsyncLoadPhase.Loading;
        private int ctsRefCount;
        private bool externalCancelRequested;
        private bool callbacksRegistered;

        public PendingAsyncLoad(
            CompositeDrawable owner,
            TDrawable[] components,
            Action? invokeOnLoaded,
            Func<TDrawable[], TResult> resultSelector,
            IReadOnlyDependencyCollection parentDependencies,
            CancellationToken externalToken)
        {
            this.owner = owner;
            this.components = components;
            this.invokeOnLoaded = invokeOnLoaded;
            this.resultSelector = resultSelector;
            this.parentDependencies = parentDependencies;
            this.externalToken = externalToken;

            linkedCts = new CancellationTokenSource();
            disposeCallback = onComponentDisposed;
            ctsRefCount = components.Length > 0 ? 2 : 1;
        }

        public Task<TResult> Start()
        {
            if (!owner.tryRegisterPendingLoad(this))
            {
                linkedCts.Dispose();
                throw new ObjectDisposedException(owner.GetType().FullName);
            }

            if (components.Length > 0)
            {
                callbacksRegistered = true;

                for (var i = 0; i < components.Length; i++)
                {
                    if (!components[i].RegisterDisposeCallback(disposeCallback))
                    {
                        cancelImmediate(disposeAllComponents: true);
                        releaseLinkedCts();
                        return tcs.Task;
                    }
                }

                // If a sibling component disposed concurrently during registration, clean up any callbacks registered after.
                lock (stateLock)
                {
                    if (phase != AsyncLoadPhase.Loading)
                    {
                        for (var i = 0; i < components.Length; i++)
                            components[i].UnregisterDisposeCallback(disposeCallback);

                        releaseLinkedCts();
                        return tcs.Task;
                    }
                }
            }

            if (externalToken.CanBeCanceled)
            {
                externalRegistration = externalToken.Register(
                    static state => ((PendingAsyncLoad<TDrawable, TResult>)state!).onExternalCancelled(),
                    this);
            }

            if (components.Length == 0)
            {
                scheduleCompletion();
                return tcs.Task;
            }

            _ = Task.Run(executeBackgroundLoadAsync);
            return tcs.Task;
        }

        public void CancelFromParentDispose()
        {
            cancelImmediate(disposeAllComponents: true);
        }

        private async Task executeBackgroundLoadAsync()
        {
            Exception? failure = null;

            try
            {
                var token = linkedCts.Token;

                if (components.Length == 1)
                {
                    components[0].LoadCore(parentDependencies, token);
                }
                else
                {
                    using var batchAbortCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                    var batchToken = batchAbortCts.Token;
                    var tasks = new Task[components.Length];

                    for (var i = 0; i < components.Length; i++)
                    {
                        var component = components[i];
                        tasks[i] = Task.Run(() =>
                        {
                            try
                            {
                                component.LoadCore(parentDependencies, batchToken);
                            }
                            catch (Exception ex) when (ex is not OperationCanceledException || !batchToken.IsCancellationRequested)
                            {
                                Interlocked.CompareExchange(ref failure, ex, null);
                                batchAbortCts.Cancel();
                            }
                        });
                    }

                    await Task.WhenAll(tasks).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                Interlocked.CompareExchange(ref failure, ex, null);
            }
            finally
            {
                releaseLinkedCts();
            }

            onBackgroundLoadFinished(failure);
        }

        private void onBackgroundLoadFinished(Exception? failure)
        {
            bool isCancelled;

            lock (stateLock)
            {
                if (phase != AsyncLoadPhase.Loading)
                    return;

                isCancelled = externalCancelRequested
                              || linkedCts.IsCancellationRequested
                              || Volatile.Read(ref owner.isDisposingOrDisposed)
                              || owner.IsDisposed
                              || anyComponentDisposed();

                if (isCancelled || failure is not null)
                    phase = AsyncLoadPhase.Done;
            }

            if (isCancelled)
            {
                cleanupTracking();
                disposeUndisposedComponents();
                tcs.TrySetCanceled(selectCancelToken());
                releaseLinkedCts();
                return;
            }

            if (failure is not null)
            {
                cleanupTracking();

                if (components.Length > 1)
                    disposeReadyBatchComponents();

                tcs.TrySetException(failure);
                releaseLinkedCts();
                return;
            }

            scheduleCompletion();
        }

        private void scheduleCompletion()
        {
            ScheduledDelegate? scheduled;

            lock (stateLock)
            {
                if (phase != AsyncLoadPhase.Loading)
                    return;

                if (externalCancelRequested
                    || linkedCts.IsCancellationRequested
                    || Volatile.Read(ref owner.isDisposingOrDisposed)
                    || owner.IsDisposed
                    || anyComponentDisposed())
                {
                    phase = AsyncLoadPhase.Done;
                    scheduled = null;
                }
                else
                {
                    phase = AsyncLoadPhase.Scheduled;
                    scheduled = owner.Schedule(completeOnUpdateThread);
                    scheduledDelegate = scheduled;
                }
            }

            if (scheduled is null)
            {
                cleanupTracking();
                disposeUndisposedComponents();
                tcs.TrySetCanceled(selectCancelToken());
                releaseLinkedCts();
                return;
            }

            // Re-check in case parent/component disposal raced with owner.Schedule above.
            if (Volatile.Read(ref owner.isDisposingOrDisposed) || owner.IsDisposed || anyComponentDisposed())
                cancelImmediate(disposeAllComponents: true);
        }

        private void completeOnUpdateThread()
        {
            lock (stateLock)
            {
                if (phase != AsyncLoadPhase.Scheduled)
                    return;

                scheduledDelegate = null;

                if (externalCancelRequested
                    || linkedCts.IsCancellationRequested
                    || Volatile.Read(ref owner.isDisposingOrDisposed)
                    || owner.IsDisposed
                    || anyComponentDisposed())
                {
                    phase = AsyncLoadPhase.Done;
                    goto cancelled;
                }

                phase = AsyncLoadPhase.Completing;
            }

            cleanupTracking();

            try
            {
                invokeOnLoaded?.Invoke();
                var result = resultSelector(components);

                lock (stateLock)
                    phase = AsyncLoadPhase.Done;

                tcs.TrySetResult(result);
            }
            catch (Exception ex)
            {
                lock (stateLock)
                    phase = AsyncLoadPhase.Done;

                if (Volatile.Read(ref owner.isDisposingOrDisposed) || owner.IsDisposed || anyComponentDisposed() || linkedCts.IsCancellationRequested)
                {
                    disposeUndisposedComponents();
                    tcs.TrySetCanceled(selectCancelToken());
                }
                else
                {
                    tcs.TrySetException(ex);
                }
            }
            finally
            {
                releaseLinkedCts();
            }

            return;

        cancelled:
            cleanupTracking();
            disposeUndisposedComponents();
            tcs.TrySetCanceled(selectCancelToken());
            releaseLinkedCts();
        }

        private void onComponentDisposed()
        {
            cancelImmediate(disposeAllComponents: true);
        }

        private void onExternalCancelled()
        {
            externalCancelRequested = true;
            linkedCts.Cancel();

            ScheduledDelegate? pendingDelegate;

            lock (stateLock)
            {
                if (phase != AsyncLoadPhase.Scheduled)
                    return;

                phase = AsyncLoadPhase.Done;
                pendingDelegate = scheduledDelegate;
                scheduledDelegate = null;
            }

            pendingDelegate?.Cancel();
            cleanupTracking();
            disposeUndisposedComponents();
            tcs.TrySetCanceled(externalToken);
            releaseLinkedCts();
        }

        private void cancelImmediate(bool disposeAllComponents)
        {
            linkedCts.Cancel();

            ScheduledDelegate? pendingDelegate;

            lock (stateLock)
            {
                if (phase is AsyncLoadPhase.Completing or AsyncLoadPhase.Done)
                    return;

                phase = AsyncLoadPhase.Done;
                pendingDelegate = scheduledDelegate;
                scheduledDelegate = null;
            }

            pendingDelegate?.Cancel();
            cleanupTracking();

            if (disposeAllComponents)
                disposeUndisposedComponents();

            tcs.TrySetCanceled(selectCancelToken());
            releaseLinkedCts();
        }

        private void cleanupTracking()
        {
            owner.unregisterPendingLoad(this);
            externalRegistration.Dispose();

            if (callbacksRegistered)
            {
                callbacksRegistered = false;

                for (var i = 0; i < components.Length; i++)
                    components[i].UnregisterDisposeCallback(disposeCallback);
            }
        }

        private bool anyComponentDisposed()
        {
            for (var i = 0; i < components.Length; i++)
            {
                if (components[i].IsDisposed)
                    return true;
            }

            return false;
        }

        private void disposeUndisposedComponents()
        {
            for (var i = 0; i < components.Length; i++)
            {
                if (!components[i].IsDisposed)
                    components[i].Dispose();
            }
        }

        private void disposeReadyBatchComponents()
        {
            for (var i = 0; i < components.Length; i++)
            {
                var component = components[i];

                if (!component.IsDisposed && component.LoadState != LoadState.NotLoaded)
                    component.Dispose();
            }
        }

        private CancellationToken selectCancelToken()
            => externalCancelRequested && externalToken.CanBeCanceled ? externalToken : CancellationToken.None;

        private void releaseLinkedCts()
        {
            if (Interlocked.Decrement(ref ctsRefCount) == 0)
                linkedCts.Dispose();
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
