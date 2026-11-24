using System.Numerics;
using kajarlabs.Framework.Bindables;
using kajarlabs.Framework.Numerics;
using kajarlabs.Framework.Timing;

namespace kajarlabs.Framework.Scene.Transforms.Tests;

public sealed class TransformAllocationTests
{
    [Fact]
    public void UnusedDrawableControlCallsAndUpdatesAllocateZeroBytes()
    {
        using var box = new Box();

        // Warm up JIT.
        _ = box.RemoveCompletedTransforms;
        _ = box.HasTransforms;
        _ = box.LatestTransformEndTime;
        box.RemoveCompletedTransforms = true;
        box.ClearTransforms();
        box.ClearTransformsAfter(100);
        box.FinishTransforms();
        box.UpdateSubTree();

        var startAllocated = GC.GetAllocatedBytesForCurrentThread();

        for (var i = 0; i < 64; i++)
        {
            _ = box.RemoveCompletedTransforms;
            _ = box.HasTransforms;
            _ = box.LatestTransformEndTime;
            box.RemoveCompletedTransforms = true;
            box.ClearTransforms();
            box.ClearTransformsAfter(100);
            box.FinishTransforms();
            box.UpdateSubTree();
        }

        var endAllocated = GC.GetAllocatedBytesForCurrentThread();
        Assert.Equal(0, endAllocated - startAllocated);
        Assert.False(box.TryGetExtension<DrawableTransformState>(out _));
    }

    [Fact]
    public void ActiveTimelineUpdateHotPathAllocatesZeroBytesAcrossBuiltInCustomEasingAndDelegateTransforms()
    {
        var manualClock = new TestManualClock(initialTime: 0);
        var clock = new FramedClock(manualClock);
        using var container = new CustomValueContainer { Clock = clock, Alpha = 0f, Position = Vector2.Zero };

        var state = DrawableTransformState.GetOrAttach(container);
        state.AddBuiltInTransform(container, BuiltInTransformTarget.Alpha, TransformValue.FromSingle(1f), 0, 1000, Easing.InOutCubic);
        state.AddBuiltInTransform(container, BuiltInTransformTarget.Position, TransformValue.FromVector2(new Vector2(500f, 300f)), 0, 1000, Easing.OutExpo);
        state.AddBuiltInTransform(container, BuiltInTransformTarget.Color, TransformValue.FromColor4(Color4.WHITE), 0, 1000, Easing.InSine);
        state.AddBuiltInTransform(container, BuiltInTransformTarget.Padding, TransformValue.FromMarginPadding(new MarginPadding(20f)), 0, 1000, Easing.None);
        state.AddCustomTransform(
            container,
            new BuiltInCustomEasingHandler<CubicBezierEasingFunction>(
                BuiltInTransformTarget.Rotation,
                default,
                TransformValue.FromSingle(180f),
                CubicBezierEasingFunction.EASE_IN_OUT),
            0,
            1000);
        state.AddCustomTransform(
            container,
            new DelegateTransformHandler<CustomValueContainer, double, DefaultEasingFunction, DoubleInterpolator>(
                TransformTargetKey.FromCustomName(nameof(CustomValueContainer.CustomMetric)),
                static d => d.CustomMetric,
                static (d, v) => d.CustomMetric = v,
                0.0,
                100.0,
                Easing.OutQuad,
                default),
            0,
            1000);

        // Warm up JIT and start-value capture.
        manualClock.Seek(10);
        container.UpdateSubTree();
        manualClock.Seek(20);
        container.UpdateSubTree();

        var startAllocated = GC.GetAllocatedBytesForCurrentThread();

        for (var step = 1; step <= 60; step++)
        {
            manualClock.Seek(20 + (step * 10));
            container.UpdateSubTree();
        }

        var endAllocated = GC.GetAllocatedBytesForCurrentThread();

        Assert.Equal(0, endAllocated - startAllocated);
        Assert.True(container.Alpha > 0.5f);
        Assert.True(container.CustomMetric > 50.0);
    }

    [Fact]
    public void BindableTransformHandlerAddsZeroOverheadBeyondDirectBindableAssignment()
    {
        var manualClock = new TestManualClock(initialTime: 0);
        var clock = new FramedClock(manualClock);
        using var box = new Box { Clock = clock };
        var transformBindable = new Bindable<float>(0f);
        var directBindable = new Bindable<float>(0f);

        var state = DrawableTransformState.GetOrAttach(box);
        state.AddCustomTransform(
            box,
            new BindableTransformHandler<float, DefaultEasingFunction, SingleInterpolator>(transformBindable, 0f, 100f, Easing.InQuad, default),
            0,
            1000);

        // Warm up both paths.
        manualClock.Seek(10);
        box.UpdateSubTree();
        directBindable.Value = 1f;

        const int steps = 40;

        var directStart = GC.GetAllocatedBytesForCurrentThread();
        for (var step = 1; step <= steps; step++)
            directBindable.Value = step + 10f;
        var directAllocated = GC.GetAllocatedBytesForCurrentThread() - directStart;

        var transformStart = GC.GetAllocatedBytesForCurrentThread();
        for (var step = 1; step <= steps; step++)
        {
            manualClock.Seek(20 + (step * 10));
            box.UpdateSubTree();
        }

        var transformAllocated = GC.GetAllocatedBytesForCurrentThread() - transformStart;

        Assert.Equal(directAllocated, transformAllocated);
        Assert.True(transformBindable.Value > 10f);
    }

    [Fact]
    public void RewindAndLoopHotPathsAllocateZeroBytes()
    {
        var manualClock = new TestManualClock(initialTime: 0);
        var clock = new FramedClock(manualClock);
        using var rewindBox = new Box { Clock = clock, Alpha = 0f };
        rewindBox.RemoveCompletedTransforms = false;

        var rewindState = DrawableTransformState.GetOrAttach(rewindBox);
        rewindState.AddBuiltInTransform(rewindBox, BuiltInTransformTarget.Alpha, TransformValue.FromSingle(1f), 0, 100);
        rewindState.AddBuiltInTransform(rewindBox, BuiltInTransformTarget.Alpha, TransformValue.FromSingle(0f), 100, 100);

        using var loopBox = new Box { Clock = clock, X = 0f };
        var loopState = DrawableTransformState.GetOrAttach(loopBox);
        loopState.AddBuiltInTransform(loopBox, BuiltInTransformTarget.X, TransformValue.FromSingle(100f), 0, 100, hasExplicitStartValue: true, startValue: TransformValue.FromSingle(0f), loopDelay: 100, remainingLoops: -1);

        // Warm up forward, rewind, and loop transitions.
        manualClock.Seek(50);
        rewindBox.UpdateSubTree();
        loopBox.UpdateSubTree();
        manualClock.Seek(250);
        rewindBox.UpdateSubTree();
        loopBox.UpdateSubTree();
        manualClock.Seek(25);
        rewindBox.UpdateSubTree();

        var startAllocated = GC.GetAllocatedBytesForCurrentThread();

        for (var i = 0; i < 40; i++)
        {
            manualClock.Seek(150);
            rewindBox.UpdateSubTree();
            manualClock.Seek(50);
            rewindBox.UpdateSubTree();
        }

        for (var i = 1; i <= 40; i++)
        {
            manualClock.Seek(250 + (i * 60));
            loopBox.UpdateSubTree();
        }

        var endAllocated = GC.GetAllocatedBytesForCurrentThread();
        Assert.Equal(0, endAllocated - startAllocated);
    }

    private sealed class CustomValueContainer : Container
    {
        public double CustomMetric { get; set; }
    }
}
