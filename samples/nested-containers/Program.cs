using System.Numerics;
using kajarlabs.Framework;
using kajarlabs.Framework.Graphics;
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
    Application = "NestedContainersSample",
    WindowTitle = "kajarlabs.Framework - Nested Rotating & Resizing Containers",
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
    using var game = new NestedContainersSampleGame(autoExitAfter);
    host.Run(game);
}
finally
{
    Console.CancelKeyPress -= onCancelKeyPress;
}

internal sealed partial class NestedContainersSampleGame : Game
{
    private const int checker_texture_size = 64;

    private readonly TimeSpan? autoExitAfter;
    private ITexture? checkerTexture;
    private Container? outerRotatingContainer;
    private Container? innerCounterRotatingContainer;
    private Container? autoSizedCluster;
    private Box? pulsingBox;
    private double elapsedRunMs;
    private bool exitRequested;

    public NestedContainersSampleGame(TimeSpan? autoExitAfter)
    {
        this.autoExitAfter = autoExitAfter;
    }

    protected override void Load()
    {
        checkerTexture = Renderer.CreateTexture(checker_texture_size, checker_texture_size);
        checkerTexture.SetData(createCheckerPixels(checker_texture_size));

        var responsiveFrame = new Container
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            RelativeSizeAxes = Axes.Both,
            Size = new Vector2(0.88f, 0.84f),
            Padding = MarginPadding.All(24f),
        };

        responsiveFrame.Add(new Box
        {
            RelativeSizeAxes = Axes.Both,
            Size = Vector2.One,
            Color = new Color4(0.14f, 0.17f, 0.24f, 1f),
            Depth = 10f,
        });

        var maskedViewport = new Container
        {
            Anchor = Anchor.CentreLeft,
            Origin = Anchor.CentreLeft,
            RelativeSizeAxes = Axes.Both,
            Size = new Vector2(0.47f, 0.88f),
            Padding = MarginPadding.All(12f),
            Masking = true,
        };

        maskedViewport.Add(new Box
        {
            RelativeSizeAxes = Axes.Both,
            Size = Vector2.One,
            Color = new Color4(0.09f, 0.11f, 0.16f, 1f),
            Depth = 10f,
        });

        outerRotatingContainer = new Container
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            RelativeSizeAxes = Axes.Both,
            Size = new Vector2(0.82f, 0.82f),
        };

        outerRotatingContainer.AddRange([
            new Box
            {
                RelativeSizeAxes = Axes.Both,
                Size = Vector2.One,
                Color = new Color4(0.24f, 0.38f, 0.62f, 0.5f),
                Depth = 5f,
            },
            createCornerBox(Anchor.TopLeft, Color4.CYAN),
            createCornerBox(Anchor.TopRight, Color4.YELLOW),
            createCornerBox(Anchor.BottomLeft, Color4.MAGENTA),
            createCornerBox(Anchor.BottomRight, Color4.GREEN),
        ]);

        innerCounterRotatingContainer = new Container
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            RelativeSizeAxes = Axes.Both,
            Size = new Vector2(0.54f, 0.54f),
        };

        innerCounterRotatingContainer.AddRange([
            new Box
            {
                RelativeSizeAxes = Axes.Both,
                Size = Vector2.One,
                Color = new Color4(0.95f, 0.45f, 0.2f, 0.4f),
                Depth = 5f,
            },
            new Sprite
            {
                Texture = checkerTexture,
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                RelativeSizeAxes = Axes.Both,
                Size = new Vector2(0.7f, 0.7f),
                Sampler = TextureSamplerParameters.NEAREST_CLAMP,
            },
            new Box
            {
                Anchor = Anchor.TopRight,
                Origin = Anchor.Centre,
                Size = new Vector2(20f, 20f),
                Color = Color4.WHITE,
                Depth = -1f,
            },
        ]);

        outerRotatingContainer.Add(innerCounterRotatingContainer);
        maskedViewport.Add(outerRotatingContainer);

        var rightPanel = new Container
        {
            Anchor = Anchor.CentreRight,
            Origin = Anchor.CentreRight,
            RelativeSizeAxes = Axes.Both,
            Size = new Vector2(0.47f, 0.88f),
            Padding = MarginPadding.All(16f),
            Masking = true,
        };

        rightPanel.Add(new Box
        {
            RelativeSizeAxes = Axes.Both,
            Size = Vector2.One,
            Color = new Color4(0.11f, 0.14f, 0.2f, 1f),
            Depth = 10f,
        });

        autoSizedCluster = new Container
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            AutoSizeAxes = Axes.Both,
            Padding = MarginPadding.All(14f),
        };

        var tiledBadgeGroup = new Container<Box>
        {
            AutoSizeAxes = Axes.Both,
            Position = new Vector2(0f, 76f),
        };

        tiledBadgeGroup.AddRange([
            new Box
            {
                Size = new Vector2(88f, 44f),
                Color = new Color4(0.25f, 0.7f, 0.9f, 0.9f),
                Depth = 1f,
            },
            new Box
            {
                Position = new Vector2(28f, 12f),
                Size = new Vector2(68f, 36f),
                Color = new Color4(0.95f, 0.75f, 0.25f, 0.95f),
                Depth = -1f,
            },
        ]);

        pulsingBox = new Box
        {
            Position = new Vector2(80f, 0f),
            Size = new Vector2(110f, 64f),
            Color = new Color4(0.35f, 0.88f, 0.55f, 0.9f),
            Margin = new MarginPadding(6f, 0f, 0f, 0f),
        };

        autoSizedCluster.AddRange([
            new Sprite
            {
                Texture = checkerTexture,
                TextureCoords = new RectangleF(0f, 0f, 0.5f, 0.5f),
                Sampler = TextureSamplerParameters.NEAREST_CLAMP,
                Size = new Vector2(64f, 64f),
            },
            pulsingBox,
            tiledBadgeGroup,
        ]);

        rightPanel.Add(autoSizedCluster);
        responsiveFrame.AddRange([maskedViewport, rightPanel]);
        Root.Add(responsiveFrame);

        logSampleLoaded(Logger, Renderer.Viewport.Width, Renderer.Viewport.Height);
    }

    protected override void Update()
    {
        var dtMs = Root.ElapsedFrameTime;
        elapsedRunMs += dtMs;
        var seconds = (float)(elapsedRunMs / 1000.0);

        if (outerRotatingContainer is not null)
        {
            outerRotatingContainer.Rotation = (seconds * 28f) % 360f;
            var outerPulse = 1f + (0.12f * MathF.Sin(seconds * 1.6f));
            outerRotatingContainer.Scale = new Vector2(outerPulse, outerPulse);
        }

        innerCounterRotatingContainer?.Rotation = -(seconds * 52f) % 360f;

        autoSizedCluster?.Rotation = MathF.Sin(seconds * 1.1f) * 18f;

        pulsingBox?.Size = new Vector2(
            100f + (45f * MathF.Sin(seconds * 2.2f)),
            64f + (24f * MathF.Cos(seconds * 1.7f)));

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
            checkerTexture?.Dispose();

        base.Dispose(disposing);
    }

    private static Box createCornerBox(Anchor anchor, Color4 color) => new()
    {
        Anchor = anchor,
        Origin = anchor,
        RelativeSizeAxes = Axes.Both,
        Size = new Vector2(0.22f, 0.22f),
        Color = color,
    };

    private static byte[] createCheckerPixels(int size)
    {
        var bytes = new byte[size * size * 4];
        var cellSize = size / 4;

        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var offset = ((y * size) + x) * 4;
                var isLight = (((x / cellSize) + (y / cellSize)) & 1) == 0;

                if (isLight)
                {
                    bytes[offset + 0] = 240;
                    bytes[offset + 1] = 248;
                    bytes[offset + 2] = 255;
                    bytes[offset + 3] = 255;
                }
                else
                {
                    bytes[offset + 0] = 48;
                    bytes[offset + 1] = 110;
                    bytes[offset + 2] = 195;
                    bytes[offset + 3] = 255;
                }
            }
        }

        return bytes;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Nested containers sample loaded (initial viewport: {Width}x{Height}). Resize window to test layout propagation.")]
    private static partial void logSampleLoaded(ILogger logger, float width, float height);
}
