using System.Numerics;
using kajarlabs.Framework.Bindables;
using kajarlabs.Framework.Timing;

namespace kajarlabs.Framework.Scene.Transforms.Tests;

public sealed class TransformConflictAndControlTests
{
    [Fact]
    public void ImmediateOverrideAbortsActiveTransformWithoutSnappingOrClobberingManualValue()
    {
        var manualClock = new TestManualClock(initialTime: 0);
        var clock = new FramedClock(manualClock);
        using var box = new Box { Clock = clock, Alpha = 0f };

        var firstObserver = new RecordingObserver();
        var secondObserver = new RecordingObserver();
        var state = DrawableTransformState.GetOrAttach(box);

        state.AddBuiltInTransform(box, BuiltInTransformTarget.Alpha, TransformValue.FromSingle(1f), 0, 1000, observer: firstObserver);

        manualClock.Seek(200);
        box.UpdateSubTree();
        Assert.Equal(0.2f, box.Alpha, precision: 5);

        // User manually overrides Alpha before starting a new transform at currentTime.
        box.Alpha = 0.8f;
        state.AddBuiltInTransform(box, BuiltInTransformTarget.Alpha, TransformValue.FromSingle(0f), 200, 400, observer: secondObserver);

        Assert.Equal(1, firstObserver.AbortedCount);
        Assert.Equal(0, firstObserver.CompletedCount);
        Assert.Equal(0.8f, box.Alpha);

        manualClock.Seek(400);
        box.UpdateSubTree();
        Assert.Equal(0.4f, box.Alpha, precision: 5);

        manualClock.Seek(600);
        box.UpdateSubTree();
        Assert.Equal(0f, box.Alpha);
        Assert.Equal(1, secondObserver.CompletedCount);
    }

    [Fact]
    public void FutureOverrideTruncatesAlongOriginalCurveAndHandsOffIntermediateValue()
    {
        var manualClock = new TestManualClock(initialTime: 0);
        var clock = new FramedClock(manualClock);
        using var box = new Box { Clock = clock, X = 0f };

        var firstObserver = new RecordingObserver();
        var secondObserver = new RecordingObserver();
        var state = DrawableTransformState.GetOrAttach(box);

        // A: 0 -> 100 over [0, 1000].
        state.AddBuiltInTransform(box, BuiltInTransformTarget.X, TransformValue.FromSingle(100f), 0, 1000, observer: firstObserver);

        // B scheduled at t=200 to start in the future at t=500 -> 0 over [500, 1000].
        manualClock.Seek(200);
        box.UpdateSubTree();
        Assert.Equal(20f, box.X, precision: 4);

        state.AddBuiltInTransform(box, BuiltInTransformTarget.X, TransformValue.FromSingle(0f), 500, 500, observer: secondObserver);

        // Not aborted yet because newStartTime (500) > currentTime (200).
        Assert.Equal(0, firstObserver.AbortedCount);

        // At t=400, A still interpolates smoothly along its original [0, 1000] curve -> 40.
        manualClock.Seek(400);
        box.UpdateSubTree();
        Assert.Equal(40f, box.X, precision: 4);
        Assert.Equal(0, firstObserver.AbortedCount);

        // Frame jumps directly from 400 to 750:
        // A evaluates at AbortTime=500 (X=50), aborts without snapping to 100,
        // and B captures StartValue=50 and interpolates halfway to 0 -> 25.
        manualClock.Seek(750);
        box.UpdateSubTree();
        Assert.Equal(25f, box.X, precision: 4);
        Assert.Equal(1, firstObserver.AbortedCount);
        Assert.Equal(0, firstObserver.CompletedCount);

        manualClock.Seek(1000);
        box.UpdateSubTree();
        Assert.Equal(0f, box.X);
        Assert.Equal(1, secondObserver.CompletedCount);
    }

    [Fact]
    public void FutureTransformStartingAtOrAfterNewStartTimeIsRemovedAndAbortedImmediately()
    {
        var manualClock = new TestManualClock(initialTime: 0);
        var clock = new FramedClock(manualClock);
        using var box = new Box { Clock = clock, Alpha = 1f };

        var supersededObserver = new RecordingObserver();
        var state = DrawableTransformState.GetOrAttach(box);

        state.AddBuiltInTransform(box, BuiltInTransformTarget.Alpha, TransformValue.FromSingle(0f), 200, 100, observer: supersededObserver);
        state.AddBuiltInTransform(box, BuiltInTransformTarget.Alpha, TransformValue.FromSingle(0.5f), 100, 300);

        Assert.Equal(1, supersededObserver.AbortedCount);
        Assert.Equal(0, supersededObserver.CompletedCount);
        Assert.Equal(400, box.LatestTransformEndTime);
    }

    [Fact]
    public void CompositeAxisCrossConflictsMatchDecisionMatrix()
    {
        var manualClock = new TestManualClock(initialTime: 0);
        var clock = new FramedClock(manualClock);
        using var box = new Box { Clock = clock, Position = Vector2.Zero, Size = Vector2.Zero };

        var xObserver = new RecordingObserver();
        var yObserver = new RecordingObserver();
        var widthObserver = new RecordingObserver();
        var heightObserver = new RecordingObserver();
        var state = DrawableTransformState.GetOrAttach(box);

        // Independent axes X and Y do NOT conflict.
        state.AddBuiltInTransform(box, BuiltInTransformTarget.X, TransformValue.FromSingle(100f), 0, 200, observer: xObserver);
        state.AddBuiltInTransform(box, BuiltInTransformTarget.Y, TransformValue.FromSingle(200f), 0, 200, observer: yObserver);

        manualClock.Seek(100);
        box.UpdateSubTree();
        Assert.Equal(new Vector2(50f, 100f), box.Position);
        Assert.Equal(0, xObserver.AbortedCount);
        Assert.Equal(0, yObserver.AbortedCount);

        // Position conflicts with both X and Y.
        state.AddBuiltInTransform(box, BuiltInTransformTarget.Position, TransformValue.FromVector2(new Vector2(10f, 20f)), 100, 100);
        Assert.Equal(1, xObserver.AbortedCount);
        Assert.Equal(1, yObserver.AbortedCount);

        // Independent axes Width and Height do NOT conflict, but Size conflicts with both.
        state.AddBuiltInTransform(box, BuiltInTransformTarget.Width, TransformValue.FromSingle(80f), 100, 200, observer: widthObserver);
        state.AddBuiltInTransform(box, BuiltInTransformTarget.Height, TransformValue.FromSingle(40f), 100, 200, observer: heightObserver);
        Assert.Equal(0, widthObserver.AbortedCount);
        Assert.Equal(0, heightObserver.AbortedCount);

        state.AddBuiltInTransform(box, BuiltInTransformTarget.Size, TransformValue.FromVector2(new Vector2(200f, 200f)), 150, 100);

        manualClock.Seek(150);
        box.UpdateSubTree();
        Assert.Equal(1, widthObserver.AbortedCount);
        Assert.Equal(1, heightObserver.AbortedCount);
    }

    [Fact]
    public void CustomTargetKeysIsolateDistinctNamesAndBindableInstances()
    {
        var manualClock = new TestManualClock(initialTime: 0);
        var clock = new FramedClock(manualClock);
        using var target = new DualValueDrawable { Clock = clock };
        var bindableA = new Bindable<float>(0f);
        var bindableB = new Bindable<float>(0f);

        var obsA = new RecordingObserver();
        var obsB = new RecordingObserver();
        var obsBindableA = new RecordingObserver();
        var obsBindableB = new RecordingObserver();
        var state = DrawableTransformState.GetOrAttach(target);

        state.AddCustomTransform(
            target,
            new DelegateTransformHandler<DualValueDrawable, float, DefaultEasingFunction, SingleInterpolator>(
                TransformTargetKey.FromCustomName("First"),
                static d => d.First,
                static (d, v) => d.First = v,
                0f,
                100f,
                Easing.None,
                default),
            0,
            100,
            observer: obsA);

        state.AddCustomTransform(
            target,
            new DelegateTransformHandler<DualValueDrawable, float, DefaultEasingFunction, SingleInterpolator>(
                TransformTargetKey.FromCustomName("Second"),
                static d => d.Second,
                static (d, v) => d.Second = v,
                0f,
                200f,
                Easing.None,
                default),
            0,
            100,
            observer: obsB);

        state.AddCustomTransform(
            target,
            new BindableTransformHandler<float, DefaultEasingFunction, SingleInterpolator>(bindableA, 0f, 10f, Easing.None, default),
            0,
            100,
            observer: obsBindableA);

        state.AddCustomTransform(
            target,
            new BindableTransformHandler<float, DefaultEasingFunction, SingleInterpolator>(bindableB, 0f, 20f, Easing.None, default),
            0,
            100,
            observer: obsBindableB);

        Assert.Equal(0, obsA.AbortedCount);
        Assert.Equal(0, obsB.AbortedCount);
        Assert.Equal(0, obsBindableA.AbortedCount);
        Assert.Equal(0, obsBindableB.AbortedCount);

        // Conflicting with "First" and bindableA only.
        state.AddCustomTransform(
            target,
            new DelegateTransformHandler<DualValueDrawable, float, DefaultEasingFunction, SingleInterpolator>(
                TransformTargetKey.FromCustomName("First"),
                static d => d.First,
                static (d, v) => d.First = v,
                0f,
                50f,
                Easing.None,
                default),
            0,
            100);

        state.AddCustomTransform(
            target,
            new BindableTransformHandler<float, DefaultEasingFunction, SingleInterpolator>(bindableA, 0f, 99f, Easing.None, default),
            0,
            100);

        Assert.Equal(1, obsA.AbortedCount);
        Assert.Equal(0, obsB.AbortedCount);
        Assert.Equal(1, obsBindableA.AbortedCount);
        Assert.Equal(0, obsBindableB.AbortedCount);
    }

    [Fact]
    public void ClearTransformsAndClearTransformsAfterAbortAndTruncateDeterministically()
    {
        var manualClock = new TestManualClock(initialTime: 0);
        var clock = new FramedClock(manualClock);
        using var box = new Box { Clock = clock, Alpha = 0f, X = 0f };

        var firstObs = new RecordingObserver();
        var secondObs = new RecordingObserver();
        var thirdObs = new RecordingObserver();
        var state = DrawableTransformState.GetOrAttach(box);

        state.AddBuiltInTransform(box, BuiltInTransformTarget.Alpha, TransformValue.FromSingle(1f), 0, 100, observer: firstObs);
        state.AddBuiltInTransform(box, BuiltInTransformTarget.Alpha, TransformValue.FromSingle(0f), 100, 200, observer: secondObs);
        state.AddBuiltInTransform(box, BuiltInTransformTarget.X, TransformValue.FromSingle(100f), 0, 400, observer: thirdObs);

        // ClearTransformsAfter(100):
        // - Alpha [0, 100] ends at 100 -> preserved.
        // - Alpha [100, 300] starts at 100 -> removed & aborted immediately.
        // - X [0, 400] spans 100 -> truncated at 100 (animates to 25 at t=100, then aborts).
        box.ClearTransformsAfter(100);

        Assert.Equal(0, firstObs.AbortedCount);
        Assert.Equal(1, secondObs.AbortedCount);
        Assert.Equal(0, thirdObs.AbortedCount);

        manualClock.Seek(150);
        box.UpdateSubTree();

        Assert.Equal(1f, box.Alpha);
        Assert.Equal(25f, box.X, precision: 4);
        Assert.Equal(1, firstObs.CompletedCount);
        Assert.Equal(1, thirdObs.AbortedCount);
        Assert.False(box.HasTransforms);

        // Schedule new transforms and ClearTransforms().
        var clearObs = new RecordingObserver();
        state.AddBuiltInTransform(box, BuiltInTransformTarget.Alpha, TransformValue.FromSingle(0f), 150, 200, observer: clearObs);
        box.ClearTransforms();

        Assert.Equal(1, clearObs.AbortedCount);
        Assert.False(box.HasTransforms);
        Assert.Equal(1f, box.Alpha);
    }

    [Fact]
    public void FinishTransformsCompletesFiniteAndTruncatedTransformsAndStopsInfiniteLoopsWithoutHanging()
    {
        var manualClock = new TestManualClock(initialTime: 0);
        var clock = new FramedClock(manualClock);
        using var box = new Box { Clock = clock, Alpha = 0f, X = 0f, Y = 0f, Rotation = 0f };

        var truncatedObs = new RecordingObserver();
        var followUpObs = new RecordingObserver();
        var finiteLoopObs = new RecordingObserver();
        var infiniteLoopObs = new RecordingObserver();
        var reentrantScheduledCount = 0;

        var state = DrawableTransformState.GetOrAttach(box);

        // Truncated X [0, 1000] -> 100, truncated at 500 by follow-up X [500, 700] -> 200.
        state.AddBuiltInTransform(box, BuiltInTransformTarget.X, TransformValue.FromSingle(100f), 0, 1000, observer: truncatedObs);
        state.AddBuiltInTransform(box, BuiltInTransformTarget.X, TransformValue.FromSingle(200f), 500, 200, observer: followUpObs);

        // Finite loop (3 total iterations).
        state.AddBuiltInTransform(box, BuiltInTransformTarget.Y, TransformValue.FromSingle(50f), 0, 100, observer: finiteLoopObs, loopDelay: 150, remainingLoops: 2);

        // Infinite loop (-1): must terminate cleanly in FinishTransforms without hanging.
        state.AddBuiltInTransform(box, BuiltInTransformTarget.Rotation, TransformValue.FromSingle(360f), 0, 100, observer: infiniteLoopObs, loopDelay: 100, remainingLoops: -1);

        // Callback that schedules a new transform during FinishTransforms must NOT recurse infinitely.
        var reentrantObs = new CallbackObserver(onCompleted: d =>
        {
            reentrantScheduledCount++;
            DrawableTransformState.GetOrAttach(d).AddBuiltInTransform(d, BuiltInTransformTarget.Alpha, TransformValue.FromSingle(0.25f), 0, 100);
        });
        state.AddBuiltInTransform(box, BuiltInTransformTarget.Alpha, TransformValue.FromSingle(1f), 0, 100, observer: reentrantObs);

        box.FinishTransforms();

        Assert.Equal(200f, box.X);
        Assert.Equal(50f, box.Y);
        Assert.Equal(360f, box.Rotation);
        Assert.Equal(1f, box.Alpha);
        Assert.Equal(1, truncatedObs.AbortedCount);
        Assert.Equal(1, followUpObs.CompletedCount);
        Assert.Equal(1, finiteLoopObs.CompletedCount);
        Assert.Equal(1, infiniteLoopObs.AbortedCount);
        Assert.Equal(1, reentrantScheduledCount);
        // The newly scheduled transform from onCompleted remains pending rather than being recursively finished.
        Assert.True(box.HasTransforms);
    }

    [Fact]
    public void ReentrantCallbacksInsideOnUpdateCanClearOrDisposeSafely()
    {
        var manualClock = new TestManualClock(initialTime: 0);
        var clock = new FramedClock(manualClock);
        var secondObs = new RecordingObserver();

        using (var box = new Box { Clock = clock, Alpha = 0f, X = 0f })
        {
            var state = DrawableTransformState.GetOrAttach(box);
            state.AddBuiltInTransform(
                box,
                BuiltInTransformTarget.Alpha,
                TransformValue.FromSingle(1f),
                0,
                100,
                observer: new CallbackObserver(onCompleted: static d => d.ClearTransforms()));
            state.AddBuiltInTransform(box, BuiltInTransformTarget.X, TransformValue.FromSingle(50f), 200, 100, observer: secondObs);

            manualClock.Seek(100);
            box.UpdateSubTree();

            Assert.Equal(1f, box.Alpha);
            Assert.False(box.HasTransforms);
            Assert.Equal(1, secondObs.AbortedCount);
        }

        // Self-disposing callback during OnUpdate.
        var selfDisposeObs = new RecordingObserver();
        var disposableBox = new Box { Clock = clock, Alpha = 0f, X = 0f };
        var disposeState = DrawableTransformState.GetOrAttach(disposableBox);
        disposeState.AddBuiltInTransform(
            disposableBox,
            BuiltInTransformTarget.Alpha,
            TransformValue.FromSingle(1f),
            100,
            50,
            observer: new CallbackObserver(onCompleted: static d => d.Dispose()));
        disposeState.AddBuiltInTransform(
            disposableBox,
            BuiltInTransformTarget.X,
            TransformValue.FromSingle(10f),
            100,
            50,
            observer: selfDisposeObs);

        manualClock.Seek(200);
        Assert.False(disposableBox.UpdateSubTree());
        Assert.True(disposableBox.IsDisposed);
        // Second callback must not fire after the drawable was disposed by the first callback.
        Assert.Equal(0, selfDisposeObs.CompletedCount);
    }

    private sealed class DualValueDrawable : Drawable
    {
        public float First { get; set; }

        public float Second { get; set; }
    }

    private sealed class RecordingObserver : ITransformObserver
    {
        public int CompletedCount { get; private set; }

        public int AbortedCount { get; private set; }

        public void OnTransformCompleted(Drawable target) => CompletedCount++;

        public void OnTransformAborted(Drawable target) => AbortedCount++;
    }

    private sealed class CallbackObserver : ITransformObserver
    {
        private readonly Action<Drawable>? onCompleted;
        private readonly Action<Drawable>? onAborted;

        public CallbackObserver(Action<Drawable>? onCompleted = null, Action<Drawable>? onAborted = null)
        {
            this.onCompleted = onCompleted;
            this.onAborted = onAborted;
        }

        public void OnTransformCompleted(Drawable target) => onCompleted?.Invoke(target);

        public void OnTransformAborted(Drawable target) => onAborted?.Invoke(target);
    }
}
