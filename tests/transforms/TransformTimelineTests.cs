using System.Numerics;
using kajarlabs.Framework.Bindables;
using kajarlabs.Framework.Numerics;
using kajarlabs.Framework.Timing;

namespace kajarlabs.Framework.Scene.Transforms.Tests;

public sealed class TransformTimelineTests
{
    [Fact]
    public void TransformsDoNotMutateBeforeStartTimeAndInterpolateAccurately()
    {
        var manualClock = new TestManualClock(initialTime: 0);
        var clock = new FramedClock(manualClock);
        using var box = new Box { Clock = clock, Alpha = 0.2f };

        var state = DrawableTransformState.GetOrAttach(box);
        state.AddBuiltInTransform(box, BuiltInTransformTarget.Alpha, TransformValue.FromSingle(1f), startTime: 100, duration: 200, easing: Easing.InQuad);

        Assert.True(box.HasTransforms);
        Assert.Equal(300, box.LatestTransformEndTime);

        // Before StartTime: untouched.
        manualClock.Seek(50);
        box.UpdateSubTree();
        Assert.Equal(0.2f, box.Alpha);

        // Exactly at StartTime: captures 0.2f, evaluates at t=0 -> 0.2f.
        manualClock.Seek(100);
        box.UpdateSubTree();
        Assert.Equal(0.2f, box.Alpha);

        // Halfway at t=200 (normalized 0.5 -> InQuad 0.25): 0.2 + 0.8 * 0.25 = 0.4.
        manualClock.Seek(200);
        box.UpdateSubTree();
        Assert.Equal(0.4f, box.Alpha, precision: 5);

        // Past EndTime: clamps to exact EndValue and removes completed transform.
        manualClock.Seek(350);
        box.UpdateSubTree();
        Assert.Equal(1f, box.Alpha);
        Assert.False(box.HasTransforms);
    }

    [Fact]
    public void OutOfOrderDistinctPropertiesExecuteInStartTimeOrderAndChainedTransformsCaptureStartValuesAcrossLargeSteps()
    {
        var manualClock = new TestManualClock(initialTime: 0);
        var clock = new FramedClock(manualClock);
        using var box = new Box { Clock = clock, Alpha = 0f, X = 0f, Y = 0f };

        var order = new List<string>();
        var state = DrawableTransformState.GetOrAttach(box);

        // Schedule distinct properties intentionally out of chronological order; execution must follow StartTime order.
        state.AddBuiltInTransform(box, BuiltInTransformTarget.Y, TransformValue.FromSingle(30f), startTime: 200, duration: 50, observer: new OrderObserver("Y", order));
        state.AddBuiltInTransform(box, BuiltInTransformTarget.X, TransformValue.FromSingle(10f), startTime: 0, duration: 50, observer: new OrderObserver("X", order));
        state.AddBuiltInTransform(box, BuiltInTransformTarget.Rotation, TransformValue.FromSingle(90f), startTime: 100, duration: 50, observer: new OrderObserver("Rotation", order));

        // Schedule chained Alpha transforms chronologically; a single frame jump skips T1 and T2 and lands halfway into T3.
        state.AddBuiltInTransform(box, BuiltInTransformTarget.Alpha, TransformValue.FromSingle(1f), startTime: 0, duration: 100);
        state.AddBuiltInTransform(box, BuiltInTransformTarget.Alpha, TransformValue.FromSingle(0.4f), startTime: 100, duration: 100);
        state.AddBuiltInTransform(box, BuiltInTransformTarget.Alpha, TransformValue.FromSingle(0f), startTime: 200, duration: 100);

        // Single large frame jump to t=250:
        // - X, Rotation, Y all complete in exact StartTime order (0 -> 100 -> 200).
        // - Alpha T1 sets 1.0 -> T2 captures 1.0 and sets 0.4 -> T3 captures 0.4 and interpolates halfway to 0.0 -> 0.2.
        manualClock.Seek(250);
        box.UpdateSubTree();

        Assert.Equal(["X", "Rotation", "Y"], order);
        Assert.Equal(0.2f, box.Alpha, precision: 5);
        Assert.True(box.HasTransforms);

        manualClock.Seek(300);
        box.UpdateSubTree();

        Assert.Equal(0f, box.Alpha);
        Assert.False(box.HasTransforms);
    }

    [Fact]
    public void ZeroDurationTransformAppliesAtStartTimeAndChainsCleanlyWithFollowingTransform()
    {
        var manualClock = new TestManualClock(initialTime: 0);
        var clock = new FramedClock(manualClock);
        using var box = new Box { Clock = clock, Alpha = 1f };

        var observer = new RecordingObserver();
        var state = DrawableTransformState.GetOrAttach(box);

        // FadeInFromZero pattern: instant set to 0 at t=100, followed by 0 -> 1 over [100, 300].
        state.AddBuiltInTransform(box, BuiltInTransformTarget.Alpha, TransformValue.FromSingle(0f), startTime: 100, duration: 0, observer: observer);
        state.AddBuiltInTransform(box, BuiltInTransformTarget.Alpha, TransformValue.FromSingle(1f), startTime: 100, duration: 200);

        // Before t=100: zero-duration transform must not have mutated Alpha yet.
        manualClock.Seek(99);
        box.UpdateSubTree();
        Assert.Equal(1f, box.Alpha);
        Assert.Equal(0, observer.CompletedCount);

        // Jump straight to t=150: zero-duration applies 0f and completes, then second transform captures 0f and reaches 0.25f.
        manualClock.Seek(150);
        box.UpdateSubTree();
        Assert.Equal(0.25f, box.Alpha, precision: 5);
        Assert.Equal(1, observer.CompletedCount);
        Assert.Equal(0, observer.AbortedCount);
    }

    [Fact]
    public void AllBuiltInTargetsInterpolateCorrectly()
    {
        var manualClock = new TestManualClock(initialTime: 0);
        var clock = new FramedClock(manualClock);
        using var container = new Container
        {
            Clock = clock,
            Alpha = 0f,
            Color = Color4.BLACK,
            Position = Vector2.Zero,
            Size = Vector2.Zero,
            Scale = Vector2.One,
            Rotation = 0f,
            Shear = Vector2.Zero,
            Margin = MarginPadding.ZERO,
            Padding = MarginPadding.ZERO,
        };

        var state = DrawableTransformState.GetOrAttach(container);
        state.AddBuiltInTransform(container, BuiltInTransformTarget.Alpha, TransformValue.FromSingle(1f), 0, 100);
        state.AddBuiltInTransform(container, BuiltInTransformTarget.Color, TransformValue.FromColor4(Color4.WHITE), 0, 100);
        state.AddBuiltInTransform(container, BuiltInTransformTarget.Position, TransformValue.FromVector2(new Vector2(100f, 200f)), 0, 100);
        state.AddBuiltInTransform(container, BuiltInTransformTarget.Size, TransformValue.FromVector2(new Vector2(300f, 400f)), 0, 100);
        state.AddBuiltInTransform(container, BuiltInTransformTarget.Scale, TransformValue.FromVector2(new Vector2(3f, 5f)), 0, 100);
        state.AddBuiltInTransform(container, BuiltInTransformTarget.Rotation, TransformValue.FromSingle(90f), 0, 100);
        state.AddBuiltInTransform(container, BuiltInTransformTarget.Shear, TransformValue.FromVector2(new Vector2(0.5f, -0.5f)), 0, 100);
        state.AddBuiltInTransform(container, BuiltInTransformTarget.Margin, TransformValue.FromMarginPadding(new MarginPadding(10f, 20f, 30f, 40f)), 0, 100);
        state.AddBuiltInTransform(container, BuiltInTransformTarget.Padding, TransformValue.FromMarginPadding(new MarginPadding(4f, 8f, 12f, 16f)), 0, 100);

        manualClock.Seek(50);
        container.UpdateSubTree();

        Assert.Equal(0.5f, container.Alpha, precision: 5);
        Assert.Equal(new Color4(0.5f, 0.5f, 0.5f, 1f), container.Color);
        Assert.Equal(new Vector2(50f, 100f), container.Position);
        Assert.Equal(new Vector2(150f, 200f), container.Size);
        Assert.Equal(new Vector2(2f, 3f), container.Scale);
        Assert.Equal(45f, container.Rotation, precision: 5);
        Assert.Equal(new Vector2(0.25f, -0.25f), container.Shear);
        Assert.Equal(new MarginPadding(5f, 10f, 15f, 20f), container.Margin);
        Assert.Equal(new MarginPadding(2f, 4f, 6f, 8f), container.Padding);

        // Single-axis targets (X, Y, Width, Height).
        state.AddBuiltInTransform(container, BuiltInTransformTarget.X, TransformValue.FromSingle(250f), 100, 100);
        state.AddBuiltInTransform(container, BuiltInTransformTarget.Y, TransformValue.FromSingle(400f), 100, 100);
        state.AddBuiltInTransform(container, BuiltInTransformTarget.Width, TransformValue.FromSingle(500f), 100, 100);
        state.AddBuiltInTransform(container, BuiltInTransformTarget.Height, TransformValue.FromSingle(600f), 100, 100);

        manualClock.Seek(150);
        container.UpdateSubTree();

        Assert.Equal(175f, container.X, precision: 5);
        Assert.Equal(300f, container.Y, precision: 5);
        Assert.Equal(400f, container.Width, precision: 5);
        Assert.Equal(500f, container.Height, precision: 5);
    }

    [Fact]
    public void CustomEasingCustomPropertyAndBindableHandlersInterpolateWithoutReflection()
    {
        var manualClock = new TestManualClock(initialTime: 0);
        var clock = new FramedClock(manualClock);
        using var target = new CustomPropertyDrawable { Clock = clock, Alpha = 0f, Progress = 10.0 };
        var bindable = new Bindable<float>(0f);

        var bezier = CubicBezierEasingFunction.EASE_IN_OUT;
        var state = DrawableTransformState.GetOrAttach(target);

        state.AddCustomTransform(
            target,
            new BuiltInCustomEasingHandler<CubicBezierEasingFunction>(
                BuiltInTransformTarget.Alpha,
                default,
                TransformValue.FromSingle(1f),
                bezier),
            startTime: 0,
            duration: 100);

        state.AddCustomTransform(
            target,
            new DelegateTransformHandler<CustomPropertyDrawable, double, DefaultEasingFunction, DoubleInterpolator>(
                TransformTargetKey.FromCustomName(nameof(CustomPropertyDrawable.Progress)),
                static d => d.Progress,
                static (d, v) => d.Progress = v,
                startValue: 0.0,
                endValue: 110.0,
                easing: Easing.None,
                interpolator: default),
            startTime: 0,
            duration: 100);

        state.AddCustomTransform(
            target,
            new BindableTransformHandler<float, DefaultEasingFunction, SingleInterpolator>(
                bindable,
                startValue: 0f,
                endValue: 200f,
                easing: Easing.OutQuad,
                interpolator: default),
            startTime: 0,
            duration: 100);

        manualClock.Seek(25);
        target.UpdateSubTree();

        Assert.Equal((float)bezier.ApplyEasing(0.25), target.Alpha, precision: 5);
        Assert.Equal(35.0, target.Progress, precision: 10);
        Assert.Equal((float)(200.0 * DefaultEasingFunction.ApplyEasing(Easing.OutQuad, 0.25)), bindable.Value, precision: 4);

        manualClock.Seek(100);
        target.UpdateSubTree();

        Assert.Equal(1f, target.Alpha);
        Assert.Equal(110.0, target.Progress);
        Assert.Equal(200f, bindable.Value);
    }

    [Fact]
    public void ScheduleValidationRejectsInvalidDurationsTimesTargetsAndEasings()
    {
        using var box = new Box();
        var state = DrawableTransformState.GetOrAttach(box);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            state.AddBuiltInTransform(box, BuiltInTransformTarget.Alpha, TransformValue.FromSingle(1f), startTime: 0, duration: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            state.AddBuiltInTransform(box, BuiltInTransformTarget.Alpha, TransformValue.FromSingle(1f), startTime: 0, duration: double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            state.AddBuiltInTransform(box, BuiltInTransformTarget.Alpha, TransformValue.FromSingle(1f), startTime: 0, duration: double.PositiveInfinity));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            state.AddBuiltInTransform(box, BuiltInTransformTarget.Alpha, TransformValue.FromSingle(1f), startTime: double.NaN, duration: 10));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            state.AddBuiltInTransform(box, BuiltInTransformTarget.Alpha, TransformValue.FromSingle(1f), startTime: double.MaxValue, duration: double.MaxValue));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            state.AddBuiltInTransform(box, BuiltInTransformTarget.Alpha, TransformValue.FromSingle(1f), startTime: 0, duration: 10, easing: (Easing)999));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            state.AddBuiltInTransform(box, BuiltInTransformTarget.Alpha, TransformValue.FromSingle(1f), startTime: 0, duration: 10, loopDelay: 0, remainingLoops: 2));
        Assert.Throws<ArgumentException>(() =>
            state.AddBuiltInTransform(box, BuiltInTransformTarget.Padding, TransformValue.FromMarginPadding(MarginPadding.ZERO), startTime: 0, duration: 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => box.ClearTransformsAfter(double.NaN));
    }

    private sealed class CustomPropertyDrawable : Drawable
    {
        public double Progress { get; set; }
    }

    private sealed class RecordingObserver : ITransformObserver
    {
        public int CompletedCount { get; private set; }

        public int AbortedCount { get; private set; }

        public void OnTransformCompleted(Drawable target) => CompletedCount++;

        public void OnTransformAborted(Drawable target) => AbortedCount++;
    }

    private sealed class OrderObserver : ITransformObserver
    {
        private readonly string label;
        private readonly List<string> log;

        public OrderObserver(string label, List<string> log)
        {
            this.label = label;
            this.log = log;
        }

        public void OnTransformCompleted(Drawable target) => log.Add(label);

        public void OnTransformAborted(Drawable target)
        {
        }
    }
}
