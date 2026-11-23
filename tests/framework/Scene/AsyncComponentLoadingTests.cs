using System.Numerics;
using kajarlabs.Framework.DependencyInjection;
using kajarlabs.Framework.Graphics;
using kajarlabs.Framework.Graphics.Rendering;
using kajarlabs.Framework.IO;
using kajarlabs.Framework.Numerics;
using kajarlabs.Framework.Platform;
using kajarlabs.Framework.Scene;
using Microsoft.Extensions.Logging;

namespace kajarlabs.Framework.Tests.Scene;

[Collection("SDL")]
public sealed partial class AsyncComponentLoadingTests
{
    [Fact]
    public void LoadComponentAsyncExecutesLoadOffUpdateThreadAndMarshalsCompletionViaScheduler()
    {
        var ct = TestContext.Current.CancellationToken;
        var updateThreadId = Environment.CurrentManagedThreadId;
        using var parent = new ServiceCachingContainer("async-parent");
        parent.Load(DependencyCollection.EMPTY);
        Assert.True(parent.UpdateSubTree());

        using var loadStarted = new ManualResetEventSlim(false);
        using var allowLoadFinish = new ManualResetEventSlim(false);
        var child = new AsyncProbeDrawable(loadStarted, allowLoadFinish);

        var onLoadedThreadId = 0;
        var loadTask = parent.LoadComponentAsync(child, loaded =>
        {
            onLoadedThreadId = Environment.CurrentManagedThreadId;
            parent.Add(loaded);
        }, ct);

        Assert.True(loadStarted.Wait(TimeSpan.FromSeconds(5), ct));
        Assert.Equal(LoadState.Loading, child.LoadState);
        Assert.NotEqual(updateThreadId, child.LoadThreadId);
        Assert.Equal("async-parent", child.ResolvedService.Name);

        // Pumping the update thread while Load() is still running must not block or attach early.
        Assert.True(parent.UpdateSubTree());
        Assert.Empty(parent.Children);
        Assert.False(loadTask.IsCompleted);

        allowLoadFinish.Set();

        var completed = pumpUntilCompleted(parent, loadTask, ct);

        Assert.Same(child, completed);
        Assert.Equal(updateThreadId, onLoadedThreadId);
        Assert.Equal(updateThreadId, child.LoadCompleteThreadId);
        Assert.Equal(updateThreadId, child.UpdateThreadId);
        Assert.Equal(LoadState.Loaded, child.LoadState);
        Assert.Equal(1, child.LoadCount);
        Assert.Equal(1, child.LoadCompleteCount);
        Assert.Single(parent.Children);
        Assert.Same(child, parent.Children[0]);
    }

    [Fact]
    public void LoadComponentsAsyncLoadsBatchOffUpdateThreadAndPreservesOrder()
    {
        var ct = TestContext.Current.CancellationToken;
        var updateThreadId = Environment.CurrentManagedThreadId;
        using var parent = new ServiceCachingContainer("batch-parent");
        parent.Load(DependencyCollection.EMPTY);
        Assert.True(parent.UpdateSubTree());

        using var firstStarted = new ManualResetEventSlim(false);
        using var secondStarted = new ManualResetEventSlim(false);
        using var allowFirstFinish = new ManualResetEventSlim(false);

        var first = new AsyncProbeDrawable(firstStarted, allowFirstFinish);
        var second = new AsyncProbeDrawable(secondStarted);
        var third = new AsyncProbeDrawable();

        IReadOnlyList<AsyncProbeDrawable>? receivedBatch = null;
        var onLoadedThreadId = 0;

        var batchTask = parent.LoadComponentsAsync(
            new[] { first, second, third },
            loaded =>
            {
                onLoadedThreadId = Environment.CurrentManagedThreadId;
                receivedBatch = loaded.ToArray();
                parent.AddRange(loaded);
            },
            ct);

        // Second and third must be able to run without waiting on first, proving concurrent batch execution.
        Assert.True(firstStarted.Wait(TimeSpan.FromSeconds(5), ct));
        Assert.True(secondStarted.Wait(TimeSpan.FromSeconds(5), ct));
        Assert.False(batchTask.IsCompleted);

        allowFirstFinish.Set();

        var result = pumpUntilCompleted(parent, batchTask, ct);

        Assert.Equal(updateThreadId, onLoadedThreadId);
        Assert.Equal(new[] { first, second, third }, result);
        Assert.Equal(new[] { first, second, third }, receivedBatch);
        Assert.Equal(new Drawable[] { first, second, third }, parent.Children);
        Assert.All(result, item => Assert.Equal(LoadState.Loaded, item.LoadState));

        // Empty batch still marshals onLoaded onto the update thread.
        var emptyCallbackRan = false;
        var emptyTask = parent.LoadComponentsAsync(
            Array.Empty<Drawable>(),
            _ => emptyCallbackRan = Environment.CurrentManagedThreadId == updateThreadId,
            ct);

        Assert.False(emptyCallbackRan);
        var emptyResult = pumpUntilCompleted(parent, emptyTask, ct);
        Assert.Empty(emptyResult);
        Assert.True(emptyCallbackRan);
    }

    [Fact]
    public async Task LoadComponentAsyncCancelsAndDisposesWhenCancellationTokenIsCancelled()
    {
        var ct = TestContext.Current.CancellationToken;
        using var parent = new ServiceCachingContainer("cancel-parent");
        parent.Load(DependencyCollection.EMPTY);
        Assert.True(parent.UpdateSubTree());

        // 1. Pre-cancelled token.
        using (var preCancelled = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            preCancelled.Cancel();
            var preChild = new AsyncProbeDrawable();
            var preCallbackCalled = false;

            var preTask = parent.LoadComponentAsync(preChild, _ => preCallbackCalled = true, preCancelled.Token);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => preTask);

            Assert.False(preCallbackCalled);
            Assert.Equal(0, preChild.LoadCount);
            Assert.True(preChild.IsDisposed);
        }

        // 2. Cancelled mid-Load() via LoadCancellationToken.
        using (var midCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
        using (var enteredLoad = new ManualResetEventSlim(false))
        {
            var midChild = new CancellableWaitDrawable(enteredLoad);
            var midCallbackCalled = false;

            var midTask = parent.LoadComponentAsync(
                midChild,
                loaded =>
                {
                    midCallbackCalled = true;
                    parent.Add(loaded);
                },
                midCts.Token);

            Assert.True(enteredLoad.Wait(TimeSpan.FromSeconds(5), ct));
            Assert.Equal(LoadState.Loading, midChild.LoadState);

            midCts.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => midTask);

            Assert.True(midChild.ObservedCancellation);
            Assert.True(midChild.IsDisposed);
            Assert.False(midCallbackCalled);
            Assert.Empty(parent.Children);
        }

        // 3. Cancelled after Load() completed while queued in Scheduler.
        using (var queuedCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            var queuedChild = new AsyncProbeDrawable();
            var queuedCallbackCalled = false;

            var queuedTask = parent.LoadComponentAsync(
                queuedChild,
                loaded =>
                {
                    queuedCallbackCalled = true;
                    parent.Add(loaded);
                },
                queuedCts.Token);

            // Wait briefly for Load() to finish and queue onto Scheduler without pumping UpdateSubTree().
            SpinWait.SpinUntil(() => queuedChild.LoadState == LoadState.Ready, TimeSpan.FromSeconds(5));
            Assert.Equal(LoadState.Ready, queuedChild.LoadState);

            queuedCts.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queuedTask);

            Assert.True(queuedChild.IsDisposed);
            Assert.True(parent.UpdateSubTree());
            Assert.False(queuedCallbackCalled);
            Assert.Empty(parent.Children);
        }
    }

    [Fact]
    public async Task DisposingParentWhileLoadingCancelsTasksAndDisposesPendingComponents()
    {
        var ct = TestContext.Current.CancellationToken;

        // Case 1: Parent disposed while child LoadState == Loading.
        using var loadStarted = new ManualResetEventSlim(false);
        using var allowLoadFinish = new ManualResetEventSlim(false);
        var loadingChild = new AsyncProbeDrawable(loadStarted, allowLoadFinish);
        var callbackCalled = false;

        Task<AsyncProbeDrawable> loadTask;

        using (var parent = new ServiceCachingContainer("disposing-parent"))
        {
            parent.Load(DependencyCollection.EMPTY);
            loadTask = parent.LoadComponentAsync(loadingChild, _ => callbackCalled = true, ct);

            Assert.True(loadStarted.Wait(TimeSpan.FromSeconds(5), ct));
            Assert.Equal(LoadState.Loading, loadingChild.LoadState);
        }

        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => loadTask);
            Assert.True(loadingChild.IsDisposed);
            Assert.False(callbackCalled);
        }
        finally
        {
            allowLoadFinish.Set();
        }

        // Case 2: Parent disposed while completion is already queued on Scheduler.
        var queuedChild = new AsyncProbeDrawable();
        var queuedCallbackCalled = false;
        Task<AsyncProbeDrawable> queuedTask;

        using (var parent = new ServiceCachingContainer("queued-disposing-parent"))
        {
            parent.Load(DependencyCollection.EMPTY);
            queuedTask = parent.LoadComponentAsync(queuedChild, _ => queuedCallbackCalled = true, ct);
            Assert.True(SpinWait.SpinUntil(() => queuedChild.LoadState == LoadState.Ready, TimeSpan.FromSeconds(5)));
        }

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queuedTask);
        Assert.True(queuedChild.IsDisposed);
        Assert.False(queuedCallbackCalled);
    }

    [Fact]
    public async Task DisposingPendingComponentWhileLoadingCancelsLoadAndBatchSafely()
    {
        var ct = TestContext.Current.CancellationToken;
        using var parent = new ServiceCachingContainer("parent");
        parent.Load(DependencyCollection.EMPTY);
        Assert.True(parent.UpdateSubTree());

        // Single component disposed while Loading.
        using (var loadStarted = new ManualResetEventSlim(false))
        using (var allowFinish = new ManualResetEventSlim(false))
        {
            var pending = new AsyncProbeDrawable(loadStarted, allowFinish);
            var callbackCalled = false;

            var loadTask = parent.LoadComponentAsync(pending, _ => callbackCalled = true, ct);
            Assert.True(loadStarted.Wait(TimeSpan.FromSeconds(5), ct));
            Assert.Equal(LoadState.Loading, pending.LoadState);

            pending.Dispose();

            try
            {
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => loadTask);
                Assert.True(pending.IsDisposed);
                Assert.True(parent.UpdateSubTree());
                Assert.False(callbackCalled);
                Assert.Empty(parent.Children);
            }
            finally
            {
                allowFinish.Set();
            }
        }

        // Batch where one component is disposed while Loading cancels the sibling and disposes it.
        using (var firstStarted = new ManualResetEventSlim(false))
        using (var allowBatchFinish = new ManualResetEventSlim(false))
        {
            var first = new AsyncProbeDrawable(firstStarted, allowBatchFinish);
            var second = new AsyncProbeDrawable();
            var batchCallbackCalled = false;

            var batchTask = parent.LoadComponentsAsync(new[] { first, second }, _ => batchCallbackCalled = true, ct);
            Assert.True(firstStarted.Wait(TimeSpan.FromSeconds(5), ct));

            first.Dispose();

            try
            {
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => batchTask);
                Assert.True(first.IsDisposed);
                Assert.True(second.IsDisposed);
                Assert.True(parent.UpdateSubTree());
                Assert.False(batchCallbackCalled);
                Assert.Empty(parent.Children);
            }
            finally
            {
                allowBatchFinish.Set();
            }
        }
    }

    [Fact]
    public async Task LoadComponentAsyncPropagatesExceptionWhenLoadFailsWithoutCancellation()
    {
        using var parent = new Container();
        parent.Load(DependencyCollection.EMPTY);
        Assert.True(parent.UpdateSubTree());

        var missingDepChild = new MissingDependencyDrawable();
        var callbackCalled = false;

        var task = parent.LoadComponentAsync(missingDepChild, _ => callbackCalled = true, TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<DependencyNotFoundException>(() => task);
        Assert.False(callbackCalled);
        Assert.Equal(LoadState.NotLoaded, missingDepChild.LoadState);
        Assert.False(missingDepChild.IsDisposed);
        Assert.Empty(parent.Children);
    }

    [Fact]
    public void HeadlessGameHostCompletesAsyncAndBatchLoadsAcrossFramesAndRendersDrawNodes()
    {
        using var host = new HeadlessGameHost();
        using var game = new AsyncHostFlowGame();

        host.RunFrames(game, 500);

        Assert.NotNull(game.AsyncLoadTask);
        Assert.True(game.AsyncLoadTask.IsCompletedSuccessfully);
        Assert.NotNull(game.BatchLoadTask);
        Assert.True(game.BatchLoadTask.IsCompletedSuccessfully);

        Assert.NotEqual(0, game.UpdateThreadId);
        Assert.NotEqual(game.UpdateThreadId, game.SingleChild.LoadThreadId);
        Assert.Equal(game.UpdateThreadId, game.SingleChild.OnLoadedThreadId);
        Assert.Equal(game.UpdateThreadId, game.SingleChild.LoadCompleteThreadId);
        Assert.Equal(LoadState.Loaded, game.SingleChild.LoadState);
        Assert.True(game.SingleChild.UpdateCount >= 1);
        Assert.True(game.SingleChild.ReadDrawCount() >= 1);

        Assert.All(game.BatchChildren, child =>
        {
            Assert.NotEqual(game.UpdateThreadId, child.LoadThreadId);
            Assert.Equal(game.UpdateThreadId, child.LoadCompleteThreadId);
            Assert.Equal(LoadState.Loaded, child.LoadState);
        });

        Assert.False(game.SingleChildAttachedAtFrame1);
        Assert.Contains(game.SingleChild, game.Root.Children);
        Assert.True(host.Renderer.LastFrameQuadCount >= 1);
    }

    [Fact]
    public async Task HeadlessGameHostDisposingGameWhileComponentIsLoadingCancelsCleanly()
    {
        using var host = new HeadlessGameHost();
        using var game = new DisposalCancellationHostGame();

        host.RunFrames(game, 2);

        Assert.NotNull(game.LoadTask);
        Assert.False(game.LoadTask.IsCompleted);
        Assert.Equal(LoadState.Loading, game.PendingChild.LoadState);

        game.Dispose();

        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => game.LoadTask);
            Assert.True(game.PendingChild.IsDisposed);
            Assert.False(game.CallbackInvoked);
        }
        finally
        {
            game.ReleaseBlockedLoad();
        }
    }

    [Fact]
    public void HeadlessGameHostInjectsRootGameAndHostServicesIntoGameSyncAndAsyncDrawables()
    {
        using var host = new HeadlessGameHost();
        using var game = new DependencyInjectionHostGame();

        host.RunFrames(game, 500);

        Assert.NotNull(game.AsyncChildLoadTask);
        Assert.True(game.AsyncChildLoadTask.IsCompletedSuccessfully);

        // Game itself resolved core services and cached services.
        Assert.Same(game, game.ResolvedGame);
        Assert.Same(host, game.ResolvedHost);
        Assert.Same(host.Renderer, game.ResolvedRenderer);
        Assert.Same(host.Storage, game.ResolvedStorage);
        Assert.Same(host.LoggerFactory, game.ResolvedLoggerFactory);
        Assert.Null(game.ResolvedWindow);
        Assert.Same(game.CustomService, game.ResolvedOwnCustomService);

        // Synchronous child resolved all root services.
        Assert.Same(game, game.SyncChild.ResolvedGame);
        Assert.Same(host, game.SyncChild.ResolvedHost);
        Assert.Same(host.Renderer, game.SyncChild.ResolvedRenderer);
        Assert.Same(host.Storage, game.SyncChild.ResolvedStorage);
        Assert.Same(host.LoggerFactory, game.SyncChild.ResolvedLoggerFactory);
        Assert.Null(game.SyncChild.ResolvedWindow);
        Assert.Same(game.CustomService, game.SyncChild.ResolvedCustomService);
        Assert.Equal(LoadState.Loaded, game.SyncChild.LoadState);

        // Asynchronous child resolved all root services off-thread and completed on UpdateThread.
        Assert.Same(game, game.AsyncChild.ResolvedGame);
        Assert.Same(host, game.AsyncChild.ResolvedHost);
        Assert.Same(host.Renderer, game.AsyncChild.ResolvedRenderer);
        Assert.Same(host.Storage, game.AsyncChild.ResolvedStorage);
        Assert.Same(host.LoggerFactory, game.AsyncChild.ResolvedLoggerFactory);
        Assert.Null(game.AsyncChild.ResolvedWindow);
        Assert.Same(game.CustomService, game.AsyncChild.ResolvedCustomService);
        Assert.Equal(LoadState.Loaded, game.AsyncChild.LoadState);
    }

    private static T pumpUntilCompleted<T>(Container parent, Task<T> task, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);

        while (!task.IsCompleted)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.True(DateTime.UtcNow < deadline, "Timed out waiting for async load task while pumping UpdateSubTree.");
            parent.UpdateSubTree();
            Thread.Sleep(1);
        }

        return task.GetAwaiter().GetResult();
    }

    public interface ITestService
    {
        string Name { get; }
    }

    public sealed class TestService(string name) : ITestService
    {
        public string Name { get; } = name;
    }

    private sealed partial class ServiceCachingContainer(string serviceName) : Container
    {
        [Cached(typeof(ITestService))]
        private readonly TestService service = new(serviceName);
    }

    private sealed partial class AsyncProbeDrawable : Drawable
    {
        private readonly ManualResetEventSlim? loadStarted;
        private readonly ManualResetEventSlim? allowLoadFinish;

        public AsyncProbeDrawable(ManualResetEventSlim? loadStarted = null, ManualResetEventSlim? allowLoadFinish = null)
        {
            this.loadStarted = loadStarted;
            this.allowLoadFinish = allowLoadFinish;
        }

        [Resolved]
        public ITestService ResolvedService { get; private set; } = null!;

        public int LoadThreadId { get; private set; }

        public int LoadCompleteThreadId { get; private set; }

        public int UpdateThreadId { get; private set; }

        public int LoadCount { get; private set; }

        public int LoadCompleteCount { get; private set; }

        protected override void Load()
        {
            LoadCount++;
            LoadThreadId = Environment.CurrentManagedThreadId;
            loadStarted?.Set();
            allowLoadFinish?.Wait(TimeSpan.FromSeconds(5));
        }

        protected override void LoadComplete()
        {
            LoadCompleteCount++;
            LoadCompleteThreadId = Environment.CurrentManagedThreadId;
        }

        protected override void Update()
        {
            if (UpdateThreadId == 0)
                UpdateThreadId = Environment.CurrentManagedThreadId;
        }
    }

    private sealed class CancellableWaitDrawable(ManualResetEventSlim enteredLoad) : Drawable
    {
        public bool ObservedCancellation { get; private set; }

        protected override void Load()
        {
            enteredLoad.Set();

            try
            {
                LoadCancellationToken.WaitHandle.WaitOne(TimeSpan.FromSeconds(5));
                LoadCancellationToken.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException)
            {
                ObservedCancellation = true;
                throw;
            }
        }
    }

    private sealed partial class MissingDependencyDrawable : Drawable
    {
        [Resolved]
        public ITestService MissingService { get; private set; } = null!;
    }

    private sealed class AsyncHostFlowGame : Game
    {
        private readonly ManualResetEventSlim loadStarted = new(false);
        private readonly ManualResetEventSlim allowFinish = new(false);
        private readonly ManualResetEventSlim singleChildDrawn = new(false);
        private int frame;
        private bool waitingForDraw;

        public HostBoxDrawable SingleChild { get; } = new(null, null)
        {
            Size = new Vector2(120f, 80f),
            Color = Color4.WHITE,
        };

        public HostBoxDrawable[] BatchChildren { get; } =
        [
            new(null, null) { Size = new Vector2(40f, 40f), Color = Color4.RED },
            new(null, null) { Size = new Vector2(40f, 40f), Color = Color4.GREEN },
        ];

        public Task<HostBoxDrawable>? AsyncLoadTask { get; private set; }

        public Task<IReadOnlyList<HostBoxDrawable>>? BatchLoadTask { get; private set; }

        public int UpdateThreadId { get; private set; }

        public bool SingleChildAttachedAtFrame1 { get; private set; } = true;

        protected override void Load()
        {
            SingleChild.SetSignals(loadStarted, allowFinish, singleChildDrawn);
            AsyncLoadTask = Root.LoadComponentAsync(SingleChild, loaded =>
            {
                loaded.OnLoadedThreadId = Environment.CurrentManagedThreadId;
                Root.Add(loaded);
            });
            BatchLoadTask = Root.LoadComponentsAsync(BatchChildren, Root.AddRange);
        }

        protected override void Update()
        {
            frame++;
            UpdateThreadId = Environment.CurrentManagedThreadId;

            if (frame == 1)
            {
                Assert.True(loadStarted.Wait(TimeSpan.FromSeconds(5)));
                SingleChildAttachedAtFrame1 = Root.Children.Contains(SingleChild);
            }
            else if (frame == 2)
            {
                allowFinish.Set();
            }
            else if (waitingForDraw)
            {
                Assert.True(singleChildDrawn.Wait(TimeSpan.FromSeconds(5)));
                Exit();
            }
            else if (AsyncLoadTask is { IsCompleted: true }
                     && BatchLoadTask is { IsCompleted: true }
                     && SingleChild.LoadState == LoadState.Loaded
                     && BatchChildren.All(c => c.LoadState == LoadState.Loaded))
            {
                // DrawNode is published after Update() returns, so wait on the subsequent frame.
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
                allowFinish.Set();
                loadStarted.Dispose();
                allowFinish.Dispose();
                singleChildDrawn.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    private sealed class HostBoxDrawable : Box
    {
        private ManualResetEventSlim? loadStarted;
        private ManualResetEventSlim? allowFinish;
        private ManualResetEventSlim? drawnSignal;
        private int drawCount;

        public HostBoxDrawable(ManualResetEventSlim? loadStarted, ManualResetEventSlim? allowFinish)
        {
            this.loadStarted = loadStarted;
            this.allowFinish = allowFinish;
        }

        public int LoadThreadId { get; private set; }

        public int OnLoadedThreadId { get; set; }

        public int LoadCompleteThreadId { get; private set; }

        public int UpdateCount { get; private set; }

        public int ReadDrawCount() => Volatile.Read(ref drawCount);

        public void SetSignals(ManualResetEventSlim started, ManualResetEventSlim allow, ManualResetEventSlim drawn)
        {
            loadStarted = started;
            allowFinish = allow;
            drawnSignal = drawn;
        }

        protected override void Load()
        {
            LoadThreadId = Environment.CurrentManagedThreadId;
            loadStarted?.Set();
            allowFinish?.Wait(TimeSpan.FromSeconds(5));
        }

        protected override void LoadComplete()
            => LoadCompleteThreadId = Environment.CurrentManagedThreadId;

        protected override void Update() => UpdateCount++;

        protected override DrawNode CreateDrawNode() => new ProbeDrawNode(this);

        private sealed class ProbeDrawNode(HostBoxDrawable owner) : DrawNode(owner)
        {
            public override void Draw(IRenderer renderer)
            {
                Interlocked.Increment(ref owner.drawCount);
                ApplyBlending(renderer);
                renderer.DrawQuad(renderer.WhitePixel, ScreenSpaceDrawQuad, DrawColorInfo.Color);
                owner.drawnSignal?.Set();
            }
        }
    }

    private sealed class DisposalCancellationHostGame : Game
    {
        private readonly ManualResetEventSlim loadStarted = new(false);
        private readonly ManualResetEventSlim allowFinish = new(false);

        public AsyncBlockedDrawable PendingChild { get; }

        public Task<AsyncBlockedDrawable>? LoadTask { get; private set; }

        public bool CallbackInvoked { get; private set; }

        public DisposalCancellationHostGame()
        {
            PendingChild = new AsyncBlockedDrawable(loadStarted, allowFinish);
        }

        public void ReleaseBlockedLoad() => allowFinish.Set();

        protected override void Load()
        {
            LoadTask = Root.LoadComponentAsync(PendingChild, loaded =>
            {
                CallbackInvoked = true;
                Root.Add(loaded);
            });

            Assert.True(loadStarted.Wait(TimeSpan.FromSeconds(5)));
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            if (disposing)
            {
                allowFinish.Set();
                loadStarted.Dispose();
                allowFinish.Dispose();
            }
        }
    }

    private sealed class AsyncBlockedDrawable(ManualResetEventSlim loadStarted, ManualResetEventSlim allowFinish) : Drawable
    {
        protected override void Load()
        {
            loadStarted.Set();
            allowFinish.Wait(TimeSpan.FromSeconds(5));
        }
    }

    private sealed partial class DependencyInjectionHostGame : Game
    {
        [Cached(typeof(ITestService))]
        public TestService CustomService { get; } = new("game-root-custom");

        [Resolved]
        public Game ResolvedGame { get; private set; } = null!;

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

        [Resolved]
        public ITestService ResolvedOwnCustomService { get; private set; } = null!;

        public ServiceConsumerDrawable SyncChild { get; } = new();

        public ServiceConsumerDrawable AsyncChild { get; } = new();

        public Task<ServiceConsumerDrawable>? AsyncChildLoadTask { get; private set; }

        protected override void Load()
        {
            Root.Add(SyncChild);
            AsyncChildLoadTask = Root.LoadComponentAsync(AsyncChild, Root.Add);
        }

        protected override void Update()
        {
            if (AsyncChildLoadTask is { IsCompleted: true } && AsyncChild.LoadState == LoadState.Loaded)
                Exit();
            else
                Thread.Sleep(1);
        }
    }

    private sealed partial class ServiceConsumerDrawable : Drawable
    {
        [Resolved]
        public Game ResolvedGame { get; private set; } = null!;

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

        [Resolved]
        public ITestService ResolvedCustomService { get; private set; } = null!;
    }
}
