using System.Numerics;
using kajarlabs.Framework;
using kajarlabs.Framework.Input;
using kajarlabs.Framework.Numerics;
using kajarlabs.Framework.Scene;
using kajarlabs.Framework.Scene.Transforms;
using Microsoft.Extensions.Logging;

namespace kajarlabs.Framework.Samples.TransformsAnimations;

internal enum ShowcaseAction
{
    ReplaySequences,
    SpawnBurst,
    ResetStage,
    Exit,
}

internal sealed partial class TransformsShowcaseGame : Game
{
    private static readonly (Vector2 Offset, float Rotation)[] burst_directions =
    [
        (new Vector2(-90f, -65f), -140f),
        (new Vector2(0f, -110f), 90f),
        (new Vector2(95f, -60f), 160f),
        (new Vector2(-105f, 35f), -200f),
        (new Vector2(105f, 40f), 210f),
        (new Vector2(0f, 95f), 180f),
    ];

    private readonly TimeSpan? autoExitAfter;
    private readonly List<EasingComparisonLane> easingLanes = [];
    private readonly List<InteractiveAnimatedCard> interactiveCards = [];

    private Container? cardStage;
    private Container? particleLayer;
    private Box? chainedRunner;
    private double elapsedRunMs;
    private double ambientSpawnAccumulatorMs;
    private int ambientPaletteIndex;
    private bool exitRequested;

    public TransformsShowcaseGame(TimeSpan? autoExitAfter)
    {
        this.autoExitAfter = autoExitAfter;
    }

    protected override void Load()
    {
        var backdrop = new Box
        {
            RelativeSizeAxes = Axes.Both,
            Size = Vector2.One,
            Color = new Color4(0.06f, 0.08f, 0.12f, 1f),
            Depth = 30f,
        };

        var bindingContainer = new ShowcaseKeyBindingContainer(this)
        {
            RelativeSizeAxes = Axes.Both,
            Size = Vector2.One,
        };

        var layoutRoot = new Container
        {
            RelativeSizeAxes = Axes.Both,
            Size = Vector2.One,
            Padding = MarginPadding.All(24f),
        };

        var lanesPanel = buildEasingPanel();
        var showcasePanel = buildInteractivePanel();

        particleLayer = new Container
        {
            RelativeSizeAxes = Axes.Both,
            Size = Vector2.One,
            Depth = -20f,
        };

        layoutRoot.AddRange([lanesPanel, showcasePanel, particleLayer]);
        bindingContainer.Add(layoutRoot);
        Root.AddRange([backdrop, bindingContainer]);

        chainedRunner?.Schedule(replayChainedRunner);
        logShowcaseReady(Logger);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Transforms showcase ready. Controls: hover/click cards, Space=Replay, B=Burst, R=Reset, Esc=Exit.")]
    private static partial void logShowcaseReady(ILogger logger);

    protected override void Update()
    {
        base.Update();

        var frameElapsed = Root.ElapsedFrameTime;
        elapsedRunMs += frameElapsed;
        ambientSpawnAccumulatorMs += frameElapsed;

        if (ambientSpawnAccumulatorMs >= 950.0)
        {
            ambientSpawnAccumulatorMs -= 950.0;
            spawnAmbientPulse();
        }

        if (autoExitAfter is { } limit && !exitRequested && elapsedRunMs >= limit.TotalMilliseconds)
        {
            exitRequested = true;
            Host.Exit();
        }
    }

    private Container buildEasingPanel()
    {
        var panel = new Container
        {
            Anchor = Anchor.TopLeft,
            Origin = Anchor.TopLeft,
            RelativeSizeAxes = Axes.Both,
            Size = new Vector2(0.47f, 1f),
            Padding = MarginPadding.All(14f),
        };

        panel.Add(new Box
        {
            RelativeSizeAxes = Axes.Both,
            Size = Vector2.One,
            Color = new Color4(0.09f, 0.11f, 0.17f, 1f),
            Depth = 20f,
        });

        (Easing? Standard, CubicBezierEasingFunction? Bezier, Color4 Color)[] specs =
        [
            (Easing.None, null, Color4.WHITE),
            (Easing.OutQuad, null, new Color4(0.35f, 0.85f, 1f, 1f)),
            (Easing.OutQuint, null, new Color4(0.35f, 0.92f, 0.58f, 1f)),
            (Easing.OutExpo, null, new Color4(0.98f, 0.78f, 0.28f, 1f)),
            (Easing.OutBack, null, new Color4(0.96f, 0.45f, 0.58f, 1f)),
            (Easing.OutElastic, null, new Color4(0.75f, 0.52f, 0.98f, 1f)),
            (Easing.OutBounce, null, new Color4(1f, 0.55f, 0.25f, 1f)),
            (null, CubicBezierEasingFunction.EASE_IN_OUT, Color4.CYAN),
        ];

        for (var i = 0; i < specs.Length; i++)
        {
            var (standard, bezier, color) = specs[i];
            var lane = new EasingComparisonLane(standard, bezier, color, staggerDelayMs: i * 45.0)
            {
                Position = new Vector2(0f, i * 46f),
            };

            easingLanes.Add(lane);
            panel.Add(lane);
        }

        return panel;
    }

    private Container buildInteractivePanel()
    {
        cardStage = new Container
        {
            Anchor = Anchor.TopRight,
            Origin = Anchor.TopRight,
            RelativeSizeAxes = Axes.Both,
            Size = new Vector2(0.50f, 1f),
            Padding = MarginPadding.All(14f),
        };

        cardStage.Add(new Box
        {
            RelativeSizeAxes = Axes.Both,
            Size = Vector2.One,
            Color = new Color4(0.09f, 0.11f, 0.17f, 1f),
            Depth = 20f,
        });

        chainedRunner = new Box
        {
            Position = new Vector2(36f, 36f),
            Origin = Anchor.Centre,
            Size = new Vector2(36f, 36f),
            Color = new Color4(0.35f, 0.90f, 0.65f, 1f),
            Alpha = 0f,
        };
        cardStage.Add(chainedRunner);

        (Vector2 Pos, Color4 Base, Color4 Hover, double Delay)[] cards =
        [
            (new Vector2(110f, 175f), new Color4(0.20f, 0.42f, 0.72f, 1f), new Color4(0.32f, 0.68f, 0.98f, 1f), 80),
            (new Vector2(285f, 175f), new Color4(0.46f, 0.28f, 0.64f, 1f), new Color4(0.72f, 0.45f, 0.95f, 1f), 180),
            (new Vector2(110f, 295f), new Color4(0.62f, 0.34f, 0.24f, 1f), new Color4(0.95f, 0.56f, 0.32f, 1f), 280),
            (new Vector2(285f, 295f), new Color4(0.20f, 0.52f, 0.46f, 1f), new Color4(0.32f, 0.85f, 0.72f, 1f), 380),
        ];

        for (var i = 0; i < cards.Length; i++)
        {
            var (pos, baseColor, hoverColor, delay) = cards[i];
            var card = new InteractiveAnimatedCard(
                pos,
                baseColor,
                hoverColor,
                delay,
                spawnBurstAtScreenPosition);

            interactiveCards.Add(card);
            cardStage.Add(card);
        }

        return cardStage;
    }

    private void replayChainedRunner()
    {
        if (chainedRunner is null)
            return;

        chainedRunner.ClearTransforms();
        chainedRunner.Alpha = 0f;
        chainedRunner.Position = new Vector2(36f, 36f);
        chainedRunner.Rotation = 0f;
        chainedRunner.Scale = Vector2.One;

        chainedRunner.FadeIn(180, Easing.OutQuad)
            .Then()
            .MoveTo(new Vector2(340f, 36f), 650, Easing.OutExpo)
            .RotateTo(180f, 650, Easing.OutExpo)
            .Then(120)
            .MoveTo(new Vector2(36f, 36f), 550, Easing.InOutCubic)
            .RotateTo(360f, 550, Easing.InOutCubic)
            .Loop(pause: 300);
    }

    private void replayAll()
    {
        replayChainedRunner();

        for (var i = 0; i < easingLanes.Count; i++)
            easingLanes[i].StartCycle();

        for (var i = 0; i < interactiveCards.Count; i++)
            interactiveCards[i].ReplayIntro();
    }

    private void spawnBurstAtScreenPosition(Vector2 screenPosition, Color4 color)
    {
        if (particleLayer is null)
            return;

        var local = particleLayer.ToLocalSpace(screenPosition);

        for (var i = 0; i < burst_directions.Length; i++)
        {
            var (offset, rotation) = burst_directions[i];
            particleLayer.Add(new TransientBurstParticle(local, offset, rotation, color));
        }
    }

    private void spawnAmbientPulse()
    {
        if (particleLayer is null)
            return;

        var localCenter = new Vector2(particleLayer.DrawSize.X * 0.74f, particleLayer.DrawSize.Y * 0.85f);
        var color = (ambientPaletteIndex++ % 3) switch
        {
            0 => new Color4(0.35f, 0.85f, 1f, 1f),
            1 => new Color4(0.95f, 0.65f, 0.25f, 1f),
            _ => new Color4(0.68f, 0.45f, 0.96f, 1f),
        };

        var (offset, rotation) = burst_directions[ambientPaletteIndex % burst_directions.Length];
        particleLayer.Add(new TransientBurstParticle(localCenter, offset * 0.6f, rotation, color));
    }

    private sealed class ShowcaseKeyBindingContainer : KeyBindingContainer<ShowcaseAction>, IKeyBindingHandler<ShowcaseAction>
    {
        private readonly TransformsShowcaseGame game;

        public ShowcaseKeyBindingContainer(TransformsShowcaseGame game)
            : base(SimultaneousBindingMode.All)
        {
            this.game = game;
            KeyBindings =
            [
                new(Key.Space, ShowcaseAction.ReplaySequences),
                new(Key.B, ShowcaseAction.SpawnBurst),
                new(Key.R, ShowcaseAction.ResetStage),
                new(Key.Escape, ShowcaseAction.Exit),
            ];
        }

        public bool OnPressed(KeyBindingPressEvent<ShowcaseAction> e)
        {
            switch (e.Action)
            {
                case ShowcaseAction.ReplaySequences:
                case ShowcaseAction.ResetStage:
                    game.replayAll();
                    return true;

                case ShowcaseAction.SpawnBurst:
                    if (game.particleLayer is not null)
                        game.spawnBurstAtScreenPosition(game.particleLayer.ToScreenSpace(game.particleLayer.DrawSize * 0.65f), Color4.CYAN);
                    return true;

                case ShowcaseAction.Exit:
                    game.Host.Exit();
                    return true;

                default:
                    return false;
            }
        }

        public void OnReleased(KeyBindingReleaseEvent<ShowcaseAction> e)
        {
        }
    }
}
