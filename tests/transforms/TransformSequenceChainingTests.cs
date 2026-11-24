using System.Numerics;
using kajarlabs.Framework.Platform;
using kajarlabs.Framework.Timing;

namespace kajarlabs.Framework.Scene.Transforms.Tests;

public sealed class TransformSequenceChainingTests
{
    [Fact]
    public void FadeInThenMoveToExecutesSequentiallyWhileParallelTransformsRunTogether()
    {
        var manualClock = new TestManualClock(initialTime: 100);
        var clock = new FramedClock(manualClock);
        using var box = new Box { Clock = clock, Alpha = 0f, Position = Vector2.Zero, Scale = Vector2.One };

        var completed = 0;
        var finallyCount = 0;

        var sequence = box.FadeIn(200)
            .ScaleTo(2f, 100)
            .Then()
            .MoveTo(new Vector2(300f, 150f), 400)
            .OnComplete(b =>
            {
                completed++;
                Assert.Same(box, b);
            })
            .Finally(_ => finallyCount++);

        Assert.Equal(100, sequence.OriginTime);
        Assert.Equal(300, sequence.CurrentStartTime);
        Assert.Equal(700, sequence.EndTime);

        // At t = 200: FadeIn halfway (0.5), ScaleTo finished (2.0), MoveTo hasn't started (0, 0).
        manualClock.Seek(200);
        box.UpdateSubTree();
        Assert.Equal(0.5f, box.Alpha, precision: 5);
        Assert.Equal(new Vector2(2f, 2f), box.Scale);
        Assert.Equal(Vector2.Zero, box.Position);
        Assert.Equal(0, completed);

        // At t = 500: FadeIn finished (1.0), MoveTo halfway (150, 75), OnComplete still waiting.
        manualClock.Seek(500);
        box.UpdateSubTree();
        Assert.Equal(1f, box.Alpha);
        Assert.Equal(new Vector2(150f, 75f), box.Position);
        Assert.Equal(0, completed);

        // At t = 700: entire sequence finishes, firing OnComplete and Finally once.
        manualClock.Seek(700);
        box.UpdateSubTree();
        Assert.Equal(new Vector2(300f, 150f), box.Position);
        Assert.Equal(1, completed);
        Assert.Equal(1, finallyCount);
        Assert.False(box.HasTransforms);
    }

    [Fact]
    public void DelayAtStartMiddleAndEndRespectsExactTimelineTiming()
    {
        var manualClock = new TestManualClock(initialTime: 0);
        var clock = new FramedClock(manualClock);
        using var box = new Box { Clock = clock, Alpha = 0f, X = 0f };

        var standaloneDelayCompleted = 0;
        var trailingDelayCompleted = 0;

        box.Delay(100).OnComplete(_ => standaloneDelayCompleted++);
        var seq = box.Delay(50).FadeIn(100).Then().Delay(75).MoveToX(200f, 100).Then().Delay(50).OnComplete(_ => trailingDelayCompleted++);

        Assert.Equal(0, seq.OriginTime);
        Assert.Equal(375, seq.EndTime);

        manualClock.Seek(99);
        box.UpdateSubTree();
        Assert.Equal(0, standaloneDelayCompleted);
        Assert.Equal(0.49f, box.Alpha, precision: 3);

        manualClock.Seek(100);
        box.UpdateSubTree();
        Assert.Equal(1, standaloneDelayCompleted);

        manualClock.Seek(325);
        box.UpdateSubTree();
        Assert.Equal(200f, box.X);
        Assert.Equal(0, trailingDelayCompleted);

        manualClock.Seek(375);
        box.UpdateSubTree();
        Assert.Equal(1, trailingDelayCompleted);
    }

    [Fact]
    public void FadeOutAndExpireRemovesAndDisposesChildFromContainer()
    {
        var manualClock = new TestManualClock(initialTime: 500);
        var clock = new FramedClock(manualClock);
        using var container = new Container { Clock = clock };

        var transient = new Box { Alpha = 1f, Size = new Vector2(40f, 40f) };
        container.Add(transient);

        transient.Delay(100).FadeOut(200).Expire(calculateLifetimeStart: true);

        Assert.Equal(500, transient.LifetimeStart);
        Assert.Equal(800, transient.LifetimeEnd);

        manualClock.Seek(700);
        container.UpdateSubTree();
        Assert.Equal(0.5f, transient.Alpha, precision: 5);
        Assert.Single(container.Children);
        Assert.False(transient.IsDisposed);

        manualClock.Seek(800);
        container.UpdateSubTree();
        Assert.Equal(0f, transient.Alpha);
        Assert.Empty(container.Children);
        Assert.True(transient.IsDisposed);
    }

    [Fact]
    public void FiniteLoopRepeatsWithPauseAndFiresOnCompleteAfterFinalIteration()
    {
        var manualClock = new TestManualClock(initialTime: 0);
        var clock = new FramedClock(manualClock);
        using var box = new Box { Clock = clock, Alpha = 0f };

        var completed = 0;
        var aborted = 0;
        var finallyCount = 0;

        // 2 iterations of [0..100 FadeIn -> 100..200 FadeOut], plus 50ms pause between iterations -> ends at 450.
        var seq = box.FadeIn(100)
            .Then()
            .FadeOut(100)
            .Loop(pause: 50, numIterations: 2)
            .OnComplete(_ => completed++)
            .OnAbort(_ => aborted++)
            .Finally(_ => finallyCount++);

        Assert.Equal(450, seq.EndTime);

        manualClock.Seek(200);
        box.UpdateSubTree();
        Assert.Equal(0f, box.Alpha);
        Assert.Equal(0, completed);

        // During pause [200, 250].
        manualClock.Seek(225);
        box.UpdateSubTree();
        Assert.Equal(0f, box.Alpha);
        Assert.Equal(0, completed);

        // Iteration 2 FadeIn halfway at t=300.
        manualClock.Seek(300);
        box.UpdateSubTree();
        Assert.Equal(0.5f, box.Alpha, precision: 5);
        Assert.Equal(0, completed);

        // Iteration 2 ends at t=450.
        manualClock.Seek(450);
        box.UpdateSubTree();
        Assert.Equal(0f, box.Alpha);
        Assert.Equal(1, completed);
        Assert.Equal(0, aborted);
        Assert.Equal(1, finallyCount);
        Assert.False(box.HasTransforms);
    }

    [Fact]
    public void FiniteLoopWithTrailingDelayRespectsDelayEveryIteration()
    {
        var manualClock = new TestManualClock(initialTime: 0);
        var clock = new FramedClock(manualClock);
        using var box = new Box { Clock = clock, X = 0f };

        var completed = 0;
        var seq = box.MoveToX(100f, 100)
            .Then()
            .Delay(50)
            .Loop(pause: 25, numIterations: 2)
            .OnComplete(_ => completed++);

        // Iteration duration = 150, pause = 25 -> loopDelay = 175 -> ends at 150 + 175 = 325.
        Assert.Equal(325, seq.EndTime);

        manualClock.Seek(275);
        box.UpdateSubTree();
        Assert.Equal(100f, box.X);
        Assert.Equal(0, completed);

        manualClock.Seek(325);
        box.UpdateSubTree();
        Assert.Equal(1, completed);
    }

    [Fact]
    public void AbortingLoopedSequenceFiresOnAbortAndFinallyOnceAndCancelsPendingLoops()
    {
        var manualClock = new TestManualClock(initialTime: 0);
        var clock = new FramedClock(manualClock);
        using var box = new Box { Clock = clock, Alpha = 0f, X = 0f };

        var completed = 0;
        var aborted = 0;
        var finallyCount = 0;

        var seq = box.FadeIn(100)
            .Then()
            .MoveToX(100f, 100)
            .Loop()
            .OnComplete(_ => completed++)
            .OnAbort(_ => aborted++)
            .Finally(_ => finallyCount++);

        Assert.Equal(double.PositiveInfinity, seq.EndTime);

        // At t=50, interrupt Alpha with FadeOut -> sequence aborts, cancels MoveToX loop, fires OnAbort + Finally once.
        manualClock.Seek(50);
        box.UpdateSubTree();
        box.FadeOut(100);

        Assert.Equal(0, completed);
        Assert.Equal(1, aborted);
        Assert.Equal(1, finallyCount);

        // Advance well past initial MoveToX end; MoveToX must NOT loop and OnComplete must NOT fire.
        manualClock.Seek(500);
        box.UpdateSubTree();

        Assert.Equal(0f, box.Alpha);
        Assert.Equal(100f, box.X);
        Assert.Equal(0, completed);
        Assert.Equal(1, aborted);
        Assert.Equal(1, finallyCount);
        Assert.False(box.HasTransforms);
    }

    [Fact]
    public void SequenceValidationRejectsInvalidInputsAndUninitializedSequences()
    {
        using var box = new Box();

        TransformSequence<Box> uninitialized = default;
        Assert.Throws<InvalidOperationException>(() => uninitialized.Then());
        Assert.Throws<InvalidOperationException>(() => uninitialized.Delay(10));
        Assert.Throws<InvalidOperationException>(() => uninitialized.Loop());
        Assert.Throws<InvalidOperationException>(() => uninitialized.OnComplete(static _ => { }));
        Assert.Throws<InvalidOperationException>(() => uninitialized.Expire());

        var seq = box.FadeIn(100);
        Assert.Throws<ArgumentOutOfRangeException>(() => seq.Then(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => seq.Then(double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => seq.Delay(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => seq.Delay(double.PositiveInfinity));
        Assert.Throws<ArgumentOutOfRangeException>(() => seq.Loop(pause: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => seq.Loop(numIterations: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => seq.Loop(numIterations: -2));
        Assert.Throws<ArgumentOutOfRangeException>(() => box.FadeIn(0).Loop(pause: 0, numIterations: -1));

        var infinite = box.FadeIn(100).Loop();
        Assert.Throws<InvalidOperationException>(() => infinite.Then());
        Assert.Throws<InvalidOperationException>(() => infinite.Delay(50));
    }

    [Fact]
    public void HeadlessGameHostRunsChainedSequencesAndExpiresTransientDrawables()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"kajarlabs-seq-host-{Guid.NewGuid():N}");

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

            using var game = new SequenceHostGame();
            host.RunFrames(game, 10);

            Assert.True(game.SequenceCompleted);
            Assert.True(game.TransientDisposed);
            Assert.Empty(game.HostContainer.Children);
        }
        finally
        {
            if (Directory.Exists(tempPath))
                Directory.Delete(tempPath, recursive: true);
        }
    }

    private sealed class SequenceHostGame : Game
    {
        private readonly TestManualClock manualClock = new(start: true, initialTime: 0);
        private Box? target;

        public Container HostContainer { get; private set; } = null!;

        public bool SequenceCompleted { get; private set; }

        public bool TransientDisposed => target?.IsDisposed ?? false;

        protected override void Load()
        {
            HostContainer = new Container
            {
                Clock = new FramedClock(manualClock),
                Size = new Vector2(400f, 400f),
            };

            target = new Box
            {
                Size = new Vector2(40f, 40f),
                Alpha = 0f,
                Position = Vector2.Zero,
            };

            HostContainer.Add(target);
            Root.Add(HostContainer);

            target.FadeIn(100)
                .Then()
                .MoveTo(new Vector2(200f, 100f), 200, Easing.OutQuad)
                .Then()
                .FadeOut(100)
                .OnComplete(_ => SequenceCompleted = true)
                .Expire();
        }

        protected override void Update()
        {
            manualClock.Advance(100);
        }
    }
}
