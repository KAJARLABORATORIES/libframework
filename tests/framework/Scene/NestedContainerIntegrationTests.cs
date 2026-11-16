using System.Numerics;
using kajarlabs.Framework.Graphics;
using kajarlabs.Framework.Graphics.Rendering;
using kajarlabs.Framework.Numerics;
using kajarlabs.Framework.Platform;
using kajarlabs.Framework.Scene;

namespace kajarlabs.Framework.Tests.Scene;

[Collection("SDL")]
public sealed class NestedContainerIntegrationTests
{
    private const float tolerance = 0.01f;

    [Fact]
    public void NestedRotatingAndScaledContainersEmitExpectedQuadGeometryThroughHeadlessHost()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"kajarlabs-nested-rot-{Guid.NewGuid():N}");

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

            using var game = new NestedRotationProbeGame();
            host.RunFrames(game, 8);

            // Outer (400x300) centered at (400, 300) rotated +90 deg;
            // Inner (100x60) centered at outer center, scaled (2, 1), rotated -90 deg (net 0 deg).
            // Resulting screen quad: width = 200, height = 60 centered around (400, 300).
            assertQuadClose(
                new Quad(
                    new Vector2(300f, 270f),
                    new Vector2(500f, 270f),
                    new Vector2(500f, 330f),
                    new Vector2(300f, 330f)),
                game.AlignedBox.LastDrawnQuad);

            // Diagonal box (40x20) inside a +90 deg outer and +45 deg child (net +135 deg)
            // centered at (400, 300): half-extents (20, 10) rotated by 135 deg clockwise.
            var rad = float.DegreesToRadians(135f);
            var cos = MathF.Cos(rad);
            var sin = MathF.Sin(rad);
            var center = new Vector2(400f, 300f);

            static Vector2 rotateOffset(Vector2 offset, float c, float s)
                => new((offset.X * c) - (offset.Y * s), (offset.X * s) + (offset.Y * c));

            var expectedDiagonal = new Quad(
                center + rotateOffset(new Vector2(-20f, -10f), cos, sin),
                center + rotateOffset(new Vector2(20f, -10f), cos, sin),
                center + rotateOffset(new Vector2(20f, 10f), cos, sin),
                center + rotateOffset(new Vector2(-20f, 10f), cos, sin));

            assertQuadClose(expectedDiagonal, game.DiagonalSprite.LastDrawnQuad);
            Assert.True(game.AlignedBox.DrawCount >= 1);
            Assert.True(game.DiagonalSprite.DrawCount >= 1);
            Assert.Equal(2, host.Renderer.LastFrameQuadCount);
            Assert.Equal(2, host.Renderer.LastFrameDrawCallCount);

            game.Dispose();
            Assert.True(game.Root.IsDisposed);
            Assert.True(game.AlignedBox.IsDisposed);
            Assert.True(game.DiagonalSprite.IsDisposed);
        }
        finally
        {
            if (Directory.Exists(tempPath))
                Directory.Delete(tempPath, recursive: true);
        }
    }

    [Fact]
    public void ResizeAndAutoSizePropagateAcrossNestedMaskedContainersInHeadlessHost()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"kajarlabs-nested-resize-{Guid.NewGuid():N}");

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

            using var game = new NestedResizeProbeGame(host);
            host.RunFrames(game, 10);

            Assert.Equal(new Vector2(1200f, 900f), game.Root.Size);
            Assert.Equal(new Vector2(600f, 450f), game.MaskedParent.DrawSize);
            Assert.Equal(new Vector2(560f, 410f), game.MaskedParent.ChildSize);
            Assert.Equal(new RectangleI(100, 50, 600, 450), game.MaskedParent.ComputeEffectiveScissor());

            // RelativeChild: 50% x 50% of (560, 410) = (280, 205), anchored BottomRight inside (120..680, 70..480).
            Assert.Equal(new Vector2(280f, 205f), game.RelativeChild.DrawSize);
            assertQuadClose(
                new Quad(
                    new Vector2(400f, 275f),
                    new Vector2(680f, 275f),
                    new Vector2(680f, 480f),
                    new Vector2(400f, 480f)),
                game.RelativeBox.LastDrawnQuad);

            // AutoSizeContainer: Padding(10) around resized DynamicBox (90x50) + Margin(4, 2, 6, 8) = (100x60) -> total (120x80),
            // anchored TopRight at (1200, 0) -> container TopLeft at (1080, 0), child Box at (1094, 12).
            Assert.Equal(new Vector2(120f, 80f), game.AutoSizeContainer.DrawSize);
            assertQuadClose(
                new Quad(
                    new Vector2(1094f, 12f),
                    new Vector2(1184f, 12f),
                    new Vector2(1184f, 62f),
                    new Vector2(1094f, 62f)),
                game.AutoSizeBox.LastDrawnQuad);

            Assert.Equal(3, host.Renderer.LastFrameQuadCount);
            Assert.Equal(3, host.Renderer.LastFrameDrawCallCount);

            game.Dispose();
            Assert.True(game.Root.IsDisposed);
            Assert.True(game.MaskedParent.IsDisposed);
            Assert.True(game.AutoSizeContainer.IsDisposed);
        }
        finally
        {
            if (Directory.Exists(tempPath))
                Directory.Delete(tempPath, recursive: true);
        }
    }

    private static void assertQuadClose(Quad expected, Quad actual)
    {
        assertVectorClose(expected.TopLeft, actual.TopLeft);
        assertVectorClose(expected.TopRight, actual.TopRight);
        assertVectorClose(expected.BottomRight, actual.BottomRight);
        assertVectorClose(expected.BottomLeft, actual.BottomLeft);
    }

    private static void assertVectorClose(Vector2 expected, Vector2 actual)
    {
        Assert.InRange(actual.X, expected.X - tolerance, expected.X + tolerance);
        Assert.InRange(actual.Y, expected.Y - tolerance, expected.Y + tolerance);
    }

    private sealed class NestedRotationProbeGame : Game
    {
        private ITexture? spriteTexture;
        private int updates;

        public ProbeBox AlignedBox { get; } = new()
        {
            RelativeSizeAxes = Axes.Both,
            Size = Vector2.One,
            Color = Color4.CYAN,
        };

        public ProbeSprite DiagonalSprite { get; } = new()
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            Size = new Vector2(40f, 20f),
            Color = Color4.YELLOW,
        };

        protected override void Load()
        {
            spriteTexture = Renderer.CreateTexture(4, 4);
            spriteTexture.SetData(new byte[4 * 4 * 4]);
            DiagonalSprite.Texture = spriteTexture;

            var outer = new Container
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                RelativeSizeAxes = Axes.Both,
                Size = new Vector2(0.5f, 0.5f),
                Rotation = 90f,
            };

            var alignedInner = new Container
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Size = new Vector2(100f, 60f),
                Scale = new Vector2(2f, 1f),
                Rotation = -90f,
            };

            var diagonalInner = new Container
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Size = new Vector2(80f, 80f),
                Rotation = 45f,
            };

            alignedInner.Add(AlignedBox);
            diagonalInner.Add(DiagonalSprite);
            outer.AddRange([alignedInner, diagonalInner]);
            Root.Add(outer);
        }

        protected override void Update()
        {
            updates++;

            if (updates >= 4 && !SpinWait.SpinUntil(() => AlignedBox.DrawCount >= 1 && DiagonalSprite.DrawCount >= 1 && Renderer.LastFrameQuadCount >= 2, TimeSpan.FromSeconds(5)))
                throw new TimeoutException("DrawThread did not render nested rotated containers within timeout.");
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                spriteTexture?.Dispose();

            base.Dispose(disposing);
        }
    }

    private sealed class NestedResizeProbeGame : Game
    {
        private readonly GameHost ownerHost;
        private ITexture? spriteTexture;
        private int updates;
        private int drawPostResize;

        public NestedResizeProbeGame(GameHost ownerHost)
        {
            this.ownerHost = ownerHost;
        }

        public Container MaskedParent { get; } = new()
        {
            Position = new Vector2(100f, 50f),
            RelativeSizeAxes = Axes.Both,
            Size = new Vector2(0.5f, 0.5f),
            Padding = MarginPadding.All(20f),
            Masking = true,
        };

        public Container RelativeChild { get; } = new()
        {
            Anchor = Anchor.BottomRight,
            Origin = Anchor.BottomRight,
            RelativeSizeAxes = Axes.Both,
            Size = new Vector2(0.5f, 0.5f),
        };

        public ProbeBox RelativeBox { get; } = new()
        {
            RelativeSizeAxes = Axes.Both,
            Size = Vector2.One,
            Color = Color4.GREEN,
        };

        public ProbeSprite RelativeSprite { get; } = new()
        {
            Anchor = Anchor.TopLeft,
            Origin = Anchor.TopLeft,
        };

        public Container AutoSizeContainer { get; } = new()
        {
            Anchor = Anchor.TopRight,
            Origin = Anchor.TopRight,
            AutoSizeAxes = Axes.Both,
            Padding = MarginPadding.All(10f),
        };

        public ProbeBox AutoSizeBox { get; } = new()
        {
            Size = new Vector2(40f, 20f),
            Margin = new MarginPadding(4f, 2f, 6f, 8f),
            Color = Color4.MAGENTA,
        };

        protected override void Load()
        {
            spriteTexture = Renderer.CreateTexture(32, 16);
            spriteTexture.SetData(new byte[32 * 16 * 4]);
            RelativeSprite.Texture = spriteTexture;

            RelativeChild.AddRange([RelativeBox, RelativeSprite]);
            MaskedParent.Add(RelativeChild);
            AutoSizeContainer.Add(AutoSizeBox);
            Root.AddRange([MaskedParent, AutoSizeContainer]);
        }

        protected override void Update()
        {
            updates++;

            if (updates == 3)
            {
                ownerHost.ForwardResize(1200, 900);
                AutoSizeBox.Size = new Vector2(90f, 50f);
            }

            if (updates >= 6 && !SpinWait.SpinUntil(() => Volatile.Read(ref drawPostResize) >= 2, TimeSpan.FromSeconds(5)))
                throw new TimeoutException("DrawThread did not observe resized nested containers within timeout.");
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

    private sealed class ProbeBox : Box
    {
        private readonly Lock quadLock = new();

        public int DrawCount
        {
            get
            {
                lock (quadLock)
                    return field;
            }
            private set
            {
                lock (quadLock)
                    field = value;
            }
        }

        public Quad LastDrawnQuad
        {
            get
            {
                lock (quadLock)
                    return field;
            }
            private set
            {
                lock (quadLock)
                    field = value;
            }
        }

        protected override DrawNode CreateDrawNode() => new ProbeBoxDrawNode(this);

        private sealed class ProbeBoxDrawNode : BoxDrawNode
        {
            private readonly ProbeBox owner;

            public ProbeBoxDrawNode(ProbeBox owner)
                : base(owner)
            {
                this.owner = owner;
            }

            public override void Draw(IRenderer renderer)
            {
                base.Draw(renderer);
                owner.LastDrawnQuad = ScreenSpaceDrawQuad;
                owner.DrawCount++;
            }
        }
    }

    private sealed class ProbeSprite : Sprite
    {
        private readonly Lock quadLock = new();

        public int DrawCount
        {
            get
            {
                lock (quadLock)
                    return field;
            }
            private set
            {
                lock (quadLock)
                    field = value;
            }
        }

        public Quad LastDrawnQuad
        {
            get
            {
                lock (quadLock)
                    return field;
            }
            private set
            {
                lock (quadLock)
                    field = value;
            }
        }

        protected override DrawNode CreateDrawNode() => new ProbeSpriteDrawNode(this);

        private sealed class ProbeSpriteDrawNode : SpriteDrawNode
        {
            private readonly ProbeSprite owner;

            public ProbeSpriteDrawNode(ProbeSprite owner)
                : base(owner)
            {
                this.owner = owner;
            }

            public override void Draw(IRenderer renderer)
            {
                base.Draw(renderer);
                owner.LastDrawnQuad = ScreenSpaceDrawQuad;
                owner.DrawCount++;
            }
        }
    }
}
