using System.Numerics;
using kajarlabs.Framework.Graphics;
using kajarlabs.Framework.Graphics.Rendering;
using kajarlabs.Framework.Numerics;

namespace kajarlabs.Framework.Scene;

/// <summary>
/// A <see cref="Drawable"/> that renders an <see cref="ITexture"/> across its <see cref="Drawable.ScreenSpaceDrawQuad"/>,
/// supporting normalized UV sub-rectangles, sampler configuration, and default sizing from the assigned texture.
/// </summary>
public class Sprite : Drawable
{
    private static readonly RectangleF full_texture_coords = new(0f, 0f, 1f, 1f);

    /// <summary>
    /// Gets or sets the texture rendered by this sprite, or <see langword="null"/> to skip drawing.
    /// The caller retains ownership and disposal responsibility for the assigned <see cref="ITexture"/>.
    /// </summary>
    public ITexture? Texture
    {
        get => field;
        set
        {
            if (ReferenceEquals(field, value))
                return;

            var previousWidth = field?.Width ?? 0;
            var previousHeight = field?.Height ?? 0;

            field = value;

            var nextWidth = value?.Width ?? 0;
            var nextHeight = value?.Height ?? 0;

            var invalidation = Invalidation.DrawNode;
            var widthChanged = previousWidth != nextWidth && !RelativeSizeAxes.HasFlag(Axes.X) && Size.X == 0f;
            var heightChanged = previousHeight != nextHeight && !RelativeSizeAxes.HasFlag(Axes.Y) && Size.Y == 0f;

            if (widthChanged || heightChanged)
                invalidation |= Invalidation.Geometry | Invalidation.Layout | Invalidation.DrawInfo | Invalidation.MiscGeometry;

            Invalidate(invalidation);
        }
    }

    /// <summary>
    /// Gets or sets the normalized texture UV coordinates (<c>(0, 0, 1, 1)</c> by default)
    /// used when sampling <see cref="Texture"/>.
    /// </summary>
    public RectangleF TextureCoords
    {
        get => field;
        set
        {
            if (field == value)
                return;

            field = value;
            Invalidate(Invalidation.DrawNode);
        }
    } = full_texture_coords;

    /// <summary>
    /// Gets or sets the texture sampler parameters applied when drawing this sprite.
    /// </summary>
    public TextureSamplerParameters Sampler
    {
        get => field;
        set
        {
            if (field == value)
                return;

            field = value;
            Invalidate(Invalidation.DrawNode);
        }
    } = TextureSamplerParameters.LINEAR_CLAMP;

    /// <inheritdoc />
    protected override DrawNode? CreateDrawNode() => new SpriteDrawNode(this);

    /// <summary>
    /// Computes the resolved local size in parent units, defaulting non-relative axes with <c>0</c> size
    /// to <see cref="Texture"/>'s pixel dimensions when a texture is assigned.
    /// </summary>
    protected override Vector2 ComputeDrawSize()
    {
        var baseDrawSize = base.ComputeDrawSize();

        if (Texture is not { } texture)
            return baseDrawSize;

        return new Vector2(
            !RelativeSizeAxes.HasFlag(Axes.X) && Size.X == 0f ? texture.Width : baseDrawSize.X,
            !RelativeSizeAxes.HasFlag(Axes.Y) && Size.Y == 0f ? texture.Height : baseDrawSize.Y);
    }

    /// <summary>
    /// A <see cref="DrawNode"/> rendering a <see cref="Sprite"/>'s snapshotted <see cref="ITexture"/>,
    /// UV coordinates, and <see cref="TextureSamplerParameters"/> on the draw thread.
    /// </summary>
    public class SpriteDrawNode : DrawNode
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SpriteDrawNode"/> class bound to <paramref name="source"/>.
        /// </summary>
        public SpriteDrawNode(Sprite source)
            : base(source)
        {
        }

        /// <summary>
        /// Gets the snapshot texture captured during <see cref="ApplyState"/>.
        /// </summary>
        public ITexture? Texture { get; protected set; }

        /// <summary>
        /// Gets the snapshot normalized UV coordinates captured during <see cref="ApplyState"/>.
        /// </summary>
        public RectangleF TextureCoords { get; protected set; } = full_texture_coords;

        /// <summary>
        /// Gets the snapshot texture sampler configuration captured during <see cref="ApplyState"/>.
        /// </summary>
        public TextureSamplerParameters Sampler { get; protected set; } = TextureSamplerParameters.LINEAR_CLAMP;

        /// <summary>
        /// Gets the source <see cref="Sprite"/> used exclusively during <see cref="ApplyState"/> on the update thread.
        /// </summary>
        protected new Sprite Source => (Sprite)base.Source;

        /// <inheritdoc />
        public override void ApplyState()
        {
            base.ApplyState();

            var sprite = Source;
            Texture = sprite.Texture;
            TextureCoords = sprite.TextureCoords;
            Sampler = sprite.Sampler;
        }

        /// <inheritdoc />
        public override void Draw(IRenderer renderer)
        {
            ArgumentNullException.ThrowIfNull(renderer);

            if (Texture is null)
                return;

            ApplyBlending(renderer);
            renderer.SetSampler(Sampler);
            renderer.DrawQuad(Texture, ScreenSpaceDrawQuad, TextureCoords, DrawColorInfo.Color);
        }
    }
}
