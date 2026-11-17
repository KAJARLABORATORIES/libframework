using System.Numerics;
using kajarlabs.Framework.Bindables;
using kajarlabs.Framework.DependencyInjection;
using kajarlabs.Framework.Graphics.Rendering;
using kajarlabs.Framework.IO;
using kajarlabs.Framework.Numerics;
using kajarlabs.Framework.Platform;
using kajarlabs.Framework.Scene;
using Microsoft.Extensions.Logging;

namespace kajarlabs.Framework.Tests.Scene;

[Collection("SDL")]
public sealed partial class ReactiveComponentIntegrationTests
{
    [Fact]
    public void HeadlessGameHostIntegratesDiReactiveBindablesAndAsyncLoadingAcrossFrames()
    {
        using var host = new HeadlessGameHost();
        using var game = new ReactiveIntegrationGame();

        host.RunFrames(game, 500);

        Assert.NotNull(game.AsyncBoxTask);
        Assert.True(game.AsyncBoxTask.IsCompletedSuccessfully);
        Assert.NotNull(game.AsyncTilesTask);
        Assert.True(game.AsyncTilesTask.IsCompletedSuccessfully);

        // Root services injected via [Resolved] on Game and async children.
        Assert.Same(host, game.ResolvedHost);
        Assert.Same(host.Renderer, game.ResolvedRenderer);
        Assert.Same(host.Storage, game.ResolvedStorage);
        Assert.Same(host.LoggerFactory, game.ResolvedLoggerFactory);
        Assert.Null(game.ResolvedWindow);

        Assert.Same(game, game.AsyncBox.ResolvedGame);
        Assert.Same(host, game.AsyncBox.ResolvedHost);
        Assert.Same(host.Renderer, game.AsyncBox.ResolvedRenderer);
        Assert.Null(game.AsyncBox.ResolvedWindow);

        // Async components loaded off UpdateThread and completed on UpdateThread.
        Assert.NotEqual(0, game.UpdateThreadId);
        Assert.NotEqual(game.UpdateThreadId, game.AsyncBox.LoadThreadId);
        Assert.Equal(game.UpdateThreadId, game.AsyncBox.LoadCompleteThreadId);
        Assert.Equal(LoadState.Loaded, game.AsyncBox.LoadState);
        Assert.Equal(LoadState.Loaded, game.SyncStage.LoadState);

        // Clamped (3.5 -> 2.0) and snapped (1.37 -> 1.25 with step 0.25) reactive values propagated and rendered.
        Assert.Equal(Color4.GREEN, game.AsyncBox.Color);
        Assert.Equal(new Vector2(125f, 125f), game.AsyncBox.DrawSize);
        Assert.Equal(new Vector2(125f, 125f), game.AsyncBox.LastDrawnSize);
        Assert.Equal(Color4.GREEN, game.AsyncBox.LastDrawnColor);
        Assert.False(game.SyncStage.Masking);
        Assert.All(game.AsyncTiles, tile =>
        {
            Assert.Equal(LoadState.Loaded, tile.LoadState);
            Assert.Equal(Color4.GREEN, tile.Color);
            Assert.Equal(1.25f, tile.ObservedScale);
        });
    }

    [Fact]
    public void DisposingReactiveChildUnbindsSubscriptionsWithoutAffectingSiblings()
    {
        using var host = new HeadlessGameHost();
        using var game = new DisposalUnbindIntegrationGame();

        host.RunFrames(game, 500);

        Assert.True(game.DisposedChild.IsDisposed);
        Assert.Equal(LoadState.Loaded, game.SurvivingChild.LoadState);

        // Disposed child saw initial value (1.0f) and pre-removal update (1.5f), but NOT post-removal update (2.0f).
        Assert.Equal(2, game.DisposedChild.CallbackCount);
        Assert.Equal(1.5f, game.DisposedChild.ObservedScale);

        // Surviving child received initial (1.0f), pre-removal (1.5f), and post-removal (2.0f).
        Assert.Equal(3, game.SurvivingChild.CallbackCount);
        Assert.Equal(2.0f, game.SurvivingChild.ObservedScale);
    }

    [Fact]
    public async Task ParallelHeadlessGameHostsIsolateDiBindablesAndAsyncComponents()
    {
        var ct = TestContext.Current.CancellationToken;
        using var hostA = new HeadlessGameHost();
        using var hostB = new HeadlessGameHost();
        using var barrier = new Barrier(2);

        using var gameA = new IsolatedParallelGame(Color4.RED, 0.75f, barrier);
        using var gameB = new IsolatedParallelGame(Color4.CYAN, 1.75f, barrier);

        var runA = Task.Run(() => hostA.RunFrames(gameA, 500), ct);
        var runB = Task.Run(() => hostB.RunFrames(gameB, 500), ct);

        await Task.WhenAll(runA, runB);

        Assert.Same(hostA, gameA.Child.ResolvedHost);
        Assert.Same(hostB, gameB.Child.ResolvedHost);
        Assert.NotSame(gameA.AccentColor, gameB.AccentColor);

        Assert.Equal(Color4.RED, gameA.Child.Color);
        Assert.Equal(new Vector2(75f, 75f), gameA.Child.LastDrawnSize);
        Assert.Equal(Color4.RED, gameA.Child.LastDrawnColor);

        Assert.Equal(Color4.CYAN, gameB.Child.Color);
        Assert.Equal(new Vector2(175f, 175f), gameB.Child.LastDrawnSize);
        Assert.Equal(Color4.CYAN, gameB.Child.LastDrawnColor);
    }

    private sealed partial class ReactiveIntegrationGame : Game
    {
        private readonly ManualResetEventSlim postMutationDrawn = new(false);
        private int phase;
        private int mutationApplyFrame;

        [Cached]
        public Bindable<Color4> AccentColor { get; } = new(Color4.CYAN);

        [Cached]
        public BindableNumber<float> ScaleFactor { get; } = new(1.0f)
        {
            MinValue = 0.5f,
            MaxValue = 2.0f,
            Precision = 0.25f,
        };

        [Cached]
        public BindableBool MaskingEnabled { get; } = new(true);

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

        public SyncReactiveStage SyncStage { get; } = new();

        public AsyncReactiveBox AsyncBox { get; }

        public AsyncReactiveTile[] AsyncTiles { get; } = [new(), new()];

        public Task<AsyncReactiveBox>? AsyncBoxTask { get; private set; }

        public Task<IReadOnlyList<AsyncReactiveTile>>? AsyncTilesTask { get; private set; }

        public int UpdateThreadId { get; private set; }

        public ReactiveIntegrationGame()
        {
            AsyncBox = new AsyncReactiveBox(onDrawn: (drawnSize, drawnColor) =>
            {
                if (Volatile.Read(ref mutationApplyFrame) > 0
                    && drawnColor == Color4.GREEN
                    && drawnSize == new Vector2(125f, 125f))
                {
                    postMutationDrawn.Set();
                }
            });
        }

        protected override void Load()
        {
            Root.Add(SyncStage);
            AsyncBoxTask = Root.LoadComponentAsync(AsyncBox, SyncStage.Add);
            AsyncTilesTask = Root.LoadComponentsAsync(AsyncTiles, SyncStage.AddRange);
        }

        protected override void Update()
        {
            UpdateThreadId = Environment.CurrentManagedThreadId;

            if (phase == 0)
            {
                if (AsyncBoxTask is { IsCompleted: true }
                    && AsyncTilesTask is { IsCompleted: true }
                    && AsyncBox.LoadState == LoadState.Loaded
                    && AsyncTiles.All(t => t.LoadState == LoadState.Loaded))
                {
                    // Exercise clamping (3.5 -> 2.0), precision snapping (1.37 -> 1.25), color, and bool toggle.
                    ScaleFactor.Value = 3.5f;
                    Assert.Equal(2.0f, ScaleFactor.Value);

                    ScaleFactor.Value = 1.37f;
                    AccentColor.Value = Color4.GREEN;
                    MaskingEnabled.Toggle();

                    Volatile.Write(ref mutationApplyFrame, 1);
                    phase = 1;
                }
                else
                {
                    Thread.Sleep(1);
                }
            }
            else if (phase == 1)
            {
                // DrawNode is published after Update() finishes on the mutation frame; wait on the following frame.
                phase = 2;
            }
            else if (phase == 2)
            {
                Assert.True(postMutationDrawn.Wait(TimeSpan.FromSeconds(5)));
                Exit();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                postMutationDrawn.Dispose();
                AccentColor.UnbindAll();
                ScaleFactor.UnbindAll();
                MaskingEnabled.UnbindAll();
            }

            base.Dispose(disposing);
        }
    }

    private sealed partial class SyncReactiveStage : Container
    {
        [Resolved]
        private BindableBool maskingEnabled = null!;

        private readonly BindableBool boundMasking = new();

        protected override void Load()
        {
            RelativeSizeAxes = Axes.Both;
            Size = Vector2.One;
            boundMasking.BindTo(maskingEnabled);
        }

        protected override void LoadComplete()
        {
            boundMasking.BindValueChanged(e => Masking = e.NewValue, runOnceImmediately: true);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                boundMasking.UnbindAll();

            base.Dispose(disposing);
        }
    }

    private sealed partial class AsyncReactiveBox(Action<Vector2, Color4>? onDrawn = null) : Box
    {
        [Resolved]
        public Game ResolvedGame { get; private set; } = null!;

        [Resolved]
        public GameHost ResolvedHost { get; private set; } = null!;

        [Resolved]
        public IRenderer ResolvedRenderer { get; private set; } = null!;

        [Resolved]
        public GameWindow? ResolvedWindow { get; private set; }

        [Resolved]
        private Bindable<Color4> accentColor = null!;

        [Resolved]
        private BindableNumber<float> scaleFactor = null!;

        private readonly Bindable<Color4> boundAccent = new();
        private readonly BindableNumber<float> boundScale = new();

        public int LoadThreadId { get; private set; }

        public int LoadCompleteThreadId { get; private set; }

        public Vector2 LastDrawnSize { get; private set; }

        public Color4 LastDrawnColor { get; private set; }

        protected override void Load()
        {
            LoadThreadId = Environment.CurrentManagedThreadId;
            boundAccent.BindTo(accentColor);
            boundScale.BindTo(scaleFactor);
        }

        protected override void LoadComplete()
        {
            LoadCompleteThreadId = Environment.CurrentManagedThreadId;
            boundAccent.BindValueChanged(e => Color = e.NewValue, runOnceImmediately: true);
            boundScale.BindValueChanged(e => Size = new Vector2(100f * e.NewValue, 100f * e.NewValue), runOnceImmediately: true);
        }

        protected override DrawNode CreateDrawNode() => new ProbeBoxDrawNode(this, onDrawn);

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                boundAccent.UnbindAll();
                boundScale.UnbindAll();
            }

            base.Dispose(disposing);
        }

        private sealed class ProbeBoxDrawNode(AsyncReactiveBox owner, Action<Vector2, Color4>? onDrawn) : DrawNode(owner)
        {
            public override void Draw(IRenderer renderer)
            {
                var drawnSize = ScreenSpaceDrawQuad.Size;
                var drawnColor = DrawColorInfo.Color;
                owner.LastDrawnSize = drawnSize;
                owner.LastDrawnColor = drawnColor;
                ApplyBlending(renderer);
                renderer.DrawQuad(renderer.WhitePixel, ScreenSpaceDrawQuad, drawnColor);
                onDrawn?.Invoke(drawnSize, drawnColor);
            }
        }
    }

    private sealed partial class AsyncReactiveTile : Box
    {
        [Resolved]
        private Bindable<Color4> accentColor = null!;

        [Resolved]
        private BindableNumber<float> scaleFactor = null!;

        private readonly Bindable<Color4> boundAccent = new();
        private readonly BindableNumber<float> boundScale = new();

        public float ObservedScale { get; private set; }

        public int CallbackCount { get; private set; }

        protected override void Load()
        {
            Size = new Vector2(20f, 20f);
            boundAccent.BindTo(accentColor);
            boundScale.BindTo(scaleFactor);
        }

        protected override void LoadComplete()
        {
            boundAccent.BindValueChanged(e => Color = e.NewValue, runOnceImmediately: true);
            boundScale.BindValueChanged(e =>
            {
                CallbackCount++;
                ObservedScale = e.NewValue;
            }, runOnceImmediately: true);
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

    private sealed partial class DisposalUnbindIntegrationGame : Game
    {
        private int step;

        [Cached]
        public Bindable<Color4> AccentColor { get; } = new(Color4.WHITE);

        [Cached]
        public BindableNumber<float> ScaleFactor { get; } = new(1.0f)
        {
            MinValue = 0.5f,
            MaxValue = 3.0f,
            Precision = 0.5f,
        };

        public AsyncReactiveTile DisposedChild { get; } = new();

        public AsyncReactiveTile SurvivingChild { get; } = new();

        private Task<IReadOnlyList<AsyncReactiveTile>>? loadTask;

        protected override void Load()
        {
            loadTask = Root.LoadComponentsAsync([DisposedChild, SurvivingChild], Root.AddRange);
        }

        protected override void Update()
        {
            if (step == 0)
            {
                if (loadTask is { IsCompleted: true }
                    && DisposedChild.LoadState == LoadState.Loaded
                    && SurvivingChild.LoadState == LoadState.Loaded)
                {
                    ScaleFactor.Value = 1.5f;
                    step = 1;
                }
                else
                {
                    Thread.Sleep(1);
                }
            }
            else if (step == 1)
            {
                Assert.True(Root.Remove(DisposedChild, disposeImmediately: true));
                ScaleFactor.Value = 2.0f;
                Exit();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                AccentColor.UnbindAll();
                ScaleFactor.UnbindAll();
            }

            base.Dispose(disposing);
        }
    }

    private sealed partial class IsolatedParallelGame(Color4 initialColor, float initialScale, Barrier barrier) : Game
    {
        private readonly ManualResetEventSlim drawnSignal = new(false);
        private Task<AsyncReactiveBox>? loadTask;
        private bool waitingForDraw;

        [Cached]
        public Bindable<Color4> AccentColor { get; } = new(initialColor);

        [Cached]
        public BindableNumber<float> ScaleFactor { get; } = new(initialScale)
        {
            MinValue = 0.25f,
            MaxValue = 2.5f,
            Precision = 0.25f,
        };

        public AsyncReactiveBox Child { get; private set; } = null!;

        protected override void Load()
        {
            barrier.SignalAndWait(TimeSpan.FromSeconds(5));

            Child = new AsyncReactiveBox(onDrawn: (drawnSize, drawnColor) =>
            {
                if (drawnColor == initialColor && drawnSize == new Vector2(100f * initialScale, 100f * initialScale))
                    drawnSignal.Set();
            });

            loadTask = Root.LoadComponentAsync(Child, Root.Add);
        }

        protected override void Update()
        {
            if (waitingForDraw)
            {
                Assert.True(drawnSignal.Wait(TimeSpan.FromSeconds(5)));
                Exit();
            }
            else if (loadTask is { IsCompleted: true } && Child.LoadState == LoadState.Loaded)
            {
                waitingForDraw = true;
            }
            else
            {
                Thread.Sleep(1);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                drawnSignal.Dispose();
                AccentColor.UnbindAll();
                ScaleFactor.UnbindAll();
            }

            base.Dispose(disposing);
        }
    }
}
