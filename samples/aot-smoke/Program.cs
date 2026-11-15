using System.Numerics;
using System.Text;
using kajarlabs.Framework;
using kajarlabs.Framework.Graphics;
using kajarlabs.Framework.Graphics.Rendering;
using kajarlabs.Framework.Graphics.Rendering.Vertices;
using kajarlabs.Framework.IO;
using kajarlabs.Framework.Numerics;
using kajarlabs.Framework.Platform;
using kajarlabs.Framework.Threading;
using kajarlabs.Framework.Timing;
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
    headlessHost.RunFrames(headlessGame, 3);

    if (headlessGame.Updates != 3 || headlessHost.Window is not null || headlessGame.Draws == 0 || headlessHost.Renderer.Viewport.Width <= 0)
        throw new InvalidOperationException("HeadlessGameHost failed bounded execution or renderer verification under NativeAOT.");

    using var smokeTex = headlessHost.Renderer.CreateTexture(2, 2);
    smokeTex.SetData(new byte[2 * 2 * 4]);
    var white = headlessHost.Renderer.WhitePixel;

    if (white.Width != 1 || white.Height != 1 || TextureSamplerParameters.LINEAR_CLAMP.Filtering != TextureFilteringMode.Linear)
        throw new InvalidOperationException("Texture or sampler API check failed under NativeAOT.");

    if (headlessHost.Renderer.LastFrameDrawCallCount == 0 || headlessHost.Renderer.LastFrameQuadCount < 2)
        throw new InvalidOperationException("Renderer quad batch telemetry check failed under NativeAOT.");
}

var viewport = new Viewport(new RectangleI(0, 0, 1280, 720));
var projection = GraphicsMath.CreateOrthographicProjection(viewport);
var vertex = new TexturedVertex2D(Vector2.Zero, Vector2.One, Color4.WHITE.WithAlpha(0.5f));
var clip = Vector4.Transform(new Vector4(vertex.Position, 0f, 1f), projection);

if (MathF.Abs(clip.X - (-1f)) > 0.0001f || MathF.Abs(clip.Y - 1f) > 0.0001f || !BlendingParameters.ALPHA.IsEnabled)
    throw new InvalidOperationException("Graphics primitives failed projection check under NativeAOT.");

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
