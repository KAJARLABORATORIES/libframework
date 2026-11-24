using System.Numerics;
using kajarlabs.Framework.Platform;
using kajarlabs.Framework.Timing;

namespace kajarlabs.Framework.Scene.Transforms.Tests;

public sealed class TransformRewindAndLifecycleTests
{
    [Fact]
    public void LazyAttachmentDoesNotAttachExtensionForUnusedDrawables()
    {
        using var box = new Box();

        Assert.True(box.RemoveCompletedTransforms);
        Assert.False(box.HasTransforms);
        Assert.Equal(0.0, box.LatestTransformEndTime);

        box.RemoveCompletedTransforms = true;
        box.ClearTransforms();
        box.ClearTransformsAfter(100);
        box.FinishTransforms();
        box.UpdateSubTree();

        Assert.False(box.TryGetExtension<DrawableTransformState>(out _));

        // Setting RemoveCompletedTransforms = false attaches lazily.
        box.RemoveCompletedTransforms = false;
        Assert.True(box.TryGetExtension<DrawableTransformState>(out _));
        Assert.False(box.RemoveCompletedTransforms);
    }

    [Fact]
    public void RewindableTimelineRestoresStartValuesAndReplaysAccuratelyWithoutDoubleCallbacks()
    {
        var manualClock = new TestManualClock(initialTime: 0);
        var clock = new FramedClock(manualClock);
        using var box = new Box { Clock = clock, Alpha = 0.1f };
        box.RemoveCompletedTransforms = false;

        var firstObs = new RecordingObserver();
        var secondObs = new RecordingObserver();
        var state = DrawableTransformState.GetOrAttach(box);

        state.AddBuiltInTransform(box, BuiltInTransformTarget.Alpha, TransformValue.FromSingle(0.9f), 100, 100, observer: firstObs);
        state.AddBuiltInTransform(box, BuiltInTransformTarget.Alpha, TransformValue.FromSingle(0.3f), 200, 100, observer: secondObs);

        // Forward to completion.
        manualClock.Seek(350);
        box.UpdateSubTree();
        Assert.Equal(0.3f, box.Alpha, precision: 5);
        Assert.Equal(1, firstObs.CompletedCount);
        Assert.Equal(1, secondObs.CompletedCount);
        Assert.True(box.HasTransforms);

        // Rewind to midpoint of T1 (t=150): 0.1 -> 0.9 at 0.5 = 0.5.
        manualClock.Seek(150);
        box.UpdateSubTree();
        Assert.Equal(0.5f, box.Alpha, precision: 5);

        // Rewind before T1.StartTime (t=50): restores original 0.1f.
        manualClock.Seek(50);
        box.UpdateSubTree();
        Assert.Equal(0.1f, box.Alpha, precision: 5);

        // Seek forward to midpoint of T2 (t=250): 0.9 -> 0.3 at 0.5 = 0.6.
        manualClock.Seek(250);
        box.UpdateSubTree();
        Assert.Equal(0.6f, box.Alpha, precision: 5);

        // Seek past end again: callbacks must NOT double-fire.
        manualClock.Seek(400);
        box.UpdateSubTree();
        Assert.Equal(0.3f, box.Alpha, precision: 5);
        Assert.Equal(1, firstObs.CompletedCount);
        Assert.Equal(1, secondObs.CompletedCount);

        // Re-enabling RemoveCompletedTransforms immediately compacts completed entries.
        box.RemoveCompletedTransforms = true;
        Assert.False(box.HasTransforms);
    }

    [Fact]
    public void RewindAcrossTruncatedAndZeroDurationTransformsPreservesExactIntermediateValues()
    {
        var manualClock = new TestManualClock(initialTime: 0);
        var clock = new FramedClock(manualClock);
        using var box = new Box { Clock = clock, X = 10f, Alpha = 1f };
        box.RemoveCompletedTransforms = false;

        var abortedObs = new RecordingObserver();
        var state = DrawableTransformState.GetOrAttach(box);

        // Zero-duration Alpha at t=100 (1f -> 0f).
        state.AddBuiltInTransform(box, BuiltInTransformTarget.Alpha, TransformValue.FromSingle(0f), 100, 0);

        // X: A [0, 1000] (10 -> 110) truncated at 500 by B [500, 1000] (-> 0).
        state.AddBuiltInTransform(box, BuiltInTransformTarget.X, TransformValue.FromSingle(110f), 0, 1000, observer: abortedObs);
        state.AddBuiltInTransform(box, BuiltInTransformTarget.X, TransformValue.FromSingle(0f), 500, 500);

        manualClock.Seek(750);
        box.UpdateSubTree();
        Assert.Equal(0f, box.Alpha);
        Assert.Equal(30f, box.X, precision: 4); // A hits 60 at t=500; B interpolates 60 -> 0 at 0.5 = 30.
        Assert.Equal(1, abortedObs.AbortedCount);

        // Rewind to t=50 (before zero-duration Alpha and during A).
        manualClock.Seek(50);
        box.UpdateSubTree();
        Assert.Equal(1f, box.Alpha);
        Assert.Equal(15f, box.X, precision: 4);

        // Seek forward to t=750 again: exact replay and no duplicate abort.
        manualClock.Seek(750);
        box.UpdateSubTree();
        Assert.Equal(0f, box.Alpha);
        Assert.Equal(30f, box.X, precision: 4);
        Assert.Equal(1, abortedObs.AbortedCount);
    }

    [Fact]
    public void CustomClockDrivesChildTransformsIndependentlyWithRateScaling()
    {
        var parentManual = new TestManualClock(initialTime: 0);
        var parentClock = new FramedClock(parentManual);

        var childManual = new TestManualClock(initialTime: 0) { Rate = 2.0 };
        var childClock = new FramedClock(childManual);

        using var container = new Container { Clock = parentClock };
        var child = new Box { Clock = childClock, Alpha = 0f };
        container.Add(child);

        var state = DrawableTransformState.GetOrAttach(child);
        state.AddBuiltInTransform(child, BuiltInTransformTarget.Alpha, TransformValue.FromSingle(1f), 0, 200);

        parentManual.Advance(50);
        childManual.Advance(50); // Rate = 2.0 -> child time = 100ms.
        container.UpdateSubTree();

        Assert.Equal(50.0, container.Time);
        Assert.Equal(100.0, child.Time);
        Assert.Equal(0.5f, child.Alpha, precision: 5);

        // Disable ProcessCustomClock: child clock stops advancing during UpdateSubTree.
        child.ProcessCustomClock = false;
        childManual.Advance(50);
        container.UpdateSubTree();

        Assert.Equal(100.0, child.Time);
        Assert.Equal(0.5f, child.Alpha, precision: 5);
    }

    [Fact]
    public void LoopingTransformFastForwardsCleanlyAcrossLargeFrameJumps()
    {
        var manualClock = new TestManualClock(initialTime: 0);
        var clock = new FramedClock(manualClock);
        using var box = new Box { Clock = clock, Alpha = 0f };

        var finiteObs = new RecordingObserver();
        var state = DrawableTransformState.GetOrAttach(box);

        // Infinite loop: 0 -> 1 over [0, 100], stride 100.
        state.AddBuiltInTransform(box, BuiltInTransformTarget.Alpha, TransformValue.FromSingle(1f), 0, 100, hasExplicitStartValue: true, startValue: TransformValue.FromSingle(0f), loopDelay: 100, remainingLoops: -1);

        // Finite loop on X: 0 -> 100 over [0, 100], stride 100, 2 extra iterations (ends at t=300).
        state.AddBuiltInTransform(box, BuiltInTransformTarget.X, TransformValue.FromSingle(100f), 0, 100, hasExplicitStartValue: true, startValue: TransformValue.FromSingle(0f), observer: finiteObs, loopDelay: 100, remainingLoops: 2);

        // Huge single jump to t=100_025 (25ms into iteration 1001 for Alpha; far past t=300 for X).
        manualClock.Seek(100_025);
        box.UpdateSubTree();

        Assert.Equal(0.25f, box.Alpha, precision: 4);
        Assert.Equal(100f, box.X);
        Assert.Equal(1, finiteObs.CompletedCount);
    }

    [Fact]
    public void DisposalMidAnimationReleasesTimelineAndBlocksFurtherMutations()
    {
        var manualClock = new TestManualClock(initialTime: 0);
        var clock = new FramedClock(manualClock);
        var observer = new RecordingObserver();

        var box = new Box { Clock = clock, Alpha = 0f };
        var state = DrawableTransformState.GetOrAttach(box);
        state.AddBuiltInTransform(box, BuiltInTransformTarget.Alpha, TransformValue.FromSingle(1f), 0, 200, observer: observer);

        manualClock.Seek(100);
        box.UpdateSubTree();
        Assert.Equal(0.5f, box.Alpha, precision: 5);

        box.Dispose();

        Assert.False(box.HasTransforms);
        Assert.Equal(0, state.Count);
        Assert.Equal(0, observer.AbortedCount);
        Assert.Equal(0, observer.CompletedCount);

        manualClock.Seek(200);
        Assert.False(box.UpdateSubTree());
        Assert.Equal(0.5f, box.Alpha, precision: 5);

        Assert.Throws<ObjectDisposedException>(() => DrawableTransformState.GetOrAttach(box));
    }

    [Fact]
    public void HeadlessGameHostDrivesTransformsAndHandlesMidAnimationExpiryAndDisposal()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"kajarlabs-transforms-host-{Guid.NewGuid():N}");

        try
        {
            using var host = new HeadlessGameHost(new HostOptions
            {
                UserDataPath = tempPath,
                WindowWidth = 640,
                WindowHeight = 480,
                EnableConsoleLogging = false,
                EnableFileLogging = false,
            });

            using var game = new TransformHostProbeGame();
            host.RunFrames(game, 10);

            Assert.True(game.AnimatedBoxReachedMidpoint);
            Assert.True(game.AnimatedBoxCompleted);
            Assert.Equal(new Vector2(200f, 100f), game.AnimatedBoxPosition);
            Assert.True(game.DisposedMidAnimationBoxDisposed);
            Assert.Empty(game.ExpiredContainerChildren);
        }
        finally
        {
            if (Directory.Exists(tempPath))
                Directory.Delete(tempPath, recursive: true);
        }
    }

    private sealed class RecordingObserver : ITransformObserver
    {
        public int CompletedCount { get; private set; }

        public int AbortedCount { get; private set; }

        public void OnTransformCompleted(Drawable target) => CompletedCount++;

        public void OnTransformAborted(Drawable target) => AbortedCount++;
    }

    private sealed class TransformHostProbeGame : Game
    {
        private readonly TestManualClock manualClock = new(start: true, initialTime: 0);
        private FramedClock? customClock;
        private Box? animatedBox;
        private Box? disposedMidAnimationBox;
        private Container? transientHost;
        private int frame;

        public bool AnimatedBoxReachedMidpoint { get; private set; }

        public bool AnimatedBoxCompleted { get; private set; }

        public Vector2 AnimatedBoxPosition => animatedBox?.Position ?? Vector2.Zero;

        public bool DisposedMidAnimationBoxDisposed => disposedMidAnimationBox?.IsDisposed ?? false;

        public IReadOnlyList<Drawable> ExpiredContainerChildren => transientHost?.Children ?? [];

        protected override void Load()
        {
            customClock = new FramedClock(manualClock);

            transientHost = new Container
            {
                Clock = customClock,
                Size = new Vector2(400f, 400f),
            };

            animatedBox = new Box { Size = new Vector2(50f, 50f), Position = Vector2.Zero };
            disposedMidAnimationBox = new Box { Size = new Vector2(50f, 50f), Alpha = 1f };

            transientHost.Add(animatedBox);
            transientHost.Add(disposedMidAnimationBox);
            Root.Add(transientHost);

            var animState = DrawableTransformState.GetOrAttach(animatedBox);
            animState.AddBuiltInTransform(
                animatedBox,
                BuiltInTransformTarget.Position,
                TransformValue.FromVector2(new Vector2(200f, 100f)),
                startTime: 0,
                duration: 400,
                observer: new CompletionActionObserver(d =>
                {
                    AnimatedBoxCompleted = true;
                    d.Expire();
                }));

            var disposeState = DrawableTransformState.GetOrAttach(disposedMidAnimationBox);
            disposeState.AddBuiltInTransform(
                disposedMidAnimationBox,
                BuiltInTransformTarget.Alpha,
                TransformValue.FromSingle(0f),
                startTime: 0,
                duration: 1000);
        }

        protected override void Update()
        {
            frame++;

            if (frame == 2 && animatedBox is not null && !animatedBox.IsDisposed)
                AnimatedBoxReachedMidpoint = animatedBox.Position.X > 0f && animatedBox.Position.X < 200f;

            if (frame == 3 && disposedMidAnimationBox is not null && !disposedMidAnimationBox.IsDisposed)
                transientHost?.Remove(disposedMidAnimationBox, disposeImmediately: true);

            manualClock.Advance(100);
        }

        private sealed class CompletionActionObserver : ITransformObserver
        {
            private readonly Action<Drawable> onComplete;

            public CompletionActionObserver(Action<Drawable> onComplete)
            {
                this.onComplete = onComplete;
            }

            public void OnTransformCompleted(Drawable target) => onComplete(target);

            public void OnTransformAborted(Drawable target)
            {
            }
        }
    }
}
