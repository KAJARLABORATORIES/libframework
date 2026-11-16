using kajarlabs.Framework.Graphics.Rendering;
using kajarlabs.Framework.Numerics;

namespace kajarlabs.Framework.Scene;

/// <summary>
/// A <see cref="DrawNode"/> representing a <see cref="CompositeDrawable"/> subtree,
/// reusing a pre-allocated child node list and applying scissor masking state on the draw thread.
/// </summary>
public class CompositeDrawNode : DrawNode
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CompositeDrawNode"/> class bound to <paramref name="source"/>.
    /// </summary>
    public CompositeDrawNode(CompositeDrawable source)
        : base(source)
    {
    }

    /// <summary>
    /// Gets the ordered child <see cref="DrawNode"/> snapshots rendered by this composite node.
    /// </summary>
    public IReadOnlyList<DrawNode> Children => WritableChildren;

    /// <summary>
    /// Gets a value indicating whether scissor masking is enabled for this subtree.
    /// </summary>
    public bool Masking { get; private set; }

    /// <summary>
    /// Gets the effective screen-space scissor rectangle applied while drawing <see cref="Children"/>.
    /// </summary>
    public RectangleI? EffectiveScissor { get; private set; }

    /// <summary>
    /// Gets the ancestor scissor rectangle restored after drawing <see cref="Children"/>.
    /// </summary>
    public RectangleI? PreviousScissor { get; private set; }

    internal List<DrawNode> WritableChildren { get; } = [];

    internal long SyncedSubTreeInvalidationID { get; set; } = -1;

    internal RectangleI? SyncedParentScissor { get; set; }

    internal bool HasSyncedParentScissor { get; set; }

    internal void UpdateScissorState(bool masking, RectangleI? effectiveScissor, RectangleI? previousScissor)
    {
        Masking = masking;
        EffectiveScissor = effectiveScissor;
        PreviousScissor = previousScissor;
    }

    /// <inheritdoc />
    public override void Draw(IRenderer renderer)
    {
        if (WritableChildren.Count == 0)
            return;

        if (Masking)
        {
            renderer.SetScissor(EffectiveScissor);

            try
            {
                for (var i = 0; i < WritableChildren.Count; i++)
                    WritableChildren[i].Draw(renderer);
            }
            finally
            {
                renderer.SetScissor(PreviousScissor);
            }

            return;
        }

        for (var i = 0; i < WritableChildren.Count; i++)
            WritableChildren[i].Draw(renderer);
    }
}
