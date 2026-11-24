using System.Numerics;
using kajarlabs.Framework.Numerics;

namespace kajarlabs.Framework.Scene.Transforms;

/// <summary>
/// Provides timing scopes, fluent sequence entry points, and built-in animation extension methods
/// for <see cref="Drawable"/>, <see cref="CompositeDrawable"/>, and <see cref="TransformSequence{TDrawable}"/>.
/// </summary>
public static class DrawableTransformSequenceExtensions
{
    #region Timing scopes & sequence entry

    /// <summary>
    /// Enters a disposable scope that offsets the default transform start time for <paramref name="target"/>
    /// by <paramref name="delay"/> milliseconds relative to the current active start time (or <see cref="Drawable.Time"/>).
    /// </summary>
    public static TransformSequenceScope BeginDelayedSequence(this Drawable target, double delay)
    {
        ArgumentNullException.ThrowIfNull(target);
        return DrawableTransformState.GetOrAttach(target).EnterDelayedSequence(target, delay);
    }

    /// <summary>
    /// Enters a disposable scope that sets the default transform start time for <paramref name="target"/>
    /// to the absolute clock time <paramref name="newTransformStartTime"/> in milliseconds.
    /// </summary>
    public static TransformSequenceScope BeginAbsoluteSequence(this Drawable target, double newTransformStartTime)
    {
        ArgumentNullException.ThrowIfNull(target);
        return DrawableTransformState.GetOrAttach(target).EnterAbsoluteSequence(target, newTransformStartTime);
    }

    /// <summary>
    /// Begins a new <see cref="TransformSequence{TDrawable}"/> advanced by <paramref name="duration"/> milliseconds.
    /// </summary>
    public static TransformSequence<TDrawable> Delay<TDrawable>(this TDrawable target, double duration)
        where TDrawable : Drawable
        => TransformSequence<TDrawable>.Create(target).Delay(duration);

    #endregion

    #region Opacity & Color

    /// <summary>
    /// Animates <see cref="Drawable.Alpha"/> to <paramref name="newAlpha"/> over <paramref name="duration"/> milliseconds.
    /// </summary>
    public static TransformSequence<TDrawable> FadeTo<TDrawable>(
        this TDrawable target,
        float newAlpha,
        double duration = 0.0,
        Easing easing = Easing.None)
        where TDrawable : Drawable
        => TransformSequence<TDrawable>.Create(target).FadeTo(newAlpha, duration, easing);

    /// <summary>
    /// Animates <see cref="Drawable.Alpha"/> to <paramref name="newAlpha"/> over <paramref name="duration"/> milliseconds
    /// using the value-type easing function <paramref name="easing"/>.
    /// </summary>
    public static TransformSequence<TDrawable> FadeTo<TDrawable, TEasing>(
        this TDrawable target,
        float newAlpha,
        double duration,
        TEasing easing)
        where TDrawable : Drawable
        where TEasing : struct, IEasingFunction
        => TransformSequence<TDrawable>.Create(target).FadeTo(newAlpha, duration, easing);

    /// <summary>
    /// Appends an animation of <see cref="Drawable.Alpha"/> to <paramref name="newAlpha"/> onto <paramref name="sequence"/>.
    /// </summary>
    public static TransformSequence<TDrawable> FadeTo<TDrawable>(
        this TransformSequence<TDrawable> sequence,
        float newAlpha,
        double duration = 0.0,
        Easing easing = Easing.None)
        where TDrawable : Drawable
        => sequence.AppendBuiltIn(BuiltInTransformTarget.Alpha, TransformValue.FromSingle(newAlpha), duration, easing);

    /// <summary>
    /// Appends an animation of <see cref="Drawable.Alpha"/> to <paramref name="newAlpha"/> onto <paramref name="sequence"/>
    /// using the value-type easing function <paramref name="easing"/>.
    /// </summary>
    public static TransformSequence<TDrawable> FadeTo<TDrawable, TEasing>(
        this TransformSequence<TDrawable> sequence,
        float newAlpha,
        double duration,
        TEasing easing)
        where TDrawable : Drawable
        where TEasing : struct, IEasingFunction
        => sequence.AppendCustom(
            new BuiltInCustomEasingHandler<TEasing>(BuiltInTransformTarget.Alpha, default, TransformValue.FromSingle(newAlpha), easing),
            duration);

    /// <summary>
    /// Fades <see cref="Drawable.Alpha"/> to <c>1</c> over <paramref name="duration"/> milliseconds.
    /// </summary>
    public static TransformSequence<TDrawable> FadeIn<TDrawable>(
        this TDrawable target,
        double duration = 0.0,
        Easing easing = Easing.None)
        where TDrawable : Drawable
        => target.FadeTo(1f, duration, easing);

    /// <summary>
    /// Fades <see cref="Drawable.Alpha"/> to <c>1</c> over <paramref name="duration"/> milliseconds using <paramref name="easing"/>.
    /// </summary>
    public static TransformSequence<TDrawable> FadeIn<TDrawable, TEasing>(
        this TDrawable target,
        double duration,
        TEasing easing)
        where TDrawable : Drawable
        where TEasing : struct, IEasingFunction
        => target.FadeTo(1f, duration, easing);

    /// <summary>
    /// Appends a fade-in to <c>Alpha = 1</c> onto <paramref name="sequence"/>.
    /// </summary>
    public static TransformSequence<TDrawable> FadeIn<TDrawable>(
        this TransformSequence<TDrawable> sequence,
        double duration = 0.0,
        Easing easing = Easing.None)
        where TDrawable : Drawable
        => sequence.FadeTo(1f, duration, easing);

    /// <summary>
    /// Appends a fade-in to <c>Alpha = 1</c> onto <paramref name="sequence"/> using <paramref name="easing"/>.
    /// </summary>
    public static TransformSequence<TDrawable> FadeIn<TDrawable, TEasing>(
        this TransformSequence<TDrawable> sequence,
        double duration,
        TEasing easing)
        where TDrawable : Drawable
        where TEasing : struct, IEasingFunction
        => sequence.FadeTo(1f, duration, easing);

    /// <summary>
    /// Sets <see cref="Drawable.Alpha"/> to <c>0</c> at the sequence start time and fades in to <c>1</c> over <paramref name="duration"/> milliseconds.
    /// </summary>
    public static TransformSequence<TDrawable> FadeInFromZero<TDrawable>(
        this TDrawable target,
        double duration = 0.0,
        Easing easing = Easing.None)
        where TDrawable : Drawable
        => TransformSequence<TDrawable>.Create(target).FadeInFromZero(duration, easing);

    /// <summary>
    /// Sets <see cref="Drawable.Alpha"/> to <c>0</c> at the sequence start time and fades in to <c>1</c> using <paramref name="easing"/>.
    /// </summary>
    public static TransformSequence<TDrawable> FadeInFromZero<TDrawable, TEasing>(
        this TDrawable target,
        double duration,
        TEasing easing)
        where TDrawable : Drawable
        where TEasing : struct, IEasingFunction
        => TransformSequence<TDrawable>.Create(target).FadeInFromZero(duration, easing);

    /// <summary>
    /// Appends a reset to <c>Alpha = 0</c> followed by a fade-in to <c>Alpha = 1</c> onto <paramref name="sequence"/>.
    /// </summary>
    public static TransformSequence<TDrawable> FadeInFromZero<TDrawable>(
        this TransformSequence<TDrawable> sequence,
        double duration = 0.0,
        Easing easing = Easing.None)
        where TDrawable : Drawable
        => sequence.FadeTo(0f).FadeTo(1f, duration, easing);

    /// <summary>
    /// Appends a reset to <c>Alpha = 0</c> followed by a fade-in to <c>Alpha = 1</c> onto <paramref name="sequence"/> using <paramref name="easing"/>.
    /// </summary>
    public static TransformSequence<TDrawable> FadeInFromZero<TDrawable, TEasing>(
        this TransformSequence<TDrawable> sequence,
        double duration,
        TEasing easing)
        where TDrawable : Drawable
        where TEasing : struct, IEasingFunction
        => sequence.FadeTo(0f).FadeTo(1f, duration, easing);

    /// <summary>
    /// Fades <see cref="Drawable.Alpha"/> to <c>0</c> over <paramref name="duration"/> milliseconds.
    /// </summary>
    public static TransformSequence<TDrawable> FadeOut<TDrawable>(
        this TDrawable target,
        double duration = 0.0,
        Easing easing = Easing.None)
        where TDrawable : Drawable
        => target.FadeTo(0f, duration, easing);

    /// <summary>
    /// Fades <see cref="Drawable.Alpha"/> to <c>0</c> over <paramref name="duration"/> milliseconds using <paramref name="easing"/>.
    /// </summary>
    public static TransformSequence<TDrawable> FadeOut<TDrawable, TEasing>(
        this TDrawable target,
        double duration,
        TEasing easing)
        where TDrawable : Drawable
        where TEasing : struct, IEasingFunction
        => target.FadeTo(0f, duration, easing);

    /// <summary>
    /// Appends a fade-out to <c>Alpha = 0</c> onto <paramref name="sequence"/>.
    /// </summary>
    public static TransformSequence<TDrawable> FadeOut<TDrawable>(
        this TransformSequence<TDrawable> sequence,
        double duration = 0.0,
        Easing easing = Easing.None)
        where TDrawable : Drawable
        => sequence.FadeTo(0f, duration, easing);

    /// <summary>
    /// Appends a fade-out to <c>Alpha = 0</c> onto <paramref name="sequence"/> using <paramref name="easing"/>.
    /// </summary>
    public static TransformSequence<TDrawable> FadeOut<TDrawable, TEasing>(
        this TransformSequence<TDrawable> sequence,
        double duration,
        TEasing easing)
        where TDrawable : Drawable
        where TEasing : struct, IEasingFunction
        => sequence.FadeTo(0f, duration, easing);

    /// <summary>
    /// Sets <see cref="Drawable.Alpha"/> to <c>1</c> at the sequence start time and fades out to <c>0</c> over <paramref name="duration"/> milliseconds.
    /// </summary>
    public static TransformSequence<TDrawable> FadeOutFromOne<TDrawable>(
        this TDrawable target,
        double duration = 0.0,
        Easing easing = Easing.None)
        where TDrawable : Drawable
        => TransformSequence<TDrawable>.Create(target).FadeOutFromOne(duration, easing);

    /// <summary>
    /// Sets <see cref="Drawable.Alpha"/> to <c>1</c> at the sequence start time and fades out to <c>0</c> using <paramref name="easing"/>.
    /// </summary>
    public static TransformSequence<TDrawable> FadeOutFromOne<TDrawable, TEasing>(
        this TDrawable target,
        double duration,
        TEasing easing)
        where TDrawable : Drawable
        where TEasing : struct, IEasingFunction
        => TransformSequence<TDrawable>.Create(target).FadeOutFromOne(duration, easing);

    /// <summary>
    /// Appends a reset to <c>Alpha = 1</c> followed by a fade-out to <c>Alpha = 0</c> onto <paramref name="sequence"/>.
    /// </summary>
    public static TransformSequence<TDrawable> FadeOutFromOne<TDrawable>(
        this TransformSequence<TDrawable> sequence,
        double duration = 0.0,
        Easing easing = Easing.None)
        where TDrawable : Drawable
        => sequence.FadeTo(1f).FadeTo(0f, duration, easing);

    /// <summary>
    /// Appends a reset to <c>Alpha = 1</c> followed by a fade-out to <c>Alpha = 0</c> onto <paramref name="sequence"/> using <paramref name="easing"/>.
    /// </summary>
    public static TransformSequence<TDrawable> FadeOutFromOne<TDrawable, TEasing>(
        this TransformSequence<TDrawable> sequence,
        double duration,
        TEasing easing)
        where TDrawable : Drawable
        where TEasing : struct, IEasingFunction
        => sequence.FadeTo(1f).FadeTo(0f, duration, easing);

    /// <summary>
    /// Animates <see cref="Drawable.Color"/> to <paramref name="newColor"/> over <paramref name="duration"/> milliseconds.
    /// </summary>
    public static TransformSequence<TDrawable> FadeColor<TDrawable>(
        this TDrawable target,
        Color4 newColor,
        double duration = 0.0,
        Easing easing = Easing.None)
        where TDrawable : Drawable
        => TransformSequence<TDrawable>.Create(target).FadeColor(newColor, duration, easing);

    /// <summary>
    /// Animates <see cref="Drawable.Color"/> to <paramref name="newColor"/> over <paramref name="duration"/> milliseconds using <paramref name="easing"/>.
    /// </summary>
    public static TransformSequence<TDrawable> FadeColor<TDrawable, TEasing>(
        this TDrawable target,
        Color4 newColor,
        double duration,
        TEasing easing)
        where TDrawable : Drawable
        where TEasing : struct, IEasingFunction
        => TransformSequence<TDrawable>.Create(target).FadeColor(newColor, duration, easing);

    /// <summary>
    /// Appends a <see cref="Drawable.Color"/> animation to <paramref name="newColor"/> onto <paramref name="sequence"/>.
    /// </summary>
    public static TransformSequence<TDrawable> FadeColor<TDrawable>(
        this TransformSequence<TDrawable> sequence,
        Color4 newColor,
        double duration = 0.0,
        Easing easing = Easing.None)
        where TDrawable : Drawable
        => sequence.AppendBuiltIn(BuiltInTransformTarget.Color, TransformValue.FromColor4(newColor), duration, easing);

    /// <summary>
    /// Appends a <see cref="Drawable.Color"/> animation to <paramref name="newColor"/> onto <paramref name="sequence"/> using <paramref name="easing"/>.
    /// </summary>
    public static TransformSequence<TDrawable> FadeColor<TDrawable, TEasing>(
        this TransformSequence<TDrawable> sequence,
        Color4 newColor,
        double duration,
        TEasing easing)
        where TDrawable : Drawable
        where TEasing : struct, IEasingFunction
        => sequence.AppendCustom(
            new BuiltInCustomEasingHandler<TEasing>(BuiltInTransformTarget.Color, default, TransformValue.FromColor4(newColor), easing),
            duration);

    /// <summary>
    /// Immediately flashes <see cref="Drawable.Color"/> to <paramref name="flashColor"/> at the sequence start time,
    /// then fades back to the underlying target color over <paramref name="duration"/> milliseconds.
    /// </summary>
    public static TransformSequence<TDrawable> FlashColor<TDrawable>(
        this TDrawable target,
        Color4 flashColor,
        double duration,
        Easing easing = Easing.None)
        where TDrawable : Drawable
        => TransformSequence<TDrawable>.Create(target).FlashColor(flashColor, duration, easing);

    /// <summary>
    /// Immediately flashes <see cref="Drawable.Color"/> to <paramref name="flashColor"/> at the sequence start time,
    /// then fades back to the underlying target color over <paramref name="duration"/> milliseconds using <paramref name="easing"/>.
    /// </summary>
    public static TransformSequence<TDrawable> FlashColor<TDrawable, TEasing>(
        this TDrawable target,
        Color4 flashColor,
        double duration,
        TEasing easing)
        where TDrawable : Drawable
        where TEasing : struct, IEasingFunction
        => TransformSequence<TDrawable>.Create(target).FlashColor(flashColor, duration, easing);

    /// <summary>
    /// Appends a color flash from <paramref name="flashColor"/> back to the resolved target color onto <paramref name="sequence"/>.
    /// </summary>
    public static TransformSequence<TDrawable> FlashColor<TDrawable>(
        this TransformSequence<TDrawable> sequence,
        Color4 flashColor,
        double duration,
        Easing easing = Easing.None)
        where TDrawable : Drawable
    {
        var returnColor = sequence.State.ResolveEffectiveColorForFlash(sequence.Target, sequence.CurrentStartTime);
        return sequence.FadeColor(flashColor).FadeColor(returnColor, duration, easing);
    }

    /// <summary>
    /// Appends a color flash from <paramref name="flashColor"/> back to the resolved target color onto <paramref name="sequence"/>
    /// using <paramref name="easing"/>.
    /// </summary>
    public static TransformSequence<TDrawable> FlashColor<TDrawable, TEasing>(
        this TransformSequence<TDrawable> sequence,
        Color4 flashColor,
        double duration,
        TEasing easing)
        where TDrawable : Drawable
        where TEasing : struct, IEasingFunction
    {
        var returnColor = sequence.State.ResolveEffectiveColorForFlash(sequence.Target, sequence.CurrentStartTime);
        return sequence.FadeColor(flashColor).FadeColor(returnColor, duration, easing);
    }

    #endregion

    #region Spatial & Geometry

    /// <summary>
    /// Animates <see cref="Drawable.Position"/> to <paramref name="newPosition"/> over <paramref name="duration"/> milliseconds.
    /// </summary>
    public static TransformSequence<TDrawable> MoveTo<TDrawable>(
        this TDrawable target,
        Vector2 newPosition,
        double duration = 0.0,
        Easing easing = Easing.None)
        where TDrawable : Drawable
        => TransformSequence<TDrawable>.Create(target).MoveTo(newPosition, duration, easing);

    /// <summary>
    /// Animates <see cref="Drawable.Position"/> to <paramref name="newPosition"/> over <paramref name="duration"/> milliseconds using <paramref name="easing"/>.
    /// </summary>
    public static TransformSequence<TDrawable> MoveTo<TDrawable, TEasing>(
        this TDrawable target,
        Vector2 newPosition,
        double duration,
        TEasing easing)
        where TDrawable : Drawable
        where TEasing : struct, IEasingFunction
        => TransformSequence<TDrawable>.Create(target).MoveTo(newPosition, duration, easing);

    /// <summary>
    /// Appends a <see cref="Drawable.Position"/> animation to <paramref name="newPosition"/> onto <paramref name="sequence"/>.
    /// </summary>
    public static TransformSequence<TDrawable> MoveTo<TDrawable>(
        this TransformSequence<TDrawable> sequence,
        Vector2 newPosition,
        double duration = 0.0,
        Easing easing = Easing.None)
        where TDrawable : Drawable
        => sequence.AppendBuiltIn(BuiltInTransformTarget.Position, TransformValue.FromVector2(newPosition), duration, easing);

    /// <summary>
    /// Appends a <see cref="Drawable.Position"/> animation to <paramref name="newPosition"/> onto <paramref name="sequence"/> using <paramref name="easing"/>.
    /// </summary>
    public static TransformSequence<TDrawable> MoveTo<TDrawable, TEasing>(
        this TransformSequence<TDrawable> sequence,
        Vector2 newPosition,
        double duration,
        TEasing easing)
        where TDrawable : Drawable
        where TEasing : struct, IEasingFunction
        => sequence.AppendCustom(
            new BuiltInCustomEasingHandler<TEasing>(BuiltInTransformTarget.Position, default, TransformValue.FromVector2(newPosition), easing),
            duration);

    /// <summary>
    /// Animates <see cref="Drawable.X"/> to <paramref name="newX"/> over <paramref name="duration"/> milliseconds.
    /// </summary>
    public static TransformSequence<TDrawable> MoveToX<TDrawable>(
        this TDrawable target,
        float newX,
        double duration = 0.0,
        Easing easing = Easing.None)
        where TDrawable : Drawable
        => TransformSequence<TDrawable>.Create(target).MoveToX(newX, duration, easing);

    /// <summary>
    /// Animates <see cref="Drawable.X"/> to <paramref name="newX"/> over <paramref name="duration"/> milliseconds using <paramref name="easing"/>.
    /// </summary>
    public static TransformSequence<TDrawable> MoveToX<TDrawable, TEasing>(
        this TDrawable target,
        float newX,
        double duration,
        TEasing easing)
        where TDrawable : Drawable
        where TEasing : struct, IEasingFunction
        => TransformSequence<TDrawable>.Create(target).MoveToX(newX, duration, easing);

    /// <summary>
    /// Appends a <see cref="Drawable.X"/> animation to <paramref name="newX"/> onto <paramref name="sequence"/>.
    /// </summary>
    public static TransformSequence<TDrawable> MoveToX<TDrawable>(
        this TransformSequence<TDrawable> sequence,
        float newX,
        double duration = 0.0,
        Easing easing = Easing.None)
        where TDrawable : Drawable
        => sequence.AppendBuiltIn(BuiltInTransformTarget.X, TransformValue.FromSingle(newX), duration, easing);

    /// <summary>
    /// Appends a <see cref="Drawable.X"/> animation to <paramref name="newX"/> onto <paramref name="sequence"/> using <paramref name="easing"/>.
    /// </summary>
    public static TransformSequence<TDrawable> MoveToX<TDrawable, TEasing>(
        this TransformSequence<TDrawable> sequence,
        float newX,
        double duration,
        TEasing easing)
        where TDrawable : Drawable
        where TEasing : struct, IEasingFunction
        => sequence.AppendCustom(
            new BuiltInCustomEasingHandler<TEasing>(BuiltInTransformTarget.X, default, TransformValue.FromSingle(newX), easing),
            duration);

    /// <summary>
    /// Animates <see cref="Drawable.Y"/> to <paramref name="newY"/> over <paramref name="duration"/> milliseconds.
    /// </summary>
    public static TransformSequence<TDrawable> MoveToY<TDrawable>(
        this TDrawable target,
        float newY,
        double duration = 0.0,
        Easing easing = Easing.None)
        where TDrawable : Drawable
        => TransformSequence<TDrawable>.Create(target).MoveToY(newY, duration, easing);

    /// <summary>
    /// Animates <see cref="Drawable.Y"/> to <paramref name="newY"/> over <paramref name="duration"/> milliseconds using <paramref name="easing"/>.
    /// </summary>
    public static TransformSequence<TDrawable> MoveToY<TDrawable, TEasing>(
        this TDrawable target,
        float newY,
        double duration,
        TEasing easing)
        where TDrawable : Drawable
        where TEasing : struct, IEasingFunction
        => TransformSequence<TDrawable>.Create(target).MoveToY(newY, duration, easing);

    /// <summary>
    /// Appends a <see cref="Drawable.Y"/> animation to <paramref name="newY"/> onto <paramref name="sequence"/>.
    /// </summary>
    public static TransformSequence<TDrawable> MoveToY<TDrawable>(
        this TransformSequence<TDrawable> sequence,
        float newY,
        double duration = 0.0,
        Easing easing = Easing.None)
        where TDrawable : Drawable
        => sequence.AppendBuiltIn(BuiltInTransformTarget.Y, TransformValue.FromSingle(newY), duration, easing);

    /// <summary>
    /// Appends a <see cref="Drawable.Y"/> animation to <paramref name="newY"/> onto <paramref name="sequence"/> using <paramref name="easing"/>.
    /// </summary>
    public static TransformSequence<TDrawable> MoveToY<TDrawable, TEasing>(
        this TransformSequence<TDrawable> sequence,
        float newY,
        double duration,
        TEasing easing)
        where TDrawable : Drawable
        where TEasing : struct, IEasingFunction
        => sequence.AppendCustom(
            new BuiltInCustomEasingHandler<TEasing>(BuiltInTransformTarget.Y, default, TransformValue.FromSingle(newY), easing),
            duration);

    /// <summary>
    /// Animates <see cref="Drawable.Position"/> by adding <paramref name="offset"/> to the effective position at the sequence start time.
    /// </summary>
    public static TransformSequence<TDrawable> MoveToOffset<TDrawable>(
        this TDrawable target,
        Vector2 offset,
        double duration = 0.0,
        Easing easing = Easing.None)
        where TDrawable : Drawable
        => TransformSequence<TDrawable>.Create(target).MoveToOffset(offset, duration, easing);

    /// <summary>
    /// Animates <see cref="Drawable.Position"/> by adding <paramref name="offset"/> to the effective position at the sequence start time using <paramref name="easing"/>.
    /// </summary>
    public static TransformSequence<TDrawable> MoveToOffset<TDrawable, TEasing>(
        this TDrawable target,
        Vector2 offset,
        double duration,
        TEasing easing)
        where TDrawable : Drawable
        where TEasing : struct, IEasingFunction
        => TransformSequence<TDrawable>.Create(target).MoveToOffset(offset, duration, easing);

    /// <summary>
    /// Appends a relative position offset animation onto <paramref name="sequence"/>.
    /// </summary>
    public static TransformSequence<TDrawable> MoveToOffset<TDrawable>(
        this TransformSequence<TDrawable> sequence,
        Vector2 offset,
        double duration = 0.0,
        Easing easing = Easing.None)
        where TDrawable : Drawable
    {
        var basePosition = sequence.State.ResolveEffectivePositionAt(sequence.Target, sequence.CurrentStartTime);
        return sequence.MoveTo(basePosition + offset, duration, easing);
    }

    /// <summary>
    /// Appends a relative position offset animation onto <paramref name="sequence"/> using <paramref name="easing"/>.
    /// </summary>
    public static TransformSequence<TDrawable> MoveToOffset<TDrawable, TEasing>(
        this TransformSequence<TDrawable> sequence,
        Vector2 offset,
        double duration,
        TEasing easing)
        where TDrawable : Drawable
        where TEasing : struct, IEasingFunction
    {
        var basePosition = sequence.State.ResolveEffectivePositionAt(sequence.Target, sequence.CurrentStartTime);
        return sequence.MoveTo(basePosition + offset, duration, easing);
    }

    /// <summary>
    /// Animates <see cref="Drawable.Size"/> to <paramref name="newSize"/> over <paramref name="duration"/> milliseconds.
    /// </summary>
    public static TransformSequence<TDrawable> ResizeTo<TDrawable>(
        this TDrawable target,
        Vector2 newSize,
        double duration = 0.0,
        Easing easing = Easing.None)
        where TDrawable : Drawable
        => TransformSequence<TDrawable>.Create(target).ResizeTo(newSize, duration, easing);

    /// <summary>
    /// Animates <see cref="Drawable.Size"/> to <paramref name="newSize"/> on both axes over <paramref name="duration"/> milliseconds.
    /// </summary>
    public static TransformSequence<TDrawable> ResizeTo<TDrawable>(
        this TDrawable target,
        float newSize,
        double duration = 0.0,
        Easing easing = Easing.None)
        where TDrawable : Drawable
        => target.ResizeTo(new Vector2(newSize), duration, easing);

    /// <summary>
    /// Animates <see cref="Drawable.Size"/> to <paramref name="newSize"/> over <paramref name="duration"/> milliseconds using <paramref name="easing"/>.
    /// </summary>
    public static TransformSequence<TDrawable> ResizeTo<TDrawable, TEasing>(
        this TDrawable target,
        Vector2 newSize,
        double duration,
        TEasing easing)
        where TDrawable : Drawable
        where TEasing : struct, IEasingFunction
        => TransformSequence<TDrawable>.Create(target).ResizeTo(newSize, duration, easing);

    /// <summary>
    /// Animates <see cref="Drawable.Size"/> to <paramref name="newSize"/> on both axes over <paramref name="duration"/> milliseconds using <paramref name="easing"/>.
    /// </summary>
    public static TransformSequence<TDrawable> ResizeTo<TDrawable, TEasing>(
        this TDrawable target,
        float newSize,
        double duration,
        TEasing easing)
        where TDrawable : Drawable
        where TEasing : struct, IEasingFunction
        => target.ResizeTo(new Vector2(newSize), duration, easing);

    /// <summary>
    /// Appends a <see cref="Drawable.Size"/> animation to <paramref name="newSize"/> onto <paramref name="sequence"/>.
    /// </summary>
    public static TransformSequence<TDrawable> ResizeTo<TDrawable>(
        this TransformSequence<TDrawable> sequence,
        Vector2 newSize,
        double duration = 0.0,
        Easing easing = Easing.None)
        where TDrawable : Drawable
        => sequence.AppendBuiltIn(BuiltInTransformTarget.Size, TransformValue.FromVector2(newSize), duration, easing);

    /// <summary>
    /// Appends a uniform <see cref="Drawable.Size"/> animation to <paramref name="newSize"/> onto <paramref name="sequence"/>.
    /// </summary>
    public static TransformSequence<TDrawable> ResizeTo<TDrawable>(
        this TransformSequence<TDrawable> sequence,
        float newSize,
        double duration = 0.0,
        Easing easing = Easing.None)
        where TDrawable : Drawable
        => sequence.ResizeTo(new Vector2(newSize), duration, easing);

    /// <summary>
    /// Appends a <see cref="Drawable.Size"/> animation to <paramref name="newSize"/> onto <paramref name="sequence"/> using <paramref name="easing"/>.
    /// </summary>
    public static TransformSequence<TDrawable> ResizeTo<TDrawable, TEasing>(
        this TransformSequence<TDrawable> sequence,
        Vector2 newSize,
        double duration,
        TEasing easing)
        where TDrawable : Drawable
        where TEasing : struct, IEasingFunction
        => sequence.AppendCustom(
            new BuiltInCustomEasingHandler<TEasing>(BuiltInTransformTarget.Size, default, TransformValue.FromVector2(newSize), easing),
            duration);

    /// <summary>
    /// Appends a uniform <see cref="Drawable.Size"/> animation to <paramref name="newSize"/> onto <paramref name="sequence"/> using <paramref name="easing"/>.
    /// </summary>
    public static TransformSequence<TDrawable> ResizeTo<TDrawable, TEasing>(
        this TransformSequence<TDrawable> sequence,
        float newSize,
        double duration,
        TEasing easing)
        where TDrawable : Drawable
        where TEasing : struct, IEasingFunction
        => sequence.ResizeTo(new Vector2(newSize), duration, easing);

    /// <summary>
    /// Animates <see cref="Drawable.Width"/> to <paramref name="newWidth"/> over <paramref name="duration"/> milliseconds.
    /// </summary>
    public static TransformSequence<TDrawable> ResizeWidthTo<TDrawable>(
        this TDrawable target,
        float newWidth,
        double duration = 0.0,
        Easing easing = Easing.None)
        where TDrawable : Drawable
        => TransformSequence<TDrawable>.Create(target).ResizeWidthTo(newWidth, duration, easing);

    /// <summary>
    /// Animates <see cref="Drawable.Width"/> to <paramref name="newWidth"/> over <paramref name="duration"/> milliseconds using <paramref name="easing"/>.
    /// </summary>
    public static TransformSequence<TDrawable> ResizeWidthTo<TDrawable, TEasing>(
        this TDrawable target,
        float newWidth,
        double duration,
        TEasing easing)
        where TDrawable : Drawable
        where TEasing : struct, IEasingFunction
        => TransformSequence<TDrawable>.Create(target).ResizeWidthTo(newWidth, duration, easing);

    /// <summary>
    /// Appends a <see cref="Drawable.Width"/> animation to <paramref name="newWidth"/> onto <paramref name="sequence"/>.
    /// </summary>
    public static TransformSequence<TDrawable> ResizeWidthTo<TDrawable>(
        this TransformSequence<TDrawable> sequence,
        float newWidth,
        double duration = 0.0,
        Easing easing = Easing.None)
        where TDrawable : Drawable
        => sequence.AppendBuiltIn(BuiltInTransformTarget.Width, TransformValue.FromSingle(newWidth), duration, easing);

    /// <summary>
    /// Appends a <see cref="Drawable.Width"/> animation to <paramref name="newWidth"/> onto <paramref name="sequence"/> using <paramref name="easing"/>.
    /// </summary>
    public static TransformSequence<TDrawable> ResizeWidthTo<TDrawable, TEasing>(
        this TransformSequence<TDrawable> sequence,
        float newWidth,
        double duration,
        TEasing easing)
        where TDrawable : Drawable
        where TEasing : struct, IEasingFunction
        => sequence.AppendCustom(
            new BuiltInCustomEasingHandler<TEasing>(BuiltInTransformTarget.Width, default, TransformValue.FromSingle(newWidth), easing),
            duration);

    /// <summary>
    /// Animates <see cref="Drawable.Height"/> to <paramref name="newHeight"/> over <paramref name="duration"/> milliseconds.
    /// </summary>
    public static TransformSequence<TDrawable> ResizeHeightTo<TDrawable>(
        this TDrawable target,
        float newHeight,
        double duration = 0.0,
        Easing easing = Easing.None)
        where TDrawable : Drawable
        => TransformSequence<TDrawable>.Create(target).ResizeHeightTo(newHeight, duration, easing);

    /// <summary>
    /// Animates <see cref="Drawable.Height"/> to <paramref name="newHeight"/> over <paramref name="duration"/> milliseconds using <paramref name="easing"/>.
    /// </summary>
    public static TransformSequence<TDrawable> ResizeHeightTo<TDrawable, TEasing>(
        this TDrawable target,
        float newHeight,
        double duration,
        TEasing easing)
        where TDrawable : Drawable
        where TEasing : struct, IEasingFunction
        => TransformSequence<TDrawable>.Create(target).ResizeHeightTo(newHeight, duration, easing);

    /// <summary>
    /// Appends a <see cref="Drawable.Height"/> animation to <paramref name="newHeight"/> onto <paramref name="sequence"/>.
    /// </summary>
    public static TransformSequence<TDrawable> ResizeHeightTo<TDrawable>(
        this TransformSequence<TDrawable> sequence,
        float newHeight,
        double duration = 0.0,
        Easing easing = Easing.None)
        where TDrawable : Drawable
        => sequence.AppendBuiltIn(BuiltInTransformTarget.Height, TransformValue.FromSingle(newHeight), duration, easing);

    /// <summary>
    /// Appends a <see cref="Drawable.Height"/> animation to <paramref name="newHeight"/> onto <paramref name="sequence"/> using <paramref name="easing"/>.
    /// </summary>
    public static TransformSequence<TDrawable> ResizeHeightTo<TDrawable, TEasing>(
        this TransformSequence<TDrawable> sequence,
        float newHeight,
        double duration,
        TEasing easing)
        where TDrawable : Drawable
        where TEasing : struct, IEasingFunction
        => sequence.AppendCustom(
            new BuiltInCustomEasingHandler<TEasing>(BuiltInTransformTarget.Height, default, TransformValue.FromSingle(newHeight), easing),
            duration);

    /// <summary>
    /// Animates <see cref="Drawable.Scale"/> to <paramref name="newScale"/> over <paramref name="duration"/> milliseconds.
    /// </summary>
    public static TransformSequence<TDrawable> ScaleTo<TDrawable>(
        this TDrawable target,
        Vector2 newScale,
        double duration = 0.0,
        Easing easing = Easing.None)
        where TDrawable : Drawable
        => TransformSequence<TDrawable>.Create(target).ScaleTo(newScale, duration, easing);

    /// <summary>
    /// Animates <see cref="Drawable.Scale"/> uniformly to <paramref name="newScale"/> on both axes over <paramref name="duration"/> milliseconds.
    /// </summary>
    public static TransformSequence<TDrawable> ScaleTo<TDrawable>(
        this TDrawable target,
        float newScale,
        double duration = 0.0,
        Easing easing = Easing.None)
        where TDrawable : Drawable
        => target.ScaleTo(new Vector2(newScale), duration, easing);

    /// <summary>
    /// Animates <see cref="Drawable.Scale"/> to <paramref name="newScale"/> over <paramref name="duration"/> milliseconds using <paramref name="easing"/>.
    /// </summary>
    public static TransformSequence<TDrawable> ScaleTo<TDrawable, TEasing>(
        this TDrawable target,
        Vector2 newScale,
        double duration,
        TEasing easing)
        where TDrawable : Drawable
        where TEasing : struct, IEasingFunction
        => TransformSequence<TDrawable>.Create(target).ScaleTo(newScale, duration, easing);

    /// <summary>
    /// Animates <see cref="Drawable.Scale"/> uniformly to <paramref name="newScale"/> over <paramref name="duration"/> milliseconds using <paramref name="easing"/>.
    /// </summary>
    public static TransformSequence<TDrawable> ScaleTo<TDrawable, TEasing>(
        this TDrawable target,
        float newScale,
        double duration,
        TEasing easing)
        where TDrawable : Drawable
        where TEasing : struct, IEasingFunction
        => target.ScaleTo(new Vector2(newScale), duration, easing);

    /// <summary>
    /// Appends a <see cref="Drawable.Scale"/> animation to <paramref name="newScale"/> onto <paramref name="sequence"/>.
    /// </summary>
    public static TransformSequence<TDrawable> ScaleTo<TDrawable>(
        this TransformSequence<TDrawable> sequence,
        Vector2 newScale,
        double duration = 0.0,
        Easing easing = Easing.None)
        where TDrawable : Drawable
        => sequence.AppendBuiltIn(BuiltInTransformTarget.Scale, TransformValue.FromVector2(newScale), duration, easing);

    /// <summary>
    /// Appends a uniform <see cref="Drawable.Scale"/> animation to <paramref name="newScale"/> onto <paramref name="sequence"/>.
    /// </summary>
    public static TransformSequence<TDrawable> ScaleTo<TDrawable>(
        this TransformSequence<TDrawable> sequence,
        float newScale,
        double duration = 0.0,
        Easing easing = Easing.None)
        where TDrawable : Drawable
        => sequence.ScaleTo(new Vector2(newScale), duration, easing);

    /// <summary>
    /// Appends a <see cref="Drawable.Scale"/> animation to <paramref name="newScale"/> onto <paramref name="sequence"/> using <paramref name="easing"/>.
    /// </summary>
    public static TransformSequence<TDrawable> ScaleTo<TDrawable, TEasing>(
        this TransformSequence<TDrawable> sequence,
        Vector2 newScale,
        double duration,
        TEasing easing)
        where TDrawable : Drawable
        where TEasing : struct, IEasingFunction
        => sequence.AppendCustom(
            new BuiltInCustomEasingHandler<TEasing>(BuiltInTransformTarget.Scale, default, TransformValue.FromVector2(newScale), easing),
            duration);

    /// <summary>
    /// Appends a uniform <see cref="Drawable.Scale"/> animation to <paramref name="newScale"/> onto <paramref name="sequence"/> using <paramref name="easing"/>.
    /// </summary>
    public static TransformSequence<TDrawable> ScaleTo<TDrawable, TEasing>(
        this TransformSequence<TDrawable> sequence,
        float newScale,
        double duration,
        TEasing easing)
        where TDrawable : Drawable
        where TEasing : struct, IEasingFunction
        => sequence.ScaleTo(new Vector2(newScale), duration, easing);

    /// <summary>
    /// Animates <see cref="Drawable.Rotation"/> to <paramref name="newRotation"/> degrees over <paramref name="duration"/> milliseconds.
    /// </summary>
    public static TransformSequence<TDrawable> RotateTo<TDrawable>(
        this TDrawable target,
        float newRotation,
        double duration = 0.0,
        Easing easing = Easing.None)
        where TDrawable : Drawable
        => TransformSequence<TDrawable>.Create(target).RotateTo(newRotation, duration, easing);

    /// <summary>
    /// Animates <see cref="Drawable.Rotation"/> to <paramref name="newRotation"/> degrees over <paramref name="duration"/> milliseconds using <paramref name="easing"/>.
    /// </summary>
    public static TransformSequence<TDrawable> RotateTo<TDrawable, TEasing>(
        this TDrawable target,
        float newRotation,
        double duration,
        TEasing easing)
        where TDrawable : Drawable
        where TEasing : struct, IEasingFunction
        => TransformSequence<TDrawable>.Create(target).RotateTo(newRotation, duration, easing);

    /// <summary>
    /// Appends a <see cref="Drawable.Rotation"/> animation to <paramref name="newRotation"/> degrees onto <paramref name="sequence"/>.
    /// </summary>
    public static TransformSequence<TDrawable> RotateTo<TDrawable>(
        this TransformSequence<TDrawable> sequence,
        float newRotation,
        double duration = 0.0,
        Easing easing = Easing.None)
        where TDrawable : Drawable
        => sequence.AppendBuiltIn(BuiltInTransformTarget.Rotation, TransformValue.FromSingle(newRotation), duration, easing);

    /// <summary>
    /// Appends a <see cref="Drawable.Rotation"/> animation to <paramref name="newRotation"/> degrees onto <paramref name="sequence"/> using <paramref name="easing"/>.
    /// </summary>
    public static TransformSequence<TDrawable> RotateTo<TDrawable, TEasing>(
        this TransformSequence<TDrawable> sequence,
        float newRotation,
        double duration,
        TEasing easing)
        where TDrawable : Drawable
        where TEasing : struct, IEasingFunction
        => sequence.AppendCustom(
            new BuiltInCustomEasingHandler<TEasing>(BuiltInTransformTarget.Rotation, default, TransformValue.FromSingle(newRotation), easing),
            duration);

    /// <summary>
    /// Continuously rotates <see cref="Drawable.Rotation"/> by 360 degrees per <paramref name="duration"/> milliseconds
    /// in the specified <paramref name="direction"/>, starting from the effective rotation at the sequence start time.
    /// </summary>
    public static TransformSequence<TDrawable> Spin<TDrawable>(
        this TDrawable target,
        double duration,
        RotationDirection direction = RotationDirection.Clockwise)
        where TDrawable : Drawable
        => TransformSequence<TDrawable>.Create(target).Spin(duration, direction);

    /// <summary>
    /// Continuously rotates <see cref="Drawable.Rotation"/> by 360 degrees per <paramref name="duration"/> milliseconds
    /// in the specified <paramref name="direction"/>, starting from <paramref name="startRotation"/> degrees.
    /// </summary>
    public static TransformSequence<TDrawable> Spin<TDrawable>(
        this TDrawable target,
        double duration,
        RotationDirection direction,
        float startRotation)
        where TDrawable : Drawable
        => TransformSequence<TDrawable>.Create(target).Spin(duration, direction, startRotation);

    /// <summary>
    /// Appends an infinite 360-degree spin animation onto <paramref name="sequence"/> starting from the effective rotation
    /// at <see cref="TransformSequence{TDrawable}.CurrentStartTime"/>.
    /// </summary>
    public static TransformSequence<TDrawable> Spin<TDrawable>(
        this TransformSequence<TDrawable> sequence,
        double duration,
        RotationDirection direction = RotationDirection.Clockwise)
        where TDrawable : Drawable
    {
        var startRotation = sequence.State.ResolveEffectiveRotationAt(sequence.Target, sequence.CurrentStartTime);
        return sequence.Spin(duration, direction, startRotation);
    }

    /// <summary>
    /// Appends an infinite 360-degree spin animation onto <paramref name="sequence"/> starting from <paramref name="startRotation"/> degrees.
    /// </summary>
    public static TransformSequence<TDrawable> Spin<TDrawable>(
        this TransformSequence<TDrawable> sequence,
        double duration,
        RotationDirection direction,
        float startRotation)
        where TDrawable : Drawable
    {
        if (!double.IsFinite(duration) || duration <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(duration), duration, "Spin duration must be finite and greater than zero.");

        var endRotation = startRotation + (direction == RotationDirection.Clockwise ? 360f : -360f);
        var startValue = TransformValue.FromSingle(startRotation);
        var endValue = TransformValue.FromSingle(endRotation);

        return sequence
            .AppendBuiltIn(
                BuiltInTransformTarget.Rotation,
                startValue,
                duration: 0.0,
                hasExplicitStartValue: true,
                startValue: startValue,
                loopDelay: duration,
                remainingLoops: -1)
            .AppendBuiltIn(
                BuiltInTransformTarget.Rotation,
                endValue,
                duration: duration,
                hasExplicitStartValue: true,
                startValue: startValue,
                loopDelay: duration,
                remainingLoops: -1);
    }

    /// <summary>
    /// Animates <see cref="Drawable.Shear"/> to <paramref name="newShear"/> over <paramref name="duration"/> milliseconds.
    /// </summary>
    public static TransformSequence<TDrawable> ShearTo<TDrawable>(
        this TDrawable target,
        Vector2 newShear,
        double duration = 0.0,
        Easing easing = Easing.None)
        where TDrawable : Drawable
        => TransformSequence<TDrawable>.Create(target).ShearTo(newShear, duration, easing);

    /// <summary>
    /// Animates <see cref="Drawable.Shear"/> to <paramref name="newShear"/> over <paramref name="duration"/> milliseconds using <paramref name="easing"/>.
    /// </summary>
    public static TransformSequence<TDrawable> ShearTo<TDrawable, TEasing>(
        this TDrawable target,
        Vector2 newShear,
        double duration,
        TEasing easing)
        where TDrawable : Drawable
        where TEasing : struct, IEasingFunction
        => TransformSequence<TDrawable>.Create(target).ShearTo(newShear, duration, easing);

    /// <summary>
    /// Appends a <see cref="Drawable.Shear"/> animation to <paramref name="newShear"/> onto <paramref name="sequence"/>.
    /// </summary>
    public static TransformSequence<TDrawable> ShearTo<TDrawable>(
        this TransformSequence<TDrawable> sequence,
        Vector2 newShear,
        double duration = 0.0,
        Easing easing = Easing.None)
        where TDrawable : Drawable
        => sequence.AppendBuiltIn(BuiltInTransformTarget.Shear, TransformValue.FromVector2(newShear), duration, easing);

    /// <summary>
    /// Appends a <see cref="Drawable.Shear"/> animation to <paramref name="newShear"/> onto <paramref name="sequence"/> using <paramref name="easing"/>.
    /// </summary>
    public static TransformSequence<TDrawable> ShearTo<TDrawable, TEasing>(
        this TransformSequence<TDrawable> sequence,
        Vector2 newShear,
        double duration,
        TEasing easing)
        where TDrawable : Drawable
        where TEasing : struct, IEasingFunction
        => sequence.AppendCustom(
            new BuiltInCustomEasingHandler<TEasing>(BuiltInTransformTarget.Shear, default, TransformValue.FromVector2(newShear), easing),
            duration);

    /// <summary>
    /// Animates <see cref="Drawable.Margin"/> to <paramref name="newMargin"/> over <paramref name="duration"/> milliseconds.
    /// </summary>
    public static TransformSequence<TDrawable> TransformMarginTo<TDrawable>(
        this TDrawable target,
        MarginPadding newMargin,
        double duration = 0.0,
        Easing easing = Easing.None)
        where TDrawable : Drawable
        => TransformSequence<TDrawable>.Create(target).TransformMarginTo(newMargin, duration, easing);

    /// <summary>
    /// Animates <see cref="Drawable.Margin"/> to <paramref name="newMargin"/> over <paramref name="duration"/> milliseconds using <paramref name="easing"/>.
    /// </summary>
    public static TransformSequence<TDrawable> TransformMarginTo<TDrawable, TEasing>(
        this TDrawable target,
        MarginPadding newMargin,
        double duration,
        TEasing easing)
        where TDrawable : Drawable
        where TEasing : struct, IEasingFunction
        => TransformSequence<TDrawable>.Create(target).TransformMarginTo(newMargin, duration, easing);

    /// <summary>
    /// Appends a <see cref="Drawable.Margin"/> animation to <paramref name="newMargin"/> onto <paramref name="sequence"/>.
    /// </summary>
    public static TransformSequence<TDrawable> TransformMarginTo<TDrawable>(
        this TransformSequence<TDrawable> sequence,
        MarginPadding newMargin,
        double duration = 0.0,
        Easing easing = Easing.None)
        where TDrawable : Drawable
        => sequence.AppendBuiltIn(BuiltInTransformTarget.Margin, TransformValue.FromMarginPadding(newMargin), duration, easing);

    /// <summary>
    /// Appends a <see cref="Drawable.Margin"/> animation to <paramref name="newMargin"/> onto <paramref name="sequence"/> using <paramref name="easing"/>.
    /// </summary>
    public static TransformSequence<TDrawable> TransformMarginTo<TDrawable, TEasing>(
        this TransformSequence<TDrawable> sequence,
        MarginPadding newMargin,
        double duration,
        TEasing easing)
        where TDrawable : Drawable
        where TEasing : struct, IEasingFunction
        => sequence.AppendCustom(
            new BuiltInCustomEasingHandler<TEasing>(BuiltInTransformTarget.Margin, default, TransformValue.FromMarginPadding(newMargin), easing),
            duration);

    /// <summary>
    /// Animates <see cref="CompositeDrawable.Padding"/> to <paramref name="newPadding"/> over <paramref name="duration"/> milliseconds.
    /// </summary>
    public static TransformSequence<TComposite> TransformPaddingTo<TComposite>(
        this TComposite target,
        MarginPadding newPadding,
        double duration = 0.0,
        Easing easing = Easing.None)
        where TComposite : CompositeDrawable
        => TransformSequence<TComposite>.Create(target).TransformPaddingTo(newPadding, duration, easing);

    /// <summary>
    /// Animates <see cref="CompositeDrawable.Padding"/> to <paramref name="newPadding"/> over <paramref name="duration"/> milliseconds using <paramref name="easing"/>.
    /// </summary>
    public static TransformSequence<TComposite> TransformPaddingTo<TComposite, TEasing>(
        this TComposite target,
        MarginPadding newPadding,
        double duration,
        TEasing easing)
        where TComposite : CompositeDrawable
        where TEasing : struct, IEasingFunction
        => TransformSequence<TComposite>.Create(target).TransformPaddingTo(newPadding, duration, easing);

    /// <summary>
    /// Appends a <see cref="CompositeDrawable.Padding"/> animation to <paramref name="newPadding"/> onto <paramref name="sequence"/>.
    /// </summary>
    public static TransformSequence<TComposite> TransformPaddingTo<TComposite>(
        this TransformSequence<TComposite> sequence,
        MarginPadding newPadding,
        double duration = 0.0,
        Easing easing = Easing.None)
        where TComposite : CompositeDrawable
        => sequence.AppendBuiltIn(BuiltInTransformTarget.Padding, TransformValue.FromMarginPadding(newPadding), duration, easing);

    /// <summary>
    /// Appends a <see cref="CompositeDrawable.Padding"/> animation to <paramref name="newPadding"/> onto <paramref name="sequence"/> using <paramref name="easing"/>.
    /// </summary>
    public static TransformSequence<TComposite> TransformPaddingTo<TComposite, TEasing>(
        this TransformSequence<TComposite> sequence,
        MarginPadding newPadding,
        double duration,
        TEasing easing)
        where TComposite : CompositeDrawable
        where TEasing : struct, IEasingFunction
        => sequence.AppendCustom(
            new BuiltInCustomEasingHandler<TEasing>(BuiltInTransformTarget.Padding, default, TransformValue.FromMarginPadding(newPadding), easing),
            duration);

    #endregion
}
