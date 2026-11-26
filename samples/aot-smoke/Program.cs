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
using kajarlabs.Framework.IO.Resources;
using kajarlabs.Framework.Numerics;
using kajarlabs.Framework.Platform;
using kajarlabs.Framework.Scene;
using kajarlabs.Framework.Scene.Transforms;
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

    using var manualInput = new ManualInputManager { Size = new Vector2(200f, 200f) };
    using var passThrough = new PassThroughInputManager { Size = new Vector2(200f, 200f) };
    using var smokeButton = new SmokeInteractiveControl { Size = new Vector2(80f, 80f) };
    passThrough.Add(smokeButton);
    manualInput.Add(passThrough);

    manualInput.SetWindowFocus(true);
    manualInput.Click(MouseButton.Left, new Vector2(40f, 40f));
    manualInput.PressKey(Key.Enter);
    manualInput.InputText("ok");

    if (!smokeButton.IsHovered
        || !smokeButton.HasFocus
        || smokeButton.Clicks != 1
        || smokeButton.Keys != 1
        || smokeButton.TextInputs != 1)
    {
        throw new InvalidOperationException("ManualInputManager / PassThroughInputManager check failed under NativeAOT.");
    }

    using var bindingManual = new ManualInputManager { Size = new Vector2(200f, 200f) };
    using var bindingContainer = new KeyBindingContainer<SmokeBindingAction>(SimultaneousBindingMode.All)
    {
        RelativeSizeAxes = Axes.Both,
        Size = Vector2.One,
        KeyBindings =
        [
            new(new KeyCombination(InputKey.Control, Key.S), SmokeBindingAction.Save),
        ],
    };
    using var bindingHandler = new SmokeBindingHandler();
    bindingContainer.Add(bindingHandler);
    bindingManual.Add(bindingContainer);

    bindingManual.PressKey(Key.ControlLeft);
    bindingManual.PressKey(Key.S);
    bindingManual.ReleaseKey(Key.S);
    bindingManual.ReleaseKey(Key.ControlLeft);

    if (bindingHandler.PressedCount != 1 || bindingHandler.ReleasedCount != 1)
        throw new InvalidOperationException("KeyBindingContainer<TAction> check failed under NativeAOT.");
}

using (var phase5Host = new HeadlessGameHost())
{
    using var phase5Game = new Phase5InputSmokeGame(phase5Host);
    phase5Host.RunFrames(phase5Game, 7);

    if (!ReferenceEquals(phase5Game.InputManager, phase5Game.ResolvedInputManager)
        || !ReferenceEquals(phase5Game.InputManager, phase5Game.RotatedTarget.ResolvedInputManager)
        || phase5Game.RotatedTarget.Clicks != 1
        || phase5Game.ClippedOverflowTarget.Clicks != 0
        || phase5Game.DragTarget.DragStarts != 1
        || phase5Game.DragTarget.DragEnds != 1
        || phase5Game.DragTarget.Scrolls != 1
        || phase5Game.PassThroughTarget.Clicks != 1
        || phase5Game.RotatedTarget.FocusLosses != 1
        || !phase5Game.TextTarget.HasFocus
        || phase5Game.TextTarget.ReceivedText != "aot"
        || phase5Game.ActionHandler.ResetPressed != 1
        || phase5Game.ActionHandler.ResetReleased != 1
        || phase5Game.ActionHandler.TabPressed != 1
        || phase5Game.ActionHandler.TabReleased != 1
        || phase5Game.ActionHandler.ZoomPressed != 1
        || phase5Game.ActionHandler.ZoomReleased != 1)
    {
        throw new InvalidOperationException("Phase 5 end-to-end HeadlessGameHost input pipeline check failed under NativeAOT.");
    }
}

{
    var outCubicValue = Interpolation.ValueAt(50.0, 0f, 100f, 0.0, 100.0, Easing.OutCubic);
    var bezier = CubicBezierEasingFunction.EASE_IN_OUT;
    var bezierValue = Interpolation.ValueAt(25.0, Vector2.Zero, new Vector2(100f, 200f), 0.0, 100.0, bezier);

    if (outCubicValue <= 50f || outCubicValue >= 100f || bezierValue.X <= 0f || bezierValue.Y <= 0f)
        throw new InvalidOperationException("Phase 6 easing or interpolation check failed under NativeAOT.");

    using var phase6Host = new HeadlessGameHost();
    using var phase6Game = new Phase6TransformsSmokeGame();
    phase6Host.RunFrames(phase6Game, 12);

    if (!phase6Game.ChainCompleted
        || !phase6Game.LoopCompleted
        || !phase6Game.AbortSignaled
        || !phase6Game.FinallySignaled
        || !phase6Game.TransientExpiredAndDisposed
        || phase6Game.Hero.Position != new Vector2(120f, 60f)
        || MathF.Abs(phase6Game.Hero.CustomMetric - 100f) > 0.001f
        || MathF.Abs(phase6Game.BoundScale.Value - 2.5f) > 0.001f
        || phase6Game.TransientStage.Children.Count != 0)
    {
        throw new InvalidOperationException("Phase 6 transforms sequence/loop/expire check failed under NativeAOT.");
    }

    Console.WriteLine("kajarlabs.Framework.Scene.Transforms verified under NativeAOT.");
}

{
    var phase7TempRoot = Path.Combine(Path.GetTempPath(), $"kajarlabs-aot-phase7-{Guid.NewGuid():N}");

    try
    {
        var diskStorage = new FileSystemStorage(phase7TempRoot);

        // Write a valid 2x2 24-bit uncompressed BMP to disk for StorageResourceProvider + StbImageNext BMP check.
        byte[] diskBmp =
        [
            0x42, 0x4D, 0x46, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x36, 0x00, 0x00, 0x00,
            0x28, 0x00, 0x00, 0x00, 0x02, 0x00, 0x00, 0x00, 0x02, 0x00, 0x00, 0x00, 0x01, 0x00, 0x18, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x10, 0x00, 0x00, 0x00, 0x13, 0x0B, 0x00, 0x00, 0x13, 0x0B, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            255, 60, 50, 100, 150, 200, 0, 0,
            20, 10, 255, 40, 255, 30, 0, 0,
        ];

        using (var bmpOut = diskStorage.GetStream("Textures/smoke-disk.bmp", FileAccess.Write, FileMode.Create))
            bmpOut.Write(diskBmp, 0, diskBmp.Length);

        // Also override embedded smoke-rgba (2x2 PNG) with a 4x4 JPEG on disk in a separate override probe.
        using (var memBase = new MemoryResourceProvider())
        using (var memOverride = new MemoryResourceProvider())
        using (var composite = new CompositeResourceProvider())
        {
            memBase.Add("Textures/item.png", [1, 2]);
            memOverride.Add("Textures/item.jpg", [9, 8]);
            composite.AddProvider(memBase, leaveOpen: true);
            composite.AddProvider(memOverride, leaveOpen: true);
            composite.AddExtension(".png");
            composite.AddExtension(".jpg");

            using var scoped = new NamespacedResourceProvider(composite, "Textures", leaveOpen: true);
            var overrideBytes = scoped.GetBytes("item");

            if (overrideBytes is null || overrideBytes.Length != 2 || overrideBytes[0] != 9)
                throw new InvalidOperationException("CompositeResourceProvider/NamespacedResourceProvider override check failed under NativeAOT.");

            var escaped = false;

            try
            {
                _ = scoped.Exists("../outside");
            }
            catch (ArgumentException)
            {
                escaped = true;
            }

            if (!escaped)
                throw new InvalidOperationException("NamespacedResourceProvider failed to reject '..' escape under NativeAOT.");
        }

        using var phase7Host = new HeadlessGameHost();
        using var phase7Game = new Phase7ResourcesSmokeGame(diskStorage);
        phase7Host.RunFrames(phase7Game, 500);

        if (!ReferenceEquals(phase7Game.Resources, phase7Game.ResolvedResources)
            || !ReferenceEquals(phase7Game.Resources, phase7Game.ResolvedComposite)
            || !ReferenceEquals(phase7Game.Textures, phase7Game.ResolvedTextures)
            || phase7Game.PngTexture is not { Width: 2, Height: 2 }
            || phase7Game.JpgTexture is not { Width: 4, Height: 4 }
            || phase7Game.BmpTexture is not { Width: 2, Height: 2 }
            || phase7Game.AsyncSprite.LoadState != LoadState.Loaded
            || phase7Game.AsyncSprite.Texture is not { Width: 2, Height: 2 }
            || !ReferenceEquals(phase7Game.PngTexture, phase7Game.Textures.Get("smoke-rgba.png"))
            || !phase7Game.Textures.TryGetCached("smoke-rgba", out _)
            || phase7Host.Renderer.LastFrameQuadCount < 2)
        {
            throw new InvalidOperationException("Phase 7 resource providers, image decoding, and TextureCache check failed under NativeAOT.");
        }
    }
    finally
    {
        if (Directory.Exists(phase7TempRoot))
            Directory.Delete(phase7TempRoot, recursive: true);
    }

    Console.WriteLine("kajarlabs.Framework Phase 7 resources and TextureCache verified under NativeAOT.");
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
    private bool pushedWatchEvents;

    protected override void Load()
    {
        Loaded = true;
        SawWindow = Window?.IsCreated == true;
        SawRenderer = Renderer is IRenderer && Renderer.Viewport.Width > 0;
    }

    protected override void Update()
    {
        if (!pushedWatchEvents)
        {
            pushedWatchEvents = true;

            var resizeEvent = new SDL.Event
            {
                Type = (uint)SDL.EventType.WindowPixelSizeChanged,
                Window = new SDL.WindowEvent
                {
                    Type = SDL.EventType.WindowPixelSizeChanged,
                    WindowID = 1,
                    Data1 = 800,
                    Data2 = 600,
                },
            };
            SDL.PushEvent(ref resizeEvent);

            var exposeEvent = new SDL.Event
            {
                Type = (uint)SDL.EventType.WindowExposed,
                Window = new SDL.WindowEvent
                {
                    Type = SDL.EventType.WindowExposed,
                    WindowID = 1,
                    Data1 = 1,
                },
            };
            SDL.PushEvent(ref exposeEvent);
            return;
        }

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

internal sealed class SmokeInteractiveControl : Drawable
{
    public int Clicks { get; private set; }

    public int Keys { get; private set; }

    public int TextInputs { get; private set; }

    public override bool AcceptsFocus => true;

    protected override bool OnHover(HoverEvent e) => true;

    protected override bool OnMouseDown(MouseDownEvent e) => true;

    protected override bool OnClick(ClickEvent e)
    {
        Clicks++;
        return true;
    }

    protected override bool OnKeyDown(KeyDownEvent e)
    {
        Keys++;
        return true;
    }

    protected override bool OnTextInput(TextInputEvent e)
    {
        TextInputs++;
        return true;
    }
}

internal enum SmokeBindingAction
{
    Save,
}

internal sealed class SmokeBindingHandler : Drawable, IKeyBindingHandler<SmokeBindingAction>
{
    public int PressedCount { get; private set; }

    public int ReleasedCount { get; private set; }

    public bool OnPressed(KeyBindingPressEvent<SmokeBindingAction> e)
    {
        if (e.Action == SmokeBindingAction.Save)
        {
            PressedCount++;
            return true;
        }

        return false;
    }

    public void OnReleased(KeyBindingReleaseEvent<SmokeBindingAction> e)
    {
        if (e.Action == SmokeBindingAction.Save)
            ReleasedCount++;
    }
}

internal enum Phase5SmokeAction
{
    Reset,
    CycleFocus,
    ZoomIn,
}

internal sealed partial class Phase5InputSmokeGame : Game
{
    private readonly HeadlessGameHost headlessHost;
    private int frame;

    public Phase5InputSmokeGame(HeadlessGameHost headlessHost)
    {
        this.headlessHost = headlessHost;
    }

    [Resolved]
    public InputManager ResolvedInputManager { get; private set; } = null!;

    public Phase5SmokeControl RotatedTarget { get; } = new()
    {
        Position = new Vector2(10f, 10f),
        Size = new Vector2(60f, 60f),
    };

    public Phase5SmokeControl ClippedOverflowTarget { get; } = new()
    {
        Position = new Vector2(180f, 10f),
        Size = new Vector2(100f, 60f),
    };

    public Phase5SmokeControl DragTarget { get; } = new()
    {
        Position = new Vector2(260f, 40f),
        Size = new Vector2(100f, 80f),
        AllowDrag = true,
    };

    public Phase5SmokeControl TextTarget { get; } = new()
    {
        Position = new Vector2(260f, 150f),
        Size = new Vector2(120f, 60f),
        RequestsTextInputFlag = true,
    };

    public Phase5SmokeControl PassThroughTarget { get; } = new()
    {
        Position = new Vector2(10f, 10f),
        Size = new Vector2(80f, 60f),
    };

    public Phase5ActionHandler ActionHandler { get; } = new()
    {
        RelativeSizeAxes = Axes.Both,
        Size = Vector2.One,
    };

    protected override void Load()
    {
        var bindings = new KeyBindingContainer<Phase5SmokeAction>(SimultaneousBindingMode.All)
        {
            RelativeSizeAxes = Axes.Both,
            Size = Vector2.One,
            KeyBindings =
            [
                new(new KeyCombination(InputKey.Control, Key.R), Phase5SmokeAction.Reset),
                new(new KeyCombination(Key.Tab), Phase5SmokeAction.CycleFocus),
                new(new KeyCombination(InputKey.Control, InputKey.MouseWheelUp), Phase5SmokeAction.ZoomIn),
            ],
        };

        var maskedPanel = new Container
        {
            Position = new Vector2(20f, 20f),
            Size = new Vector2(200f, 200f),
            Masking = true,
        };

        var rotatedGroup = new Container
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            Size = new Vector2(120f, 120f),
            Rotation = 45f,
        };

        rotatedGroup.Add(RotatedTarget);
        maskedPanel.AddRange([rotatedGroup, ClippedOverflowTarget]);

        var passThrough = new PassThroughInputManager
        {
            Position = new Vector2(400f, 40f),
            Size = new Vector2(120f, 100f),
        };

        passThrough.Add(PassThroughTarget);
        bindings.AddRange([maskedPanel, DragTarget, TextTarget, passThrough, ActionHandler]);
        Root.Add(bindings);
    }

    protected override void Update()
    {
        frame++;

        switch (frame)
        {
            case 1:
            {
                var rotatedCenter = RotatedTarget.ToScreenSpace(new Vector2(30f, 30f));
                headlessHost.InjectWindowFocus(true);
                headlessHost.InjectMouseMove(rotatedCenter);
                headlessHost.InjectMouseDown(MouseButton.Left, rotatedCenter);
                headlessHost.InjectMouseUp(MouseButton.Left, rotatedCenter);

                // Clipped overflow region outside maskedPanel (x=250 is past 20+200=220).
                headlessHost.InjectMouseDown(MouseButton.Left, new Vector2(250f, 50f));
                headlessHost.InjectMouseUp(MouseButton.Left, new Vector2(250f, 50f));
                break;
            }

            case 2:
                // Drag & scroll on DragTarget ([260..360, 40..120]).
                headlessHost.InjectMouseScroll(new Vector2(0f, -1f), new Vector2(300f, 80f));
                headlessHost.InjectMouseDown(MouseButton.Left, new Vector2(300f, 80f));
                headlessHost.InjectMouseMove(new Vector2(330f, 80f));
                break;

            case 3:
                headlessHost.InjectMouseMove(new Vector2(360f, 95f));
                headlessHost.InjectMouseUp(MouseButton.Left, new Vector2(360f, 95f));
                break;

            case 4:
                // Click PassThroughTarget inside nested PassThroughInputManager ([410..490, 50..110]).
                headlessHost.InjectMouseMove(new Vector2(440f, 70f));
                headlessHost.InjectMouseDown(MouseButton.Left, new Vector2(440f, 70f));
                headlessHost.InjectMouseUp(MouseButton.Left, new Vector2(440f, 70f));
                break;

            case 5:
                // Switch focus to TextTarget, send text and exercise key bindings (chord, single, positional scroll).
                InputManager.ChangeFocus(TextTarget);
                headlessHost.InjectTextInput("aot");
                headlessHost.InjectKeyDown(Key.Tab);
                headlessHost.InjectKeyUp(Key.Tab);
                headlessHost.InjectKeyDown(Key.ControlLeft, KeyModifiers.Control);
                headlessHost.InjectKeyDown(Key.R, KeyModifiers.Control);
                headlessHost.InjectMouseScroll(new Vector2(0f, 1f), new Vector2(300f, 180f));
                headlessHost.InjectKeyUp(Key.R, KeyModifiers.Control);
                headlessHost.InjectKeyUp(Key.ControlLeft);
                break;
        }
    }
}

internal sealed partial class Phase5SmokeControl : Drawable
{
    [Resolved]
    public InputManager ResolvedInputManager { get; private set; } = null!;

    public bool AllowDrag { get; init; }

    public bool RequestsTextInputFlag { get; init; }

    public int Clicks { get; private set; }

    public int DragStarts { get; private set; }

    public int DragEnds { get; private set; }

    public int Scrolls { get; private set; }

    public int FocusLosses { get; private set; }

    public string ReceivedText { get; private set; } = string.Empty;

    public override bool AcceptsFocus => true;

    public override bool RequestsTextInput => RequestsTextInputFlag;

    protected override bool OnHover(HoverEvent e) => true;

    protected override bool OnMouseDown(MouseDownEvent e) => e.Button == MouseButton.Left;

    protected override bool OnClick(ClickEvent e)
    {
        Clicks++;
        return true;
    }

    protected override bool OnDragStart(DragStartEvent e)
    {
        if (!AllowDrag || e.Button != MouseButton.Left)
            return false;

        DragStarts++;
        return true;
    }

    protected override bool OnDrag(DragEvent e) => AllowDrag;

    protected override void OnDragEnd(DragEndEvent e) => DragEnds++;

    protected override bool OnScroll(ScrollEvent e)
    {
        if (e.CurrentState.Modifiers.HasControl)
            return false;

        Scrolls++;
        return true;
    }

    protected override void OnFocusLost(FocusLostEvent e) => FocusLosses++;

    protected override bool OnTextInput(TextInputEvent e)
    {
        ReceivedText += e.Text;
        return true;
    }
}

internal sealed class Phase5ActionHandler : Drawable, IKeyBindingHandler<Phase5SmokeAction>
{
    public int ResetPressed { get; private set; }

    public int ResetReleased { get; private set; }

    public int TabPressed { get; private set; }

    public int TabReleased { get; private set; }

    public int ZoomPressed { get; private set; }

    public int ZoomReleased { get; private set; }

    public bool OnPressed(KeyBindingPressEvent<Phase5SmokeAction> e)
    {
        switch (e.Action)
        {
            case Phase5SmokeAction.Reset:
                ResetPressed++;
                return true;

            case Phase5SmokeAction.CycleFocus:
                TabPressed++;
                return true;

            case Phase5SmokeAction.ZoomIn:
                ZoomPressed++;
                return true;

            default:
                return false;
        }
    }

    public void OnReleased(KeyBindingReleaseEvent<Phase5SmokeAction> e)
    {
        switch (e.Action)
        {
            case Phase5SmokeAction.Reset:
                ResetReleased++;
                break;

            case Phase5SmokeAction.CycleFocus:
                TabReleased++;
                break;

            case Phase5SmokeAction.ZoomIn:
                ZoomReleased++;
                break;
        }
    }
}

internal sealed class SmokeAdjustableClock : IAdjustableClock
{
    public double CurrentTime { get; private set; }

    public double Rate { get; set; } = 1.0;

    public bool IsRunning { get; private set; } = true;

    public void Start() => IsRunning = true;

    public void Stop() => IsRunning = false;

    public void Reset() => CurrentTime = 0.0;

    public bool Seek(double position)
    {
        CurrentTime = position;
        return true;
    }

    public void Advance(double deltaMs)
    {
        if (IsRunning)
            CurrentTime += deltaMs * Rate;
    }
}

internal sealed class Phase6SmokeBox : Box
{
    public float CustomMetric { get; set; }
}

internal sealed class Phase6TransformsSmokeGame : Game
{
    private readonly SmokeAdjustableClock manualClock = new();
    private readonly Box loopBox = new() { Size = new Vector2(20f, 20f), Alpha = 0f };
    private readonly Box interruptedBox = new() { Size = new Vector2(20f, 20f), Alpha = 0f };
    private readonly Box transientBox = new() { Size = new Vector2(20f, 20f), Alpha = 1f };

    public Container TransientStage { get; } = new() { Size = new Vector2(400f, 400f) };

    public Phase6SmokeBox Hero { get; } = new() { Size = new Vector2(40f, 40f), Alpha = 0f, Position = Vector2.Zero };

    public Bindable<float> BoundScale { get; } = new(1f);

    public bool ChainCompleted { get; private set; }

    public bool LoopCompleted { get; private set; }

    public bool AbortSignaled { get; private set; }

    public bool FinallySignaled { get; private set; }

    public bool TransientExpiredAndDisposed => transientBox.IsDisposed;

    protected override void Load()
    {
        var stage = new Container
        {
            Clock = new FramedClock(manualClock),
            Size = new Vector2(400f, 400f),
        };

        stage.AddRange([Hero, loopBox, interruptedBox, TransientStage]);
        TransientStage.Add(transientBox);
        Root.Add(stage);

        using (Hero.BeginDelayedSequence(50))
        {
            Hero.FadeIn(100, Easing.OutQuad)
                .Then()
                .MoveTo(new Vector2(120f, 60f), 150, CubicBezierEasingFunction.EASE_IN_OUT)
                .TransformTo(static b => b.CustomMetric, static (b, v) => b.CustomMetric = v, 100f, 150, Easing.OutCubic)
                .TransformBindableTo(BoundScale, 2.5f, 150, Easing.None)
                .OnComplete(_ => ChainCompleted = true);
        }

        loopBox.FadeIn(50)
            .Then()
            .FadeOut(50)
            .Loop(pause: 25, numIterations: 2)
            .OnComplete(_ => LoopCompleted = true);

        interruptedBox.FadeIn(200)
            .OnAbort(_ => AbortSignaled = true)
            .Finally(_ => FinallySignaled = true);

        transientBox.FadeOut(150, Easing.InQuad).Expire();
    }

    protected override void Update()
    {
        manualClock.Advance(50);

        if (Math.Abs(manualClock.CurrentTime - 100.0) < 0.001)
            interruptedBox.FadeOut(50);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            BoundScale.UnbindAll();

        base.Dispose(disposing);
    }
}

internal sealed partial class Phase7ResourcesSmokeGame : Game
{
    private int postAsyncUpdates;
    private int postAsyncDraws;

    public Phase7ResourcesSmokeGame(Storage diskStorage)
    {
        Resources.AddProvider(new AssemblyResourceProvider(
            typeof(Phase7ResourcesSmokeGame).Assembly,
            "kajarlabs.Framework.Samples.AotSmoke.Resources"));
        Resources.AddProvider(new StorageResourceProvider(diskStorage));
    }

    [Resolved]
    public IResourceProvider ResolvedResources { get; private set; } = null!;

    [Resolved]
    public CompositeResourceProvider ResolvedComposite { get; private set; } = null!;

    [Resolved]
    public TextureCache ResolvedTextures { get; private set; } = null!;

    public ITexture? PngTexture { get; private set; }

    public ITexture? JpgTexture { get; private set; }

    public ITexture? BmpTexture { get; private set; }

    public Phase7SyncTextureSprite SyncSprite { get; } = new("smoke-rgba");

    public Phase7AsyncTextureSprite AsyncSprite { get; } = new("smoke-rgba.tga");

    public Task<Phase7AsyncTextureSprite>? AsyncTask { get; private set; }

    protected override void Load()
    {
        PngTexture = Textures.Get("smoke-rgba");
        JpgTexture = Textures.Get("smoke-rgb.jpg");
        BmpTexture = Textures.Get("smoke-disk");

        // Verify caller Dispose() on cached texture is a safe no-op.
        PngTexture?.Dispose();

        Root.Add(SyncSprite);
        AsyncTask = Root.LoadComponentAsync(AsyncSprite, Root.Add);
    }

    protected override void Update()
    {
        if (AsyncTask is { IsCompleted: true } && AsyncSprite.LoadState == LoadState.Loaded)
        {
            var published = Interlocked.Increment(ref postAsyncUpdates);

            if (published >= 2 && Volatile.Read(ref postAsyncDraws) < 2)
            {
                SpinWait.SpinUntil(
                    () => Volatile.Read(ref postAsyncDraws) >= 2,
                    TimeSpan.FromSeconds(5));
            }

            if (Volatile.Read(ref postAsyncDraws) >= 2)
                Exit();

            return;
        }

        Thread.Sleep(1);
    }

    protected override void Draw()
    {
        if (Volatile.Read(ref postAsyncUpdates) >= 2)
            Interlocked.Increment(ref postAsyncDraws);
    }
}

internal sealed partial class Phase7SyncTextureSprite : Sprite
{
    private readonly string textureName;

    [Resolved]
    private TextureCache textures { get; set; } = null!;

    public Phase7SyncTextureSprite(string textureName)
    {
        this.textureName = textureName;
    }

    protected override void Load() => Texture = textures.Get(textureName);
}

internal sealed partial class Phase7AsyncTextureSprite : Sprite
{
    private readonly string textureName;

    [Resolved]
    private TextureCache textures { get; set; } = null!;

    public Phase7AsyncTextureSprite(string textureName)
    {
        this.textureName = textureName;
    }

    protected override void Load() => Texture = textures.GetAsync(textureName).GetAwaiter().GetResult();
}
