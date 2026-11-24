using System.Numerics;
using kajarlabs.Framework.Bindables;
using kajarlabs.Framework.Numerics;
using kajarlabs.Framework.Timing;

namespace kajarlabs.Framework.Scene.Transforms.Tests;

public sealed class DrawableBuiltInTransformExtensionsTests
{
    [Fact]
    public void OpacityAndColorExtensionsAnimateAndPreserveUnderlyingFlashColor()
    {
        var manualClock = new TestManualClock(initialTime: 0);
        var clock = new FramedClock(manualClock);
        using var box = new Box { Clock = clock, Alpha = 0.5f, Color = Color4.BLUE };

        box.FadeInFromZero(100);
        manualClock.Seek(50);
        box.UpdateSubTree();
        Assert.Equal(0.5f, box.Alpha, precision: 5);

        manualClock.Seek(100);
        box.UpdateSubTree();
        box.FadeOutFromOne(100);
        manualClock.Seek(150);
        box.UpdateSubTree();
        Assert.Equal(0.5f, box.Alpha, precision: 5);

        // Repeated FlashColor during an active FlashColor must return to original Color4.BLUE (no color drift).
        manualClock.Seek(200);
        box.UpdateSubTree();
        box.FlashColor(Color4.WHITE, 200);

        manualClock.Seek(250);
        box.UpdateSubTree();
        box.FlashColor(Color4.WHITE, 200);

        manualClock.Seek(450);
        box.UpdateSubTree();
        Assert.Equal(Color4.BLUE, box.Color);

        // Chained FadeColor().Then().FlashColor() returns to the scheduled target color.
        box.FadeColor(Color4.RED, 100).Then().FlashColor(Color4.WHITE, 100);
        manualClock.Seek(550);
        box.UpdateSubTree();
        Assert.Equal(Color4.WHITE, box.Color);

        manualClock.Seek(650);
        box.UpdateSubTree();
        Assert.Equal(Color4.RED, box.Color);
    }

    [Fact]
    public void SpatialGeometryAndCompositePaddingExtensionsWorkAndPreserveConcreteType()
    {
        var manualClock = new TestManualClock(initialTime: 0);
        var clock = new FramedClock(manualClock);
        using var container = new TypedPanel
        {
            Clock = clock,
            Position = Vector2.Zero,
            Size = new Vector2(10f, 10f),
            Scale = Vector2.One,
            Rotation = 0f,
            Shear = Vector2.Zero,
            Margin = MarginPadding.ZERO,
            Padding = MarginPadding.ZERO,
        };

        // Sequence preserves TypedPanel return type through chaining and TransformPaddingTo.
        var seq = container.MoveTo(new Vector2(100f, 50f), 100)
            .Then()
            .MoveToOffset(new Vector2(20f, -10f), 100)
            .ResizeTo(new Vector2(200f, 100f), 100)
            .ScaleTo(3f, 100)
            .RotateTo(45f, 100)
            .ShearTo(new Vector2(0.4f, 0.2f), 100)
            .TransformMarginTo(new MarginPadding(8f), 100)
            .TransformPaddingTo(new MarginPadding(12f), 100)
            .OnComplete(panel => panel.Completed = true);

        Assert.IsType<TransformSequence<TypedPanel>>(seq);

        manualClock.Seek(200);
        container.UpdateSubTree();

        Assert.True(container.Completed);
        Assert.Equal(new Vector2(120f, 40f), container.Position);
        Assert.Equal(new Vector2(200f, 100f), container.Size);
        Assert.Equal(new Vector2(3f, 3f), container.Scale);
        Assert.Equal(45f, container.Rotation, precision: 4);
        Assert.Equal(new Vector2(0.4f, 0.2f), container.Shear);
        Assert.Equal(new MarginPadding(8f), container.Margin);
        Assert.Equal(new MarginPadding(12f), container.Padding);

        // Single-axis helpers + uniform ResizeTo(float).
        container.ResizeTo(50f, 100)
            .Then()
            .ResizeWidthTo(150f, 100)
            .ResizeHeightTo(80f, 100);

        manualClock.Seek(400);
        container.UpdateSubTree();
        Assert.Equal(new Vector2(150f, 80f), container.Size);
    }

    [Fact]
    public void SpinRotatesContinuouslyInBothDirectionsAndSupportsChainingAfterFadeIn()
    {
        var manualClock = new TestManualClock(initialTime: 0);
        var clock = new FramedClock(manualClock);
        using var box = new Box { Clock = clock, Alpha = 0f, Rotation = 10f };

        // FadeIn runs once [0..100], Spin runs infinitely starting at t=100 from effective rotation 10 -> 370.
        box.FadeIn(100).Then().Spin(400, RotationDirection.Clockwise);

        manualClock.Seek(50);
        box.UpdateSubTree();
        Assert.Equal(0.5f, box.Alpha, precision: 5);
        Assert.Equal(10f, box.Rotation);

        manualClock.Seek(200); // 100ms into first spin -> 10 + 90 = 100.
        box.UpdateSubTree();
        Assert.Equal(1f, box.Alpha);
        Assert.Equal(100f, box.Rotation, precision: 4);

        manualClock.Seek(600); // 100ms into second spin -> 100 again.
        box.UpdateSubTree();
        Assert.Equal(100f, box.Rotation, precision: 4);

        // Counterclockwise from explicit startRotation = 0.
        box.Spin(400, RotationDirection.Counterclockwise, startRotation: 0f);
        manualClock.Seek(700);
        box.UpdateSubTree();
        Assert.Equal(-90f, box.Rotation, precision: 4);

        Assert.Throws<ArgumentOutOfRangeException>(() => box.Spin(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => box.Spin(-100));
    }

    [Fact]
    public void CustomEasingCustomPropertyTransformToAndBindableOverloadsWorkWithoutReflection()
    {
        var manualClock = new TestManualClock(initialTime: 0);
        var clock = new FramedClock(manualClock);
        using var drawable = new CustomTargetsDrawable
        {
            Clock = clock,
            Alpha = 0f,
            FloatProp = 0f,
            DoubleProp = 0.0,
            IntProp = 0,
            VecProp = Vector2.Zero,
            ColorProp = Color4.BLACK,
            MarginProp = MarginPadding.ZERO,
            RectProp = RectangleF.EMPTY,
            CustomStructProp = new CustomScalar(0.0),
        };

        var floatBindable = new Bindable<float>(0f);
        var customBindable = new Bindable<CustomScalar>(new CustomScalar(10.0));
        var bezier = CubicBezierEasingFunction.EASE_IN_OUT;

        drawable.FadeIn(100, bezier)
            .TransformTo(static d => d.FloatProp, static (d, v) => d.FloatProp = v, 100f, 100, bezier)
            .TransformTo(static d => d.DoubleProp, static (d, v) => d.DoubleProp = v, 200.0, 100)
            .TransformTo(static d => d.IntProp, static (d, v) => d.IntProp = v, 50, 100)
            .TransformTo(static d => d.VecProp, static (d, v) => d.VecProp = v, new Vector2(10f, 20f), 100)
            .TransformTo(static d => d.ColorProp, static (d, v) => d.ColorProp = v, Color4.WHITE, 100)
            .TransformTo(static d => d.MarginProp, static (d, v) => d.MarginProp = v, new MarginPadding(10f), 100)
            .TransformTo(static d => d.RectProp, static (d, v) => d.RectProp = v, new RectangleF(10f, 20f, 30f, 40f), 100)
            .TransformTo(
                static d => d.CustomStructProp,
                static (d, v) => d.CustomStructProp = v,
                new CustomScalar(100.0),
                100,
                static (a, b, t) => new CustomScalar(Interpolation.Lerp(a.Value, b.Value, t)))
            .TransformTo(floatBindable, 40f, 100)
            .TransformBindableTo(
                customBindable,
                new CustomScalar(110.0),
                100,
                static (a, b, t) => new CustomScalar(Interpolation.Lerp(a.Value, b.Value, t)));

        manualClock.Seek(50);
        drawable.UpdateSubTree();

        var easedHalf = (float)bezier.ApplyEasing(0.5);
        Assert.Equal(easedHalf, drawable.Alpha, precision: 5);
        Assert.Equal(100f * easedHalf, drawable.FloatProp, precision: 3);
        Assert.Equal(100.0, drawable.DoubleProp, precision: 10);
        Assert.Equal(25, drawable.IntProp);
        Assert.Equal(new Vector2(5f, 10f), drawable.VecProp);
        Assert.Equal(new Color4(0.5f, 0.5f, 0.5f, 1f), drawable.ColorProp);
        Assert.Equal(new MarginPadding(5f), drawable.MarginProp);
        Assert.Equal(new RectangleF(5f, 10f, 15f, 20f), drawable.RectProp);
        Assert.Equal(50.0, drawable.CustomStructProp.Value, precision: 10);
        Assert.Equal(20f, floatBindable.Value, precision: 5);
        Assert.Equal(60.0, customBindable.Value.Value, precision: 10);
    }

    private sealed class TypedPanel : Container
    {
        public bool Completed { get; set; }
    }

    private sealed class CustomTargetsDrawable : Drawable
    {
        public float FloatProp { get; set; }

        public double DoubleProp { get; set; }

        public int IntProp { get; set; }

        public Vector2 VecProp { get; set; }

        public Color4 ColorProp { get; set; }

        public MarginPadding MarginProp { get; set; }

        public RectangleF RectProp { get; set; }

        public CustomScalar CustomStructProp { get; set; }
    }

    private readonly record struct CustomScalar(double Value);
}
