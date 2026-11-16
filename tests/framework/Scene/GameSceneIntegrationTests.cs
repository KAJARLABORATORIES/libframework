using System.Numerics;
using kajarlabs.Framework.Graphics.Rendering;
using kajarlabs.Framework.Numerics;
using kajarlabs.Framework.Platform;
using kajarlabs.Framework.Scene;

namespace kajarlabs.Framework.Tests.Scene;

[Collection("SDL")]
public sealed class GameSceneIntegrationTests
{
    [Fact]
    public void HeadlessGameHostDrivesRootSceneAcrossUpdateAndDrawThreadsAndDisposesCleanly()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"kajarlabs-scene-host-{Guid.NewGuid():N}");

        try
        {
            using var host = new HeadlessGameHost(new HostOptions
            {
                UserDataPath = tempPath,
                WindowWidth = 1024,
                WindowHeight = 768,
                EnableConsoleLogging = false,
                EnableFileLogging = false,
            });

            using var game = new SceneProbeGame();
            host.RunFrames(game, 10);

            Assert.Equal(10, game.UpdateCallCount);
            Assert.Equal(10, game.SceneChild.UpdateCount);
            Assert.True(game.SceneChild.DrawCount >= 1);
            Assert.NotEqual(0, game.UpdateThreadId);
            Assert.NotEqual(0, game.DrawThreadId);
            Assert.NotEqual(game.UpdateThreadId, game.DrawThreadId);

            // Root synced with viewport size before Load(), so RelativeSizeAxes child filled 1024x768.
            Assert.Equal(new Vector2(1024f, 768f), game.RootSizeObservedInLoad);
            Assert.Equal(new Vector2(1024f, 768f), game.SceneChild.LastAppliedQuad.Size);

            // Legacy Game.Draw() remained active alongside scene Root rendering.
            Assert.True(game.LegacyDrawCount >= 1);
            Assert.True(host.Renderer.LastFrameQuadCount >= 2);
        }
        finally
        {
            if (Directory.Exists(tempPath))
                Directory.Delete(tempPath, recursive: true);
        }
    }

    [Fact]
    public void ViewportResizeUpdatesRootSizeAndRelativeChildrenAcrossFrames()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"kajarlabs-scene-resize-{Guid.NewGuid():N}");

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

            using var game = new ResizeProbeGame(host);
            host.RunFrames(game, 8);

            Assert.Equal(new Vector2(1280f, 720f), game.Root.Size);
            Assert.Equal(new Vector2(640f, 360f), game.HalfSizeChild.DrawSize);
            Assert.Equal(new Vector2(640f, 360f), game.HalfSizeChild.LastAppliedQuad.Size);

            game.Dispose();
            Assert.True(game.Root.IsDisposed);
            Assert.True(game.HalfSizeChild.IsDisposed);
        }
        finally
        {
            if (Directory.Exists(tempPath))
                Directory.Delete(tempPath, recursive: true);
        }
    }

    [Fact]
    public void ZeroFrameHeadlessRunInitializesAndDisposesRootWithoutErrors()
    {
        using var host = new HeadlessGameHost();
        using var game = new SceneProbeGame();

        host.RunFrames(game, 0);

        Assert.Equal(0, game.UpdateCallCount);
        Assert.Equal(0, game.SceneChild.UpdateCount);

        game.Dispose();
        Assert.True(game.Root.IsDisposed);
        Assert.True(game.SceneChild.IsDisposed);
    }

    private sealed class SceneProbeGame : Game
    {
        private readonly ManualResetEventSlim drawObserved = new(false);

        public SceneQuadDrawable SceneChild { get; } = new()
        {
            RelativeSizeAxes = Axes.Both,
            Size = Vector2.One,
            Color = Color4.CYAN,
        };

        public Vector2 RootSizeObservedInLoad { get; private set; }

        public int UpdateCallCount { get; private set; }

        public int LegacyDrawCount { get; private set; }

        public int UpdateThreadId { get; private set; }

        public int DrawThreadId { get; private set; }

        protected override void Load()
        {
            RootSizeObservedInLoad = Root.Size;
            Root.Add(SceneChild);
        }

        protected override void Update()
        {
            UpdateThreadId = Environment.CurrentManagedThreadId;
            UpdateCallCount++;
            SceneChild.Alpha = 0.5f + (0.05f * UpdateCallCount);

            if (UpdateCallCount >= 10)
                drawObserved.Wait(TimeSpan.FromSeconds(5));
        }

        protected override void Draw()
        {
            DrawThreadId = Environment.CurrentManagedThreadId;
            LegacyDrawCount++;
            Renderer.Clear(Color4.BLACK);
            Renderer.DrawQuad(Renderer.WhitePixel, new RectangleF(0f, 0f, 10f, 10f), Color4.WHITE);
            drawObserved.Set();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                drawObserved.Dispose();

            base.Dispose(disposing);
        }
    }

    private sealed class ResizeProbeGame : Game
    {
        private readonly GameHost ownerHost;
        private int frame;

        public ResizeProbeGame(GameHost ownerHost)
        {
            this.ownerHost = ownerHost;
        }

        public SceneQuadDrawable HalfSizeChild { get; } = new()
        {
            RelativeSizeAxes = Axes.Both,
            Size = new Vector2(0.5f, 0.5f),
        };

        protected override void Load() => Root.Add(HalfSizeChild);

        protected override void Update()
        {
            frame++;

            if (frame == 3)
                ownerHost.ForwardResize(1280, 720);
        }
    }

    private sealed class SceneQuadDrawable : Drawable
    {
        public int UpdateCount { get; private set; }

        public int DrawCount { get; private set; }

        public Quad LastAppliedQuad { get; private set; }

        protected override void Update() => UpdateCount++;

        protected override DrawNode CreateDrawNode() => new SceneQuadDrawNode(this);

        private sealed class SceneQuadDrawNode : DrawNode
        {
            private readonly SceneQuadDrawable owner;

            public SceneQuadDrawNode(SceneQuadDrawable owner)
                : base(owner)
            {
                this.owner = owner;
            }

            public override void ApplyState()
            {
                base.ApplyState();
                owner.LastAppliedQuad = ScreenSpaceDrawQuad;
            }

            public override void Draw(IRenderer renderer)
            {
                owner.DrawCount++;
                ApplyBlending(renderer);
                renderer.DrawQuad(renderer.WhitePixel, ScreenSpaceDrawQuad, DrawColorInfo.Color);
            }
        }
    }
}
