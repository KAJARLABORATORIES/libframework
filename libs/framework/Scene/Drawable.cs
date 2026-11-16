using System.Numerics;
using kajarlabs.Framework.Numerics;
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
    public bool IsDisposed { get; private set; }

    /// <summary>
    /// Gets the monotonically increasing invalidation counter used to detect state changes across frames.
    /// </summary>
    public long InvalidationID { get; private set; }

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
                var availableSize = getAvailableParentRectangle().Size;
                cachedDrawSize = new Vector2(
                    RelativeSizeAxes.HasFlag(Axes.X) ? size.X * availableSize.X : size.X,
                    RelativeSizeAxes.HasFlag(Axes.Y) ? size.Y * availableSize.Y : size.Y);
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
    /// Advances this drawable and its subtree for the current frame.
    /// </summary>
    /// <returns><see langword="false"/> if this drawable is disposed and was skipped; otherwise <see langword="true"/>.</returns>
    public virtual bool UpdateSubTree()
    {
        if (IsDisposed)
            return false;

        if (customClock is not null && ProcessCustomClock)
            customClock.ProcessFrame();

        Update();

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
        if (!disposing || IsDisposed)
            return;

        IsDisposed = true;
        Parent = null;
        customClock = null;
        inheritedClock = null;
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

        if ((invalidation & Invalidation.Geometry) != Invalidation.None)
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
}
