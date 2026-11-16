using System.Numerics;
using kajarlabs.Framework.Graphics;
using kajarlabs.Framework.Graphics.Rendering;
using kajarlabs.Framework.Numerics;
using kajarlabs.Framework.Platform;
using kajarlabs.Framework.Scene;

namespace kajarlabs.Framework.Tests.Scene;

[Collection("SDL")]
public sealed class BoxAndSpriteTests
{
    [Fact]
    public void BoxEmitsBoxDrawNodeAndDrawsWhitePixelWithInheritedColorAndBlending()
    {
        var root = new Container
        {
            Size = new Vector2(400f, 300f),
            Color = new Color4(0.5f, 1f, 0.5f, 0.5f),
        };

        var box = new Box
        {
            Position = new Vector2(20f, 30f),
            Size = new Vector2(100f, 50f),
            Origin = Anchor.Centre,
            Rotation = 90f,
            Color = new Color4(1f, 0.5f, 1f, 0.5f),
            Blending = BlendingParameters.ADDITIVE,
        };

        root.Add(box);

        var rootNode = Assert.IsType<CompositeDrawNode>(root.GenerateDrawNodeSubtree(0));
        _ = Assert.IsType<Box.BoxDrawNode>(Assert.Single(rootNode.Children));

        var renderer = new RecordingRenderer();
        rootNode.Draw(renderer);

        var call = Assert.Single(renderer.DrawCalls);
        Assert.Same(renderer.WhitePixel, call.Texture);
        Assert.Equal(new RectangleF(0f, 0f, 1f, 1f), call.TextureCoords);
        Assert.Equal(box.ScreenSpaceDrawQuad, call.Destination);
        Assert.Equal(new Color4(0.5f, 0.5f, 0.5f, 0.25f), call.Color);
        Assert.Equal(BlendingParameters.ADDITIVE, renderer.LastBlending);
        Assert.Null(renderer.LastSampler);
    }

    [Fact]
    public void BoxIsOmittedFromDrawNodeTreeWhenAlphaIsZero()
    {
        var root = new Container { Size = new Vector2(200f, 200f) };
        var box = new Box
        {
            Size = new Vector2(50f, 50f),
            Alpha = 0f,
        };

        root.Add(box);

        var rootNode = Assert.IsType<CompositeDrawNode>(root.GenerateDrawNodeSubtree(0));
        Assert.Empty(rootNode.Children);

        box.Alpha = 1f;
        _ = root.GenerateDrawNodeSubtree(0);
        Assert.IsType<Box.BoxDrawNode>(Assert.Single(rootNode.Children));
    }

    [Fact]
    public void SpriteDefaultsToTextureSizeRespectsExplicitAndRelativeAxesAndUpdatesAutoSizeParent()
    {
        using var renderer = new NullRenderer(800, 600);
        using var texture64x32 = renderer.CreateTexture(64, 32);
        using var texture128x48 = renderer.CreateTexture(128, 48);

        var autoSizedParent = new Container
        {
            AutoSizeAxes = Axes.Both,
        };

        var sprite = new Sprite
        {
            Texture = texture64x32,
        };

        autoSizedParent.Add(sprite);

        Assert.Equal(new Vector2(64f, 32f), sprite.DrawSize);
        Assert.Equal(new Vector2(64f, 32f), autoSizedParent.DrawSize);

        // Changing texture dimensions updates default sizing and auto-sized parent.
        sprite.Texture = texture128x48;
        Assert.Equal(new Vector2(128f, 48f), sprite.DrawSize);
        Assert.Equal(new Vector2(128f, 48f), autoSizedParent.DrawSize);

        // Partial explicit axis keeps texture default on the remaining axis.
        sprite.Width = 200f;
        Assert.Equal(new Vector2(200f, 48f), sprite.DrawSize);

        // Explicit Size overrides both axes regardless of texture.
        sprite.Size = new Vector2(30f, 15f);
        Assert.Equal(new Vector2(30f, 15f), sprite.DrawSize);

        // Clearing texture resets default-sized axes to zero.
        sprite.Size = Vector2.Zero;
        sprite.Texture = null;
        Assert.Equal(Vector2.Zero, sprite.DrawSize);
        Assert.Equal(Vector2.Zero, autoSizedParent.DrawSize);

        // RelativeSizeAxes uses parent ChildSize even when Size is zero and Texture is set.
        var fixedParent = new Container { Size = new Vector2(300f, 200f) };
        var relativeSprite = new Sprite
        {
            Texture = texture64x32,
            RelativeSizeAxes = Axes.X,
            Size = new Vector2(0.5f, 0f),
        };
        fixedParent.Add(relativeSprite);
        Assert.Equal(new Vector2(150f, 32f), relativeSprite.DrawSize);
    }

    [Fact]
    public void SpriteDrawNodeSnapshotsTextureUvSubRectSamplerAndSkipsNullTexture()
    {
        using var nullRenderer = new NullRenderer(800, 600);
        using var texture = nullRenderer.CreateTexture(64, 64);

        var sprite = new Sprite
        {
            Origin = Anchor.Centre,
            Position = new Vector2(100f, 80f),
            Color = Color4.YELLOW,
            Alpha = 0.5f,
            Blending = BlendingParameters.PREMULTIPLIED_ALPHA,
            Sampler = TextureSamplerParameters.NEAREST_REPEAT,
            TextureCoords = new RectangleF(0.25f, 0.5f, 0.5f, 0.25f),
        };

        var recorder = new RecordingRenderer();

        // Null texture produces a SpriteDrawNode that safely emits no draw calls.
        var node = Assert.IsType<Sprite.SpriteDrawNode>(sprite.GenerateDrawNodeSubtree(0));
        Assert.Null(node.Texture);
        node.Draw(recorder);
        Assert.Empty(recorder.DrawCalls);

        // Assigning texture invalidates DrawNode and applies all sprite state on next sync.
        sprite.Texture = texture;
        _ = sprite.GenerateDrawNodeSubtree(0);

        Assert.Same(texture, node.Texture);
        Assert.Equal(new RectangleF(0.25f, 0.5f, 0.5f, 0.25f), node.TextureCoords);
        Assert.Equal(TextureSamplerParameters.NEAREST_REPEAT, node.Sampler);
        Assert.Equal(new Vector2(64f, 64f), sprite.DrawSize);

        node.Draw(recorder);

        var call = Assert.Single(recorder.DrawCalls);
        Assert.Same(texture, call.Texture);
        Assert.Equal(sprite.ScreenSpaceDrawQuad, call.Destination);
        Assert.Equal(new RectangleF(0.25f, 0.5f, 0.5f, 0.25f), call.TextureCoords);
        Assert.Equal(Color4.YELLOW.MultiplyAlpha(0.5f), call.Color);
        Assert.Equal(BlendingParameters.PREMULTIPLIED_ALPHA, recorder.LastBlending);
        Assert.Equal(TextureSamplerParameters.NEAREST_REPEAT, recorder.LastSampler);
    }

    [Fact]
    public void HeadlessGameHostBatchesBoxAndSpriteQuadsWithAccurateTelemetry()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"kajarlabs-box-sprite-batch-{Guid.NewGuid():N}");

        try
        {
            using var host = new HeadlessGameHost(new HostOptions
            {
                UserDataPath = tempPath,
                WindowWidth = 800,
                WindowHeight = 600,
                EnableConsoleLogging = false,
                EnableFileLogging = false,
            });

            using var game = new BatchTelemetryGame(expectedQuads: 4);
            host.RunFrames(game, 10);

            // 2 Boxes (WhitePixel, default blending) = 1 draw call
            // 2 Sprites sharing customTexture (default LINEAR_CLAMP & ALPHA) = 1 draw call
            Assert.Equal(2, host.Renderer.LastFrameDrawCallCount);
            Assert.Equal(4, host.Renderer.LastFrameQuadCount);
        }
        finally
        {
            if (Directory.Exists(tempPath))
                Directory.Delete(tempPath, recursive: true);
        }
    }

    [Fact]
    public void HeadlessGameHostSplitsDrawCallsOnBlendingOrSamplerChanges()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"kajarlabs-box-sprite-split-{Guid.NewGuid():N}");

        try
        {
            using var host = new HeadlessGameHost(new HostOptions
            {
                UserDataPath = tempPath,
                WindowWidth = 800,
                WindowHeight = 600,
                EnableConsoleLogging = false,
                EnableFileLogging = false,
            });

            using var game = new StateSplitTelemetryGame();
            host.RunFrames(game, 10);

            // Box 1 (ALPHA, WhitePixel), Box 2 (ADDITIVE, WhitePixel),
            // Sprite 1 (ALPHA, tex, LINEAR_CLAMP), Sprite 2 (ALPHA, tex, NEAREST_CLAMP)
            Assert.Equal(4, host.Renderer.LastFrameDrawCallCount);
            Assert.Equal(4, host.Renderer.LastFrameQuadCount);
        }
        finally
        {
            if (Directory.Exists(tempPath))
                Directory.Delete(tempPath, recursive: true);
        }
    }

    private sealed class BatchTelemetryGame : Game
    {
        private readonly int expectedQuads;
        private ITexture? sharedTexture;
        private int updates;

        public BatchTelemetryGame(int expectedQuads)
        {
            this.expectedQuads = expectedQuads;
        }

        protected override void Load()
        {
            sharedTexture = Renderer.CreateTexture(32, 16);
            sharedTexture.SetData(new byte[32 * 16 * 4]);

            Root.AddRange([
                new Box
                {
                    Position = new Vector2(10f, 10f),
                    Size = new Vector2(40f, 40f),
                    Color = Color4.RED,
                },
                new Box
                {
                    Position = new Vector2(60f, 10f),
                    Size = new Vector2(40f, 40f),
                    Rotation = 30f,
                    Color = Color4.GREEN,
                },
                new Sprite
                {
                    Texture = sharedTexture,
                    Position = new Vector2(120f, 20f),
                },
                new Sprite
                {
                    Texture = sharedTexture,
                    Position = new Vector2(180f, 20f),
                    TextureCoords = new RectangleF(0f, 0f, 0.5f, 0.5f),
                },
            ]);
        }

        protected override void Update()
        {
            updates++;

            if (updates >= 3 && !SpinWait.SpinUntil(() => Renderer.LastFrameQuadCount >= expectedQuads, TimeSpan.FromSeconds(5)))
                throw new TimeoutException("DrawThread did not complete rendering Box/Sprite scene within timeout.");
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                sharedTexture?.Dispose();

            base.Dispose(disposing);
        }
    }

    private sealed class StateSplitTelemetryGame : Game
    {
        private ITexture? texture;
        private int updates;

        protected override void Load()
        {
            texture = Renderer.CreateTexture(16, 16);
            texture.SetData(new byte[16 * 16 * 4]);

            Root.AddRange([
                new Box
                {
                    Size = new Vector2(20f, 20f),
                    Blending = BlendingParameters.ALPHA,
                },
                new Box
                {
                    Position = new Vector2(30f, 0f),
                    Size = new Vector2(20f, 20f),
                    Blending = BlendingParameters.ADDITIVE,
                },
                new Sprite
                {
                    Texture = texture,
                    Position = new Vector2(60f, 0f),
                    Sampler = TextureSamplerParameters.LINEAR_CLAMP,
                },
                new Sprite
                {
                    Texture = texture,
                    Position = new Vector2(90f, 0f),
                    Sampler = TextureSamplerParameters.NEAREST_CLAMP,
                },
            ]);
        }

        protected override void Update()
        {
            updates++;

            if (updates >= 3 && !SpinWait.SpinUntil(() => Renderer.LastFrameQuadCount >= 4, TimeSpan.FromSeconds(5)))
                throw new TimeoutException("DrawThread did not complete rendering Box/Sprite split scene within timeout.");
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                texture?.Dispose();

            base.Dispose(disposing);
        }
    }

    private sealed class RecordingRenderer : IRenderer
    {
        public List<RecordedDrawCall> DrawCalls { get; } = [];

        public BlendingParameters? LastBlending { get; private set; }

        public TextureSamplerParameters? LastSampler { get; private set; }

        public Viewport Viewport => new(800, 600);

        public ITexture WhitePixel { get; } = new StubTexture();

        public int LastFrameDrawCallCount => 0;

        public int LastFrameQuadCount => 0;

        public ITexture CreateTexture(int width, int height) => new StubTexture();

        public void Clear(Color4 color)
        {
        }

        public void SetBlending(BlendingParameters blending) => LastBlending = blending;

        public void SetSampler(TextureSamplerParameters sampler) => LastSampler = sampler;

        public void SetScissor(RectangleI? scissor)
        {
        }

        public void SetProjection(Matrix4x4? projection)
        {
        }

        public void DrawQuad(ITexture texture, RectangleF destination, Color4 color)
            => DrawQuad(texture, Quad.FromRectangle(destination), new RectangleF(0f, 0f, 1f, 1f), color);

        public void DrawQuad(ITexture texture, RectangleF destination, RectangleF textureCoords, Color4 color)
            => DrawQuad(texture, Quad.FromRectangle(destination), textureCoords, color);

        public void DrawQuad(ITexture texture, Quad destination, Color4 color)
            => DrawQuad(texture, destination, new RectangleF(0f, 0f, 1f, 1f), color);

        public void DrawQuad(ITexture texture, Quad destination, RectangleF textureCoords, Color4 color)
            => DrawCalls.Add(new RecordedDrawCall(texture, destination, textureCoords, color));

        public readonly record struct RecordedDrawCall(
            ITexture Texture,
            Quad Destination,
            RectangleF TextureCoords,
            Color4 Color);

        private sealed class StubTexture : ITexture
        {
            public int Width => 1;
            public int Height => 1;

            public void SetData(ReadOnlySpan<byte> rgba8Pixels)
            {
            }

            public void SetData(RectangleI region, ReadOnlySpan<byte> rgba8Pixels)
            {
            }

            public void Dispose()
            {
            }
        }
    }
}
