using System.Numerics;
using System.Runtime.CompilerServices;
using kajarlabs.Framework.Bindables;
using kajarlabs.Framework.Numerics;

namespace kajarlabs.Framework.Scene.Transforms;

/// <summary>
/// Provides reflection-free, NativeAOT-safe custom property and <see cref="Bindable{T}"/> transform extension methods
/// for <see cref="Drawable"/> and <see cref="TransformSequence{TDrawable}"/>.
/// </summary>
public static class CustomTransformExtensions
{
    #region Custom property TransformTo (built-in interpolable types)

    /// <summary>
    /// Animates a custom <see cref="float"/> property exposed via <paramref name="getter"/> and <paramref name="setter"/> without reflection.
    /// </summary>
    public static TransformSequence<TDrawable> TransformTo<TDrawable>(
        this TDrawable target,
        Func<TDrawable, float> getter,
        Action<TDrawable, float> setter,
        float endValue,
        double duration = 0.0,
        Easing easing = Easing.None,
        [CallerArgumentExpression(nameof(setter))] string? targetName = null)
        where TDrawable : Drawable
        => TransformSequence<TDrawable>.Create(target).TransformTo(getter, setter, endValue, duration, easing, targetName);

    /// <summary>
    /// Animates a custom <see cref="float"/> property using a value-type <typeparamref name="TEasing"/> function without reflection.
    /// </summary>
    public static TransformSequence<TDrawable> TransformTo<TDrawable, TEasing>(
        this TDrawable target,
        Func<TDrawable, float> getter,
        Action<TDrawable, float> setter,
        float endValue,
        double duration,
        TEasing easing,
        [CallerArgumentExpression(nameof(setter))] string? targetName = null)
        where TDrawable : Drawable
        where TEasing : struct, IEasingFunction
        => TransformSequence<TDrawable>.Create(target).TransformTo(getter, setter, endValue, duration, easing, targetName);

    /// <summary>
    /// Appends a custom <see cref="float"/> property animation onto <paramref name="sequence"/> without reflection.
    /// </summary>
    public static TransformSequence<TDrawable> TransformTo<TDrawable>(
        this TransformSequence<TDrawable> sequence,
        Func<TDrawable, float> getter,
        Action<TDrawable, float> setter,
        float endValue,
        double duration = 0.0,
        Easing easing = Easing.None,
        [CallerArgumentExpression(nameof(setter))] string? targetName = null)
        where TDrawable : Drawable
        => sequence.TransformTo(getter, setter, endValue, duration, new DefaultEasingFunction(easing), targetName);

    /// <summary>
    /// Appends a custom <see cref="float"/> property animation onto <paramref name="sequence"/> using <paramref name="easing"/> without reflection.
    /// </summary>
    public static TransformSequence<TDrawable> TransformTo<TDrawable, TEasing>(
        this TransformSequence<TDrawable> sequence,
        Func<TDrawable, float> getter,
        Action<TDrawable, float> setter,
        float endValue,
        double duration,
        TEasing easing,
        [CallerArgumentExpression(nameof(setter))] string? targetName = null)
        where TDrawable : Drawable
        where TEasing : struct, IEasingFunction
        => appendDelegateTransform<TDrawable, float, TEasing, SingleInterpolator>(sequence, getter, setter, endValue, duration, easing, default, targetName);

    /// <summary>
    /// Animates a custom <see cref="double"/> property exposed via <paramref name="getter"/> and <paramref name="setter"/> without reflection.
    /// </summary>
    public static TransformSequence<TDrawable> TransformTo<TDrawable>(
        this TDrawable target,
        Func<TDrawable, double> getter,
        Action<TDrawable, double> setter,
        double endValue,
        double duration = 0.0,
        Easing easing = Easing.None,
        [CallerArgumentExpression(nameof(setter))] string? targetName = null)
        where TDrawable : Drawable
        => TransformSequence<TDrawable>.Create(target).TransformTo(getter, setter, endValue, duration, easing, targetName);

    /// <summary>
    /// Animates a custom <see cref="double"/> property using <paramref name="easing"/> without reflection.
    /// </summary>
    public static TransformSequence<TDrawable> TransformTo<TDrawable, TEasing>(
        this TDrawable target,
        Func<TDrawable, double> getter,
        Action<TDrawable, double> setter,
        double endValue,
        double duration,
        TEasing easing,
        [CallerArgumentExpression(nameof(setter))] string? targetName = null)
        where TDrawable : Drawable
        where TEasing : struct, IEasingFunction
        => TransformSequence<TDrawable>.Create(target).TransformTo(getter, setter, endValue, duration, easing, targetName);

    /// <summary>
    /// Appends a custom <see cref="double"/> property animation onto <paramref name="sequence"/> without reflection.
    /// </summary>
    public static TransformSequence<TDrawable> TransformTo<TDrawable>(
        this TransformSequence<TDrawable> sequence,
        Func<TDrawable, double> getter,
        Action<TDrawable, double> setter,
        double endValue,
        double duration = 0.0,
        Easing easing = Easing.None,
        [CallerArgumentExpression(nameof(setter))] string? targetName = null)
        where TDrawable : Drawable
        => sequence.TransformTo(getter, setter, endValue, duration, new DefaultEasingFunction(easing), targetName);

    /// <summary>
    /// Appends a custom <see cref="double"/> property animation onto <paramref name="sequence"/> using <paramref name="easing"/> without reflection.
    /// </summary>
    public static TransformSequence<TDrawable> TransformTo<TDrawable, TEasing>(
        this TransformSequence<TDrawable> sequence,
        Func<TDrawable, double> getter,
        Action<TDrawable, double> setter,
        double endValue,
        double duration,
        TEasing easing,
        [CallerArgumentExpression(nameof(setter))] string? targetName = null)
        where TDrawable : Drawable
        where TEasing : struct, IEasingFunction
        => appendDelegateTransform<TDrawable, double, TEasing, DoubleInterpolator>(sequence, getter, setter, endValue, duration, easing, default, targetName);

    /// <summary>
    /// Animates a custom <see cref="int"/> property exposed via <paramref name="getter"/> and <paramref name="setter"/> without reflection.
    /// </summary>
    public static TransformSequence<TDrawable> TransformTo<TDrawable>(
        this TDrawable target,
        Func<TDrawable, int> getter,
        Action<TDrawable, int> setter,
        int endValue,
        double duration = 0.0,
        Easing easing = Easing.None,
        [CallerArgumentExpression(nameof(setter))] string? targetName = null)
        where TDrawable : Drawable
        => TransformSequence<TDrawable>.Create(target).TransformTo(getter, setter, endValue, duration, easing, targetName);

    /// <summary>
    /// Animates a custom <see cref="int"/> property using <paramref name="easing"/> without reflection.
    /// </summary>
    public static TransformSequence<TDrawable> TransformTo<TDrawable, TEasing>(
        this TDrawable target,
        Func<TDrawable, int> getter,
        Action<TDrawable, int> setter,
        int endValue,
        double duration,
        TEasing easing,
        [CallerArgumentExpression(nameof(setter))] string? targetName = null)
        where TDrawable : Drawable
        where TEasing : struct, IEasingFunction
        => TransformSequence<TDrawable>.Create(target).TransformTo(getter, setter, endValue, duration, easing, targetName);

    /// <summary>
    /// Appends a custom <see cref="int"/> property animation onto <paramref name="sequence"/> without reflection.
    /// </summary>
    public static TransformSequence<TDrawable> TransformTo<TDrawable>(
        this TransformSequence<TDrawable> sequence,
        Func<TDrawable, int> getter,
        Action<TDrawable, int> setter,
        int endValue,
        double duration = 0.0,
        Easing easing = Easing.None,
        [CallerArgumentExpression(nameof(setter))] string? targetName = null)
        where TDrawable : Drawable
        => sequence.TransformTo(getter, setter, endValue, duration, new DefaultEasingFunction(easing), targetName);

    /// <summary>
    /// Appends a custom <see cref="int"/> property animation onto <paramref name="sequence"/> using <paramref name="easing"/> without reflection.
    /// </summary>
    public static TransformSequence<TDrawable> TransformTo<TDrawable, TEasing>(
        this TransformSequence<TDrawable> sequence,
        Func<TDrawable, int> getter,
        Action<TDrawable, int> setter,
        int endValue,
        double duration,
        TEasing easing,
        [CallerArgumentExpression(nameof(setter))] string? targetName = null)
        where TDrawable : Drawable
        where TEasing : struct, IEasingFunction
        => appendDelegateTransform<TDrawable, int, TEasing, Int32Interpolator>(sequence, getter, setter, endValue, duration, easing, default, targetName);

    /// <summary>
    /// Animates a custom <see cref="Vector2"/> property exposed via <paramref name="getter"/> and <paramref name="setter"/> without reflection.
    /// </summary>
    public static TransformSequence<TDrawable> TransformTo<TDrawable>(
        this TDrawable target,
        Func<TDrawable, Vector2> getter,
        Action<TDrawable, Vector2> setter,
        Vector2 endValue,
        double duration = 0.0,
        Easing easing = Easing.None,
        [CallerArgumentExpression(nameof(setter))] string? targetName = null)
        where TDrawable : Drawable
        => TransformSequence<TDrawable>.Create(target).TransformTo(getter, setter, endValue, duration, easing, targetName);

    /// <summary>
    /// Animates a custom <see cref="Vector2"/> property using <paramref name="easing"/> without reflection.
    /// </summary>
    public static TransformSequence<TDrawable> TransformTo<TDrawable, TEasing>(
        this TDrawable target,
        Func<TDrawable, Vector2> getter,
        Action<TDrawable, Vector2> setter,
        Vector2 endValue,
        double duration,
        TEasing easing,
        [CallerArgumentExpression(nameof(setter))] string? targetName = null)
        where TDrawable : Drawable
        where TEasing : struct, IEasingFunction
        => TransformSequence<TDrawable>.Create(target).TransformTo(getter, setter, endValue, duration, easing, targetName);

    /// <summary>
    /// Appends a custom <see cref="Vector2"/> property animation onto <paramref name="sequence"/> without reflection.
    /// </summary>
    public static TransformSequence<TDrawable> TransformTo<TDrawable>(
        this TransformSequence<TDrawable> sequence,
        Func<TDrawable, Vector2> getter,
        Action<TDrawable, Vector2> setter,
        Vector2 endValue,
        double duration = 0.0,
        Easing easing = Easing.None,
        [CallerArgumentExpression(nameof(setter))] string? targetName = null)
        where TDrawable : Drawable
        => sequence.TransformTo(getter, setter, endValue, duration, new DefaultEasingFunction(easing), targetName);

    /// <summary>
    /// Appends a custom <see cref="Vector2"/> property animation onto <paramref name="sequence"/> using <paramref name="easing"/> without reflection.
    /// </summary>
    public static TransformSequence<TDrawable> TransformTo<TDrawable, TEasing>(
        this TransformSequence<TDrawable> sequence,
        Func<TDrawable, Vector2> getter,
        Action<TDrawable, Vector2> setter,
        Vector2 endValue,
        double duration,
        TEasing easing,
        [CallerArgumentExpression(nameof(setter))] string? targetName = null)
        where TDrawable : Drawable
        where TEasing : struct, IEasingFunction
        => appendDelegateTransform<TDrawable, Vector2, TEasing, Vector2Interpolator>(sequence, getter, setter, endValue, duration, easing, default, targetName);

    /// <summary>
    /// Animates a custom <see cref="Color4"/> property exposed via <paramref name="getter"/> and <paramref name="setter"/> without reflection.
    /// </summary>
    public static TransformSequence<TDrawable> TransformTo<TDrawable>(
        this TDrawable target,
        Func<TDrawable, Color4> getter,
        Action<TDrawable, Color4> setter,
        Color4 endValue,
        double duration = 0.0,
        Easing easing = Easing.None,
        [CallerArgumentExpression(nameof(setter))] string? targetName = null)
        where TDrawable : Drawable
        => TransformSequence<TDrawable>.Create(target).TransformTo(getter, setter, endValue, duration, easing, targetName);

    /// <summary>
    /// Animates a custom <see cref="Color4"/> property using <paramref name="easing"/> without reflection.
    /// </summary>
    public static TransformSequence<TDrawable> TransformTo<TDrawable, TEasing>(
        this TDrawable target,
        Func<TDrawable, Color4> getter,
        Action<TDrawable, Color4> setter,
        Color4 endValue,
        double duration,
        TEasing easing,
        [CallerArgumentExpression(nameof(setter))] string? targetName = null)
        where TDrawable : Drawable
        where TEasing : struct, IEasingFunction
        => TransformSequence<TDrawable>.Create(target).TransformTo(getter, setter, endValue, duration, easing, targetName);

    /// <summary>
    /// Appends a custom <see cref="Color4"/> property animation onto <paramref name="sequence"/> without reflection.
    /// </summary>
    public static TransformSequence<TDrawable> TransformTo<TDrawable>(
        this TransformSequence<TDrawable> sequence,
        Func<TDrawable, Color4> getter,
        Action<TDrawable, Color4> setter,
        Color4 endValue,
        double duration = 0.0,
        Easing easing = Easing.None,
        [CallerArgumentExpression(nameof(setter))] string? targetName = null)
        where TDrawable : Drawable
        => sequence.TransformTo(getter, setter, endValue, duration, new DefaultEasingFunction(easing), targetName);

    /// <summary>
    /// Appends a custom <see cref="Color4"/> property animation onto <paramref name="sequence"/> using <paramref name="easing"/> without reflection.
    /// </summary>
    public static TransformSequence<TDrawable> TransformTo<TDrawable, TEasing>(
        this TransformSequence<TDrawable> sequence,
        Func<TDrawable, Color4> getter,
        Action<TDrawable, Color4> setter,
        Color4 endValue,
        double duration,
        TEasing easing,
        [CallerArgumentExpression(nameof(setter))] string? targetName = null)
        where TDrawable : Drawable
        where TEasing : struct, IEasingFunction
        => appendDelegateTransform<TDrawable, Color4, TEasing, Color4Interpolator>(sequence, getter, setter, endValue, duration, easing, default, targetName);

    /// <summary>
    /// Animates a custom <see cref="MarginPadding"/> property exposed via <paramref name="getter"/> and <paramref name="setter"/> without reflection.
    /// </summary>
    public static TransformSequence<TDrawable> TransformTo<TDrawable>(
        this TDrawable target,
        Func<TDrawable, MarginPadding> getter,
        Action<TDrawable, MarginPadding> setter,
        MarginPadding endValue,
        double duration = 0.0,
        Easing easing = Easing.None,
        [CallerArgumentExpression(nameof(setter))] string? targetName = null)
        where TDrawable : Drawable
        => TransformSequence<TDrawable>.Create(target).TransformTo(getter, setter, endValue, duration, easing, targetName);

    /// <summary>
    /// Animates a custom <see cref="MarginPadding"/> property using <paramref name="easing"/> without reflection.
    /// </summary>
    public static TransformSequence<TDrawable> TransformTo<TDrawable, TEasing>(
        this TDrawable target,
        Func<TDrawable, MarginPadding> getter,
        Action<TDrawable, MarginPadding> setter,
        MarginPadding endValue,
        double duration,
        TEasing easing,
        [CallerArgumentExpression(nameof(setter))] string? targetName = null)
        where TDrawable : Drawable
        where TEasing : struct, IEasingFunction
        => TransformSequence<TDrawable>.Create(target).TransformTo(getter, setter, endValue, duration, easing, targetName);

    /// <summary>
    /// Appends a custom <see cref="MarginPadding"/> property animation onto <paramref name="sequence"/> without reflection.
    /// </summary>
    public static TransformSequence<TDrawable> TransformTo<TDrawable>(
        this TransformSequence<TDrawable> sequence,
        Func<TDrawable, MarginPadding> getter,
        Action<TDrawable, MarginPadding> setter,
        MarginPadding endValue,
        double duration = 0.0,
        Easing easing = Easing.None,
        [CallerArgumentExpression(nameof(setter))] string? targetName = null)
        where TDrawable : Drawable
        => sequence.TransformTo(getter, setter, endValue, duration, new DefaultEasingFunction(easing), targetName);

    /// <summary>
    /// Appends a custom <see cref="MarginPadding"/> property animation onto <paramref name="sequence"/> using <paramref name="easing"/> without reflection.
    /// </summary>
    public static TransformSequence<TDrawable> TransformTo<TDrawable, TEasing>(
        this TransformSequence<TDrawable> sequence,
        Func<TDrawable, MarginPadding> getter,
        Action<TDrawable, MarginPadding> setter,
        MarginPadding endValue,
        double duration,
        TEasing easing,
        [CallerArgumentExpression(nameof(setter))] string? targetName = null)
        where TDrawable : Drawable
        where TEasing : struct, IEasingFunction
        => appendDelegateTransform<TDrawable, MarginPadding, TEasing, MarginPaddingInterpolator>(sequence, getter, setter, endValue, duration, easing, default, targetName);

    /// <summary>
    /// Animates a custom <see cref="RectangleF"/> property exposed via <paramref name="getter"/> and <paramref name="setter"/> without reflection.
    /// </summary>
    public static TransformSequence<TDrawable> TransformTo<TDrawable>(
        this TDrawable target,
        Func<TDrawable, RectangleF> getter,
        Action<TDrawable, RectangleF> setter,
        RectangleF endValue,
        double duration = 0.0,
        Easing easing = Easing.None,
        [CallerArgumentExpression(nameof(setter))] string? targetName = null)
        where TDrawable : Drawable
        => TransformSequence<TDrawable>.Create(target).TransformTo(getter, setter, endValue, duration, easing, targetName);

    /// <summary>
    /// Animates a custom <see cref="RectangleF"/> property using <paramref name="easing"/> without reflection.
    /// </summary>
    public static TransformSequence<TDrawable> TransformTo<TDrawable, TEasing>(
        this TDrawable target,
        Func<TDrawable, RectangleF> getter,
        Action<TDrawable, RectangleF> setter,
        RectangleF endValue,
        double duration,
        TEasing easing,
        [CallerArgumentExpression(nameof(setter))] string? targetName = null)
        where TDrawable : Drawable
        where TEasing : struct, IEasingFunction
        => TransformSequence<TDrawable>.Create(target).TransformTo(getter, setter, endValue, duration, easing, targetName);

    /// <summary>
    /// Appends a custom <see cref="RectangleF"/> property animation onto <paramref name="sequence"/> without reflection.
    /// </summary>
    public static TransformSequence<TDrawable> TransformTo<TDrawable>(
        this TransformSequence<TDrawable> sequence,
        Func<TDrawable, RectangleF> getter,
        Action<TDrawable, RectangleF> setter,
        RectangleF endValue,
        double duration = 0.0,
        Easing easing = Easing.None,
        [CallerArgumentExpression(nameof(setter))] string? targetName = null)
        where TDrawable : Drawable
        => sequence.TransformTo(getter, setter, endValue, duration, new DefaultEasingFunction(easing), targetName);

    /// <summary>
    /// Appends a custom <see cref="RectangleF"/> property animation onto <paramref name="sequence"/> using <paramref name="easing"/> without reflection.
    /// </summary>
    public static TransformSequence<TDrawable> TransformTo<TDrawable, TEasing>(
        this TransformSequence<TDrawable> sequence,
        Func<TDrawable, RectangleF> getter,
        Action<TDrawable, RectangleF> setter,
        RectangleF endValue,
        double duration,
        TEasing easing,
        [CallerArgumentExpression(nameof(setter))] string? targetName = null)
        where TDrawable : Drawable
        where TEasing : struct, IEasingFunction
        => appendDelegateTransform<TDrawable, RectangleF, TEasing, RectangleFInterpolator>(sequence, getter, setter, endValue, duration, easing, default, targetName);

    /// <summary>
    /// Animates an arbitrary custom <typeparamref name="TValue"/> property using an explicit <paramref name="interpolator"/> without reflection.
    /// </summary>
    public static TransformSequence<TDrawable> TransformTo<TDrawable, TValue>(
        this TDrawable target,
        Func<TDrawable, TValue> getter,
        Action<TDrawable, TValue> setter,
        TValue endValue,
        double duration,
        Func<TValue, TValue, double, TValue> interpolator,
        Easing easing = Easing.None,
        [CallerArgumentExpression(nameof(setter))] string? targetName = null)
        where TDrawable : Drawable
        => TransformSequence<TDrawable>.Create(target).TransformTo(getter, setter, endValue, duration, interpolator, easing, targetName);

    /// <summary>
    /// Animates an arbitrary custom <typeparamref name="TValue"/> property using an explicit <paramref name="interpolator"/> and <paramref name="easing"/>.
    /// </summary>
    public static TransformSequence<TDrawable> TransformTo<TDrawable, TValue, TEasing>(
        this TDrawable target,
        Func<TDrawable, TValue> getter,
        Action<TDrawable, TValue> setter,
        TValue endValue,
        double duration,
        Func<TValue, TValue, double, TValue> interpolator,
        TEasing easing,
        [CallerArgumentExpression(nameof(setter))] string? targetName = null)
        where TDrawable : Drawable
        where TEasing : struct, IEasingFunction
        => TransformSequence<TDrawable>.Create(target).TransformTo(getter, setter, endValue, duration, interpolator, easing, targetName);

    /// <summary>
    /// Appends a custom <typeparamref name="TValue"/> property animation using an explicit <paramref name="interpolator"/> onto <paramref name="sequence"/>.
    /// </summary>
    public static TransformSequence<TDrawable> TransformTo<TDrawable, TValue>(
        this TransformSequence<TDrawable> sequence,
        Func<TDrawable, TValue> getter,
        Action<TDrawable, TValue> setter,
        TValue endValue,
        double duration,
        Func<TValue, TValue, double, TValue> interpolator,
        Easing easing = Easing.None,
        [CallerArgumentExpression(nameof(setter))] string? targetName = null)
        where TDrawable : Drawable
        => sequence.TransformTo(getter, setter, endValue, duration, interpolator, new DefaultEasingFunction(easing), targetName);

    /// <summary>
    /// Appends a custom <typeparamref name="TValue"/> property animation using an explicit <paramref name="interpolator"/> and <paramref name="easing"/> onto <paramref name="sequence"/>.
    /// </summary>
    public static TransformSequence<TDrawable> TransformTo<TDrawable, TValue, TEasing>(
        this TransformSequence<TDrawable> sequence,
        Func<TDrawable, TValue> getter,
        Action<TDrawable, TValue> setter,
        TValue endValue,
        double duration,
        Func<TValue, TValue, double, TValue> interpolator,
        TEasing easing,
        [CallerArgumentExpression(nameof(setter))] string? targetName = null)
        where TDrawable : Drawable
        where TEasing : struct, IEasingFunction
    {
        ArgumentNullException.ThrowIfNull(interpolator);
        return appendDelegateTransform(sequence, getter, setter, endValue, duration, easing, new DelegateInterpolator<TValue>(interpolator), targetName);
    }

    #endregion

    #region Bindable<T> TransformBindableTo / TransformTo

    /// <summary>
    /// Animates <see cref="Bindable{T}.Value"/> on <paramref name="bindable"/> to <paramref name="endValue"/> over <paramref name="duration"/> milliseconds.
    /// </summary>
    public static TransformSequence<TDrawable> TransformBindableTo<TDrawable>(
        this TDrawable target,
        Bindable<float> bindable,
        float endValue,
        double duration = 0.0,
        Easing easing = Easing.None)
        where TDrawable : Drawable
        => TransformSequence<TDrawable>.Create(target).TransformBindableTo(bindable, endValue, duration, easing);

    /// <summary>
    /// Animates <see cref="Bindable{T}.Value"/> on <paramref name="bindable"/> using <paramref name="easing"/>.
    /// </summary>
    public static TransformSequence<TDrawable> TransformBindableTo<TDrawable, TEasing>(
        this TDrawable target,
        Bindable<float> bindable,
        float endValue,
        double duration,
        TEasing easing)
        where TDrawable : Drawable
        where TEasing : struct, IEasingFunction
        => TransformSequence<TDrawable>.Create(target).TransformBindableTo(bindable, endValue, duration, easing);

    /// <summary>
    /// Appends a <see cref="Bindable{T}.Value"/> animation onto <paramref name="sequence"/>.
    /// </summary>
    public static TransformSequence<TDrawable> TransformBindableTo<TDrawable>(
        this TransformSequence<TDrawable> sequence,
        Bindable<float> bindable,
        float endValue,
        double duration = 0.0,
        Easing easing = Easing.None)
        where TDrawable : Drawable
        => sequence.TransformBindableTo(bindable, endValue, duration, new DefaultEasingFunction(easing));

    /// <summary>
    /// Appends a <see cref="Bindable{T}.Value"/> animation onto <paramref name="sequence"/> using <paramref name="easing"/>.
    /// </summary>
    public static TransformSequence<TDrawable> TransformBindableTo<TDrawable, TEasing>(
        this TransformSequence<TDrawable> sequence,
        Bindable<float> bindable,
        float endValue,
        double duration,
        TEasing easing)
        where TDrawable : Drawable
        where TEasing : struct, IEasingFunction
        => appendBindableTransform<TDrawable, float, TEasing, SingleInterpolator>(sequence, bindable, endValue, duration, easing, default);

    /// <inheritdoc cref="TransformBindableTo{TDrawable}(TDrawable, Bindable{float}, float, double, Easing)"/>
    public static TransformSequence<TDrawable> TransformBindableTo<TDrawable>(
        this TDrawable target,
        Bindable<double> bindable,
        double endValue,
        double duration = 0.0,
        Easing easing = Easing.None)
        where TDrawable : Drawable
        => TransformSequence<TDrawable>.Create(target).TransformBindableTo(bindable, endValue, duration, easing);

    /// <inheritdoc cref="TransformBindableTo{TDrawable, TEasing}(TDrawable, Bindable{float}, float, double, TEasing)"/>
    public static TransformSequence<TDrawable> TransformBindableTo<TDrawable, TEasing>(
        this TDrawable target,
        Bindable<double> bindable,
        double endValue,
        double duration,
        TEasing easing)
        where TDrawable : Drawable
        where TEasing : struct, IEasingFunction
        => TransformSequence<TDrawable>.Create(target).TransformBindableTo(bindable, endValue, duration, easing);

    /// <inheritdoc cref="TransformBindableTo{TDrawable}(TransformSequence{TDrawable}, Bindable{float}, float, double, Easing)"/>
    public static TransformSequence<TDrawable> TransformBindableTo<TDrawable>(
        this TransformSequence<TDrawable> sequence,
        Bindable<double> bindable,
        double endValue,
        double duration = 0.0,
        Easing easing = Easing.None)
        where TDrawable : Drawable
        => sequence.TransformBindableTo(bindable, endValue, duration, new DefaultEasingFunction(easing));

    /// <inheritdoc cref="TransformBindableTo{TDrawable, TEasing}(TransformSequence{TDrawable}, Bindable{float}, float, double, TEasing)"/>
    public static TransformSequence<TDrawable> TransformBindableTo<TDrawable, TEasing>(
        this TransformSequence<TDrawable> sequence,
        Bindable<double> bindable,
        double endValue,
        double duration,
        TEasing easing)
        where TDrawable : Drawable
        where TEasing : struct, IEasingFunction
        => appendBindableTransform<TDrawable, double, TEasing, DoubleInterpolator>(sequence, bindable, endValue, duration, easing, default);

    /// <inheritdoc cref="TransformBindableTo{TDrawable}(TDrawable, Bindable{float}, float, double, Easing)"/>
    public static TransformSequence<TDrawable> TransformBindableTo<TDrawable>(
        this TDrawable target,
        Bindable<int> bindable,
        int endValue,
        double duration = 0.0,
        Easing easing = Easing.None)
        where TDrawable : Drawable
        => TransformSequence<TDrawable>.Create(target).TransformBindableTo(bindable, endValue, duration, easing);

    /// <inheritdoc cref="TransformBindableTo{TDrawable, TEasing}(TDrawable, Bindable{float}, float, double, TEasing)"/>
    public static TransformSequence<TDrawable> TransformBindableTo<TDrawable, TEasing>(
        this TDrawable target,
        Bindable<int> bindable,
        int endValue,
        double duration,
        TEasing easing)
        where TDrawable : Drawable
        where TEasing : struct, IEasingFunction
        => TransformSequence<TDrawable>.Create(target).TransformBindableTo(bindable, endValue, duration, easing);

    /// <inheritdoc cref="TransformBindableTo{TDrawable}(TransformSequence{TDrawable}, Bindable{float}, float, double, Easing)"/>
    public static TransformSequence<TDrawable> TransformBindableTo<TDrawable>(
        this TransformSequence<TDrawable> sequence,
        Bindable<int> bindable,
        int endValue,
        double duration = 0.0,
        Easing easing = Easing.None)
        where TDrawable : Drawable
        => sequence.TransformBindableTo(bindable, endValue, duration, new DefaultEasingFunction(easing));

    /// <inheritdoc cref="TransformBindableTo{TDrawable, TEasing}(TransformSequence{TDrawable}, Bindable{float}, float, double, TEasing)"/>
    public static TransformSequence<TDrawable> TransformBindableTo<TDrawable, TEasing>(
        this TransformSequence<TDrawable> sequence,
        Bindable<int> bindable,
        int endValue,
        double duration,
        TEasing easing)
        where TDrawable : Drawable
        where TEasing : struct, IEasingFunction
        => appendBindableTransform<TDrawable, int, TEasing, Int32Interpolator>(sequence, bindable, endValue, duration, easing, default);

    /// <inheritdoc cref="TransformBindableTo{TDrawable}(TDrawable, Bindable{float}, float, double, Easing)"/>
    public static TransformSequence<TDrawable> TransformBindableTo<TDrawable>(
        this TDrawable target,
        Bindable<Vector2> bindable,
        Vector2 endValue,
        double duration = 0.0,
        Easing easing = Easing.None)
        where TDrawable : Drawable
        => TransformSequence<TDrawable>.Create(target).TransformBindableTo(bindable, endValue, duration, easing);

    /// <inheritdoc cref="TransformBindableTo{TDrawable, TEasing}(TDrawable, Bindable{float}, float, double, TEasing)"/>
    public static TransformSequence<TDrawable> TransformBindableTo<TDrawable, TEasing>(
        this TDrawable target,
        Bindable<Vector2> bindable,
        Vector2 endValue,
        double duration,
        TEasing easing)
        where TDrawable : Drawable
        where TEasing : struct, IEasingFunction
        => TransformSequence<TDrawable>.Create(target).TransformBindableTo(bindable, endValue, duration, easing);

    /// <inheritdoc cref="TransformBindableTo{TDrawable}(TransformSequence{TDrawable}, Bindable{float}, float, double, Easing)"/>
    public static TransformSequence<TDrawable> TransformBindableTo<TDrawable>(
        this TransformSequence<TDrawable> sequence,
        Bindable<Vector2> bindable,
        Vector2 endValue,
        double duration = 0.0,
        Easing easing = Easing.None)
        where TDrawable : Drawable
        => sequence.TransformBindableTo(bindable, endValue, duration, new DefaultEasingFunction(easing));

    /// <inheritdoc cref="TransformBindableTo{TDrawable, TEasing}(TransformSequence{TDrawable}, Bindable{float}, float, double, TEasing)"/>
    public static TransformSequence<TDrawable> TransformBindableTo<TDrawable, TEasing>(
        this TransformSequence<TDrawable> sequence,
        Bindable<Vector2> bindable,
        Vector2 endValue,
        double duration,
        TEasing easing)
        where TDrawable : Drawable
        where TEasing : struct, IEasingFunction
        => appendBindableTransform<TDrawable, Vector2, TEasing, Vector2Interpolator>(sequence, bindable, endValue, duration, easing, default);

    /// <inheritdoc cref="TransformBindableTo{TDrawable}(TDrawable, Bindable{float}, float, double, Easing)"/>
    public static TransformSequence<TDrawable> TransformBindableTo<TDrawable>(
        this TDrawable target,
        Bindable<Color4> bindable,
        Color4 endValue,
        double duration = 0.0,
        Easing easing = Easing.None)
        where TDrawable : Drawable
        => TransformSequence<TDrawable>.Create(target).TransformBindableTo(bindable, endValue, duration, easing);

    /// <inheritdoc cref="TransformBindableTo{TDrawable, TEasing}(TDrawable, Bindable{float}, float, double, TEasing)"/>
    public static TransformSequence<TDrawable> TransformBindableTo<TDrawable, TEasing>(
        this TDrawable target,
        Bindable<Color4> bindable,
        Color4 endValue,
        double duration,
        TEasing easing)
        where TDrawable : Drawable
        where TEasing : struct, IEasingFunction
        => TransformSequence<TDrawable>.Create(target).TransformBindableTo(bindable, endValue, duration, easing);

    /// <inheritdoc cref="TransformBindableTo{TDrawable}(TransformSequence{TDrawable}, Bindable{float}, float, double, Easing)"/>
    public static TransformSequence<TDrawable> TransformBindableTo<TDrawable>(
        this TransformSequence<TDrawable> sequence,
        Bindable<Color4> bindable,
        Color4 endValue,
        double duration = 0.0,
        Easing easing = Easing.None)
        where TDrawable : Drawable
        => sequence.TransformBindableTo(bindable, endValue, duration, new DefaultEasingFunction(easing));

    /// <inheritdoc cref="TransformBindableTo{TDrawable, TEasing}(TransformSequence{TDrawable}, Bindable{float}, float, double, TEasing)"/>
    public static TransformSequence<TDrawable> TransformBindableTo<TDrawable, TEasing>(
        this TransformSequence<TDrawable> sequence,
        Bindable<Color4> bindable,
        Color4 endValue,
        double duration,
        TEasing easing)
        where TDrawable : Drawable
        where TEasing : struct, IEasingFunction
        => appendBindableTransform<TDrawable, Color4, TEasing, Color4Interpolator>(sequence, bindable, endValue, duration, easing, default);

    /// <inheritdoc cref="TransformBindableTo{TDrawable}(TDrawable, Bindable{float}, float, double, Easing)"/>
    public static TransformSequence<TDrawable> TransformBindableTo<TDrawable>(
        this TDrawable target,
        Bindable<MarginPadding> bindable,
        MarginPadding endValue,
        double duration = 0.0,
        Easing easing = Easing.None)
        where TDrawable : Drawable
        => TransformSequence<TDrawable>.Create(target).TransformBindableTo(bindable, endValue, duration, easing);

    /// <inheritdoc cref="TransformBindableTo{TDrawable, TEasing}(TDrawable, Bindable{float}, float, double, TEasing)"/>
    public static TransformSequence<TDrawable> TransformBindableTo<TDrawable, TEasing>(
        this TDrawable target,
        Bindable<MarginPadding> bindable,
        MarginPadding endValue,
        double duration,
        TEasing easing)
        where TDrawable : Drawable
        where TEasing : struct, IEasingFunction
        => TransformSequence<TDrawable>.Create(target).TransformBindableTo(bindable, endValue, duration, easing);

    /// <inheritdoc cref="TransformBindableTo{TDrawable}(TransformSequence{TDrawable}, Bindable{float}, float, double, Easing)"/>
    public static TransformSequence<TDrawable> TransformBindableTo<TDrawable>(
        this TransformSequence<TDrawable> sequence,
        Bindable<MarginPadding> bindable,
        MarginPadding endValue,
        double duration = 0.0,
        Easing easing = Easing.None)
        where TDrawable : Drawable
        => sequence.TransformBindableTo(bindable, endValue, duration, new DefaultEasingFunction(easing));

    /// <inheritdoc cref="TransformBindableTo{TDrawable, TEasing}(TransformSequence{TDrawable}, Bindable{float}, float, double, TEasing)"/>
    public static TransformSequence<TDrawable> TransformBindableTo<TDrawable, TEasing>(
        this TransformSequence<TDrawable> sequence,
        Bindable<MarginPadding> bindable,
        MarginPadding endValue,
        double duration,
        TEasing easing)
        where TDrawable : Drawable
        where TEasing : struct, IEasingFunction
        => appendBindableTransform<TDrawable, MarginPadding, TEasing, MarginPaddingInterpolator>(sequence, bindable, endValue, duration, easing, default);

    /// <inheritdoc cref="TransformBindableTo{TDrawable}(TDrawable, Bindable{float}, float, double, Easing)"/>
    public static TransformSequence<TDrawable> TransformBindableTo<TDrawable>(
        this TDrawable target,
        Bindable<RectangleF> bindable,
        RectangleF endValue,
        double duration = 0.0,
        Easing easing = Easing.None)
        where TDrawable : Drawable
        => TransformSequence<TDrawable>.Create(target).TransformBindableTo(bindable, endValue, duration, easing);

    /// <inheritdoc cref="TransformBindableTo{TDrawable, TEasing}(TDrawable, Bindable{float}, float, double, TEasing)"/>
    public static TransformSequence<TDrawable> TransformBindableTo<TDrawable, TEasing>(
        this TDrawable target,
        Bindable<RectangleF> bindable,
        RectangleF endValue,
        double duration,
        TEasing easing)
        where TDrawable : Drawable
        where TEasing : struct, IEasingFunction
        => TransformSequence<TDrawable>.Create(target).TransformBindableTo(bindable, endValue, duration, easing);

    /// <inheritdoc cref="TransformBindableTo{TDrawable}(TransformSequence{TDrawable}, Bindable{float}, float, double, Easing)"/>
    public static TransformSequence<TDrawable> TransformBindableTo<TDrawable>(
        this TransformSequence<TDrawable> sequence,
        Bindable<RectangleF> bindable,
        RectangleF endValue,
        double duration = 0.0,
        Easing easing = Easing.None)
        where TDrawable : Drawable
        => sequence.TransformBindableTo(bindable, endValue, duration, new DefaultEasingFunction(easing));

    /// <inheritdoc cref="TransformBindableTo{TDrawable, TEasing}(TransformSequence{TDrawable}, Bindable{float}, float, double, TEasing)"/>
    public static TransformSequence<TDrawable> TransformBindableTo<TDrawable, TEasing>(
        this TransformSequence<TDrawable> sequence,
        Bindable<RectangleF> bindable,
        RectangleF endValue,
        double duration,
        TEasing easing)
        where TDrawable : Drawable
        where TEasing : struct, IEasingFunction
        => appendBindableTransform<TDrawable, RectangleF, TEasing, RectangleFInterpolator>(sequence, bindable, endValue, duration, easing, default);

    /// <summary>
    /// Animates <see cref="Bindable{T}.Value"/> on <paramref name="bindable"/> using an explicit <paramref name="interpolator"/> without reflection.
    /// </summary>
    public static TransformSequence<TDrawable> TransformBindableTo<TDrawable, TValue>(
        this TDrawable target,
        Bindable<TValue> bindable,
        TValue endValue,
        double duration,
        Func<TValue, TValue, double, TValue> interpolator,
        Easing easing = Easing.None)
        where TDrawable : Drawable
        => TransformSequence<TDrawable>.Create(target).TransformBindableTo(bindable, endValue, duration, interpolator, easing);

    /// <summary>
    /// Animates <see cref="Bindable{T}.Value"/> on <paramref name="bindable"/> using an explicit <paramref name="interpolator"/> and <paramref name="easing"/>.
    /// </summary>
    public static TransformSequence<TDrawable> TransformBindableTo<TDrawable, TValue, TEasing>(
        this TDrawable target,
        Bindable<TValue> bindable,
        TValue endValue,
        double duration,
        Func<TValue, TValue, double, TValue> interpolator,
        TEasing easing)
        where TDrawable : Drawable
        where TEasing : struct, IEasingFunction
        => TransformSequence<TDrawable>.Create(target).TransformBindableTo(bindable, endValue, duration, interpolator, easing);

    /// <summary>
    /// Appends a <see cref="Bindable{T}.Value"/> animation using an explicit <paramref name="interpolator"/> onto <paramref name="sequence"/>.
    /// </summary>
    public static TransformSequence<TDrawable> TransformBindableTo<TDrawable, TValue>(
        this TransformSequence<TDrawable> sequence,
        Bindable<TValue> bindable,
        TValue endValue,
        double duration,
        Func<TValue, TValue, double, TValue> interpolator,
        Easing easing = Easing.None)
        where TDrawable : Drawable
        => sequence.TransformBindableTo(bindable, endValue, duration, interpolator, new DefaultEasingFunction(easing));

    /// <summary>
    /// Appends a <see cref="Bindable{T}.Value"/> animation using an explicit <paramref name="interpolator"/> and <paramref name="easing"/> onto <paramref name="sequence"/>.
    /// </summary>
    public static TransformSequence<TDrawable> TransformBindableTo<TDrawable, TValue, TEasing>(
        this TransformSequence<TDrawable> sequence,
        Bindable<TValue> bindable,
        TValue endValue,
        double duration,
        Func<TValue, TValue, double, TValue> interpolator,
        TEasing easing)
        where TDrawable : Drawable
        where TEasing : struct, IEasingFunction
    {
        ArgumentNullException.ThrowIfNull(interpolator);
        return appendBindableTransform(sequence, bindable, endValue, duration, easing, new DelegateInterpolator<TValue>(interpolator));
    }

    /// <inheritdoc cref="TransformBindableTo{TDrawable}(TDrawable, Bindable{float}, float, double, Easing)"/>
    public static TransformSequence<TDrawable> TransformTo<TDrawable>(
        this TDrawable target,
        Bindable<float> bindable,
        float endValue,
        double duration = 0.0,
        Easing easing = Easing.None)
        where TDrawable : Drawable
        => target.TransformBindableTo(bindable, endValue, duration, easing);

    /// <inheritdoc cref="TransformBindableTo{TDrawable, TEasing}(TDrawable, Bindable{float}, float, double, TEasing)"/>
    public static TransformSequence<TDrawable> TransformTo<TDrawable, TEasing>(
        this TDrawable target,
        Bindable<float> bindable,
        float endValue,
        double duration,
        TEasing easing)
        where TDrawable : Drawable
        where TEasing : struct, IEasingFunction
        => target.TransformBindableTo(bindable, endValue, duration, easing);

    /// <inheritdoc cref="TransformBindableTo{TDrawable}(TransformSequence{TDrawable}, Bindable{float}, float, double, Easing)"/>
    public static TransformSequence<TDrawable> TransformTo<TDrawable>(
        this TransformSequence<TDrawable> sequence,
        Bindable<float> bindable,
        float endValue,
        double duration = 0.0,
        Easing easing = Easing.None)
        where TDrawable : Drawable
        => sequence.TransformBindableTo(bindable, endValue, duration, easing);

    /// <inheritdoc cref="TransformBindableTo{TDrawable, TEasing}(TransformSequence{TDrawable}, Bindable{float}, float, double, TEasing)"/>
    public static TransformSequence<TDrawable> TransformTo<TDrawable, TEasing>(
        this TransformSequence<TDrawable> sequence,
        Bindable<float> bindable,
        float endValue,
        double duration,
        TEasing easing)
        where TDrawable : Drawable
        where TEasing : struct, IEasingFunction
        => sequence.TransformBindableTo(bindable, endValue, duration, easing);

    /// <inheritdoc cref="TransformBindableTo{TDrawable}(TDrawable, Bindable{float}, float, double, Easing)"/>
    public static TransformSequence<TDrawable> TransformTo<TDrawable>(
        this TDrawable target,
        Bindable<double> bindable,
        double endValue,
        double duration = 0.0,
        Easing easing = Easing.None)
        where TDrawable : Drawable
        => target.TransformBindableTo(bindable, endValue, duration, easing);

    /// <inheritdoc cref="TransformBindableTo{TDrawable, TEasing}(TDrawable, Bindable{float}, float, double, TEasing)"/>
    public static TransformSequence<TDrawable> TransformTo<TDrawable, TEasing>(
        this TDrawable target,
        Bindable<double> bindable,
        double endValue,
        double duration,
        TEasing easing)
        where TDrawable : Drawable
        where TEasing : struct, IEasingFunction
        => target.TransformBindableTo(bindable, endValue, duration, easing);

    /// <inheritdoc cref="TransformBindableTo{TDrawable}(TransformSequence{TDrawable}, Bindable{float}, float, double, Easing)"/>
    public static TransformSequence<TDrawable> TransformTo<TDrawable>(
        this TransformSequence<TDrawable> sequence,
        Bindable<double> bindable,
        double endValue,
        double duration = 0.0,
        Easing easing = Easing.None)
        where TDrawable : Drawable
        => sequence.TransformBindableTo(bindable, endValue, duration, easing);

    /// <inheritdoc cref="TransformBindableTo{TDrawable, TEasing}(TransformSequence{TDrawable}, Bindable{float}, float, double, TEasing)"/>
    public static TransformSequence<TDrawable> TransformTo<TDrawable, TEasing>(
        this TransformSequence<TDrawable> sequence,
        Bindable<double> bindable,
        double endValue,
        double duration,
        TEasing easing)
        where TDrawable : Drawable
        where TEasing : struct, IEasingFunction
        => sequence.TransformBindableTo(bindable, endValue, duration, easing);

    /// <inheritdoc cref="TransformBindableTo{TDrawable}(TDrawable, Bindable{float}, float, double, Easing)"/>
    public static TransformSequence<TDrawable> TransformTo<TDrawable>(
        this TDrawable target,
        Bindable<int> bindable,
        int endValue,
        double duration = 0.0,
        Easing easing = Easing.None)
        where TDrawable : Drawable
        => target.TransformBindableTo(bindable, endValue, duration, easing);

    /// <inheritdoc cref="TransformBindableTo{TDrawable, TEasing}(TDrawable, Bindable{float}, float, double, TEasing)"/>
    public static TransformSequence<TDrawable> TransformTo<TDrawable, TEasing>(
        this TDrawable target,
        Bindable<int> bindable,
        int endValue,
        double duration,
        TEasing easing)
        where TDrawable : Drawable
        where TEasing : struct, IEasingFunction
        => target.TransformBindableTo(bindable, endValue, duration, easing);

    /// <inheritdoc cref="TransformBindableTo{TDrawable}(TransformSequence{TDrawable}, Bindable{float}, float, double, Easing)"/>
    public static TransformSequence<TDrawable> TransformTo<TDrawable>(
        this TransformSequence<TDrawable> sequence,
        Bindable<int> bindable,
        int endValue,
        double duration = 0.0,
        Easing easing = Easing.None)
        where TDrawable : Drawable
        => sequence.TransformBindableTo(bindable, endValue, duration, easing);

    /// <inheritdoc cref="TransformBindableTo{TDrawable, TEasing}(TransformSequence{TDrawable}, Bindable{float}, float, double, TEasing)"/>
    public static TransformSequence<TDrawable> TransformTo<TDrawable, TEasing>(
        this TransformSequence<TDrawable> sequence,
        Bindable<int> bindable,
        int endValue,
        double duration,
        TEasing easing)
        where TDrawable : Drawable
        where TEasing : struct, IEasingFunction
        => sequence.TransformBindableTo(bindable, endValue, duration, easing);

    /// <inheritdoc cref="TransformBindableTo{TDrawable}(TDrawable, Bindable{float}, float, double, Easing)"/>
    public static TransformSequence<TDrawable> TransformTo<TDrawable>(
        this TDrawable target,
        Bindable<Vector2> bindable,
        Vector2 endValue,
        double duration = 0.0,
        Easing easing = Easing.None)
        where TDrawable : Drawable
        => target.TransformBindableTo(bindable, endValue, duration, easing);

    /// <inheritdoc cref="TransformBindableTo{TDrawable, TEasing}(TDrawable, Bindable{float}, float, double, TEasing)"/>
    public static TransformSequence<TDrawable> TransformTo<TDrawable, TEasing>(
        this TDrawable target,
        Bindable<Vector2> bindable,
        Vector2 endValue,
        double duration,
        TEasing easing)
        where TDrawable : Drawable
        where TEasing : struct, IEasingFunction
        => target.TransformBindableTo(bindable, endValue, duration, easing);

    /// <inheritdoc cref="TransformBindableTo{TDrawable}(TransformSequence{TDrawable}, Bindable{float}, float, double, Easing)"/>
    public static TransformSequence<TDrawable> TransformTo<TDrawable>(
        this TransformSequence<TDrawable> sequence,
        Bindable<Vector2> bindable,
        Vector2 endValue,
        double duration = 0.0,
        Easing easing = Easing.None)
        where TDrawable : Drawable
        => sequence.TransformBindableTo(bindable, endValue, duration, easing);

    /// <inheritdoc cref="TransformBindableTo{TDrawable, TEasing}(TransformSequence{TDrawable}, Bindable{float}, float, double, TEasing)"/>
    public static TransformSequence<TDrawable> TransformTo<TDrawable, TEasing>(
        this TransformSequence<TDrawable> sequence,
        Bindable<Vector2> bindable,
        Vector2 endValue,
        double duration,
        TEasing easing)
        where TDrawable : Drawable
        where TEasing : struct, IEasingFunction
        => sequence.TransformBindableTo(bindable, endValue, duration, easing);

    /// <inheritdoc cref="TransformBindableTo{TDrawable}(TDrawable, Bindable{float}, float, double, Easing)"/>
    public static TransformSequence<TDrawable> TransformTo<TDrawable>(
        this TDrawable target,
        Bindable<Color4> bindable,
        Color4 endValue,
        double duration = 0.0,
        Easing easing = Easing.None)
        where TDrawable : Drawable
        => target.TransformBindableTo(bindable, endValue, duration, easing);

    /// <inheritdoc cref="TransformBindableTo{TDrawable, TEasing}(TDrawable, Bindable{float}, float, double, TEasing)"/>
    public static TransformSequence<TDrawable> TransformTo<TDrawable, TEasing>(
        this TDrawable target,
        Bindable<Color4> bindable,
        Color4 endValue,
        double duration,
        TEasing easing)
        where TDrawable : Drawable
        where TEasing : struct, IEasingFunction
        => target.TransformBindableTo(bindable, endValue, duration, easing);

    /// <inheritdoc cref="TransformBindableTo{TDrawable}(TransformSequence{TDrawable}, Bindable{float}, float, double, Easing)"/>
    public static TransformSequence<TDrawable> TransformTo<TDrawable>(
        this TransformSequence<TDrawable> sequence,
        Bindable<Color4> bindable,
        Color4 endValue,
        double duration = 0.0,
        Easing easing = Easing.None)
        where TDrawable : Drawable
        => sequence.TransformBindableTo(bindable, endValue, duration, easing);

    /// <inheritdoc cref="TransformBindableTo{TDrawable, TEasing}(TransformSequence{TDrawable}, Bindable{float}, float, double, TEasing)"/>
    public static TransformSequence<TDrawable> TransformTo<TDrawable, TEasing>(
        this TransformSequence<TDrawable> sequence,
        Bindable<Color4> bindable,
        Color4 endValue,
        double duration,
        TEasing easing)
        where TDrawable : Drawable
        where TEasing : struct, IEasingFunction
        => sequence.TransformBindableTo(bindable, endValue, duration, easing);

    /// <inheritdoc cref="TransformBindableTo{TDrawable}(TDrawable, Bindable{float}, float, double, Easing)"/>
    public static TransformSequence<TDrawable> TransformTo<TDrawable>(
        this TDrawable target,
        Bindable<MarginPadding> bindable,
        MarginPadding endValue,
        double duration = 0.0,
        Easing easing = Easing.None)
        where TDrawable : Drawable
        => target.TransformBindableTo(bindable, endValue, duration, easing);

    /// <inheritdoc cref="TransformBindableTo{TDrawable, TEasing}(TDrawable, Bindable{float}, float, double, TEasing)"/>
    public static TransformSequence<TDrawable> TransformTo<TDrawable, TEasing>(
        this TDrawable target,
        Bindable<MarginPadding> bindable,
        MarginPadding endValue,
        double duration,
        TEasing easing)
        where TDrawable : Drawable
        where TEasing : struct, IEasingFunction
        => target.TransformBindableTo(bindable, endValue, duration, easing);

    /// <inheritdoc cref="TransformBindableTo{TDrawable}(TransformSequence{TDrawable}, Bindable{float}, float, double, Easing)"/>
    public static TransformSequence<TDrawable> TransformTo<TDrawable>(
        this TransformSequence<TDrawable> sequence,
        Bindable<MarginPadding> bindable,
        MarginPadding endValue,
        double duration = 0.0,
        Easing easing = Easing.None)
        where TDrawable : Drawable
        => sequence.TransformBindableTo(bindable, endValue, duration, easing);

    /// <inheritdoc cref="TransformBindableTo{TDrawable, TEasing}(TransformSequence{TDrawable}, Bindable{float}, float, double, TEasing)"/>
    public static TransformSequence<TDrawable> TransformTo<TDrawable, TEasing>(
        this TransformSequence<TDrawable> sequence,
        Bindable<MarginPadding> bindable,
        MarginPadding endValue,
        double duration,
        TEasing easing)
        where TDrawable : Drawable
        where TEasing : struct, IEasingFunction
        => sequence.TransformBindableTo(bindable, endValue, duration, easing);

    /// <inheritdoc cref="TransformBindableTo{TDrawable}(TDrawable, Bindable{float}, float, double, Easing)"/>
    public static TransformSequence<TDrawable> TransformTo<TDrawable>(
        this TDrawable target,
        Bindable<RectangleF> bindable,
        RectangleF endValue,
        double duration = 0.0,
        Easing easing = Easing.None)
        where TDrawable : Drawable
        => target.TransformBindableTo(bindable, endValue, duration, easing);

    /// <inheritdoc cref="TransformBindableTo{TDrawable, TEasing}(TDrawable, Bindable{float}, float, double, TEasing)"/>
    public static TransformSequence<TDrawable> TransformTo<TDrawable, TEasing>(
        this TDrawable target,
        Bindable<RectangleF> bindable,
        RectangleF endValue,
        double duration,
        TEasing easing)
        where TDrawable : Drawable
        where TEasing : struct, IEasingFunction
        => target.TransformBindableTo(bindable, endValue, duration, easing);

    /// <inheritdoc cref="TransformBindableTo{TDrawable}(TransformSequence{TDrawable}, Bindable{float}, float, double, Easing)"/>
    public static TransformSequence<TDrawable> TransformTo<TDrawable>(
        this TransformSequence<TDrawable> sequence,
        Bindable<RectangleF> bindable,
        RectangleF endValue,
        double duration = 0.0,
        Easing easing = Easing.None)
        where TDrawable : Drawable
        => sequence.TransformBindableTo(bindable, endValue, duration, easing);

    /// <inheritdoc cref="TransformBindableTo{TDrawable, TEasing}(TransformSequence{TDrawable}, Bindable{float}, float, double, TEasing)"/>
    public static TransformSequence<TDrawable> TransformTo<TDrawable, TEasing>(
        this TransformSequence<TDrawable> sequence,
        Bindable<RectangleF> bindable,
        RectangleF endValue,
        double duration,
        TEasing easing)
        where TDrawable : Drawable
        where TEasing : struct, IEasingFunction
        => sequence.TransformBindableTo(bindable, endValue, duration, easing);

    /// <inheritdoc cref="TransformBindableTo{TDrawable, TValue}(TDrawable, Bindable{TValue}, TValue, double, Func{TValue, TValue, double, TValue}, Easing)"/>
    public static TransformSequence<TDrawable> TransformTo<TDrawable, TValue>(
        this TDrawable target,
        Bindable<TValue> bindable,
        TValue endValue,
        double duration,
        Func<TValue, TValue, double, TValue> interpolator,
        Easing easing = Easing.None)
        where TDrawable : Drawable
        => target.TransformBindableTo(bindable, endValue, duration, interpolator, easing);

    /// <inheritdoc cref="TransformBindableTo{TDrawable, TValue, TEasing}(TDrawable, Bindable{TValue}, TValue, double, Func{TValue, TValue, double, TValue}, TEasing)"/>
    public static TransformSequence<TDrawable> TransformTo<TDrawable, TValue, TEasing>(
        this TDrawable target,
        Bindable<TValue> bindable,
        TValue endValue,
        double duration,
        Func<TValue, TValue, double, TValue> interpolator,
        TEasing easing)
        where TDrawable : Drawable
        where TEasing : struct, IEasingFunction
        => target.TransformBindableTo(bindable, endValue, duration, interpolator, easing);

    /// <inheritdoc cref="TransformBindableTo{TDrawable, TValue}(TransformSequence{TDrawable}, Bindable{TValue}, TValue, double, Func{TValue, TValue, double, TValue}, Easing)"/>
    public static TransformSequence<TDrawable> TransformTo<TDrawable, TValue>(
        this TransformSequence<TDrawable> sequence,
        Bindable<TValue> bindable,
        TValue endValue,
        double duration,
        Func<TValue, TValue, double, TValue> interpolator,
        Easing easing = Easing.None)
        where TDrawable : Drawable
        => sequence.TransformBindableTo(bindable, endValue, duration, interpolator, easing);

    /// <inheritdoc cref="TransformBindableTo{TDrawable, TValue, TEasing}(TransformSequence{TDrawable}, Bindable{TValue}, TValue, double, Func{TValue, TValue, double, TValue}, TEasing)"/>
    public static TransformSequence<TDrawable> TransformTo<TDrawable, TValue, TEasing>(
        this TransformSequence<TDrawable> sequence,
        Bindable<TValue> bindable,
        TValue endValue,
        double duration,
        Func<TValue, TValue, double, TValue> interpolator,
        TEasing easing)
        where TDrawable : Drawable
        where TEasing : struct, IEasingFunction
        => sequence.TransformBindableTo(bindable, endValue, duration, interpolator, easing);

    #endregion

    private static TransformSequence<TDrawable> appendDelegateTransform<TDrawable, TValue, TEasing, TInterpolator>(
        TransformSequence<TDrawable> sequence,
        Func<TDrawable, TValue> getter,
        Action<TDrawable, TValue> setter,
        TValue endValue,
        double duration,
        TEasing easing,
        TInterpolator interpolator,
        string? targetName)
        where TDrawable : Drawable
        where TEasing : struct, IEasingFunction
        where TInterpolator : struct, IValueInterpolator<TValue>
    {
        ArgumentNullException.ThrowIfNull(getter);
        ArgumentNullException.ThrowIfNull(setter);

        var normalizedName = string.IsNullOrWhiteSpace(targetName) ? "__custom_property__" : targetName;
        var handler = new DelegateTransformHandler<TDrawable, TValue, TEasing, TInterpolator>(
            TransformTargetKey.FromCustomName(normalizedName),
            getter,
            setter,
            default!,
            endValue,
            easing,
            interpolator);

        return sequence.AppendCustom(handler, duration);
    }

    private static TransformSequence<TDrawable> appendBindableTransform<TDrawable, TValue, TEasing, TInterpolator>(
        TransformSequence<TDrawable> sequence,
        Bindable<TValue> bindable,
        TValue endValue,
        double duration,
        TEasing easing,
        TInterpolator interpolator)
        where TDrawable : Drawable
        where TEasing : struct, IEasingFunction
        where TInterpolator : struct, IValueInterpolator<TValue>
    {
        ArgumentNullException.ThrowIfNull(bindable);

        var handler = new BindableTransformHandler<TValue, TEasing, TInterpolator>(
            bindable,
            default!,
            endValue,
            easing,
            interpolator);

        return sequence.AppendCustom(handler, duration);
    }
}
