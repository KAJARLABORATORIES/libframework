using System.Diagnostics;
using kajarlabs.Framework;
using kajarlabs.Framework.Graphics;
using kajarlabs.Framework.Numerics;
using kajarlabs.Framework.Platform;
using Microsoft.Extensions.Logging;

var spriteCount = 10_000;
TimeSpan? autoExitAfter = null;

for (var i = 0; i < args.Length; i++)
{
    if (args[i] == "--count" && i + 1 < args.Length && int.TryParse(args[i + 1], out var parsedCount) && parsedCount > 0)
    {
        spriteCount = parsedCount;
        i++;
    }
    else if (args[i] == "--duration" && i + 1 < args.Length && double.TryParse(args[i + 1], out var parsedSeconds) && parsedSeconds > 0)
    {
        autoExitAfter = TimeSpan.FromSeconds(parsedSeconds);
        i++;
    }
}

using var host = new GameHost(new HostOptions
{
    Organization = "kajarlabs",
    Application = "SpriteBatchBenchmarkSample",
    WindowTitle = $"kajarlabs.Framework - Sprite Batch Benchmark ({spriteCount:N0} sprites)",
});

ConsoleCancelEventHandler onCancelKeyPress = (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    host.Exit();
};

Console.CancelKeyPress += onCancelKeyPress;

try
{
    using var game = new SpriteBatchBenchmarkGame(spriteCount, autoExitAfter);
    host.Run(game);
}
finally
{
    Console.CancelKeyPress -= onCancelKeyPress;
}

internal sealed partial class SpriteBatchBenchmarkGame : Game
{
    private const int texture_size = 32;

    private readonly int spriteCount;
    private readonly TimeSpan? autoExitAfter;
    private readonly SpriteSimulationState[] simulationSprites;
    private SpriteRenderData[] updateBuffer;
    private SpriteRenderData[] readyBuffer;
    private SpriteRenderData[] drawBuffer;

    private readonly Stopwatch updateClock = new();
    private readonly Stopwatch runClock = new();
    private readonly Stopwatch drawTelemetryClock = new();

    private ITexture? spriteTexture;
    private double lastUpdateSeconds;
    private int drawFramesSinceReport;
    private bool exitRequested;

    public SpriteBatchBenchmarkGame(int spriteCount, TimeSpan? autoExitAfter)
    {
        this.spriteCount = spriteCount;
        this.autoExitAfter = autoExitAfter;

        simulationSprites = new SpriteSimulationState[spriteCount];
        updateBuffer = new SpriteRenderData[spriteCount];
        readyBuffer = new SpriteRenderData[spriteCount];
        drawBuffer = new SpriteRenderData[spriteCount];
    }

    protected override void Load()
    {
        var pixels = createProceduralSpritePixels(texture_size);
        spriteTexture = Renderer.CreateTexture(texture_size, texture_size);
        spriteTexture.SetData(pixels);

        var viewport = Renderer.Viewport;
        var boundsWidth = viewport.Width > 0f ? viewport.Width : HostOptions.DEFAULT_WINDOW_WIDTH;
        var boundsHeight = viewport.Height > 0f ? viewport.Height : HostOptions.DEFAULT_WINDOW_HEIGHT;
        var rng = new Random(42);

        for (var i = 0; i < spriteCount; i++)
        {
            var size = 14f + (float)(rng.NextDouble() * 18.0);
            var maxX = MathF.Max(1f, boundsWidth - size);
            var maxY = MathF.Max(1f, boundsHeight - size);
            var x = (float)(rng.NextDouble() * maxX);
            var y = (float)(rng.NextDouble() * maxY);
            var angle = rng.NextDouble() * Math.PI * 2.0;
            var speed = 90f + (float)(rng.NextDouble() * 180.0);
            var vx = MathF.Cos((float)angle) * speed;
            var vy = MathF.Sin((float)angle) * speed;
            var color = colorFromHue((float)(i % 360));

            simulationSprites[i] = new SpriteSimulationState(x, y, vx, vy, size, color);
            var renderItem = new SpriteRenderData(x, y, size, color);
            updateBuffer[i] = renderItem;
            readyBuffer[i] = renderItem;
            drawBuffer[i] = renderItem;
        }

        updateClock.Start();
        runClock.Start();
        drawTelemetryClock.Start();

        logBenchmarkStarted(Logger, spriteCount, boundsWidth, boundsHeight);
    }

    protected override void Update()
    {
        if (autoExitAfter is { } limit && !exitRequested && runClock.Elapsed >= limit)
        {
            exitRequested = true;
            Exit();
            return;
        }

        var nowSeconds = updateClock.Elapsed.TotalSeconds;
        var dt = (float)Math.Clamp(nowSeconds - lastUpdateSeconds, 0.0, 0.05);
        lastUpdateSeconds = nowSeconds;

        var viewport = Renderer.Viewport;
        var boundsWidth = viewport.Width > 0f ? viewport.Width : HostOptions.DEFAULT_WINDOW_WIDTH;
        var boundsHeight = viewport.Height > 0f ? viewport.Height : HostOptions.DEFAULT_WINDOW_HEIGHT;

        for (var i = 0; i < spriteCount; i++)
        {
            ref var sprite = ref simulationSprites[i];
            var x = sprite.X + (sprite.VelocityX * dt);
            var y = sprite.Y + (sprite.VelocityY * dt);
            var maxX = MathF.Max(0f, boundsWidth - sprite.Size);
            var maxY = MathF.Max(0f, boundsHeight - sprite.Size);

            if (x < 0f)
            {
                x = 0f;
                sprite.VelocityX = MathF.Abs(sprite.VelocityX);
            }
            else if (x > maxX)
            {
                x = maxX;
                sprite.VelocityX = -MathF.Abs(sprite.VelocityX);
            }

            if (y < 0f)
            {
                y = 0f;
                sprite.VelocityY = MathF.Abs(sprite.VelocityY);
            }
            else if (y > maxY)
            {
                y = maxY;
                sprite.VelocityY = -MathF.Abs(sprite.VelocityY);
            }

            sprite.X = x;
            sprite.Y = y;
            updateBuffer[i] = new SpriteRenderData(x, y, sprite.Size, sprite.Color);
        }

        updateBuffer = Interlocked.Exchange(ref readyBuffer, updateBuffer);
    }

    protected override void Draw()
    {
        var texture = spriteTexture;

        if (texture is null)
            return;

        drawBuffer = Interlocked.Exchange(ref readyBuffer, drawBuffer);

        Renderer.Clear(new Color4(0.06f, 0.08f, 0.13f, 1f));
        Renderer.SetBlending(BlendingParameters.ALPHA);
        Renderer.SetSampler(TextureSamplerParameters.LINEAR_CLAMP);

        var sprites = drawBuffer;

        for (var i = 0; i < sprites.Length; i++)
        {
            var s = sprites[i];
            Renderer.DrawQuad(texture, new RectangleF(s.X, s.Y, s.Size, s.Size), s.Color);
        }

        drawFramesSinceReport++;
        var elapsedSeconds = drawTelemetryClock.Elapsed.TotalSeconds;

        if (elapsedSeconds >= 1.0)
        {
            var fps = drawFramesSinceReport / elapsedSeconds;
            var drawCalls = Renderer.LastFrameDrawCallCount;
            var quads = Renderer.LastFrameQuadCount;
            var viewport = Renderer.Viewport;

            drawFramesSinceReport = 0;
            drawTelemetryClock.Restart();

            if (Window is { } window)
                window.Title = $"kajarlabs.Framework - Sprite Batch Benchmark | {quads:N0} sprites | {fps:F1} FPS | {drawCalls} draw calls | {(int)viewport.Width}x{(int)viewport.Height}";

            logBenchmarkTelemetry(Logger, quads, fps, drawCalls, (int)viewport.Width, (int)viewport.Height);
        }
    }

    protected override void Exiting()
    {
        logBenchmarkStopping(Logger);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            spriteTexture?.Dispose();
            spriteTexture = null;
        }

        base.Dispose(disposing);
    }

    private static byte[] createProceduralSpritePixels(int size)
    {
        var pixels = new byte[size * size * 4];
        var half = (size - 1) * 0.5f;
        var radius = size * 0.45f;

        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var dx = (x - half) / radius;
                var dy = (y - half) / radius;
                var dist = MathF.Sqrt((dx * dx) + (dy * dy));
                var alpha = Math.Clamp(1f - dist, 0f, 1f);
                alpha = MathF.Pow(alpha, 0.65f);

                // Subtle bright specular core + soft outer edge
                var core = MathF.Max(0f, 1f - (dist * 1.4f));
                var channel = (byte)Math.Clamp((int)((0.75f + (0.25f * core)) * 255f), 0, 255);
                var offset = ((y * size) + x) * 4;

                pixels[offset + 0] = channel;
                pixels[offset + 1] = channel;
                pixels[offset + 2] = 255;
                pixels[offset + 3] = (byte)(alpha * 255f);
            }
        }

        return pixels;
    }

    private static Color4 colorFromHue(float hueDegrees)
    {
        var h = (hueDegrees % 360f) / 60f;
        var sector = (int)MathF.Floor(h);
        var f = h - sector;

        const float v = 0.95f;
        const float p = 0.25f;
        var q = v * (1f - (0.73f * f));
        var t = v * (1f - (0.73f * (1f - f)));

        return sector switch
        {
            0 => new Color4(v, t, p, 0.9f),
            1 => new Color4(q, v, p, 0.9f),
            2 => new Color4(p, v, t, 0.9f),
            3 => new Color4(p, q, v, 0.9f),
            4 => new Color4(t, p, v, 0.9f),
            _ => new Color4(v, p, q, 0.9f),
        };
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Sprite batch benchmark started with {SpriteCount} sprites at {ViewportWidth}x{ViewportHeight}")]
    private static partial void logBenchmarkStarted(ILogger logger, int spriteCount, float viewportWidth, float viewportHeight);

    [LoggerMessage(Level = LogLevel.Information, Message = "Benchmark telemetry: {QuadCount} quads | {Fps:F1} FPS | {DrawCalls} draw calls | {ViewportWidth}x{ViewportHeight}")]
    private static partial void logBenchmarkTelemetry(ILogger logger, int quadCount, double fps, int drawCalls, int viewportWidth, int viewportHeight);

    [LoggerMessage(Level = LogLevel.Information, Message = "Closing sprite batch benchmark sample")]
    private static partial void logBenchmarkStopping(ILogger logger);

    private struct SpriteSimulationState(float x, float y, float vx, float vy, float size, Color4 color)
    {
        public float X = x;
        public float Y = y;
        public float VelocityX = vx;
        public float VelocityY = vy;
        public float Size = size;
        public Color4 Color = color;
    }

    private readonly record struct SpriteRenderData(float X, float Y, float Size, Color4 Color);
}
