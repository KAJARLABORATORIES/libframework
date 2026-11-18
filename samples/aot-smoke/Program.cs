using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using kajarlabs.Framework;
using kajarlabs.Framework.Bindables;
using kajarlabs.Framework.DependencyInjection;
using kajarlabs.Framework.Graphics;
using kajarlabs.Framework.Graphics.Rendering;
using kajarlabs.Framework.Graphics.Rendering.Vertices;
using kajarlabs.Framework.Input;
using kajarlabs.Framework.IO;
using kajarlabs.Framework.Numerics;
using kajarlabs.Framework.Platform;
using kajarlabs.Framework.Scene;
using kajarlabs.Framework.Threading;
using kajarlabs.Framework.Timing;
using Microsoft.Extensions.Logging;
using SDL3;

SDL.SetHint("SDL_VIDEO_DRIVER", "dummy");

if (!SDL.Init(SDL.InitFlags.Video))
{
    Console.Error.WriteLine($"SDL_Init failed: {SDL.GetError()}");
    return 1;
}

try
{
    var version = SDL.GetVersion();
    Console.WriteLine($"SDL {version / 1_000_000}.{version / 1_000 % 1_000}.{version % 1_000} initialized under NativeAOT.");

    var tempRoot = Path.Combine(Path.GetTempPath(), $"kajarlabs-aot-smoke-{Guid.NewGuid():N}");

    try
    {
        var storage = new FileSystemStorage(tempRoot);
        var probeFile = "probe.txt";
        var payload = "aot-smoke";

        using (var writeStream = storage.GetStream(probeFile, FileAccess.Write, FileMode.Create))
        {
            var bytes = Encoding.UTF8.GetBytes(payload);
            writeStream.Write(bytes, 0, bytes.Length);
        }

        if (!storage.Exists(probeFile))
            throw new InvalidOperationException("FileSystemStorage failed to persist smoke probe file.");

        using (var reader = new StreamReader(storage.GetStream(probeFile, FileAccess.Read, FileMode.Open), Encoding.UTF8))
        {
            if (!string.Equals(reader.ReadToEnd(), payload, StringComparison.Ordinal))
                throw new InvalidOperationException("FileSystemStorage returned unexpected file content.");
        }

        storage.Delete(probeFile);
    }
    finally
    {
        if (Directory.Exists(tempRoot))
            Directory.Delete(tempRoot, recursive: true);
    }

    var stopwatch = new StopwatchClock(start: true);
    var framedClock = new FramedClock(stopwatch);
    framedClock.ProcessFrame();

    var localScheduler = new Scheduler(framedClock);
    var localExecuted = false;
    localScheduler.Add(() => localExecuted = true);
    localScheduler.Update();

    if (!localExecuted)
        throw new InvalidOperationException("Scheduler failed to execute scheduled task.");

    using var runner = new GameThreadRunner();
    using var threadExecuted = new ManualResetEventSlim(false);
    runner.Start();
    runner.Update.Scheduler.Add(() => threadExecuted.Set());

    if (!threadExecuted.Wait(TimeSpan.FromSeconds(5)))
        throw new TimeoutException("GameThreadRunner.Update failed to execute scheduled task within timeout.");

    runner.Stop();

    Console.WriteLine("kajarlabs.Framework subsystems verified under NativeAOT.");
}
finally
{
    SDL.Quit();
}

var hostTempRoot = Path.Combine(Path.GetTempPath(), $"kajarlabs-aot-host-{Guid.NewGuid():N}");

try
{
    using var host = new DummySmokeHost(new HostOptions
    {
        Organization = "kajarlabs",
        Application = "AotSmoke",
        UserDataPath = hostTempRoot,
        EnableConsoleLogging = false,
        EnableFileLogging = true,
    });

    var game = new SmokeGame();
    host.Run(game);

    if (!game.Loaded || !game.SawWindow || !game.SawRenderer)
        throw new InvalidOperationException("GameHost failed to initialize Game, Window, and Renderer under NativeAOT.");
}
finally
{
    if (Directory.Exists(hostTempRoot))
        Directory.Delete(hostTempRoot, recursive: true);
}

using (var headlessHost = new HeadlessGameHost())
{
    var headlessGame = new HeadlessSmokeGame();
    headlessHost.InjectWindowFocus(true);
    headlessHost.InjectMouseMove(new Vector2(20f, 30f), new Vector2(5f, 5f));
    headlessHost.InjectMouseDown(MouseButton.Left, new Vector2(20f, 30f));
    headlessHost.InjectMouseUp(MouseButton.Left, new Vector2(20f, 30f));
    headlessHost.InjectMouseScroll(new Vector2(0f, 1f), new Vector2(20f, 30f));
    headlessHost.InjectKeyDown(Key.A, KeyModifiers.Shift);
    headlessHost.InjectTextInput("A");
    headlessHost.InjectKeyUp(Key.A, KeyModifiers.Shift);
    headlessHost.InjectWindowMouseLeave();
    headlessHost.InjectWindowFocus(false);
    headlessHost.RunFrames(headlessGame, 3);

    if (headlessGame.Updates != 3 || headlessHost.Window is not null || headlessGame.Draws == 0 || headlessHost.Renderer.Viewport.Width <= 0)
        throw new InvalidOperationException("HeadlessGameHost failed bounded execution or renderer verification under NativeAOT.");

    using var smokeTex = headlessHost.Renderer.CreateTexture(2, 2);
    smokeTex.SetData(new byte[2 * 2 * 4]);
    var white = headlessHost.Renderer.WhitePixel;

    if (white.Width != 1 || white.Height != 1 || TextureSamplerParameters.LINEAR_CLAMP.Filtering != TextureFilteringMode.Linear)
        throw new InvalidOperationException("Texture or sampler API check failed under NativeAOT.");

    if (headlessHost.Renderer.LastFrameDrawCallCount == 0 || headlessHost.Renderer.LastFrameQuadCount < 3)
        throw new InvalidOperationException("Renderer quad batch telemetry check failed under NativeAOT.");
}

var viewport = new Viewport(new RectangleI(0, 0, 1280, 720));
var projection = GraphicsMath.CreateOrthographicProjection(viewport);
var vertex = new TexturedVertex2D(Vector2.Zero, Vector2.One, Color4.WHITE.WithAlpha(0.5f));
var clip = Vector4.Transform(new Vector4(vertex.Position, 0f, 1f), projection);

if (MathF.Abs(clip.X - (-1f)) > 0.0001f || MathF.Abs(clip.Y - 1f) > 0.0001f || !BlendingParameters.ALPHA.IsEnabled)
    throw new InvalidOperationException("Graphics primitives failed projection check under NativeAOT.");

var quadTransform = Matrix3x2Extensions.CreateLocalTransform(new Vector2(10f, 20f), Vector2.One, Vector2.Zero, 90f, Vector2.Zero);
var transformedQuad = new Quad(0f, 0f, 20f, 10f).Transformed(quadTransform);
var insetRect = MarginPadding.All(2f).Deflate(transformedQuad.AABB);

if (!transformedQuad.Contains(5f, 25f)
    || !quadTransform.TryInvert(out _)
    || insetRect.Width <= 0f
    || Anchor.Centre.GetRelativeOffset() != new Vector2(0.5f, 0.5f)
    || !Axes.Both.HasFlag(Axes.X))
{
    throw new InvalidOperationException("Scene geometry primitives check failed under NativeAOT.");
}

using (var smokeDrawable = new Drawable
{
    Position = new Vector2(50f, 40f),
    Size = new Vector2(80f, 30f),
    Origin = Anchor.Centre,
    Rotation = 45f,
    Color = Color4.CYAN,
    Alpha = 0.5f,
})
{
    smokeDrawable.Clock = new FramedClock(new StopwatchClock(start: true));
    var scheduledCallbackRan = false;
    smokeDrawable.Schedule(() => scheduledCallbackRan = true);
    var screenCenter = smokeDrawable.ToScreenSpace(new Vector2(40f, 15f));

    if (smokeDrawable.LoadState != LoadState.NotLoaded
        || !smokeDrawable.Contains(screenCenter)
        || !smokeDrawable.UpdateSubTree()
        || !scheduledCallbackRan
        || smokeDrawable.DrawColorInfo.Color.A <= 0f)
    {
        throw new InvalidOperationException("Drawable scene base check failed under NativeAOT.");
    }
}

using (var sceneHost = new HeadlessGameHost())
{
    using var sceneGame = new SceneBoxSpriteSmokeGame();
    sceneHost.RunFrames(sceneGame, 8);

    if (sceneGame.SpriteDrawSize != new Vector2(16f, 8f)
        || sceneGame.ResizedChildDrawSize != new Vector2(470f, 260f)
        || sceneGame.AutoSizedClusterDrawSize != new Vector2(84f, 44f)
        || sceneHost.Renderer.LastFrameDrawCallCount < 2
        || sceneHost.Renderer.LastFrameQuadCount != 4)
    {
        throw new InvalidOperationException("Nested Container, Box, and Sprite scene check failed under NativeAOT.");
    }
}

using (var diGame = new SmokeDiGame())
using (var diDrawable = new DerivedSmokeDiDrawable())
{
    var rootDeps = new DependencyCollection();
    diGame.RunSmokeDi(rootDeps);

    var childDeps = new DependencyCollection(rootDeps);
    diDrawable.RunSmokeDi(childDeps);

    if (!ReferenceEquals(diGame.Service, diDrawable.ReadResolvedService())
        || diDrawable.ReadResolvedToken() != "base-token"
        || diDrawable.MissingOptional is not null
        || !ReferenceEquals(diDrawable, childDeps.Get<DerivedSmokeDiDrawable>()))
    {
        throw new InvalidOperationException("Source-generated DI check failed under NativeAOT.");
    }
}

{
    var sourceText = new Bindable<string>("initial");
    var boundText = sourceText.GetBoundCopy();
    var observedText = string.Empty;
    boundText.BindValueChanged(e => observedText = e.NewValue, runOnceImmediately: true);
    sourceText.Value = "updated";

    var toggle = new BindableBool();
    var boundToggle = toggle.GetBoundCopy();
    toggle.Toggle();

    var number = new BindableNumber<float>(1.0f)
    {
        MinValue = 0.5f,
        MaxValue = 2.0f,
        Precision = 0.25f,
    };
    var boundNumber = new BindableNumber<float>();
    boundNumber.BindTo(number);
    number.Value = 3.2f;
    number.Value = 1.37f;
    number.Disabled = true;

    if (observedText != "updated"
        || !boundToggle.Value
        || MathF.Abs(boundNumber.Value - 1.25f) > 0.0001f
        || !boundNumber.Disabled)
    {
        throw new InvalidOperationException("Bindable reactive primitives check failed under NativeAOT.");
    }

    boundText.UnbindAll();
    boundToggle.UnbindAll();
    boundNumber.UnbindAll();
}

using (var phase4Host = new HeadlessGameHost())
{
    using var phase4Game = new Phase4SmokeGame();
    phase4Host.RunFrames(phase4Game, 500);

    if (phase4Game.AsyncChildTask is not { IsCompletedSuccessfully: true }
        || phase4Game.AsyncBatchTask is not { IsCompletedSuccessfully: true }
        || phase4Game.CancelledTask is not { IsCanceled: true }
        || !phase4Game.CancelledChild.IsDisposed
        || !ReferenceEquals(phase4Host, phase4Game.ResolvedHost)
        || !ReferenceEquals(phase4Host, phase4Game.AsyncChild.ResolvedHost)
        || phase4Game.AsyncChild.ResolvedWindow is not null
        || phase4Game.SyncChild.LoadState != LoadState.Loaded
        || phase4Game.AsyncChild.LoadState != LoadState.Loaded
        || phase4Game.AsyncChild.Color != Color4.GREEN
        || phase4Game.AsyncChild.DrawSize != new Vector2(125f, 125f)
        || phase4Game.BatchChildren.Any(c => c.LoadState != LoadState.Loaded))
    {
        throw new InvalidOperationException("Phase 4 DI, Bindable, and sync/async load lifecycle check failed under NativeAOT.");
    }
}

{
    if (RuntimeHelpers.IsReferenceOrContainsReferences<KeyboardState>()
        || RuntimeHelpers.IsReferenceOrContainsReferences<MouseState>()
        || RuntimeHelpers.IsReferenceOrContainsReferences<InputState>())
    {
        throw new InvalidOperationException("InputState snapshots must remain unmanaged-compatible value types under NativeAOT.");
    }

    var kb = KeyboardState.EMPTY.WithKeyDown(Key.ShiftLeft).WithKeyDown(Key.A);
    var mouse = MouseState.EMPTY
        .WithPosition(new Vector2(20f, 30f))
        .WithPosition(new Vector2(25f, 40f))
        .WithButtonDown(MouseButton.Left)
        .WithScrollDelta(new Vector2(0f, 1f));
    var inputState = new InputState(kb, mouse, windowHasFocus: true);

    using var target = new Drawable
    {
        Position = new Vector2(10f, 10f),
        Size = new Vector2(100f, 100f),
    };

    var click = new ClickEvent(inputState, MouseButton.Left, new Vector2(20f, 30f), target);
    var keyDown = new KeyDownEvent(inputState, Key.A, repeat: false, target);
    var textInput = new TextInputEvent(inputState, "A", target);
    var focus = new FocusEvent(inputState, previousFocused: null, target: target);

    if (!Key.ShiftLeft.IsModifier
        || KeyModifiers.FromKey(Key.ShiftLeft) != KeyModifiers.Shift
        || !inputState.Modifiers.HasShift
        || !kb.IsKeyDown(Key.A)
        || !mouse.IsButtonJustPressed(MouseButton.Left)
        || click.MouseDownPosition != new Vector2(10f, 20f)
        || keyDown.Key != Key.A
        || textInput.Text != "A"
        || !ReferenceEquals(focus.Target, target))
    {
        throw new InvalidOperationException("Input primitives and UIEvent check failed under NativeAOT.");
    }

    using var maskedRoot = new Container
    {
        Size = new Vector2(50f, 50f),
        Masking = true,
    };
    using var inputChild = new SmokeInputDrawable
    {
        Size = new Vector2(100f, 50f),
    };
    maskedRoot.Add(inputChild);

    var posQueue = new List<Drawable>();
    maskedRoot.BuildPositionalInputQueue(new Vector2(25f, 25f), posQueue);
    var clippedQueue = new List<Drawable>();
    maskedRoot.BuildPositionalInputQueue(new Vector2(75f, 25f), clippedQueue);
    var nonPosQueue = new List<Drawable>();
    maskedRoot.BuildNonPositionalInputQueue(nonPosQueue);

    if (posQueue.Count != 1
        || !ReferenceEquals(posQueue[0], inputChild)
        || clippedQueue.Count != 0
        || nonPosQueue.Count != 1
        || !ReferenceEquals(nonPosQueue[0], inputChild))
    {
        throw new InvalidOperationException("Drawable input traversal and masking check failed under NativeAOT.");
    }
}

Console.WriteLine("kajarlabs.Framework GameHost verified under NativeAOT.");
return 0;

internal sealed class DummySmokeHost(HostOptions options) : GameHost(options)
{
    protected override void ConfigureSdlVideo() => SDL.SetHint("SDL_VIDEO_DRIVER", "dummy");
}

internal sealed class SmokeGame : Game
{
    public bool Loaded { get; private set; }
    public bool SawWindow { get; private set; }
    public bool SawRenderer { get; private set; }

    protected override void Load()
    {
        Loaded = true;
        SawWindow = Window?.IsCreated == true;
        SawRenderer = Renderer is IRenderer && Renderer.Viewport.Width > 0;
        Exit();
    }
}

internal sealed class HeadlessSmokeGame : Game
{
    private readonly ManualResetEventSlim drawObserved = new(false);
    private int draws;

    public int Updates { get; private set; }
    public int Draws => Volatile.Read(ref draws);

    protected override void Update()
    {
        Updates++;

        if (Updates >= 3 && !drawObserved.Wait(TimeSpan.FromSeconds(5)))
            throw new TimeoutException("DrawThread did not execute within timeout.");
    }

    protected override void Draw()
    {
        Interlocked.Increment(ref draws);
        Renderer.Clear(Color4.BLUE);
        Renderer.SetBlending(BlendingParameters.ALPHA);
        Renderer.SetSampler(TextureSamplerParameters.LINEAR_CLAMP);
        Renderer.SetScissor(new RectangleI(0, 0, 100, 100));
        Renderer.SetProjection(GraphicsMath.CreateOrthographicProjection(Renderer.Viewport));
        Renderer.DrawQuad(Renderer.WhitePixel, new RectangleF(10f, 10f, 32f, 32f), Color4.WHITE);
        Renderer.DrawQuad(Renderer.WhitePixel, new RectangleF(50f, 10f, 16f, 16f), new RectangleF(0f, 0f, 0.5f, 0.5f), Color4.RED);
        Renderer.DrawQuad(Renderer.WhitePixel, new Quad(70f, 10f, 16f, 16f) * Matrix3x2.CreateRotation(0.25f), Color4.GREEN);
        Renderer.SetScissor(null);
        Renderer.SetProjection(null);
        drawObserved.Set();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            drawObserved.Dispose();

        base.Dispose(disposing);
    }
}

internal sealed class SceneBoxSpriteSmokeGame : Game
{
    private ITexture? spriteTexture;
    private Container? maskedContainer;
    private Container? relativeBoxHost;
    private Container<Box>? autoSizedGroup;
    private int updates;
    private int drawPostResize;

    public Vector2 SpriteDrawSize { get; private set; }

    public Vector2 ResizedChildDrawSize => relativeBoxHost?.DrawSize ?? Vector2.Zero;

    public Vector2 AutoSizedClusterDrawSize => autoSizedGroup?.DrawSize ?? Vector2.Zero;

    protected override void Load()
    {
        spriteTexture = Renderer.CreateTexture(16, 8);
        spriteTexture.SetData(new byte[16 * 8 * 4]);

        maskedContainer = new Container
        {
            Anchor = Anchor.CentreLeft,
            Origin = Anchor.CentreLeft,
            RelativeSizeAxes = Axes.Both,
            Size = new Vector2(0.5f, 0.5f),
            Padding = MarginPadding.All(10f),
            Masking = true,
            Rotation = 15f,
        };

        relativeBoxHost = new Container
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            RelativeSizeAxes = Axes.Both,
            RelativePositionAxes = Axes.X,
            Size = new Vector2(0.5f, 0.5f),
            Scale = new Vector2(1.1f, 1.1f),
        };

        var box = new Box
        {
            RelativeSizeAxes = Axes.Both,
            Size = Vector2.One,
            Color = Color4.RED,
            Depth = 1f,
        };

        var sprite = new Sprite
        {
            Texture = spriteTexture,
            Position = new Vector2(50f, 10f),
            TextureCoords = new RectangleF(0f, 0f, 0.5f, 0.5f),
            Sampler = TextureSamplerParameters.LINEAR_CLAMP,
            Depth = -1f,
        };

        SpriteDrawSize = sprite.DrawSize;
        relativeBoxHost.AddRange([box, sprite]);
        maskedContainer.Add(relativeBoxHost);

        autoSizedGroup = new Container<Box>
        {
            Anchor = Anchor.TopRight,
            Origin = Anchor.TopRight,
            AutoSizeAxes = Axes.Both,
            Padding = MarginPadding.All(10f),
        };

        autoSizedGroup.AddRange([
            new Box
            {
                Size = new Vector2(30f, 20f),
                Margin = MarginPadding.All(2f),
                Color = Color4.GREEN,
            },
            new Box
            {
                Position = new Vector2(34f, 0f),
                Size = new Vector2(30f, 24f),
                Color = Color4.CYAN,
            },
        ]);

        Root.AddRange([maskedContainer, autoSizedGroup]);
    }

    protected override void Update()
    {
        updates++;

        if (updates == 3)
            maskedContainer?.Size = new Vector2(0.75f, 0.75f);

        if (updates >= 5 && !SpinWait.SpinUntil(() => Volatile.Read(ref drawPostResize) >= 2, TimeSpan.FromSeconds(5)))
            throw new TimeoutException("DrawThread did not render nested Container/Box/Sprite smoke scene within timeout.");
    }

    protected override void Draw()
    {
        if (Volatile.Read(ref updates) >= 4)
            Interlocked.Increment(ref drawPostResize);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            spriteTexture?.Dispose();

        base.Dispose(disposing);
    }
}

internal interface ISmokeDiService
{
    string Id { get; }
}

internal sealed class SmokeDiService : ISmokeDiService
{
    public string Id => "smoke";
}

internal interface ISmokeOptionalService
{
}

internal sealed partial class SmokeDiGame : Game
{
    [Cached(typeof(ISmokeDiService))]
    public SmokeDiService Service { get; } = new();

    [Resolved]
    private ISmokeDiService? ownService;

    public void RunSmokeDi(DependencyCollection dependencies)
    {
        CacheDependencies(dependencies);
        ResolveDependencies(dependencies);

        if (!ReferenceEquals(Service, ownService))
            throw new InvalidOperationException("Game self-resolved DI check failed under NativeAOT.");
    }
}

internal partial class BaseSmokeDiDrawable : Drawable
{
    [Cached]
    private readonly string baseToken = "base-token";

    [Resolved]
    private ISmokeDiService resolvedService = null!;

    public ISmokeDiService ReadResolvedService() => resolvedService;
}

[Cached]
internal sealed partial class DerivedSmokeDiDrawable : BaseSmokeDiDrawable
{
    [Resolved]
    private string resolvedToken = null!;

    [Resolved]
    private ISmokeOptionalService? missingOptional { get; set; }

    public string ReadResolvedToken() => resolvedToken;

    public ISmokeOptionalService? MissingOptional => missingOptional;

    public void RunSmokeDi(DependencyCollection dependencies)
    {
        CacheDependencies(dependencies);
        ResolveDependencies(dependencies);
    }
}

internal sealed partial class Phase4SmokeGame : Game
{
    private readonly CancellationTokenSource cancelLoadCts = new();
    private int phase;

    [Cached]
    public Bindable<Color4> AccentColor { get; } = new(Color4.CYAN);

    [Cached]
    public BindableNumber<float> ScaleFactor { get; } = new(1.0f)
    {
        MinValue = 0.5f,
        MaxValue = 2.0f,
        Precision = 0.25f,
    };

    [Resolved]
    public GameHost ResolvedHost { get; private set; } = null!;

    [Resolved]
    public IRenderer ResolvedRenderer { get; private set; } = null!;

    [Resolved]
    public Storage ResolvedStorage { get; private set; } = null!;

    [Resolved]
    public ILoggerFactory ResolvedLoggerFactory { get; private set; } = null!;

    [Resolved]
    public GameWindow? ResolvedWindow { get; private set; }

    public Phase4SyncChild SyncChild { get; } = new();

    public Phase4AsyncChild AsyncChild { get; } = new();

    public Phase4AsyncChild[] BatchChildren { get; } = [new(), new()];

    public Phase4AsyncChild CancelledChild { get; } = new();

    public Task<Phase4AsyncChild>? AsyncChildTask { get; private set; }

    public Task<IReadOnlyList<Phase4AsyncChild>>? AsyncBatchTask { get; private set; }

    public Task<Phase4AsyncChild>? CancelledTask { get; private set; }

    protected override void Load()
    {
        Root.Add(SyncChild);
        AsyncChildTask = Root.LoadComponentAsync(AsyncChild, Root.Add);
        AsyncBatchTask = Root.LoadComponentsAsync(BatchChildren, Root.AddRange);

        cancelLoadCts.Cancel();
        CancelledTask = Root.LoadComponentAsync(CancelledChild, Root.Add, cancelLoadCts.Token);
    }

    protected override void Update()
    {
        if (phase == 0)
        {
            if (AsyncChildTask is { IsCompleted: true }
                && AsyncBatchTask is { IsCompleted: true }
                && AsyncChild.LoadState == LoadState.Loaded
                && BatchChildren.All(c => c.LoadState == LoadState.Loaded))
            {
                AccentColor.Value = Color4.GREEN;
                ScaleFactor.Value = 1.37f;
                phase = 1;
            }
            else
            {
                Thread.Sleep(1);
            }
        }
        else
        {
            Exit();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            cancelLoadCts.Dispose();
            AccentColor.UnbindAll();
            ScaleFactor.UnbindAll();
        }

        base.Dispose(disposing);
    }
}

internal sealed partial class Phase4SyncChild : Container
{
    [Resolved]
    public Game ResolvedGame { get; private set; } = null!;

    [Resolved]
    private Bindable<Color4> accentColor = null!;

    private readonly Bindable<Color4> boundAccent = new();

    public Color4 ObservedColor { get; private set; }

    protected override void Load() => boundAccent.BindTo(accentColor);

    protected override void LoadComplete()
        => boundAccent.BindValueChanged(e => ObservedColor = e.NewValue, runOnceImmediately: true);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            boundAccent.UnbindAll();

        base.Dispose(disposing);
    }
}

internal sealed partial class Phase4AsyncChild : Box
{
    [Resolved]
    public GameHost ResolvedHost { get; private set; } = null!;

    [Resolved]
    public GameWindow? ResolvedWindow { get; private set; }

    [Resolved]
    private Bindable<Color4> accentColor = null!;

    [Resolved]
    private BindableNumber<float> scaleFactor = null!;

    private readonly Bindable<Color4> boundAccent = new();
    private readonly BindableNumber<float> boundScale = new();

    protected override void Load()
    {
        boundAccent.BindTo(accentColor);
        boundScale.BindTo(scaleFactor);
    }

    protected override void LoadComplete()
    {
        boundAccent.BindValueChanged(e => Color = e.NewValue, runOnceImmediately: true);
        boundScale.BindValueChanged(e => Size = new Vector2(100f * e.NewValue, 100f * e.NewValue), runOnceImmediately: true);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            boundAccent.UnbindAll();
            boundScale.UnbindAll();
        }

        base.Dispose(disposing);
    }
}

internal sealed class SmokeInputDrawable : Drawable
{
    public override bool AcceptsFocus => true;

    public override bool HandleNonPositionalInput => true;
}
