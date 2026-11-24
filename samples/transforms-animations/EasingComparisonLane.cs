using System.Numerics;
using kajarlabs.Framework.Numerics;
using kajarlabs.Framework.Scene;
using kajarlabs.Framework.Scene.Transforms;

namespace kajarlabs.Framework.Samples.TransformsAnimations;

internal sealed class EasingComparisonLane : Container
{
    private const float track_travel = 380f;

    private readonly Easing? standardEasing;
    private readonly CubicBezierEasingFunction? bezierEasing;
    private readonly double staggerDelayMs;
    private readonly Box marker;
    private readonly Box trail;

    public EasingComparisonLane(
        Easing? standardEasing,
        CubicBezierEasingFunction? bezierEasing,
        Color4 accentColor,
        double staggerDelayMs)
    {
        this.standardEasing = standardEasing;
        this.bezierEasing = bezierEasing;
        this.staggerDelayMs = staggerDelayMs;

        RelativeSizeAxes = Axes.X;
        Size = new Vector2(1f, 34f);

        Add(new Box
        {
            RelativeSizeAxes = Axes.Both,
            Size = Vector2.One,
            Color = new Color4(0.11f, 0.14f, 0.21f, 1f),
            Depth = 10f,
        });

        trail = new Box
        {
            Anchor = Anchor.CentreLeft,
            Origin = Anchor.CentreLeft,
            Position = new Vector2(14f, 0f),
            Size = new Vector2(0f, 6f),
            Color = accentColor.WithAlpha(0.35f),
            Depth = 2f,
        };

        marker = new Box
        {
            Anchor = Anchor.CentreLeft,
            Origin = Anchor.Centre,
            Position = new Vector2(24f, 0f),
            Size = new Vector2(20f, 20f),
            Color = accentColor,
            Alpha = 0f,
        };

        AddRange([trail, marker]);
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();
        StartCycle();
    }

    public void StartCycle()
    {
        marker.ClearTransforms();
        trail.ClearTransforms();

        marker.Position = new Vector2(24f, 0f);
        marker.Scale = Vector2.One;
        marker.Alpha = 0f;
        trail.Width = 0f;

        if (bezierEasing is { } bezier)
        {
            using (marker.BeginDelayedSequence(staggerDelayMs))
            {
                marker.FadeIn(140)
                    .Then()
                    .MoveToX(24f + track_travel, 1200, bezier)
                    .ScaleTo(1.25f, 600, bezier)
                    .Then(200)
                    .MoveToX(24f, 700, bezier)
                    .ScaleTo(1f, 700, bezier)
                    .Loop(pause: 350);
            }

            using (trail.BeginDelayedSequence(staggerDelayMs))
            {
                trail.ResizeWidthTo(0f)
                    .Delay(140)
                    .ResizeWidthTo(track_travel, 1200, bezier)
                    .Then(200)
                    .ResizeWidthTo(0f, 700, bezier)
                    .Loop(pause: 350);
            }
        }
        else
        {
            var easing = standardEasing ?? Easing.None;

            using (marker.BeginDelayedSequence(staggerDelayMs))
            {
                marker.FadeIn(140)
                    .Then()
                    .MoveToX(24f + track_travel, 1200, easing)
                    .ScaleTo(1.25f, 600, easing)
                    .Then(200)
                    .MoveToX(24f, 700, easing)
                    .ScaleTo(1f, 700, easing)
                    .Loop(pause: 350);
            }

            using (trail.BeginDelayedSequence(staggerDelayMs))
            {
                trail.ResizeWidthTo(0f)
                    .Delay(140)
                    .ResizeWidthTo(track_travel, 1200, easing)
                    .Then(200)
                    .ResizeWidthTo(0f, 700, easing)
                    .Loop(pause: 350);
            }
        }
    }
}
