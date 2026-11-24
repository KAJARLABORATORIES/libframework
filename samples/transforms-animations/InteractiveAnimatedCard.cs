using System.Numerics;
using kajarlabs.Framework.Input;
using kajarlabs.Framework.Numerics;
using kajarlabs.Framework.Scene;
using kajarlabs.Framework.Scene.Transforms;

namespace kajarlabs.Framework.Samples.TransformsAnimations;

internal sealed class InteractiveAnimatedCard : Container
{
    private readonly Color4 baseColor;
    private readonly Color4 hoverColor;
    private readonly Vector2 restPosition;
    private readonly double introDelayMs;
    private readonly Action<Vector2, Color4> onSpawnBurst;
    private readonly Box background;
    private readonly Box badge;

    public InteractiveAnimatedCard(
        Vector2 restPosition,
        Color4 baseColor,
        Color4 hoverColor,
        double introDelayMs,
        Action<Vector2, Color4> onSpawnBurst)
    {
        this.restPosition = restPosition;
        this.baseColor = baseColor;
        this.hoverColor = hoverColor;
        this.introDelayMs = introDelayMs;
        this.onSpawnBurst = onSpawnBurst;

        Size = new Vector2(148f, 92f);
        Origin = Anchor.Centre;
        Alpha = 0f;
        Position = restPosition + new Vector2(-40f, 30f);

        background = new Box
        {
            RelativeSizeAxes = Axes.Both,
            Size = Vector2.One,
            Color = baseColor,
        };

        badge = new Box
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            Size = new Vector2(28f, 28f),
            Color = Color4.WHITE.WithAlpha(0.85f),
        };

        AddRange([background, badge]);
    }

    public override bool AcceptsFocus => true;

    protected override void LoadComplete()
    {
        base.LoadComplete();
        ReplayIntro();
        badge.Spin(2400, RotationDirection.Clockwise);
    }

    public void ReplayIntro()
    {
        this.ClearTransforms();
        background.ClearTransforms();

        Alpha = 0f;
        Scale = new Vector2(0.8f);
        Rotation = -8f;
        Position = restPosition + new Vector2(-40f, 30f);
        background.Color = baseColor;

        using (this.BeginDelayedSequence(introDelayMs))
        {
            this.FadeInFromZero(220, Easing.OutCubic)
                .Then()
                .MoveTo(restPosition, 420, Easing.OutBack)
                .ScaleTo(1f, 360, Easing.OutElastic)
                .RotateTo(0f, 380, Easing.OutExpo);
        }
    }

    protected override bool OnHover(HoverEvent e)
    {
        this.ScaleTo(1.08f, 180, Easing.OutBack)
            .RotateTo(3f, 180, Easing.OutQuint);
        background.FadeColor(hoverColor, 180, Easing.OutQuad);
        return true;
    }

    protected override void OnHoverLost(HoverLostEvent e)
    {
        this.ScaleTo(1f, 220, Easing.OutQuint)
            .RotateTo(0f, 220, Easing.OutQuint);
        background.FadeColor(baseColor, 220, Easing.OutQuad);
    }

    protected override bool OnMouseDown(MouseDownEvent e) => e.Button == MouseButton.Left;

    protected override bool OnClick(ClickEvent e)
    {
        background.FlashColor(Color4.WHITE, 260, Easing.OutExpo);
        this.ScaleTo(0.92f, 60, Easing.OutQuad)
            .Then()
            .ScaleTo(IsHovered ? 1.08f : 1f, 260, Easing.OutBack);

        onSpawnBurst(e.ScreenSpaceMousePosition, hoverColor);
        return true;
    }
}
