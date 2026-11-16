namespace kajarlabs.Framework.Scene;

/// <summary>
/// Specifies which cached properties on a <see cref="Drawable"/> have been invalidated.
/// </summary>
[Flags]
public enum Invalidation
{
    /// <summary>
    /// No cached state is invalidated.
    /// </summary>
    None = 0,

    /// <summary>
    /// The cached draw-space affine matrix and its inverse.
    /// </summary>
    DrawInfo = 1 << 0,

    /// <summary>
    /// Layout-dependent placement such as anchor position, draw position, and origin position.
    /// </summary>
    Layout = 1 << 1,

    /// <summary>
    /// Geometric bounds including draw size and screen-space draw quad.
    /// </summary>
    Geometry = 1 << 2,

    /// <summary>
    /// Accumulated draw color and alpha inherited from ancestors.
    /// </summary>
    Color = 1 << 3,

    /// <summary>
    /// Visibility/presence state derived from alpha and <see cref="Drawable.AlwaysPresent"/>.
    /// </summary>
    Presence = 1 << 4,

    /// <summary>
    /// Parent attachment or sibling ordering state (such as <see cref="Drawable.Depth"/>).
    /// </summary>
    Parent = 1 << 5,

    /// <summary>
    /// Render draw-node state requiring re-synchronization.
    /// </summary>
    DrawNode = 1 << 6,

    /// <summary>
    /// Auxiliary layout bounds (such as margin-inflated layout size) used by parent containers.
    /// </summary>
    MiscGeometry = 1 << 7,

    /// <summary>
    /// All invalidation categories combined.
    /// </summary>
    All = DrawInfo | Layout | Geometry | Color | Presence | Parent | DrawNode | MiscGeometry,
}

/// <summary>
/// Identifies the hierarchical origin of an <see cref="Invalidation"/> event.
/// </summary>
public enum InvalidationSource
{
    /// <summary>
    /// Triggered by a property change on this <see cref="Drawable"/> itself.
    /// </summary>
    Self,

    /// <summary>
    /// Propagated downward from an ancestor <see cref="Drawable"/>.
    /// </summary>
    Parent,

    /// <summary>
    /// Propagated upward from a child <see cref="Drawable"/>.
    /// </summary>
    Child,
}
