using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace kajarlabs.Framework.Scene.Transforms;

/// <summary>
/// Represents a fluent, value-type transform sequence targeting <typeparamref name="TDrawable"/>.
/// Chaining transforms allocates zero heap objects; sequence callback tracking is attached lazily only when
/// <see cref="OnComplete"/>, <see cref="OnAbort"/>, or <see cref="Finally"/> is registered.
/// </summary>
/// <typeparam name="TDrawable">The target <see cref="Drawable"/> subtype.</typeparam>
[StructLayout(LayoutKind.Sequential)]
public readonly struct TransformSequence<TDrawable>
    where TDrawable : Drawable
{
    private readonly DrawableTransformState state;

    internal TransformSequence(
        TDrawable target,
        DrawableTransformState state,
        int sequenceId,
        double originTime,
        double currentStartTime,
        double endTime,
        bool isLooped = false)
    {
        Target = target;
        this.state = state;
        SequenceId = sequenceId;
        OriginTime = originTime;
        CurrentStartTime = currentStartTime;
        EndTime = endTime;
        IsLooped = isLooped;
    }

    /// <summary>
    /// Gets the <typeparamref name="TDrawable"/> targeted by this sequence.
    /// </summary>
    public TDrawable Target { get; }

    /// <summary>
    /// Gets the clock time in milliseconds at which this sequence originated.
    /// </summary>
    public double OriginTime { get; }

    /// <summary>
    /// Gets the start time in milliseconds applied to the next transform added to this sequence.
    /// </summary>
    public double CurrentStartTime { get; }

    /// <summary>
    /// Gets the end time in milliseconds of this sequence, or <see cref="double.PositiveInfinity"/> for an infinite loop.
    /// </summary>
    public double EndTime { get; }

    internal int SequenceId { get; }

    internal bool IsLooped { get; }

    internal DrawableTransformState State => ensureValid();

    internal static TransformSequence<TDrawable> Create(TDrawable target)
    {
        ArgumentNullException.ThrowIfNull(target);
        var state = DrawableTransformState.GetOrAttach(target);
        var startTime = state.GetCurrentStartTime(target);
        var sequenceId = state.AllocateSequenceId();
        return new TransformSequence<TDrawable>(target, state, sequenceId, startTime, startTime, startTime);
    }

    /// <summary>
    /// Advances the sequence start time to the current <see cref="EndTime"/> plus an optional <paramref name="delay"/>,
    /// causing subsequent transforms to run sequentially after all previously scheduled transforms in this sequence.
    /// </summary>
    /// <param name="delay">Additional delay in milliseconds after <see cref="EndTime"/>.</param>
    /// <returns>An updated <see cref="TransformSequence{TDrawable}"/> positioned at the new start time.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="delay"/> is negative or non-finite.</exception>
    /// <exception cref="InvalidOperationException">Thrown when chained after an infinitely looping sequence or on an uninitialized sequence.</exception>
    public TransformSequence<TDrawable> Then(double delay = 0.0)
    {
        var validState = ensureValid();

        if (!double.IsFinite(delay) || delay < 0.0)
            throw new ArgumentOutOfRangeException(nameof(delay), delay, "Delay must be a finite non-negative number.");

        if (!double.IsFinite(EndTime))
            throw new InvalidOperationException("Cannot chain Then() after an infinite transform sequence.");

        var nextStartTime = EndTime + delay;
        if (!double.IsFinite(nextStartTime))
            throw new ArgumentOutOfRangeException(nameof(delay), delay, "Resulting sequence start time must be finite.");

        return new TransformSequence<TDrawable>(Target, validState, SequenceId, OriginTime, nextStartTime, nextStartTime, IsLooped);
    }

    /// <summary>
    /// Advances <see cref="CurrentStartTime"/> by <paramref name="duration"/> milliseconds without moving prior transforms,
    /// extending <see cref="EndTime"/> if the delayed time exceeds it.
    /// </summary>
    /// <param name="duration">Delay duration in milliseconds.</param>
    /// <returns>An updated <see cref="TransformSequence{TDrawable}"/> with the advanced start time.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="duration"/> is negative or non-finite.</exception>
    /// <exception cref="InvalidOperationException">Thrown when chained after an infinitely looping sequence or on an uninitialized sequence.</exception>
    public TransformSequence<TDrawable> Delay(double duration)
    {
        var validState = ensureValid();

        if (!double.IsFinite(duration) || duration < 0.0)
            throw new ArgumentOutOfRangeException(nameof(duration), duration, "Delay duration must be a finite non-negative number.");

        if (!double.IsFinite(CurrentStartTime) || !double.IsFinite(EndTime))
            throw new InvalidOperationException("Cannot delay an infinite transform sequence.");

        var nextStartTime = CurrentStartTime + duration;
        if (!double.IsFinite(nextStartTime))
            throw new ArgumentOutOfRangeException(nameof(duration), duration, "Resulting sequence start time must be finite.");

        var nextEndTime = nextStartTime > EndTime ? nextStartTime : EndTime;
        return new TransformSequence<TDrawable>(Target, validState, SequenceId, OriginTime, nextStartTime, nextEndTime, IsLooped);
    }

    /// <summary>
    /// Repeats all transforms and delays in this sequence <paramref name="numIterations"/> times (or indefinitely when
    /// <paramref name="numIterations"/> is <c>-1</c>), waiting <paramref name="pause"/> milliseconds between iterations.
    /// </summary>
    /// <param name="pause">Pause duration in milliseconds between consecutive iterations.</param>
    /// <param name="numIterations">Total number of iterations to execute, or <c>-1</c> to loop indefinitely.</param>
    /// <returns>An updated <see cref="TransformSequence{TDrawable}"/> reflecting the looped end time.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="pause"/> is negative/non-finite, <paramref name="numIterations"/> is <c>0</c> or less than <c>-1</c>,
    /// or total loop period is non-positive when repeating.
    /// </exception>
    public TransformSequence<TDrawable> Loop(double pause = 0.0, int numIterations = -1)
    {
        var validState = ensureValid();
        var loopedEndTime = validState.ApplyLoopToSequence(Target, SequenceId, OriginTime, EndTime, pause, numIterations);
        return new TransformSequence<TDrawable>(Target, validState, SequenceId, OriginTime, CurrentStartTime, loopedEndTime, isLooped: IsLooped || numIterations != 1);
    }

    /// <summary>
    /// Registers a callback invoked once when all transforms and delays in this sequence complete without aborting.
    /// For finite loops, the callback runs after the final iteration completes.
    /// </summary>
    /// <param name="onComplete">Callback receiving <see cref="Target"/>.</param>
    /// <returns>This <see cref="TransformSequence{TDrawable}"/> for further chaining.</returns>
    public TransformSequence<TDrawable> OnComplete(Action<TDrawable> onComplete)
    {
        ArgumentNullException.ThrowIfNull(onComplete);
        var tracker = prepareTracker();
        tracker.AddOnComplete(onComplete);
        return this;
    }

    /// <summary>
    /// Registers a callback invoked once if any transform in this sequence is aborted before completion.
    /// </summary>
    /// <param name="onAbort">Callback receiving <see cref="Target"/>.</param>
    /// <returns>This <see cref="TransformSequence{TDrawable}"/> for further chaining.</returns>
    public TransformSequence<TDrawable> OnAbort(Action<TDrawable> onAbort)
    {
        ArgumentNullException.ThrowIfNull(onAbort);
        var tracker = prepareTracker();
        tracker.AddOnAbort(onAbort);
        return this;
    }

    /// <summary>
    /// Registers a callback invoked once when this sequence finishes, whether by normal completion or abort.
    /// </summary>
    /// <param name="onFinally">Callback receiving <see cref="Target"/>.</param>
    /// <returns>This <see cref="TransformSequence{TDrawable}"/> for further chaining.</returns>
    public TransformSequence<TDrawable> Finally(Action<TDrawable> onFinally)
    {
        ArgumentNullException.ThrowIfNull(onFinally);
        var tracker = prepareTracker();
        tracker.AddFinally(onFinally);
        return this;
    }

    /// <summary>
    /// Sets <see cref="Drawable.LifetimeEnd"/> to <see cref="EndTime"/> (and optionally sets
    /// <see cref="Drawable.LifetimeStart"/> to <see cref="OriginTime"/>) so the target expires cleanly once the sequence finishes.
    /// </summary>
    /// <param name="calculateLifetimeStart">
    /// When <see langword="true"/>, also sets <see cref="Drawable.LifetimeStart"/> to <see cref="OriginTime"/>.
    /// </param>
    /// <returns>This <see cref="TransformSequence{TDrawable}"/> for further chaining.</returns>
    public TransformSequence<TDrawable> Expire(bool calculateLifetimeStart = false)
    {
        _ = ensureValid();

        if (calculateLifetimeStart)
            Target.LifetimeStart = OriginTime;

        Target.LifetimeEnd = EndTime;
        return this;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal TransformSequence<TDrawable> AppendBuiltIn(
        BuiltInTransformTarget property,
        TransformValue endValue,
        double duration,
        Easing easing = Easing.None,
        bool hasExplicitStartValue = false,
        TransformValue startValue = default,
        double loopDelay = 0.0,
        int remainingLoops = 0)
    {
        var validState = ensureValid();
        validState.AddBuiltInTransform(
            Target,
            property,
            endValue,
            CurrentStartTime,
            duration,
            easing,
            hasExplicitStartValue,
            startValue,
            observer: null,
            sequenceId: SequenceId,
            loopDelay: loopDelay,
            remainingLoops: remainingLoops);

        var transformEnd = remainingLoops < 0 ? double.PositiveInfinity : CurrentStartTime + duration;
        var nextEnd = transformEnd > EndTime ? transformEnd : EndTime;
        return new TransformSequence<TDrawable>(Target, validState, SequenceId, OriginTime, CurrentStartTime, nextEnd, IsLooped || remainingLoops != 0);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal TransformSequence<TDrawable> AppendCustom(
        CustomTransformHandler handler,
        double duration,
        bool hasExplicitStartValue = false,
        double loopDelay = 0.0,
        int remainingLoops = 0)
    {
        var validState = ensureValid();
        validState.AddCustomTransform(
            Target,
            handler,
            CurrentStartTime,
            duration,
            hasExplicitStartValue,
            observer: null,
            sequenceId: SequenceId,
            loopDelay: loopDelay,
            remainingLoops: remainingLoops);

        var transformEnd = remainingLoops < 0 ? double.PositiveInfinity : CurrentStartTime + duration;
        var nextEnd = transformEnd > EndTime ? transformEnd : EndTime;
        return new TransformSequence<TDrawable>(Target, validState, SequenceId, OriginTime, CurrentStartTime, nextEnd, IsLooped || remainingLoops != 0);
    }

    private SequenceTracker<TDrawable> prepareTracker()
    {
        var validState = ensureValid();

        if (!IsLooped)
            validState.EnsureSequenceDelayBarrier(Target, SequenceId, EndTime);

        return validState.GetOrAddSequenceTracker<TDrawable>(Target, SequenceId);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private DrawableTransformState ensureValid()
    {
        if (Target is null || state is null)
            throw new InvalidOperationException("TransformSequence is uninitialized.");

        return state;
    }
}
