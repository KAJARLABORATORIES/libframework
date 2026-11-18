using System.Numerics;
using kajarlabs.Framework.DependencyInjection;
using kajarlabs.Framework.Graphics;
using kajarlabs.Framework.Input;
using kajarlabs.Framework.Numerics;
using kajarlabs.Framework.Threading;
using kajarlabs.Framework.Timing;

namespace kajarlabs.Framework.Scene;

/// <summary>
/// Base class for all elements in the 2D scene graph, providing local/draw-space transforms,
/// <see cref="Scene.Anchor"/>/<see cref="Origin"/> layout, color/alpha inheritance, bitmask invalidation caching,
/// and clock-driven updates.
/// </summary>
public class Drawable : IDisposable
{
    [Flags]
    private enum CacheFlags
    {
        None = 0,
        DrawSize = 1 << 0,
        AnchorPosition = 1 << 1,
        DrawPosition = 1 << 2,
        OriginPosition = 1 << 3,
        LocalTransform = 1 << 4,
        DrawInfo = 1 << 5,
        ScreenSpaceDrawQuad = 1 << 6,
        DrawColorInfo = 1 << 7,
    }

    private Vector2 position;
    private Vector2 size;
    private Vector2 scale = Vector2.One;
    private Vector2 shear;
    private Anchor origin = Anchor.TopLeft;
    private Vector2 customOriginPosition;

    private IFrameBasedClock? customClock;
    private IFrameBasedClock? inheritedClock;
    private Scheduler? scheduler;
    private Action? disposeListeners;
    private int loadState = (int)LoadState.NotLoaded;
    private int isDisposed;
    private readonly DrawNode?[] drawNodes = new DrawNode?[3];

    private CacheFlags validCaches;
    private Vector2 cachedDrawSize;
    private Vector2 cachedAnchorPosition;
    private Vector2 cachedDrawPosition;
    private Vector2 cachedOriginPosition;
    private Matrix3x2 cachedLocalTransform;

    /// <summary>
    /// Gets or sets the local position relative to <see cref="AnchorPosition"/>.
    /// </summary>
    public Vector2 Position
    {
        get => position;
        set
        {
            if (position == value)
                return;

            position = value;
            Invalidate(Invalidation.Layout | Invalidation.DrawInfo | Invalidation.MiscGeometry);
        }
    }

    /// <summary>
    /// Gets or sets the horizontal component of <see cref="Position"/>.
    /// </summary>
    public float X
    {
        get => position.X;
        set => Position = new Vector2(value, position.Y);
    }

    /// <summary>
    /// Gets or sets the vertical component of <see cref="Position"/>.
    /// </summary>
    public float Y
    {
        get => position.Y;
        set => Position = new Vector2(position.X, value);
    }

    /// <summary>
    /// Gets or sets the size of this drawable (in pixels or relative multipliers depending on <see cref="RelativeSizeAxes"/>).
    /// </summary>
    public Vector2 Size
    {
        get => size;
        set
        {
            if (size == value)
                return;

            size = value;
            Invalidate(Invalidation.Geometry | Invalidation.Layout | Invalidation.DrawInfo | Invalidation.MiscGeometry);
        }
    }

    /// <summary>
    /// Gets or sets the horizontal component of <see cref="Size"/>.
    /// </summary>
    public float Width
    {
        get => size.X;
        set => Size = new Vector2(value, size.Y);
    }

    /// <summary>
    /// Gets or sets the vertical component of <see cref="Size"/>.
    /// </summary>
    public float Height
    {
        get => size.Y;
        set => Size = new Vector2(size.X, value);
    }

    /// <summary>
    /// Gets or sets the clockwise rotation in degrees around <see cref="OriginPosition"/>.
    /// </summary>
    public float Rotation
    {
        get => field;
        set
        {
            if (field == value)
                return;

            field = value;
            Invalidate(Invalidation.DrawInfo);
        }
    }

    /// <summary>
    /// Gets or sets the local scale factor around <see cref="OriginPosition"/>.
    /// </summary>
    public Vector2 Scale
    {
        get => scale;
        set
        {
            if (scale == value)
                return;

            scale = value;
            Invalidate(Invalidation.DrawInfo);
        }
    }

    /// <summary>
    /// Gets or sets the local 2D shear factor around <see cref="OriginPosition"/>.
    /// </summary>
    public Vector2 Shear
    {
        get => shear;
        set
        {
            if (shear == value)
                return;

            shear = value;
            Invalidate(Invalidation.DrawInfo);
        }
    }

    /// <summary>
    /// Gets or sets the anchor point within the parent's child area.
    /// </summary>
    public Anchor Anchor
    {
        get => field;
        set
        {
            if (field == value)
                return;

            _ = value.GetRelativeOffset();
            field = value;
            Invalidate(Invalidation.Layout | Invalidation.DrawInfo | Invalidation.MiscGeometry);
        }
    } = Anchor.TopLeft;

    /// <summary>
    /// Gets or sets the local pivot point on this drawable.
    /// </summary>
    public Anchor Origin
    {
        get => origin;
        set
        {
            if (origin == value)
                return;

            _ = value.GetRelativeOffset();
            origin = value;
            Invalidate(Invalidation.Layout | Invalidation.DrawInfo | Invalidation.MiscGeometry);
        }
    }

    /// <summary>
    /// Gets or sets the local origin offset in draw units. Setting this property switches <see cref="Origin"/> to <see cref="Anchor.Custom"/>.
    /// </summary>
    public Vector2 OriginPosition
    {
        get
        {
            if (!validCaches.HasFlag(CacheFlags.OriginPosition))
            {
                cachedOriginPosition = origin.HasFlag(Anchor.Custom)
                    ? customOriginPosition
                    : origin.GetRelativeOffset() * DrawSize;
                validCaches |= CacheFlags.OriginPosition;
            }

            return cachedOriginPosition;
        }
        set
        {
            if (origin == Anchor.Custom && customOriginPosition == value)
                return;

            customOriginPosition = value;
            origin = Anchor.Custom;
            Invalidate(Invalidation.Layout | Invalidation.DrawInfo | Invalidation.MiscGeometry);
        }
    }

    /// <summary>
    /// Gets or sets which axes of <see cref="Position"/> are relative to the parent's available child size.
    /// </summary>
    public Axes RelativePositionAxes
    {
        get => field;
        set
        {
            if (field == value)
                return;

            field = value;
            Invalidate(Invalidation.Layout | Invalidation.DrawInfo | Invalidation.MiscGeometry);
        }
    }

    /// <summary>
    /// Gets or sets which axes of <see cref="Size"/> are relative to the parent's available child size.
    /// </summary>
    public Axes RelativeSizeAxes
    {
        get => field;
        set
        {
            if (field == value)
                return;

            field = value;
            Invalidate(Invalidation.Geometry | Invalidation.Layout | Invalidation.DrawInfo | Invalidation.MiscGeometry);
        }
    }

    /// <summary>
    /// Gets or sets the exterior margin spacing around this drawable within its parent.
    /// </summary>
    public MarginPadding Margin
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
    /// Gets or sets the local tint color multiplied down the hierarchy.
    /// </summary>
    public Color4 Color
    {
        get => field;
        set
        {
            if (field == value)
                return;

            field = value;
            Invalidate(Invalidation.Color);
        }
    } = Color4.WHITE;

    /// <summary>
    /// Gets or sets the local opacity multiplier in <c>[0, 1]</c> multiplied down the hierarchy.
    /// </summary>
    public float Alpha
    {
        get => field;
        set
        {
            if (field == value)
                return;

            var wasPresent = IsPresent;
            field = value;

            var invalidation = Invalidation.Color;
            if (wasPresent != IsPresent)
                invalidation |= Invalidation.Presence;

            Invalidate(invalidation);
        }
    } = 1f;

    /// <summary>
    /// Gets or sets the sorting depth within the parent container (lower values draw behind or before higher values depending on container order).
    /// </summary>
    public float Depth
    {
        get => field;
        set
        {
            if (field == value)
                return;

            field = value;
            Invalidate(Invalidation.Parent);
            Parent?.OnChildDepthChanged(this);
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether this drawable remains present for layout and updates even when <see cref="Alpha"/> is zero or negative.
    /// </summary>
    public bool AlwaysPresent
    {
        get => field;
        set
        {
            if (field == value)
                return;

            var wasPresent = IsPresent;
            field = value;

            if (wasPresent != IsPresent)
                Invalidate(Invalidation.Presence);
        }
    }

    /// <summary>
    /// Gets a value indicating whether this drawable is considered present (<see cref="Alpha"/> &gt; 0 or <see cref="AlwaysPresent"/>).
    /// </summary>
    public virtual bool IsPresent => Alpha > 0f || AlwaysPresent;

    /// <summary>
    /// Gets the parent <see cref="Drawable"/> containing this drawable, or <see langword="null"/> if unattached.
    /// </summary>
    public Drawable? Parent { get; private set; }

    /// <summary>
    /// Gets a value indicating whether this drawable has been disposed.
    /// </summary>
    public bool IsDisposed => Volatile.Read(ref isDisposed) != 0;

    /// <summary>
    /// Gets the cancellation token associated with the currently executing <see cref="Load()"/> operation,
    /// or <see cref="CancellationToken.None"/> when not loading asynchronously.
    /// </summary>
    protected CancellationToken LoadCancellationToken { get; private set; }

    /// <summary>
    /// Gets the current loading lifecycle state of this drawable.
    /// </summary>
    public LoadState LoadState => (LoadState)Volatile.Read(ref loadState);

    /// <summary>
    /// Gets the read-only dependency collection resolved for this drawable during loading,
    /// or <see cref="DependencyCollection.EMPTY"/> before loading begins.
    /// </summary>
    public IReadOnlyDependencyCollection Dependencies { get; private set; } = DependencyCollection.EMPTY;

    /// <summary>
    /// Gets the lazily allocated thread-affine scheduler drained during <see cref="UpdateSubTree"/>.
    /// </summary>
    public Scheduler Scheduler => scheduler ?? initializeScheduler();

    /// <summary>
    /// Gets or sets the blending mode applied when rendering this drawable.
    /// </summary>
    public BlendingParameters Blending
    {
        get => field;
        set
        {
            if (field == value)
                return;

            field = value;
            Invalidate(Invalidation.DrawNode);
        }
    } = BlendingParameters.ALPHA;

    /// <summary>
    /// Gets the monotonically increasing invalidation counter used to detect state changes across frames.
    /// </summary>
    public long InvalidationID { get; private set; }

    /// <summary>
    /// Gets the monotonically increasing invalidation counter covering this drawable and all descendants.
    /// </summary>
    public long SubTreeInvalidationID { get; private set; }

    /// <summary>
    /// Gets or sets the frame-based clock driving this drawable (explicitly assigned or inherited from <see cref="Parent"/>).
    /// </summary>
    public IFrameBasedClock? Clock
    {
        get => customClock ?? inheritedClock;
        set
        {
            if (ReferenceEquals(customClock, value))
                return;

            customClock = value;
            UpdateClock(value ?? Parent?.Clock);
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether <see cref="UpdateSubTree"/> advances an explicitly assigned <see cref="Clock"/>.
    /// </summary>
    public bool ProcessCustomClock { get; set; } = true;

    /// <summary>
    /// Gets the current time in milliseconds from <see cref="Clock"/>, or <c>0</c> when no clock is attached.
    /// </summary>
    public double Time => Clock?.CurrentTime ?? 0;

    /// <summary>
    /// Gets the elapsed time in milliseconds for the current frame from <see cref="Clock"/>, or <c>0</c> when no clock is attached.
    /// </summary>
    public double ElapsedFrameTime => Clock?.ElapsedFrameTime ?? 0;

    /// <summary>
    /// Gets the resolved local size in parent units after applying <see cref="RelativeSizeAxes"/>.
    /// </summary>
    public Vector2 DrawSize
    {
        get
        {
            if (!validCaches.HasFlag(CacheFlags.DrawSize))
            {
                cachedDrawSize = ComputeDrawSize();
                validCaches |= CacheFlags.DrawSize;
            }

            return cachedDrawSize;
        }
    }

    /// <summary>
    /// Gets the local axis-aligned bounding rectangle <c>(0, 0, DrawSize.X, DrawSize.Y)</c>.
    /// </summary>
    public RectangleF DrawRectangle => new(Vector2.Zero, DrawSize);

    /// <summary>
    /// Gets the resolved size including <see cref="Margin"/> (<see cref="DrawSize"/> + <see cref="MarginPadding.Total"/>).
    /// </summary>
    public Vector2 LayoutSize => DrawSize + Margin.Total;

    /// <summary>
    /// Gets the local layout rectangle inflated by <see cref="Margin"/>.
    /// </summary>
    public RectangleF LayoutRectangle => Margin.Inflate(DrawRectangle);

    /// <summary>
    /// Gets the resolved anchor point within the parent's margin-deflated child bounds.
    /// </summary>
    public Vector2 AnchorPosition
    {
        get
        {
            if (!validCaches.HasFlag(CacheFlags.AnchorPosition))
            {
                if (Parent is null)
                {
                    cachedAnchorPosition = Vector2.Zero;
                }
                else
                {
                    var availableRect = getAvailableParentRectangle();
                    cachedAnchorPosition = availableRect.Position + (Anchor.GetRelativeOffset() * availableRect.Size);
                }

                validCaches |= CacheFlags.AnchorPosition;
            }

            return cachedAnchorPosition;
        }
    }

    /// <summary>
    /// Gets the resolved offset from <see cref="AnchorPosition"/> after applying <see cref="RelativePositionAxes"/>.
    /// </summary>
    public Vector2 DrawPosition
    {
        get
        {
            if (!validCaches.HasFlag(CacheFlags.DrawPosition))
            {
                var availableSize = getAvailableParentRectangle().Size;
                cachedDrawPosition = new Vector2(
                    RelativePositionAxes.HasFlag(Axes.X) ? position.X * availableSize.X : position.X,
                    RelativePositionAxes.HasFlag(Axes.Y) ? position.Y * availableSize.Y : position.Y);
                validCaches |= CacheFlags.DrawPosition;
            }

            return cachedDrawPosition;
        }
    }

    /// <summary>
    /// Gets the cached local-to-parent 2D affine transform matrix.
    /// </summary>
    public Matrix3x2 LocalTransform
    {
        get
        {
            if (!validCaches.HasFlag(CacheFlags.LocalTransform))
            {
                cachedLocalTransform = Matrix3x2Extensions.CreateLocalTransform(
                    AnchorPosition + DrawPosition,
                    scale,
                    shear,
                    Rotation,
                    OriginPosition);
                validCaches |= CacheFlags.LocalTransform;
            }

            return cachedLocalTransform;
        }
    }

    /// <summary>
    /// Gets the lazily cached draw-space affine transform and its inverse.
    /// </summary>
    public DrawInfo DrawInfo
    {
        get
        {
            if (!validCaches.HasFlag(CacheFlags.DrawInfo))
            {
                field = Parent is not null
                    ? Parent.DrawInfo.ApplyLocal(LocalTransform)
                    : Scene.DrawInfo.Create(LocalTransform);
                validCaches |= CacheFlags.DrawInfo;
            }

            return field;
        }
    } = Scene.DrawInfo.IDENTITY;

    /// <summary>
    /// Gets the lazily cached screen-space quadrilateral enclosing this drawable.
    /// </summary>
    public Quad ScreenSpaceDrawQuad
    {
        get
        {
            if (!validCaches.HasFlag(CacheFlags.ScreenSpaceDrawQuad))
            {
                field = new Quad(DrawRectangle) * DrawInfo.Matrix;
                validCaches |= CacheFlags.ScreenSpaceDrawQuad;
            }

            return field;
        }
    }

    /// <summary>
    /// Gets the lazily cached accumulated color and alpha inherited from ancestors.
    /// </summary>
    public DrawColorInfo DrawColorInfo
    {
        get
        {
            if (!validCaches.HasFlag(CacheFlags.DrawColorInfo))
            {
                var parentColor = Parent?.DrawColorInfo ?? Scene.DrawColorInfo.WHITE;
                field = parentColor.Multiply(Color, Alpha);
                validCaches |= CacheFlags.DrawColorInfo;
            }

            return field;
        }
    } = Scene.DrawColorInfo.WHITE;

    /// <summary>
    /// Gets a value indicating whether this drawable can acquire keyboard input focus.
    /// </summary>
    public virtual bool AcceptsFocus => false;

    /// <summary>
    /// Gets a value indicating whether this drawable requests focus when focus contention is evaluated.
    /// </summary>
    public virtual bool RequestsFocus => false;

    /// <summary>
    /// Gets a value indicating whether this drawable requests window-level text input while focused.
    /// </summary>
    public virtual bool RequestsTextInput => false;

    /// <summary>
    /// Gets a value indicating whether this drawable currently holds keyboard input focus.
    /// </summary>
    public bool HasFocus { get; internal set; }

    /// <summary>
    /// Gets a value indicating whether the pointer is currently hovering over this drawable.
    /// </summary>
    public bool IsHovered { get; internal set; }

    /// <summary>
    /// Gets a value indicating whether this drawable is currently the active drag target.
    /// </summary>
    public bool IsDragged { get; internal set; }

    /// <summary>
    /// Gets a value indicating whether this drawable participates in positional input queues.
    /// Defaults to <see cref="AcceptsFocus"/>.
    /// </summary>
    public virtual bool HandlePositionalInput => AcceptsFocus;

    /// <summary>
    /// Gets a value indicating whether this drawable participates in non-positional input queues.
    /// </summary>
    public virtual bool HandleNonPositionalInput => false;

    /// <summary>
    /// Gets the size available to children inside this drawable.
    /// </summary>
    protected internal virtual Vector2 ChildSize => DrawSize;

    /// <summary>
    /// Gets the top-left origin offset applied to children inside this drawable.
    /// </summary>
    protected internal virtual Vector2 ChildOffset => Vector2.Zero;

    /// <summary>
    /// Invalidates cached state matching <paramref name="invalidation"/> and propagates according to <paramref name="source"/>.
    /// </summary>
    /// <returns><see langword="true"/> if any invalidation flags were processed; otherwise <see langword="false"/>.</returns>
    public bool Invalidate(Invalidation invalidation = Invalidation.All, InvalidationSource source = InvalidationSource.Self)
    {
        if (invalidation == Invalidation.None)
            return false;

        validCaches &= ~mapInvalidationToCacheFlags(invalidation);
        InvalidationID++;

        for (var current = this; current is not null; current = current.Parent)
            current.SubTreeInvalidationID++;

        OnInvalidate(invalidation, source);

        if (source == InvalidationSource.Self
            && Parent is not null
            && (invalidation & (Invalidation.Layout | Invalidation.Geometry | Invalidation.Presence | Invalidation.MiscGeometry)) != Invalidation.None)
        {
            Parent.OnChildInvalidated(this, invalidation);
        }

        return true;
    }

    /// <summary>
    /// Transforms a local-space point into <see cref="Parent"/>'s local coordinate space.
    /// </summary>
    public Vector2 ToParentSpace(Vector2 input) => Vector2.Transform(input, LocalTransform);

    /// <summary>
    /// Transforms a local-space point into screen-space coordinates.
    /// </summary>
    public Vector2 ToScreenSpace(Vector2 input) => Vector2.Transform(input, DrawInfo.Matrix);

    /// <summary>
    /// Attempts to transform a screen-space point into this drawable's local coordinate space.
    /// </summary>
    /// <returns><see langword="false"/> when <see cref="DrawInfo"/> is non-invertible (for example, zero scale).</returns>
    public bool TryToLocalSpace(Vector2 screenSpacePosition, out Vector2 localPosition)
    {
        var info = DrawInfo;

        if (!info.IsInvertible)
        {
            localPosition = Vector2.Zero;
            return false;
        }

        localPosition = Vector2.Transform(screenSpacePosition, info.MatrixInverse);
        return true;
    }

    /// <summary>
    /// Transforms a screen-space point into this drawable's local coordinate space,
    /// returning <see cref="Vector2.Zero"/> when the transform matrix is singular.
    /// </summary>
    public Vector2 ToLocalSpace(Vector2 screenSpacePosition)
        => TryToLocalSpace(screenSpacePosition, out var localPosition) ? localPosition : Vector2.Zero;

    /// <summary>
    /// Determines whether <paramref name="screenSpacePosition"/> lies inside this drawable's local bounds.
    /// </summary>
    public virtual bool Contains(Vector2 screenSpacePosition)
        => TryToLocalSpace(screenSpacePosition, out var localPosition) && DrawRectangle.Contains(localPosition);

    /// <summary>
    /// Determines whether this drawable receives positional input at <paramref name="screenSpacePosition"/>.
    /// Defaults to <see cref="Contains"/>.
    /// </summary>
    public virtual bool ReceivePositionalInputAt(Vector2 screenSpacePosition) => Contains(screenSpacePosition);

    /// <summary>
    /// Appends this drawable to <paramref name="queue"/> if it is active, present, handles positional input,
    /// and passes <see cref="ReceivePositionalInputAt"/>.
    /// </summary>
    /// <returns><see langword="true"/> if this subtree was eligible for traversal; otherwise <see langword="false"/>.</returns>
    public virtual bool BuildPositionalInputQueue(Vector2 screenSpacePosition, List<Drawable> queue)
    {
        ArgumentNullException.ThrowIfNull(queue);

        if (IsDisposed || LoadState == LoadState.Loading || !IsPresent)
            return false;

        if (HandlePositionalInput && ReceivePositionalInputAt(screenSpacePosition))
            queue.Add(this);

        return true;
    }

    /// <summary>
    /// Appends this drawable to <paramref name="queue"/> if it is active, present, and handles non-positional input.
    /// </summary>
    /// <returns><see langword="true"/> if this subtree was eligible for traversal; otherwise <see langword="false"/>.</returns>
    public virtual bool BuildNonPositionalInputQueue(List<Drawable> queue)
    {
        ArgumentNullException.ThrowIfNull(queue);

        if (IsDisposed || LoadState == LoadState.Loading || !IsPresent)
            return false;

        if (HandleNonPositionalInput)
            queue.Add(this);

        return true;
    }

    /// <summary>
    /// Schedules <paramref name="action"/> to execute on the update thread during the next <see cref="UpdateSubTree"/> pass.
    /// </summary>
    public ScheduledDelegate Schedule(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        var scheduled = Scheduler.Add(action);

        if (IsDisposed)
            scheduled.Cancel();

        return scheduled;
    }

    /// <summary>
    /// Schedules <paramref name="action"/> to execute on the update thread after <paramref name="timeUntilRun"/> milliseconds
    /// relative to <see cref="Clock"/>.
    /// </summary>
    public ScheduledDelegate ScheduleDelayed(Action action, double timeUntilRun, bool repeat = false)
    {
        ArgumentNullException.ThrowIfNull(action);

        var scheduled = Scheduler.AddDelayed(action, timeUntilRun, repeat);

        if (IsDisposed)
            scheduled.Cancel();

        return scheduled;
    }

    /// <summary>
    /// Advances this drawable and its subtree for the current frame.
    /// </summary>
    /// <returns>
    /// <see langword="false"/> if this drawable is disposed or currently <see cref="LoadState.Loading"/> and was skipped;
    /// otherwise <see langword="true"/>.
    /// </returns>
    public virtual bool UpdateSubTree()
    {
        if (IsDisposed || LoadState == LoadState.Loading)
            return false;

        if (customClock is not null && ProcessCustomClock)
            customClock.ProcessFrame();

        if (Interlocked.CompareExchange(ref loadState, (int)LoadState.Loaded, (int)LoadState.Ready) == (int)LoadState.Ready)
        {
            LoadComplete();

            if (IsDisposed)
                return false;
        }

        if (scheduler is not null)
        {
            scheduler.SetCurrentThread();
            scheduler.Update();

            if (IsDisposed)
                return false;
        }

        Update();

        if (IsDisposed)
            return false;

        if (UpdateChildren())
            UpdateAfterChildren();

        return true;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    internal long ChildID { get; set; }

    internal bool Load(IReadOnlyDependencyCollection parentDependencies)
        => LoadCore(parentDependencies, CancellationToken.None);

    internal bool LoadCore(IReadOnlyDependencyCollection parentDependencies, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parentDependencies);
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        var previous = (LoadState)Interlocked.CompareExchange(ref loadState, (int)LoadState.Loading, (int)LoadState.NotLoaded);

        if (previous == LoadState.Loading)
            throw new InvalidOperationException("Cannot load a Drawable while it is already loading.");

        if (previous != LoadState.NotLoaded)
            return false;

        LoadCancellationToken = cancellationToken;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var ownDependencies = new DependencyCollection(parentDependencies);
            InvokeCacheDependencies(ownDependencies);

            var effectiveDependencies = ownDependencies.Count > 0 ? ownDependencies : parentDependencies;
            Dependencies = effectiveDependencies;

            InvokeResolveDependencies(effectiveDependencies);
            Load();

            cancellationToken.ThrowIfCancellationRequested();

            if (IsDisposed)
            {
                Dependencies = DependencyCollection.EMPTY;
                Interlocked.CompareExchange(ref loadState, (int)LoadState.NotLoaded, (int)LoadState.Loading);
                return false;
            }

            LoadChildren(cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            if (IsDisposed)
            {
                Dependencies = DependencyCollection.EMPTY;
                Interlocked.CompareExchange(ref loadState, (int)LoadState.NotLoaded, (int)LoadState.Loading);
                return false;
            }

            if (Interlocked.CompareExchange(ref loadState, (int)LoadState.Ready, (int)LoadState.Loading) == (int)LoadState.Loading)
                Invalidate(Invalidation.DrawNode);

            return true;
        }
        catch
        {
            Dependencies = DependencyCollection.EMPTY;
            Interlocked.CompareExchange(ref loadState, (int)LoadState.NotLoaded, (int)LoadState.Loading);
            throw;
        }
        finally
        {
            LoadCancellationToken = CancellationToken.None;
        }
    }

    internal virtual void LoadChildren(CancellationToken cancellationToken)
    {
    }

    internal bool RegisterDisposeCallback(Action callback)
    {
        while (true)
        {
            if (IsDisposed)
            {
                callback();
                return false;
            }

            var current = Volatile.Read(ref disposeListeners);
            var updated = (Action)Delegate.Combine(current, callback)!;

            if (ReferenceEquals(Interlocked.CompareExchange(ref disposeListeners, updated, current), current))
            {
                if (IsDisposed)
                {
                    Interlocked.Exchange(ref disposeListeners, null)?.Invoke();
                    return false;
                }

                return true;
            }
        }
    }

    internal void UnregisterDisposeCallback(Action callback)
    {
        while (true)
        {
            var current = Volatile.Read(ref disposeListeners);

            if (current is null)
                return;

            var updated = Delegate.Remove(current, callback) as Action;

            if (ReferenceEquals(Interlocked.CompareExchange(ref disposeListeners, updated, current), current))
                return;
        }
    }

    internal virtual DrawNode? GenerateDrawNodeSubtree(int treeIndex, RectangleI? parentScissor = null)
    {
        if (IsDisposed || !IsPresent || LoadState == LoadState.Loading)
            return null;

        var node = drawNodes[treeIndex];

        if (node is null)
        {
            node = CreateDrawNode();

            if (node is null)
                return null;

            drawNodes[treeIndex] = node;
        }

        if (node.InvalidationID != InvalidationID)
        {
            node.ApplyState();
            node.InvalidationID = InvalidationID;
        }

        return node;
    }

    internal void SetParent(Drawable? newParent)
    {
        if (newParent is not null)
            ObjectDisposedException.ThrowIf(IsDisposed, this);

        if (ReferenceEquals(Parent, newParent))
            return;

        if (Parent is not null && newParent is not null)
            throw new InvalidOperationException("Cannot add a Drawable that already has a Parent to another parent.");

        Parent = newParent;

        if (customClock is null)
            UpdateClock(newParent?.Clock);

        Invalidate(Invalidation.All, InvalidationSource.Parent);
    }

    /// <summary>
    /// Creates a <see cref="DrawNode"/> instance representing this drawable on the draw thread,
    /// or <see langword="null"/> if this drawable has no visual representation.
    /// </summary>
    protected virtual DrawNode? CreateDrawNode() => null;

    /// <summary>
    /// Computes the resolved local size in parent units. Subclasses such as <see cref="CompositeDrawable"/>
    /// override this to incorporate <c>AutoSizeAxes</c>.
    /// </summary>
    protected virtual Vector2 ComputeDrawSize()
    {
        var availableSize = getAvailableParentRectangle().Size;
        return new Vector2(
            RelativeSizeAxes.HasFlag(Axes.X) ? size.X * availableSize.X : size.X,
            RelativeSizeAxes.HasFlag(Axes.Y) ? size.Y * availableSize.Y : size.Y);
    }

    internal void InvokeCacheDependencies(DependencyCollection dependencies)
        => CacheDependencies(dependencies);

    internal void InvokeResolveDependencies(IReadOnlyDependencyCollection dependencies)
        => ResolveDependencies(dependencies);

    /// <summary>
    /// Registers compile-time <c>[Cached]</c> dependencies into <paramref name="dependencies"/>.
    /// Overridden by source-generated dependency injection hooks.
    /// </summary>
    protected virtual void CacheDependencies(DependencyCollection dependencies)
    {
    }

    /// <summary>
    /// Populates compile-time <c>[Resolved]</c> members from <paramref name="dependencies"/>.
    /// Overridden by source-generated dependency injection hooks.
    /// </summary>
    protected virtual void ResolveDependencies(IReadOnlyDependencyCollection dependencies)
    {
    }

    internal bool TriggerEvent(UIEvent e)
    {
        ArgumentNullException.ThrowIfNull(e);
        e.Target = this;
        return Handle(e);
    }

    /// <summary>
    /// Dispatches <paramref name="e"/> to the corresponding typed event handler on this drawable.
    /// </summary>
    /// <returns><see langword="true"/> if the event was consumed and should stop propagating; otherwise <see langword="false"/>.</returns>
    protected virtual bool Handle(UIEvent e)
    {
        switch (e)
        {
            case HoverEvent hover:
                return OnHover(hover);
            case HoverLostEvent hoverLost:
                OnHoverLost(hoverLost);
                return false;
            case MouseMoveEvent mouseMove:
                return OnMouseMove(mouseMove);
            case MouseDownEvent mouseDown:
                return OnMouseDown(mouseDown);
            case MouseUpEvent mouseUp:
                return OnMouseUp(mouseUp);
            case ClickEvent click:
                return OnClick(click);
            case DoubleClickEvent doubleClick:
                return OnDoubleClick(doubleClick);
            case DragStartEvent dragStart:
                return OnDragStart(dragStart);
            case DragEvent drag:
                return OnDrag(drag);
            case DragEndEvent dragEnd:
                OnDragEnd(dragEnd);
                return false;
            case ScrollEvent scroll:
                return OnScroll(scroll);
            case KeyDownEvent keyDown:
                return OnKeyDown(keyDown);
            case KeyUpEvent keyUp:
                return OnKeyUp(keyUp);
            case TextInputEvent textInput:
                return OnTextInput(textInput);
            case FocusEvent focus:
                OnFocus(focus);
                return false;
            case FocusLostEvent focusLost:
                OnFocusLost(focusLost);
                return false;
            default:
                return false;
        }
    }

    /// <summary>
    /// Invoked when the pointer enters or hovers over this drawable.
    /// </summary>
    /// <returns><see langword="true"/> to consume hover and block drawables behind this one.</returns>
    protected virtual bool OnHover(HoverEvent e) => false;

    /// <summary>
    /// Invoked when this drawable ceases to be hovered.
    /// </summary>
    protected virtual void OnHoverLost(HoverLostEvent e)
    {
    }

    /// <summary>
    /// Invoked when the pointer moves over this drawable.
    /// </summary>
    protected virtual bool OnMouseMove(MouseMoveEvent e) => false;

    /// <summary>
    /// Invoked when a mouse button is pressed over this drawable.
    /// </summary>
    protected virtual bool OnMouseDown(MouseDownEvent e) => false;

    /// <summary>
    /// Invoked when a mouse button captured by this drawable is released.
    /// </summary>
    protected virtual bool OnMouseUp(MouseUpEvent e) => false;

    /// <summary>
    /// Invoked when a press and release complete over this drawable without dragging.
    /// </summary>
    protected virtual bool OnClick(ClickEvent e) => false;

    /// <summary>
    /// Invoked when a double-click completes over this drawable.
    /// </summary>
    protected virtual bool OnDoubleClick(DoubleClickEvent e) => false;

    /// <summary>
    /// Invoked when pointer movement while a button is held exceeds the drag threshold.
    /// </summary>
    /// <returns><see langword="true"/> to accept the drag operation.</returns>
    protected virtual bool OnDragStart(DragStartEvent e) => false;

    /// <summary>
    /// Invoked while this drawable is being dragged.
    /// </summary>
    protected virtual bool OnDrag(DragEvent e) => false;

    /// <summary>
    /// Invoked when an active drag operation on this drawable finishes.
    /// </summary>
    protected virtual void OnDragEnd(DragEndEvent e)
    {
    }

    /// <summary>
    /// Invoked when the scroll wheel moves over this drawable.
    /// </summary>
    protected virtual bool OnScroll(ScrollEvent e) => false;

    /// <summary>
    /// Invoked when a keyboard key is pressed or repeats while this drawable is focused or in the non-positional queue.
    /// </summary>
    protected virtual bool OnKeyDown(KeyDownEvent e) => false;

    /// <summary>
    /// Invoked when a keyboard key is released.
    /// </summary>
    protected virtual bool OnKeyUp(KeyUpEvent e) => false;

    /// <summary>
    /// Invoked when committed UTF-8 text input is delivered.
    /// </summary>
    protected virtual bool OnTextInput(TextInputEvent e) => false;

    /// <summary>
    /// Invoked when this drawable acquires keyboard input focus.
    /// </summary>
    protected virtual void OnFocus(FocusEvent e)
    {
    }

    /// <summary>
    /// Invoked when this drawable loses keyboard input focus.
    /// </summary>
    protected virtual void OnFocusLost(FocusLostEvent e)
    {
    }

    /// <summary>
    /// Performs initialization logic during component loading after dependencies have been cached and resolved.
    /// </summary>
    protected virtual void Load()
    {
    }

    /// <summary>
    /// Performs post-load initialization on the update thread once this drawable transitions to <see cref="LoadState.Loaded"/>.
    /// </summary>
    protected virtual void LoadComplete()
    {
    }

    /// <summary>
    /// Performs per-frame update logic on the update thread.
    /// </summary>
    protected virtual void Update()
    {
    }

    /// <summary>
    /// Updates child drawables during <see cref="UpdateSubTree"/>.
    /// </summary>
    protected virtual bool UpdateChildren() => true;

    /// <summary>
    /// Performs post-child update logic (such as container auto-sizing) during <see cref="UpdateSubTree"/>.
    /// </summary>
    protected virtual void UpdateAfterChildren()
    {
    }

    /// <summary>
    /// Invoked during <see cref="Invalidate"/> to allow subclasses to propagate invalidations to children or react to child changes.
    /// </summary>
    protected virtual void OnInvalidate(Invalidation invalidation, InvalidationSource source)
    {
    }

    /// <summary>
    /// Invoked on a parent drawable when a child invalidates layout, geometry, or presence.
    /// </summary>
    protected internal virtual void OnChildInvalidated(Drawable child, Invalidation invalidation)
    {
    }

    /// <summary>
    /// Invoked on a parent drawable when a child's <see cref="Depth"/> changes.
    /// </summary>
    protected internal virtual void OnChildDepthChanged(Drawable child)
    {
    }

    /// <summary>
    /// Updates the inherited clock for this drawable and propagates it to subclasses when no custom clock is assigned.
    /// </summary>
    protected internal virtual void UpdateClock(IFrameBasedClock? clock)
    {
        inheritedClock = customClock is null ? clock : null;
    }

    /// <summary>
    /// Releases resources used by this <see cref="Drawable"/>.
    /// </summary>
    protected virtual void Dispose(bool disposing)
    {
        if (!disposing || Interlocked.Exchange(ref isDisposed, 1) != 0)
            return;

        Interlocked.Exchange(ref disposeListeners, null)?.Invoke();

        if (scheduler is not null)
        {
            scheduler.SetCurrentThread();
            scheduler.CancelPendingTasks();
        }

        Dependencies = DependencyCollection.EMPTY;
        Parent = null;
        HasFocus = false;
        IsHovered = false;
        IsDragged = false;
        customClock = null;
        inheritedClock = null;
        Array.Clear(drawNodes);
    }

    private Scheduler initializeScheduler()
    {
        var created = new Scheduler(new DrawableClockProxy(this));
        var existing = Interlocked.CompareExchange(ref scheduler, created, null);
        return existing ?? created;
    }

    private RectangleF getAvailableParentRectangle()
    {
        if (Parent is null)
            return RectangleF.EMPTY;

        var parentChildRect = new RectangleF(Parent.ChildOffset, Parent.ChildSize);
        return Margin == MarginPadding.ZERO ? parentChildRect : Margin.Deflate(parentChildRect);
    }

    private static CacheFlags mapInvalidationToCacheFlags(Invalidation invalidation)
    {
        var flags = CacheFlags.None;

        if ((invalidation & (Invalidation.Geometry | Invalidation.MiscGeometry)) != Invalidation.None)
            flags |= CacheFlags.DrawSize | CacheFlags.OriginPosition | CacheFlags.LocalTransform | CacheFlags.DrawInfo | CacheFlags.ScreenSpaceDrawQuad;

        if ((invalidation & Invalidation.Layout) != Invalidation.None)
            flags |= CacheFlags.AnchorPosition | CacheFlags.DrawPosition | CacheFlags.OriginPosition | CacheFlags.LocalTransform | CacheFlags.DrawInfo | CacheFlags.ScreenSpaceDrawQuad;

        if ((invalidation & Invalidation.DrawInfo) != Invalidation.None)
            flags |= CacheFlags.LocalTransform | CacheFlags.DrawInfo | CacheFlags.ScreenSpaceDrawQuad;

        if ((invalidation & Invalidation.Color) != Invalidation.None)
            flags |= CacheFlags.DrawColorInfo;

        if ((invalidation & Invalidation.Parent) != Invalidation.None)
            flags |= CacheFlags.DrawSize | CacheFlags.AnchorPosition | CacheFlags.DrawPosition | CacheFlags.OriginPosition | CacheFlags.LocalTransform | CacheFlags.DrawInfo | CacheFlags.ScreenSpaceDrawQuad | CacheFlags.DrawColorInfo;

        return flags;
    }

    private sealed class DrawableClockProxy(Drawable owner) : IClock
    {
        public double CurrentTime => owner.Time;

        public double Rate => owner.Clock?.Rate ?? 1.0;

        public bool IsRunning => owner.Clock?.IsRunning ?? false;
    }
}
