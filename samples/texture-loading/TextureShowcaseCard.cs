using System.Numerics;
using kajarlabs.Framework.DependencyInjection;
using kajarlabs.Framework.Graphics;
using kajarlabs.Framework.Numerics;
using kajarlabs.Framework.Scene;

namespace kajarlabs.Framework.Samples.TextureLoading;

internal sealed partial class TextureShowcaseCard : Container
{
    private readonly string textureName;
    private readonly Box border;
    private readonly Sprite sprite;

    [Resolved]
    private TextureCache textures { get; set; } = null!;

    public TextureShowcaseCard(string textureName, TextureSamplerParameters sampler, Color4 accentColor)
    {
        this.textureName = textureName;
        Padding = MarginPadding.All(6f);

        border = new Box
        {
            RelativeSizeAxes = Axes.Both,
            Size = Vector2.One,
            Color = accentColor,
            Depth = 10f,
        };

        var panel = new Container
        {
            RelativeSizeAxes = Axes.Both,
            Size = Vector2.One,
            Padding = MarginPadding.All(16f),
            Masking = true,
        };

        panel.Add(new Box
        {
            RelativeSizeAxes = Axes.Both,
            Size = Vector2.One,
            Color = new Color4(0.11f, 0.14f, 0.21f, 1f),
            Depth = 5f,
        });

        sprite = new Sprite
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            RelativeSizeAxes = Axes.Both,
            Size = new Vector2(0.82f, 0.82f),
            Sampler = sampler,
        };

        panel.Add(sprite);
        AddRange([border, panel]);
    }

    public void ReloadTexture() => sprite.Texture = textures.Get(textureName);

    public void SetSampler(TextureSamplerParameters sampler) => sprite.Sampler = sampler;

    public void SetAccentColor(Color4 color) => border.Color = color;

    protected override void Load() => ReloadTexture();
}
