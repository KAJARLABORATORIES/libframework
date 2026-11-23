using System.Numerics;
using kajarlabs.Framework;
using kajarlabs.Framework.Bindables;
using kajarlabs.Framework.DependencyInjection;
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
    Application = "ReactiveComponentsSample",
    WindowTitle = "kajarlabs.Framework - Reactive Bindable Components, DI & Async Loading",
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
    using var game = new ReactiveComponentsSampleGame(autoExitAfter);
    host.Run(game);
}
finally
{
    Console.CancelKeyPress -= onCancelKeyPress;
}

internal sealed partial class ReactiveComponentsSampleGame : Game
{
    private static readonly Color4[] accent_palette =
    [
        Color4.CYAN,
        new Color4(0.35f, 0.9f, 0.55f, 1f),
        new Color4(0.98f, 0.72f, 0.25f, 1f),
        Color4.MAGENTA,
    ];

    private readonly TimeSpan? autoExitAfter;
    private double elapsedRunMs;
    private double nextPaletteSwitchMs = 1200;
    private int paletteIndex;
    private bool exitRequested;

    [Cached]
    public Bindable<Color4> AccentColor { get; } = new(accent_palette[0]);

    [Cached]
    public BindableNumber<float> PulseIntensity { get; } = new(0.5f)
    {
        MinValue = 0.2f,
        MaxValue = 1.0f,
        Precision = 0.05f,
    };

    [Cached]
    public BindableBool MaskingEnabled { get; } = new(true);

    public ReactiveComponentsSampleGame(TimeSpan? autoExitAfter)
    {
        this.autoExitAfter = autoExitAfter;
    }

    protected override void Load()
    {
        var backdrop = new Box
        {
            RelativeSizeAxes = Axes.Both,
            Size = Vector2.One,
            Color = new Color4(0.08f, 0.1f, 0.15f, 1f),
            Depth = 10f,
        };

        var reactiveStage = new ReactiveStageContainer();

        Root.AddRange([backdrop, reactiveStage]);

        // Load a primary orbital centerpiece and a batch of reactive meter bars off the update thread.
        _ = Root.LoadComponentAsync(new AsyncReactiveOrbital(), Root.Add);

        var meterBars = Enumerable.Range(0, 5)
            .Select(index => new AsyncReactiveBar(index))
            .ToArray();

        _ = Root.LoadComponentsAsync(meterBars, Root.AddRange);

        logSampleStarted(Logger, Renderer.Viewport.Width, Renderer.Viewport.Height);
    }

    protected override void Update()
    {
        var dtMs = Root.ElapsedFrameTime;
        elapsedRunMs += dtMs;

        var wave = 0.6f + (0.4f * MathF.Sin((float)(elapsedRunMs / 1000.0) * 2.2f));
        PulseIntensity.Value = wave;

        if (elapsedRunMs >= nextPaletteSwitchMs)
        {
            nextPaletteSwitchMs += 1200;
            paletteIndex = (paletteIndex + 1) % accent_palette.Length;
            AccentColor.Value = accent_palette[paletteIndex];
            MaskingEnabled.Toggle();
        }

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
        {
            AccentColor.UnbindAll();
            PulseIntensity.UnbindAll();
            MaskingEnabled.UnbindAll();
        }

        base.Dispose(disposing);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Reactive components sample started (initial viewport: {Width}x{Height}).")]
    private static partial void logSampleStarted(ILogger logger, float width, float height);
}

internal sealed partial class ReactiveStageContainer : Container
{
    [Resolved]
    private Bindable<Color4> accentColor = null!;

    [Resolved]
    private BindableBool maskingEnabled = null!;

    [Resolved]
    private GameHost host = null!;

    private readonly Bindable<Color4> boundAccent = new();
    private readonly BindableBool boundMasking = new();
    private Box borderFill = null!;
    private ILogger? logger;

    protected override void Load()
    {
        logger = host.LoggerFactory.CreateLogger<ReactiveStageContainer>();
        Anchor = Anchor.Centre;
        Origin = Anchor.Centre;
        RelativeSizeAxes = Axes.Both;
        Size = new Vector2(0.7f, 0.7f);
        Padding = MarginPadding.All(16f);

        borderFill = new Box
        {
            RelativeSizeAxes = Axes.Both,
            Size = Vector2.One,
            Color = new Color4(0.14f, 0.18f, 0.27f, 1f),
            Depth = 5f,
        };

        Add(borderFill);

        boundAccent.BindTo(accentColor);
        boundMasking.BindTo(maskingEnabled);
    }

    protected override void LoadComplete()
    {
        boundAccent.BindValueChanged(e =>
        {
            borderFill.Color = new Color4(
                0.08f + (e.NewValue.R * 0.16f),
                0.1f + (e.NewValue.G * 0.16f),
                0.14f + (e.NewValue.B * 0.16f),
                1f);
            if (logger is not null)
                logAccentTintUpdated(logger);
        }, runOnceImmediately: true);

        boundMasking.BindValueChanged(e => Masking = e.NewValue, runOnceImmediately: true);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            boundAccent.UnbindAll();
            boundMasking.UnbindAll();
        }

        base.Dispose(disposing);
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Reactive stage updated accent tint.")]
    private static partial void logAccentTintUpdated(ILogger logger);
}

internal sealed partial class AsyncReactiveOrbital : Container
{
    [Resolved]
    private Bindable<Color4> accentColor = null!;

    [Resolved]
    private BindableNumber<float> pulseIntensity = null!;

    [Resolved]
    private Game game = null!;

    private readonly Bindable<Color4> boundAccent = new();
    private readonly BindableNumber<float> boundPulse = new();
    private Box coreBox = null!;
    private float currentPulse = 0.5f;

    protected override void Load()
    {
        // Simulated background work executed off the update thread.
        LoadCancellationToken.WaitHandle.WaitOne(25);
        LoadCancellationToken.ThrowIfCancellationRequested();

        Anchor = Anchor.Centre;
        Origin = Anchor.Centre;
        Size = new Vector2(180f, 180f);

        coreBox = new Box
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            RelativeSizeAxes = Axes.Both,
            Size = new Vector2(0.75f, 0.75f),
            Color = Color4.WHITE,
        };

        var cornerDot = new Box
        {
            Anchor = Anchor.TopRight,
            Origin = Anchor.Centre,
            Size = new Vector2(22f, 22f),
            Color = Color4.WHITE,
            Depth = -1f,
        };

        AddRange([coreBox, cornerDot]);

        boundAccent.BindTo(accentColor);
        boundPulse.BindTo(pulseIntensity);
    }

    protected override void LoadComplete()
    {
        logAsyncLoadCompleted(game.Logger);

        boundAccent.BindValueChanged(e => coreBox.Color = e.NewValue, runOnceImmediately: true);
        boundPulse.BindValueChanged(e =>
        {
            currentPulse = e.NewValue;
            Scale = new Vector2(0.75f + (0.45f * currentPulse));
        }, runOnceImmediately: true);
    }

    protected override void Update()
    {
        Rotation = (Rotation + (float)(ElapsedFrameTime * 0.08 * (0.5 + currentPulse))) % 360f;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            boundAccent.UnbindAll();
            boundPulse.UnbindAll();
        }

        base.Dispose(disposing);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "AsyncReactiveOrbital completed async load on update thread.")]
    private static partial void logAsyncLoadCompleted(ILogger logger);
}

internal sealed partial class AsyncReactiveBar(int index) : Box
{
    [Resolved]
    private Bindable<Color4> accentColor = null!;

    [Resolved]
    private BindableNumber<float> pulseIntensity = null!;

    private readonly Bindable<Color4> boundAccent = new();
    private readonly BindableNumber<float> boundPulse = new();

    protected override void Load()
    {
        LoadCancellationToken.ThrowIfCancellationRequested();

        Anchor = Anchor.BottomLeft;
        Origin = Anchor.BottomLeft;
        Position = new Vector2(40f + (index * 36f), -32f);
        Size = new Vector2(24f, 40f);

        boundAccent.BindTo(accentColor);
        boundPulse.BindTo(pulseIntensity);
    }

    protected override void LoadComplete()
    {
        boundAccent.BindValueChanged(e => Color = e.NewValue, runOnceImmediately: true);
        boundPulse.BindValueChanged(e =>
        {
            var factor = 0.5f + (0.5f * ((index + 1) / 5f));
            Height = 36f + (120f * e.NewValue * factor);
        }, runOnceImmediately: true);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            boundAccent.UnbindAll();
            boundPulse.UnbindAll();
        }

        base.Dispose(disposing);
    }
}
