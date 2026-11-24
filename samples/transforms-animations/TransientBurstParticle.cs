using System.Numerics;
using kajarlabs.Framework.Numerics;
using kajarlabs.Framework.Scene;
using kajarlabs.Framework.Scene.Transforms;

namespace kajarlabs.Framework.Samples.TransformsAnimations;

internal sealed class TransientBurstParticle : Box
{
    private readonly Vector2 offset;
    private readonly float targetRotation;

    public TransientBurstParticle(Vector2 startPosition, Vector2 offset, float targetRotation, Color4 color)
    {
        Position = startPosition;
        this.offset = offset;
        this.targetRotation = targetRotation;

        Origin = Anchor.Centre;
        Size = new Vector2(16f, 16f);
        Alpha = 0f;
        Scale = new Vector2(0.4f);
        Color = color;
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();

        this.FadeInFromZero(70, Easing.OutQuad)
            .ScaleTo(1.2f, 120, Easing.OutBack)
            .Then()
            .MoveToOffset(offset, 520, Easing.OutExpo)
            .RotateTo(targetRotation, 520, Easing.OutCubic)
            .ScaleTo(0.15f, 520, Easing.InQuad)
            .FadeOut(480, Easing.InQuad)
            .Expire();
    }
}
