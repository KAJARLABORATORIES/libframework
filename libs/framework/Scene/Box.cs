using kajarlabs.Framework.Graphics.Rendering;

namespace kajarlabs.Framework.Scene;

/// <summary>
/// A solid rectangular <see cref="Drawable"/> rendered using <see cref="IRenderer.WhitePixel"/>
/// across its <see cref="Drawable.ScreenSpaceDrawQuad"/> with inherited color, alpha, and blending.
/// </summary>
public class Box : Drawable
{
    /// <inheritdoc />
    protected override DrawNode? CreateDrawNode() => new BoxDrawNode(this);

    /// <summary>
    /// A <see cref="DrawNode"/> rendering a <see cref="Box"/> as a solid tinted quad
    /// using <see cref="IRenderer.WhitePixel"/>.
    /// </summary>
    public class BoxDrawNode : DrawNode
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="BoxDrawNode"/> class bound to <paramref name="source"/>.
        /// </summary>
        public BoxDrawNode(Box source)
            : base(source)
        {
        }

        /// <inheritdoc />
        public override void Draw(IRenderer renderer)
        {
            ArgumentNullException.ThrowIfNull(renderer);

            ApplyBlending(renderer);
            renderer.DrawQuad(renderer.WhitePixel, ScreenSpaceDrawQuad, DrawColorInfo.Color);
        }
    }
}
