using kajarlabs.Framework.Bindables;

namespace kajarlabs.Framework.Scene.Transforms;

internal abstract class CustomTransformHandler
{
    public abstract TransformTargetKey TargetKey { get; }

    public abstract void ReadIntoStartValue(Drawable target);

    public abstract void ApplyStartValue(Drawable target);

    public abstract void ApplyEndValue(Drawable target);

    public abstract void Apply(Drawable target, double time, double startTime, double endTime);
}

internal sealed class BuiltInCustomEasingHandler<TEasing> : CustomTransformHandler
    where TEasing : struct, IEasingFunction
{
    private readonly BuiltInTransformTarget property;
    private readonly TransformValue endValue;
    private readonly TEasing easing;
    private TransformValue startValue;

    public BuiltInCustomEasingHandler(
        BuiltInTransformTarget property,
        TransformValue startValue,
        TransformValue endValue,
        TEasing easing)
    {
        this.property = property;
        this.startValue = startValue;
        this.endValue = endValue;
        this.easing = easing;
    }

    public override TransformTargetKey TargetKey => TransformTargetKey.FromBuiltIn(property);

    public override void ReadIntoStartValue(Drawable target)
        => startValue = BuiltInTransformHelper.ReadValue(target, property);

    public override void ApplyStartValue(Drawable target)
        => BuiltInTransformHelper.ApplyValue(target, property, in startValue);

    public override void ApplyEndValue(Drawable target)
        => BuiltInTransformHelper.ApplyValue(target, property, in endValue);

    public override void Apply(Drawable target, double time, double startTime, double endTime)
        => BuiltInTransformHelper.ApplyInterpolated(target, property, in startValue, in endValue, time, startTime, endTime, easing);
}

internal sealed class DelegateTransformHandler<TDrawable, TValue, TEasing, TInterpolator> : CustomTransformHandler
    where TDrawable : Drawable
    where TEasing : struct, IEasingFunction
    where TInterpolator : struct, IValueInterpolator<TValue>
{
    private readonly TransformTargetKey targetKey;
    private readonly Func<TDrawable, TValue> getter;
    private readonly Action<TDrawable, TValue> setter;
    private readonly TValue endValue;
    private readonly TEasing easing;
    private readonly TInterpolator interpolator;
    private TValue startValue;

    public DelegateTransformHandler(
        TransformTargetKey targetKey,
        Func<TDrawable, TValue> getter,
        Action<TDrawable, TValue> setter,
        TValue startValue,
        TValue endValue,
        TEasing easing,
        TInterpolator interpolator)
    {
        ArgumentNullException.ThrowIfNull(getter);
        ArgumentNullException.ThrowIfNull(setter);

        this.targetKey = targetKey;
        this.getter = getter;
        this.setter = setter;
        this.startValue = startValue;
        this.endValue = endValue;
        this.easing = easing;
        this.interpolator = interpolator;
    }

    public override TransformTargetKey TargetKey => targetKey;

    public override void ReadIntoStartValue(Drawable target)
        => startValue = getter((TDrawable)target);

    public override void ApplyStartValue(Drawable target)
        => setter((TDrawable)target, startValue);

    public override void ApplyEndValue(Drawable target)
        => setter((TDrawable)target, endValue);

    public override void Apply(Drawable target, double time, double startTime, double endTime)
    {
        var typedTarget = (TDrawable)target;

        if (time >= endTime)
        {
            setter(typedTarget, endValue);
            return;
        }

        if (time <= startTime)
        {
            setter(typedTarget, startValue);
            return;
        }

        var progress = easing.ApplyEasing((time - startTime) / (endTime - startTime));
        setter(typedTarget, interpolator.Lerp(startValue, endValue, progress));
    }
}

internal sealed class BindableTransformHandler<TValue, TEasing, TInterpolator> : CustomTransformHandler
    where TEasing : struct, IEasingFunction
    where TInterpolator : struct, IValueInterpolator<TValue>
{
    private readonly Bindable<TValue> bindable;
    private readonly TransformTargetKey targetKey;
    private readonly TValue endValue;
    private readonly TEasing easing;
    private readonly TInterpolator interpolator;
    private TValue startValue;

    public BindableTransformHandler(
        Bindable<TValue> bindable,
        TValue startValue,
        TValue endValue,
        TEasing easing,
        TInterpolator interpolator)
    {
        ArgumentNullException.ThrowIfNull(bindable);

        this.bindable = bindable;
        targetKey = TransformTargetKey.FromCustomObject(bindable);
        this.startValue = startValue;
        this.endValue = endValue;
        this.easing = easing;
        this.interpolator = interpolator;
    }

    public override TransformTargetKey TargetKey => targetKey;

    public override void ReadIntoStartValue(Drawable target)
        => startValue = bindable.Value;

    public override void ApplyStartValue(Drawable target)
        => bindable.Value = startValue;

    public override void ApplyEndValue(Drawable target)
        => bindable.Value = endValue;

    public override void Apply(Drawable target, double time, double startTime, double endTime)
    {
        if (time >= endTime)
        {
            bindable.Value = endValue;
            return;
        }

        if (time <= startTime)
        {
            bindable.Value = startValue;
            return;
        }

        var progress = easing.ApplyEasing((time - startTime) / (endTime - startTime));
        bindable.Value = interpolator.Lerp(startValue, endValue, progress);
    }
}
