using System.Numerics;
using kajarlabs.Framework;
using kajarlabs.Framework.Bindables;
using kajarlabs.Framework.DependencyInjection;
using kajarlabs.Framework.Input;
using kajarlabs.Framework.Numerics;
using kajarlabs.Framework.Platform;
using kajarlabs.Framework.Scene;
using Microsoft.Extensions.Logging;

TimeSpan? autoExitAfter = null;

for (var i = 0; i < args.Length; i++)
{
    if (args[i] == "--duration" && i + 1 < args.Length && double.TryParse(args[i + 1], out var seconds) && seconds > 0)
    {
        autoExitAfter = TimeSpan.FromSeconds(seconds);
        i++;
    }
}

using var host = new GameHost(new HostOptions
{
    Organization = "kajarlabs",
    Application = "InteractiveInputSample",
    WindowTitle = "kajarlabs.Framework - Interactive Input, Focus Tree & Key Bindings",
    Resizable = true,
});

ConsoleCancelEventHandler onCancelKeyPress = (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    host.Exit();
};

Console.CancelKeyPress += onCancelKeyPress;

try
{
    using var game = new InteractiveInputSampleGame(autoExitAfter);
    host.Run(game);
}
finally
{
    Console.CancelKeyPress -= onCancelKeyPress;
}

internal enum SampleAction
{
    ToggleRotation,
    ResetScene,
    ToggleMasking,
    CycleFocus,
    CycleAccent,
    ZoomIn,
    ZoomOut,
    Exit,
}

internal sealed class SampleSceneState : IDisposable
{
    public Bindable<Color4> AccentColor { get; } = new(Color4.CYAN);

    public BindableBool RotationEnabled { get; } = new(true);

    public BindableBool MaskingEnabled { get; } = new(true);

    public BindableNumber<float> StageZoom { get; } = new(1f)
    {
        MinValue = 0.6f,
        MaxValue = 1.6f,
        Precision = 0.05f,
    };

    public void Dispose()
    {
        AccentColor.UnbindAll();
        RotationEnabled.UnbindAll();
        MaskingEnabled.UnbindAll();
        StageZoom.UnbindAll();
    }
}

internal sealed partial class InteractiveInputSampleGame : Game
{
    private static readonly Color4[] accent_palette =
    [
        Color4.CYAN,
        new Color4(0.35f, 0.9f, 0.55f, 1f),
        new Color4(0.98f, 0.72f, 0.25f, 1f),
        Color4.MAGENTA,
    ];

    private readonly TimeSpan? autoExitAfter;
    private readonly List<Drawable> focusCycleTargets = [];

    private SampleKeyBindingContainer? keyBindingRoot;
    private SampleActionStage? actionStage;
    private MaskedInteractivePanel? maskedPanel;
    private DraggableCard? primaryCard;
    private DraggableCard? secondaryCard;
    private double elapsedRunMs;
    private int paletteIndex;
    private int focusCycleIndex = -1;
    private bool exitRequested;

    [Cached]
    public SampleSceneState SceneState { get; } = new();

    public Bindable<Color4> AccentColor => SceneState.AccentColor;

    public BindableBool RotationEnabled => SceneState.RotationEnabled;

    public BindableBool MaskingEnabled => SceneState.MaskingEnabled;

    public BindableNumber<float> StageZoom => SceneState.StageZoom;

    public InteractiveInputSampleGame(TimeSpan? autoExitAfter)
    {
        this.autoExitAfter = autoExitAfter;
    }

    protected override void Load()
    {
        var backdrop = new Box
        {
            RelativeSizeAxes = Axes.Both,
            Size = Vector2.One,
            Color = new Color4(0.07f, 0.09f, 0.13f, 1f),
            Depth = 20f,
        };

        keyBindingRoot = new SampleKeyBindingContainer
        {
            RelativeSizeAxes = Axes.Both,
            Size = Vector2.One,
        };

        actionStage = new SampleActionStage(this)
        {
            RelativeSizeAxes = Axes.Both,
            Size = Vector2.One,
            Padding = MarginPadding.All(24f),
        };

        maskedPanel = new MaskedInteractivePanel
        {
            Anchor = Anchor.TopLeft,
            Origin = Anchor.TopLeft,
            RelativeSizeAxes = Axes.Both,
            Size = new Vector2(0.48f, 0.80f),
        };

        var dragArena = new Container
        {
            Anchor = Anchor.TopRight,
            Origin = Anchor.TopRight,
            RelativeSizeAxes = Axes.Both,
            Size = new Vector2(0.48f, 0.54f),
            Padding = MarginPadding.All(12f),
            Masking = true,
        };

        dragArena.Add(new Box
        {
            RelativeSizeAxes = Axes.Both,
            Size = Vector2.One,
            Color = new Color4(0.11f, 0.14f, 0.21f, 1f),
            Depth = 10f,
        });

        primaryCard = new DraggableCard("PrimaryCard", new Vector2(28f, 28f), new Color4(0.24f, 0.45f, 0.72f, 1f));
        secondaryCard = new DraggableCard("SecondaryCard", new Vector2(180f, 110f), new Color4(0.48f, 0.30f, 0.66f, 1f));
        dragArena.AddRange([primaryCard, secondaryCard]);

        var focusBar = new Container
        {
            Anchor = Anchor.BottomRight,
            Origin = Anchor.BottomRight,
            RelativePositionAxes = Axes.Y,
            Position = new Vector2(0f, -0.18f),
            RelativeSizeAxes = Axes.Both,
            Size = new Vector2(0.48f, 0.22f),
            Padding = MarginPadding.All(12f),
        };

        focusBar.Add(new Box
        {
            RelativeSizeAxes = Axes.Both,
            Size = Vector2.One,
            Color = new Color4(0.11f, 0.14f, 0.21f, 1f),
            Depth = 10f,
        });

        var focusTargetA = new FocusTargetTile("FocusTargetA")
        {
            Anchor = Anchor.CentreLeft,
            Origin = Anchor.CentreLeft,
            RelativeSizeAxes = Axes.Both,
            Size = new Vector2(0.28f, 0.85f),
        };

        var focusTargetB = new FocusTargetTile("FocusTargetB")
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            RelativeSizeAxes = Axes.Both,
            Size = new Vector2(0.28f, 0.85f),
        };

        var textInputTarget = new TextInputFocusTile("TextInputTarget")
        {
            Anchor = Anchor.CentreRight,
            Origin = Anchor.CentreRight,
            RelativeSizeAxes = Axes.Both,
            Size = new Vector2(0.36f, 0.85f),
        };

        focusBar.AddRange([focusTargetA, focusTargetB, textInputTarget]);

        var hudBar = actionStage.CreateBindingHud();
        actionStage.AddRange([maskedPanel, dragArena, focusBar, hudBar]);
        keyBindingRoot.Add(actionStage);

        Root.AddRange([backdrop, keyBindingRoot]);

        focusCycleTargets.AddRange([
            maskedPanel.CenterTile,
            primaryCard,
            secondaryCard,
            focusTargetA,
            focusTargetB,
            textInputTarget,
        ]);

        logSampleStarted(Logger, Renderer.Viewport.Width, Renderer.Viewport.Height);
    }

    internal void HandleSampleAction(SampleAction action)
    {
        switch (action)
        {
            case SampleAction.ToggleRotation:
                RotationEnabled.Toggle();
                logToggleRotation(Logger, RotationEnabled.Value);
                break;

            case SampleAction.ResetScene:
                RotationEnabled.Value = true;
                MaskingEnabled.Value = true;
                StageZoom.Value = 1f;
                maskedPanel?.ResetLayout();
                primaryCard?.ResetPosition();
                secondaryCard?.ResetPosition();
                logResetScene(Logger);
                break;

            case SampleAction.ToggleMasking:
                MaskingEnabled.Toggle();
                logToggleMasking(Logger, MaskingEnabled.Value);
                break;

            case SampleAction.CycleFocus:
                cycleFocus();
                break;

            case SampleAction.CycleAccent:
                paletteIndex = (paletteIndex + 1) % accent_palette.Length;
                AccentColor.Value = accent_palette[paletteIndex];
                logCycleAccent(Logger, paletteIndex);
                break;

            case SampleAction.ZoomIn:
                StageZoom.Value += 0.1f;
                logZoomIn(Logger, StageZoom.Value);
                break;

            case SampleAction.ZoomOut:
                StageZoom.Value -= 0.1f;
                logZoomOut(Logger, StageZoom.Value);
                break;

            case SampleAction.Exit:
                exitRequested = true;
                Exit();
                break;
        }
    }

    protected override void Update()
    {
        elapsedRunMs += Root.ElapsedFrameTime;

        if (autoExitAfter is { } duration && !exitRequested && elapsedRunMs >= duration.TotalMilliseconds)
        {
            exitRequested = true;
            Exit();
        }
    }

    protected override void Draw()
    {
        Renderer.Clear(new Color4(0.05f, 0.06f, 0.09f, 1f));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            SceneState.Dispose();

        base.Dispose(disposing);
    }

    private void cycleFocus()
    {
        if (focusCycleTargets.Count == 0)
            return;

        focusCycleIndex = (focusCycleIndex + 1) % focusCycleTargets.Count;
        var next = focusCycleTargets[focusCycleIndex];
        var changed = InputManager.ChangeFocus(next);

        if (Logger.IsEnabled(LogLevel.Information))
        {
            var targetName = next.GetType().Name;
            logCycleFocus(Logger, targetName, changed);
        }
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Interactive input sample started (viewport: {Width}x{Height}). Controls: hover/click/double-click tiles, scroll left panel, drag right cards, type when right-most focus box is focused. Keys: R=rotate, Ctrl+R=reset, M=masking, Tab=cycle focus, C=accent, Ctrl+Scroll=zoom, Esc=exit.")]
    private static partial void logSampleStarted(ILogger logger, float width, float height);

    [LoggerMessage(Level = LogLevel.Information, Message = "KeyBinding ToggleRotation -> RotationEnabled={Enabled}")]
    private static partial void logToggleRotation(ILogger logger, bool enabled);

    [LoggerMessage(Level = LogLevel.Information, Message = "KeyBinding ResetScene (Ctrl+R) -> scene state reset.")]
    private static partial void logResetScene(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "KeyBinding ToggleMasking -> MaskingEnabled={Enabled}")]
    private static partial void logToggleMasking(ILogger logger, bool enabled);

    [LoggerMessage(Level = LogLevel.Information, Message = "KeyBinding CycleAccent -> palette index {Index}")]
    private static partial void logCycleAccent(ILogger logger, int index);

    [LoggerMessage(Level = LogLevel.Information, Message = "KeyBinding ZoomIn -> StageZoom={Zoom:0.00}")]
    private static partial void logZoomIn(ILogger logger, float zoom);

    [LoggerMessage(Level = LogLevel.Information, Message = "KeyBinding ZoomOut -> StageZoom={Zoom:0.00}")]
    private static partial void logZoomOut(ILogger logger, float zoom);

    [LoggerMessage(Level = LogLevel.Information, Message = "KeyBinding CycleFocus -> target={Target} acquired={Acquired}")]
    private static partial void logCycleFocus(ILogger logger, string target, bool acquired);
}

internal sealed class SampleKeyBindingContainer : KeyBindingContainer<SampleAction>
{
    public SampleKeyBindingContainer()
        : base(SimultaneousBindingMode.All)
    {
    }

    public override IEnumerable<KeyBinding<SampleAction>> DefaultKeyBindings =>
    [
        new(new KeyCombination(InputKey.Control, Key.R), SampleAction.ResetScene),
        new(new KeyCombination(Key.R), SampleAction.ToggleRotation),
        new(new KeyCombination(Key.M), SampleAction.ToggleMasking),
        new(new KeyCombination(Key.Tab), SampleAction.CycleFocus),
        new(new KeyCombination(Key.C), SampleAction.CycleAccent),
        new(new KeyCombination(InputKey.Control, InputKey.MouseWheelUp), SampleAction.ZoomIn),
        new(new KeyCombination(InputKey.Control, InputKey.MouseWheelDown), SampleAction.ZoomOut),
        new(new KeyCombination(Key.Escape), SampleAction.Exit),
    ];
}

internal sealed class SampleActionStage : Container, IKeyBindingHandler<SampleAction>
{
    private readonly InteractiveInputSampleGame game;
    private readonly Dictionary<SampleAction, Box> actionIndicators = [];

    public SampleActionStage(InteractiveInputSampleGame game)
    {
        this.game = game;
    }

    public Container CreateBindingHud()
    {
        var hud = new Container
        {
            Anchor = Anchor.BottomCentre,
            Origin = Anchor.BottomCentre,
            RelativeSizeAxes = Axes.Both,
            Size = new Vector2(1f, 0.13f),
            Padding = MarginPadding.All(10f),
        };

        hud.Add(new Box
        {
            RelativeSizeAxes = Axes.Both,
            Size = Vector2.One,
            Color = new Color4(0.11f, 0.13f, 0.19f, 1f),
            Depth = 10f,
        });

        SampleAction[] hudActions =
        [
            SampleAction.ToggleRotation,
            SampleAction.ResetScene,
            SampleAction.ToggleMasking,
            SampleAction.CycleFocus,
            SampleAction.CycleAccent,
            SampleAction.ZoomIn,
            SampleAction.ZoomOut,
        ];

        var slotWidth = 1f / hudActions.Length;

        for (var i = 0; i < hudActions.Length; i++)
        {
            var slot = new Container
            {
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
                RelativePositionAxes = Axes.X,
                Position = new Vector2(i * slotWidth, 0f),
                RelativeSizeAxes = Axes.Both,
                Size = new Vector2(slotWidth, 1f),
                Padding = new MarginPadding(6f, 4f, 6f, 4f),
            };

            var indicator = new Box
            {
                RelativeSizeAxes = Axes.Both,
                Size = Vector2.One,
                Color = new Color4(0.20f, 0.24f, 0.34f, 1f),
            };

            actionIndicators[hudActions[i]] = indicator;
            slot.Add(indicator);
            hud.Add(slot);
        }

        return hud;
    }

    public bool OnPressed(KeyBindingPressEvent<SampleAction> e)
    {
        if (actionIndicators.TryGetValue(e.Action, out var indicator))
            indicator.Color = Color4.YELLOW;

        game.HandleSampleAction(e.Action);
        return true;
    }

    public void OnReleased(KeyBindingReleaseEvent<SampleAction> e)
    {
        if (actionIndicators.TryGetValue(e.Action, out var indicator))
            indicator.Color = new Color4(0.20f, 0.24f, 0.34f, 1f);
    }
}

internal sealed partial class MaskedInteractivePanel : Container
{
    [Resolved]
    private SampleSceneState sceneState = null!;

    [Resolved]
    private Game game = null!;

    private readonly BindableBool boundRotation = new();
    private readonly BindableBool boundMasking = new();
    private readonly BindableNumber<float> boundZoom = new();

    private Container outerRotating = null!;
    private Container innerCounterRotating = null!;
    private Box frameBorder = null!;
    private float scrollPanY;

    public InteractiveTile CenterTile { get; private set; } = null!;

    public override bool HandlePositionalInput => true;

    public void ResetLayout()
    {
        scrollPanY = 0f;

        if (outerRotating is not null)
        {
            outerRotating.Rotation = 0f;
            outerRotating.Position = Vector2.Zero;
        }

        innerCounterRotating?.Rotation = 0f;
    }

    protected override void Load()
    {
        Padding = MarginPadding.All(14f);

        frameBorder = new Box
        {
            RelativeSizeAxes = Axes.Both,
            Size = Vector2.One,
            Color = new Color4(0.12f, 0.16f, 0.24f, 1f),
            Depth = 10f,
        };

        outerRotating = new Container
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            Size = new Vector2(280f, 280f),
        };

        outerRotating.Add(new Box
        {
            RelativeSizeAxes = Axes.Both,
            Size = Vector2.One,
            Color = new Color4(0.19f, 0.27f, 0.42f, 0.85f),
            Depth = 8f,
        });

        var topLeftTile = new InteractiveTile("OuterTopLeft", new Color4(0.25f, 0.55f, 0.78f, 1f))
        {
            Anchor = Anchor.TopLeft,
            Origin = Anchor.TopLeft,
            Position = new Vector2(18f, 18f),
            Size = new Vector2(76f, 76f),
        };

        var bottomRightTile = new InteractiveTile("OuterBottomRight", new Color4(0.32f, 0.68f, 0.54f, 1f))
        {
            Anchor = Anchor.BottomRight,
            Origin = Anchor.BottomRight,
            Position = new Vector2(-18f, -18f),
            Size = new Vector2(76f, 76f),
        };

        innerCounterRotating = new Container
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            Size = new Vector2(124f, 124f),
        };

        innerCounterRotating.Add(new Box
        {
            RelativeSizeAxes = Axes.Both,
            Size = Vector2.One,
            Color = new Color4(0.15f, 0.20f, 0.30f, 0.95f),
            Depth = 5f,
        });

        CenterTile = new InteractiveTile("InnerCenterTile", new Color4(0.75f, 0.42f, 0.26f, 1f))
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            Size = new Vector2(74f, 74f),
        };

        innerCounterRotating.Add(CenterTile);
        outerRotating.AddRange([topLeftTile, bottomRightTile, innerCounterRotating]);

        // Intentionally overflows the right edge of this masked panel to show mask clipping.
        var overflowTile = new InteractiveTile("OverflowMaskedTile", new Color4(0.72f, 0.28f, 0.44f, 1f))
        {
            Anchor = Anchor.BottomRight,
            Origin = Anchor.BottomLeft,
            Position = new Vector2(-56f, -12f),
            Size = new Vector2(140f, 52f),
        };

        AddRange([frameBorder, outerRotating, overflowTile]);

        boundRotation.BindTo(sceneState.RotationEnabled);
        boundMasking.BindTo(sceneState.MaskingEnabled);
        boundZoom.BindTo(sceneState.StageZoom);
    }

    protected override void LoadComplete()
    {
        boundMasking.BindValueChanged(e =>
        {
            Masking = e.NewValue;
            frameBorder.Color = e.NewValue
                ? new Color4(0.12f, 0.16f, 0.24f, 1f)
                : new Color4(0.24f, 0.15f, 0.20f, 1f);
        }, runOnceImmediately: true);

        boundZoom.BindValueChanged(e =>
        {
            outerRotating.Scale = new Vector2(e.NewValue);
        }, runOnceImmediately: true);
    }

    protected override void Update()
    {
        if (!boundRotation.Value)
            return;

        var dt = (float)ElapsedFrameTime;
        outerRotating.Rotation = (outerRotating.Rotation + (dt * 0.018f)) % 360f;
        innerCounterRotating.Rotation = (innerCounterRotating.Rotation - (dt * 0.036f)) % 360f;
    }

    protected override bool OnScroll(ScrollEvent e)
    {
        if (e.CurrentState.Modifiers.HasControl)
            return false;

        scrollPanY = Math.Clamp(scrollPanY + (e.ScrollDelta.Y * 18f), -90f, 90f);
        outerRotating.Position = new Vector2(0f, scrollPanY);
        logPanelScrolled(game.Logger, scrollPanY);
        return true;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            boundRotation.UnbindAll();
            boundMasking.UnbindAll();
            boundZoom.UnbindAll();
        }

        base.Dispose(disposing);
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "MaskedInteractivePanel scrolled -> panY={PanY:0.0}")]
    private static partial void logPanelScrolled(ILogger logger, float panY);
}

internal sealed partial class InteractiveTile : Container
{
    private readonly string name;
    private readonly Color4 baseColor;

    [Resolved]
    private SampleSceneState sceneState = null!;

    [Resolved]
    private Game game = null!;

    private readonly Bindable<Color4> boundAccent = new();
    private Box focusFrame = null!;
    private Box fill = null!;
    private Box activeBadge = null!;
    private bool isPressed;
    private bool toggled;
    private double flashRemainingMs;

    public InteractiveTile(string name, Color4 baseColor)
    {
        this.name = name;
        this.baseColor = baseColor;
    }

    public override bool AcceptsFocus => true;

    protected override void Load()
    {
        focusFrame = new Box
        {
            RelativeSizeAxes = Axes.Both,
            Size = Vector2.One,
            Color = new Color4(0.10f, 0.12f, 0.17f, 1f),
            Depth = 5f,
        };

        var inner = new Container
        {
            RelativeSizeAxes = Axes.Both,
            Size = Vector2.One,
            Padding = MarginPadding.All(4f),
        };

        fill = new Box
        {
            RelativeSizeAxes = Axes.Both,
            Size = Vector2.One,
            Color = baseColor,
        };

        activeBadge = new Box
        {
            Anchor = Anchor.TopRight,
            Origin = Anchor.TopRight,
            Position = new Vector2(-4f, 4f),
            Size = new Vector2(12f, 12f),
            Color = Color4.WHITE,
            Alpha = 0f,
            Depth = -1f,
        };

        inner.AddRange([fill, activeBadge]);
        AddRange([focusFrame, inner]);

        boundAccent.BindTo(sceneState.AccentColor);
    }

    protected override void LoadComplete()
    {
        boundAccent.BindValueChanged(_ => updateVisuals(), runOnceImmediately: true);
    }

    protected override void Update()
    {
        if (flashRemainingMs > 0)
        {
            flashRemainingMs = Math.Max(0, flashRemainingMs - ElapsedFrameTime);
            updateVisuals();
        }
    }

    protected override bool OnHover(HoverEvent e)
    {
        updateVisuals();
        return true;
    }

    protected override void OnHoverLost(HoverLostEvent e) => updateVisuals();

    protected override bool OnMouseDown(MouseDownEvent e)
    {
        if (e.Button != MouseButton.Left)
            return false;

        isPressed = true;
        updateVisuals();
        return true;
    }

    protected override bool OnMouseUp(MouseUpEvent e)
    {
        isPressed = false;
        updateVisuals();
        return true;
    }

    protected override bool OnClick(ClickEvent e)
    {
        toggled = !toggled;
        flashRemainingMs = 220;
        updateVisuals();
        logTileClicked(game.Logger, name, toggled);
        return true;
    }

    protected override bool OnDoubleClick(DoubleClickEvent e)
    {
        flashRemainingMs = 450;
        Scale = Scale == Vector2.One ? new Vector2(1.15f) : Vector2.One;
        updateVisuals();
        logTileDoubleClicked(game.Logger, name, Scale.X);
        return true;
    }

    protected override void OnFocus(FocusEvent e)
    {
        updateVisuals();
        logTileFocused(game.Logger, name);
    }

    protected override void OnFocusLost(FocusLostEvent e) => updateVisuals();

    protected override bool OnKeyDown(KeyDownEvent e)
    {
        if (e.Key is Key.Space or Key.Enter)
        {
            toggled = !toggled;
            flashRemainingMs = 220;
            updateVisuals();
            return true;
        }

        return false;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            boundAccent.UnbindAll();

        base.Dispose(disposing);
    }

    private void updateVisuals()
    {
        if (fill is null || focusFrame is null || activeBadge is null)
            return;

        focusFrame.Color = HasFocus ? boundAccent.Value : new Color4(0.10f, 0.12f, 0.17f, 1f);
        activeBadge.Alpha = toggled ? 1f : 0f;

        if (flashRemainingMs > 0)
            fill.Color = Color4.WHITE;
        else if (isPressed)
            fill.Color = new Color4(baseColor.R * 0.65f, baseColor.G * 0.65f, baseColor.B * 0.65f, 1f);
        else if (IsHovered)
            fill.Color = new Color4(MathF.Min(1f, baseColor.R + 0.22f), MathF.Min(1f, baseColor.G + 0.22f), MathF.Min(1f, baseColor.B + 0.22f), 1f);
        else
            fill.Color = baseColor;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Clicked {Tile} (toggled={Toggled}).")]
    private static partial void logTileClicked(ILogger logger, string tile, bool toggled);

    [LoggerMessage(Level = LogLevel.Information, Message = "Double-clicked {Tile} -> scale={Scale}.")]
    private static partial void logTileDoubleClicked(ILogger logger, string tile, float scale);

    [LoggerMessage(Level = LogLevel.Information, Message = "Focused {Tile}.")]
    private static partial void logTileFocused(ILogger logger, string tile);
}

internal sealed partial class DraggableCard : Container
{
    private readonly string name;
    private readonly Vector2 initialPosition;
    private readonly Color4 baseColor;

    [Resolved]
    private SampleSceneState sceneState = null!;

    [Resolved]
    private Game game = null!;

    private readonly Bindable<Color4> boundAccent = new();
    private Box border = null!;
    private Box body = null!;
    private Box dragStrip = null!;

    public DraggableCard(string name, Vector2 initialPosition, Color4 baseColor)
    {
        this.name = name;
        this.initialPosition = initialPosition;
        this.baseColor = baseColor;
        Position = initialPosition;
        Size = new Vector2(130f, 92f);
    }

    public override bool AcceptsFocus => true;

    public void ResetPosition()
    {
        Position = initialPosition;
        updateVisuals();
    }

    protected override void Load()
    {
        border = new Box
        {
            RelativeSizeAxes = Axes.Both,
            Size = Vector2.One,
            Color = new Color4(0.10f, 0.12f, 0.17f, 1f),
            Depth = 5f,
        };

        var inner = new Container
        {
            RelativeSizeAxes = Axes.Both,
            Size = Vector2.One,
            Padding = MarginPadding.All(4f),
        };

        body = new Box
        {
            RelativeSizeAxes = Axes.Both,
            Size = Vector2.One,
            Color = baseColor,
        };

        dragStrip = new Box
        {
            RelativeSizeAxes = Axes.X,
            Size = new Vector2(1f, 14f),
            Color = new Color4(0.95f, 0.95f, 0.98f, 0.35f),
            Depth = -1f,
        };

        inner.AddRange([body, dragStrip]);
        AddRange([border, inner]);

        boundAccent.BindTo(sceneState.AccentColor);
    }

    protected override void LoadComplete()
    {
        boundAccent.BindValueChanged(_ => updateVisuals(), runOnceImmediately: true);
    }

    protected override bool OnHover(HoverEvent e)
    {
        updateVisuals();
        return true;
    }

    protected override void OnHoverLost(HoverLostEvent e) => updateVisuals();

    protected override bool OnMouseDown(MouseDownEvent e) => e.Button == MouseButton.Left;

    protected override bool OnDragStart(DragStartEvent e)
    {
        if (e.Button != MouseButton.Left)
            return false;

        if (Parent is not null)
            Position += Parent.ToLocalSpace(e.ScreenSpaceMousePosition) - Parent.ToLocalSpace(e.ScreenSpaceMouseDownPosition);

        updateVisuals();
        logDragStarted(game.Logger, name);
        return true;
    }

    protected override bool OnDrag(DragEvent e)
    {
        if (Parent is not null)
            Position += Parent.ToLocalSpace(e.ScreenSpaceMousePosition) - Parent.ToLocalSpace(e.ScreenSpaceLastMousePosition);

        return true;
    }

    protected override void OnDragEnd(DragEndEvent e)
    {
        updateVisuals();
        logDragFinished(game.Logger, name, Position.X, Position.Y);
    }

    protected override void OnFocus(FocusEvent e) => updateVisuals();

    protected override void OnFocusLost(FocusLostEvent e) => updateVisuals();

    protected override bool OnKeyDown(KeyDownEvent e)
    {
        const float nudge = 12f;

        var delta = e.Key switch
        {
            Key.Left => new Vector2(-nudge, 0f),
            Key.Right => new Vector2(nudge, 0f),
            Key.Up => new Vector2(0f, -nudge),
            Key.Down => new Vector2(0f, nudge),
            _ => Vector2.Zero,
        };

        if (delta == Vector2.Zero)
            return false;

        Position += delta;
        return true;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            boundAccent.UnbindAll();

        base.Dispose(disposing);
    }

    private void updateVisuals()
    {
        if (border is null || body is null || dragStrip is null)
            return;

        border.Color = IsDragged
            ? Color4.YELLOW
            : HasFocus
                ? boundAccent.Value
                : new Color4(0.10f, 0.12f, 0.17f, 1f);

        body.Color = IsDragged
            ? new Color4(MathF.Min(1f, baseColor.R + 0.25f), MathF.Min(1f, baseColor.G + 0.25f), MathF.Min(1f, baseColor.B + 0.25f), 1f)
            : IsHovered
                ? new Color4(MathF.Min(1f, baseColor.R + 0.14f), MathF.Min(1f, baseColor.G + 0.14f), MathF.Min(1f, baseColor.B + 0.14f), 1f)
                : baseColor;

        dragStrip.Color = IsDragged ? Color4.YELLOW : new Color4(0.95f, 0.95f, 0.98f, 0.35f);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Started dragging {Card}.")]
    private static partial void logDragStarted(ILogger logger, string card);

    [LoggerMessage(Level = LogLevel.Information, Message = "Finished dragging {Card} at ({X:0.0}, {Y:0.0}).")]
    private static partial void logDragFinished(ILogger logger, string card, float x, float y);
}

internal sealed partial class FocusTargetTile : Container
{
    private readonly string name;

    [Resolved]
    private SampleSceneState sceneState = null!;

    [Resolved]
    private Game game = null!;

    private readonly Bindable<Color4> boundAccent = new();
    private Box border = null!;
    private Box fill = null!;

    public FocusTargetTile(string name)
    {
        this.name = name;
    }

    public override bool AcceptsFocus => true;

    protected override void Load()
    {
        border = new Box
        {
            RelativeSizeAxes = Axes.Both,
            Size = Vector2.One,
            Color = new Color4(0.16f, 0.20f, 0.29f, 1f),
            Depth = 5f,
        };

        var inner = new Container
        {
            RelativeSizeAxes = Axes.Both,
            Size = Vector2.One,
            Padding = MarginPadding.All(4f),
        };

        fill = new Box
        {
            RelativeSizeAxes = Axes.Both,
            Size = Vector2.One,
            Color = new Color4(0.20f, 0.25f, 0.36f, 1f),
        };

        inner.Add(fill);
        AddRange([border, inner]);
        boundAccent.BindTo(sceneState.AccentColor);
    }

    protected override void LoadComplete()
    {
        boundAccent.BindValueChanged(_ => updateVisuals(), runOnceImmediately: true);
    }

    protected override bool OnHover(HoverEvent e)
    {
        updateVisuals();
        return true;
    }

    protected override void OnHoverLost(HoverLostEvent e) => updateVisuals();

    protected override bool OnMouseDown(MouseDownEvent e) => e.Button == MouseButton.Left;

    protected override void OnFocus(FocusEvent e)
    {
        updateVisuals();
        logFocusSwitched(game.Logger, name);
    }

    protected override void OnFocusLost(FocusLostEvent e) => updateVisuals();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            boundAccent.UnbindAll();

        base.Dispose(disposing);
    }

    private void updateVisuals()
    {
        if (border is null || fill is null)
            return;

        border.Color = HasFocus ? boundAccent.Value : new Color4(0.16f, 0.20f, 0.29f, 1f);
        fill.Color = HasFocus
            ? new Color4(0.27f, 0.36f, 0.50f, 1f)
            : IsHovered
                ? new Color4(0.24f, 0.30f, 0.42f, 1f)
                : new Color4(0.20f, 0.25f, 0.36f, 1f);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Focus switched to {Target}.")]
    private static partial void logFocusSwitched(ILogger logger, string target);
}

internal sealed partial class TextInputFocusTile : Container
{
    private readonly string name;

    [Resolved]
    private SampleSceneState sceneState = null!;

    [Resolved]
    private Game game = null!;

    private readonly Bindable<Color4> boundAccent = new();
    private Box border = null!;
    private Box fill = null!;
    private Box activityMeter = null!;
    private string buffer = string.Empty;

    public TextInputFocusTile(string name)
    {
        this.name = name;
    }

    public override bool AcceptsFocus => true;

    public override bool RequestsTextInput => true;

    protected override void Load()
    {
        border = new Box
        {
            RelativeSizeAxes = Axes.Both,
            Size = Vector2.One,
            Color = new Color4(0.16f, 0.20f, 0.29f, 1f),
            Depth = 5f,
        };

        var inner = new Container
        {
            RelativeSizeAxes = Axes.Both,
            Size = Vector2.One,
            Padding = MarginPadding.All(4f),
        };

        fill = new Box
        {
            RelativeSizeAxes = Axes.Both,
            Size = Vector2.One,
            Color = new Color4(0.18f, 0.23f, 0.32f, 1f),
            Depth = 2f,
        };

        activityMeter = new Box
        {
            Anchor = Anchor.BottomLeft,
            Origin = Anchor.BottomLeft,
            RelativeSizeAxes = Axes.X,
            Size = new Vector2(0f, 10f),
            Color = Color4.GREEN,
        };

        inner.AddRange([fill, activityMeter]);
        AddRange([border, inner]);
        boundAccent.BindTo(sceneState.AccentColor);
    }

    protected override void LoadComplete()
    {
        boundAccent.BindValueChanged(_ => updateVisuals(), runOnceImmediately: true);
    }

    protected override bool OnHover(HoverEvent e)
    {
        updateVisuals();
        return true;
    }

    protected override void OnHoverLost(HoverLostEvent e) => updateVisuals();

    protected override bool OnMouseDown(MouseDownEvent e) => e.Button == MouseButton.Left;

    protected override void OnFocus(FocusEvent e)
    {
        updateVisuals();
        logTextInputFocused(game.Logger, name);
    }

    protected override void OnFocusLost(FocusLostEvent e) => updateVisuals();

    protected override bool OnTextInput(TextInputEvent e)
    {
        buffer += e.Text;

        if (buffer.Length > 24)
            buffer = buffer[^24..];

        updateVisuals();
        logTextReceived(game.Logger, name, e.Text, buffer);
        return true;
    }

    protected override bool OnKeyDown(KeyDownEvent e)
    {
        if (e.Key == Key.Backspace && buffer.Length > 0)
        {
            buffer = buffer[..^1];
            updateVisuals();
            return true;
        }

        return false;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            boundAccent.UnbindAll();

        base.Dispose(disposing);
    }

    private void updateVisuals()
    {
        if (border is null || fill is null || activityMeter is null)
            return;

        border.Color = HasFocus ? boundAccent.Value : new Color4(0.16f, 0.20f, 0.29f, 1f);
        fill.Color = HasFocus
            ? new Color4(0.24f, 0.34f, 0.44f, 1f)
            : IsHovered
                ? new Color4(0.22f, 0.28f, 0.38f, 1f)
                : new Color4(0.18f, 0.23f, 0.32f, 1f);
        activityMeter.Width = MathF.Min(1f, buffer.Length / 24f);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Focused {Target} (text input active).")]
    private static partial void logTextInputFocused(ILogger logger, string target);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Target} received text '{Text}' (buffer='{Buffer}').")]
    private static partial void logTextReceived(ILogger logger, string target, string text, string buffer);
}
