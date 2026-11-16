using kajarlabs.Framework.Graphics;
using kajarlabs.Framework.Graphics.Rendering;
using kajarlabs.Framework.Numerics;

namespace kajarlabs.Framework.Scene;

/// <summary>
/// Thread-safe render snapshot generated from a <see cref="Drawable"/> on the update thread
/// and consumed by <see cref="IRenderer"/> on the draw thread.
/// </summary>
public class DrawNode
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DrawNode"/> class bound to <paramref name="source"/>.
    /// </summary>
    public DrawNode(Drawable source)
    {
        ArgumentNullException.ThrowIfNull(source);
        Source = source;
    }

    /// <summary>
    /// Gets the source <see cref="Drawable"/> used exclusively during <see cref="ApplyState"/> on the update thread.
    /// </summary>
    protected Drawable Source { get; }

    /// <summary>
    /// Gets the <see cref="Drawable.InvalidationID"/> most recently synchronized into this node.
    /// </summary>
    public long InvalidationID { get; internal set; } = -1;

    /// <summary>
    /// Gets the snapshot draw-space transform and inverse captured during <see cref="ApplyState"/>.
    /// </summary>
    public DrawInfo DrawInfo { get; protected set; } = DrawInfo.IDENTITY;

    /// <summary>
    /// Gets the snapshot screen-space quad captured during <see cref="ApplyState"/>.
    /// </summary>
    public Quad ScreenSpaceDrawQuad { get; protected set; }

    /// <summary>
    /// Gets the snapshot accumulated color and alpha captured during <see cref="ApplyState"/>.
    /// </summary>
    public DrawColorInfo DrawColorInfo { get; protected set; } = DrawColorInfo.WHITE;

    /// <summary>
    /// Gets the snapshot blending configuration captured during <see cref="ApplyState"/>.
    /// </summary>
    public BlendingParameters Blending { get; protected set; } = BlendingParameters.ALPHA;

    /// <summary>
    /// Synchronizes draw-thread-safe state from <see cref="Source"/> on the update thread.
    /// </summary>
    public virtual void ApplyState()
    {
        DrawInfo = Source.DrawInfo;
        ScreenSpaceDrawQuad = Source.ScreenSpaceDrawQuad;
        DrawColorInfo = Source.DrawColorInfo;
        Blending = Source.Blending;
    }

    /// <summary>
    /// Renders this node's snapshot state using <paramref name="renderer"/> on the draw thread.
    /// </summary>
    public virtual void Draw(IRenderer renderer)
    {
    }

    /// <summary>
    /// Applies this node's snapshotted <see cref="Blending"/> state to <paramref name="renderer"/>.
    /// </summary>
    protected void ApplyBlending(IRenderer renderer)
        => renderer.SetBlending(Blending);
}
