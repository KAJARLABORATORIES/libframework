using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.InteropServices;
using kajarlabs.Framework.Numerics;

namespace kajarlabs.Framework.Scene.Transforms;

internal sealed class DrawableTransformState : IDrawableExtension
{
    private static readonly Func<Drawable, DrawableTransformState> state_factory = static _ => new DrawableTransformState();

    private readonly List<TransformEntry> entries = [];
    private List<PendingNotification>? pendingNotifications;
    private Dictionary<int, ISequenceTracker>? sequenceTrackers;
    private long nextCreationOrder;
    private double lastUpdateTime = double.NaN;
    private double activeSequenceStartTime;
    private int nextSequenceId;
    private int nextScopeToken;
    private int activeScopeToken;
    private bool hasActiveSequenceStartTime;
    private bool isDisposed;
    private bool isDrainingNotifications;

    private DrawableTransformState()
    {
    }

    public int Count => entries.Count;

    public bool RemoveCompletedTransforms
    {
        get => field;
        set
        {
            if (field == value)
                return;

            field = value;

            if (value && entries.Count > 0)
                compactCompletedEntries();
        }
    } = true;

    public static DrawableTransformState GetOrAttach(Drawable drawable)
    {
        ArgumentNullException.ThrowIfNull(drawable);
        return drawable.GetOrAddExtension(state_factory);
    }

    public static bool TryGet(Drawable drawable, [NotNullWhen(true)] out DrawableTransformState? state)
    {
        ArgumentNullException.ThrowIfNull(drawable);
        return drawable.TryGetExtension(out state);
    }

    public int AllocateSequenceId() => ++nextSequenceId;

    public double GetCurrentStartTime(Drawable target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return hasActiveSequenceStartTime ? activeSequenceStartTime : target.Time;
    }

    public TransformSequenceScope EnterDelayedSequence(Drawable target, double delay)
    {
        ArgumentNullException.ThrowIfNull(target);
        ObjectDisposedException.ThrowIf(isDisposed || target.IsDisposed, target);

        if (!double.IsFinite(delay))
            throw new ArgumentOutOfRangeException(nameof(delay), delay, "Delay must be finite.");

        var baseTime = GetCurrentStartTime(target);
        var newStartTime = baseTime + delay;

        if (!double.IsFinite(newStartTime))
            throw new ArgumentOutOfRangeException(nameof(delay), delay, "Delayed sequence start time must be finite.");

        return pushSequenceScope(target, newStartTime);
    }

    public TransformSequenceScope EnterAbsoluteSequence(Drawable target, double newTransformStartTime)
    {
        ArgumentNullException.ThrowIfNull(target);
        ObjectDisposedException.ThrowIf(isDisposed || target.IsDisposed, target);

        if (!double.IsFinite(newTransformStartTime))
            throw new ArgumentOutOfRangeException(nameof(newTransformStartTime), newTransformStartTime, "Transform start time must be finite.");

        return pushSequenceScope(target, newTransformStartTime);
    }

    public void ExitSequenceScope(
        Drawable target,
        int scopeToken,
        int previousScopeToken,
        bool hadPreviousStartTime,
        double previousStartTime)
    {
        if (isDisposed || target.IsDisposed || activeScopeToken != scopeToken)
            return;

        hasActiveSequenceStartTime = hadPreviousStartTime;
        activeSequenceStartTime = previousStartTime;
        activeScopeToken = previousScopeToken;
    }

    public SequenceTracker<TDrawable> GetOrAddSequenceTracker<TDrawable>(Drawable target, int sequenceId)
        where TDrawable : Drawable
    {
        ArgumentNullException.ThrowIfNull(target);
        ObjectDisposedException.ThrowIf(isDisposed || target.IsDisposed, target);

        sequenceTrackers ??= [];

        if (sequenceTrackers.TryGetValue(sequenceId, out var existing))
            return (SequenceTracker<TDrawable>)existing;

        var tracker = new SequenceTracker<TDrawable>(sequenceId, this);
        sequenceTrackers.Add(sequenceId, tracker);

        var span = CollectionsMarshal.AsSpan(entries);

        for (var i = 0; i < span.Length; i++)
        {
            ref var entry = ref span[i];

            if (entry.SequenceId == sequenceId && entry.Observer is null)
                entry.Observer = tracker;
        }

        return tracker;
    }

    public void RemoveSequenceTracker(int sequenceId)
        => sequenceTrackers?.Remove(sequenceId);

    public bool HasActiveEntriesForSequence(int sequenceId)
    {
        var span = CollectionsMarshal.AsSpan(entries);

        for (var i = 0; i < span.Length; i++)
        {
            if (span[i].SequenceId == sequenceId && !span[i].IsCompleted)
                return true;
        }

        return false;
    }

    public void CancelSequenceEntriesOnAbort(int sequenceId)
    {
        if (sequenceId == 0)
            return;

        var span = CollectionsMarshal.AsSpan(entries);

        for (var i = 0; i < span.Length; i++)
        {
            ref var entry = ref span[i];

            if (entry.SequenceId != sequenceId)
                continue;

            entry.RemainingLoops = 0;

            if (entry.BuiltInTarget == BuiltInTransformTarget.None && !entry.IsCustom)
                entry.Flags |= TransformEntryFlags.Completed | TransformEntryFlags.Aborted | TransformEntryFlags.ObserverNotified;
        }
    }

    public void EnsureSequenceDelayBarrier(Drawable target, int sequenceId, double endTime)
    {
        ArgumentNullException.ThrowIfNull(target);
        ObjectDisposedException.ThrowIf(isDisposed || target.IsDisposed, target);

        if (!double.IsFinite(endTime))
            return;

        var maxEndTime = double.NegativeInfinity;
        var span = CollectionsMarshal.AsSpan(entries);

        for (var i = 0; i < span.Length; i++)
        {
            ref readonly var entry = ref span[i];

            if (entry.SequenceId != sequenceId || entry.IsCompleted)
                continue;

            if (entry.EffectiveEndTime > maxEndTime)
                maxEndTime = entry.EffectiveEndTime;
        }

        if (endTime > maxEndTime)
            AddBuiltInTransform(target, BuiltInTransformTarget.None, default, endTime, 0.0, sequenceId: sequenceId);
    }

    public double ApplyLoopToSequence(
        Drawable target,
        int sequenceId,
        double originTime,
        double singleIterationEndTime,
        double pause,
        int numIterations)
    {
        ArgumentNullException.ThrowIfNull(target);
        ObjectDisposedException.ThrowIf(isDisposed || target.IsDisposed, target);

        if (!double.IsFinite(pause) || pause < 0.0)
            throw new ArgumentOutOfRangeException(nameof(pause), pause, "Loop pause must be a finite non-negative number.");

        if (numIterations != -1 && numIterations < 1)
            throw new ArgumentOutOfRangeException(nameof(numIterations), numIterations, "Number of loop iterations must be -1 (infinite) or at least 1.");

        var remainingLoops = numIterations == -1 ? -1 : numIterations - 1;
        var iterationDuration = singleIterationEndTime - originTime;
        var loopDelay = iterationDuration + pause;

        if (remainingLoops != 0 && (!double.IsFinite(loopDelay) || loopDelay <= 0.0))
            throw new ArgumentOutOfRangeException(nameof(pause), pause, "Total loop duration (sequence duration + pause) must be finite and greater than zero.");

        if (remainingLoops == 0)
            return singleIterationEndTime;

        EnsureSequenceDelayBarrier(target, sequenceId, singleIterationEndTime);

        var span = CollectionsMarshal.AsSpan(entries);

        for (var i = 0; i < span.Length; i++)
        {
            ref var entry = ref span[i];

            if (entry.SequenceId != sequenceId || entry.IsCompleted)
                continue;

            entry.LoopDelay = loopDelay;
            entry.RemainingLoops = remainingLoops;
        }

        if (remainingLoops < 0)
            return double.PositiveInfinity;

        var finalEndTime = singleIterationEndTime + (remainingLoops * loopDelay);
        if (!double.IsFinite(finalEndTime))
            throw new ArgumentOutOfRangeException(nameof(numIterations), numIterations, "Looped sequence end time exceeds finite range.");

        return finalEndTime;
    }

    public Color4 ResolveEffectiveColorForFlash(Drawable target, double atTime)
    {
        var result = target.Color;
        var span = CollectionsMarshal.AsSpan(entries);

        for (var i = 0; i < span.Length; i++)
        {
            ref readonly var entry = ref span[i];

            if (entry.IsCompleted || entry.IsAborted || entry.HasAbortTime || entry.StartTime > atTime)
                continue;

            if (!entry.IsCustom && entry.BuiltInTarget == BuiltInTransformTarget.Color)
                result = entry.EndValue.AsColor4();
        }

        return result;
    }

    public Vector2 ResolveEffectivePositionAt(Drawable target, double atTime)
    {
        var result = target.Position;
        var span = CollectionsMarshal.AsSpan(entries);

        for (var i = 0; i < span.Length; i++)
        {
            ref readonly var entry = ref span[i];

            if (entry.IsCompleted || entry.IsAborted || entry.IsCustom || entry.StartTime > atTime)
                continue;

            if (entry.BuiltInTarget is not (BuiltInTransformTarget.Position or BuiltInTransformTarget.X or BuiltInTransformTarget.Y))
                continue;

            var effectiveEnd = entry.EffectiveEndTime;

            if (effectiveEnd <= atTime)
            {
                switch (entry.BuiltInTarget)
                {
                    case BuiltInTransformTarget.Position:
                        result = entry.EndValue.AsVector2();
                        break;

                    case BuiltInTransformTarget.X:
                        result.X = entry.EndValue.AsSingle();
                        break;

                    case BuiltInTransformTarget.Y:
                        result.Y = entry.EndValue.AsSingle();
                        break;
                }
            }
            else if (entry.StartTime < atTime)
            {
                switch (entry.BuiltInTarget)
                {
                    case BuiltInTransformTarget.Position:
                    {
                        var start = entry.HasStartValue ? entry.StartValue.AsVector2() : result;
                        result = Interpolation.ValueAt(atTime, start, entry.EndValue.AsVector2(), entry.StartTime, entry.EndTime, entry.Easing);
                        break;
                    }

                    case BuiltInTransformTarget.X:
                    {
                        var start = entry.HasStartValue ? entry.StartValue.AsSingle() : result.X;
                        result.X = Interpolation.ValueAt(atTime, start, entry.EndValue.AsSingle(), entry.StartTime, entry.EndTime, entry.Easing);
                        break;
                    }

                    case BuiltInTransformTarget.Y:
                    {
                        var start = entry.HasStartValue ? entry.StartValue.AsSingle() : result.Y;
                        result.Y = Interpolation.ValueAt(atTime, start, entry.EndValue.AsSingle(), entry.StartTime, entry.EndTime, entry.Easing);
                        break;
                    }
                }
            }
        }

        return result;
    }

    public float ResolveEffectiveRotationAt(Drawable target, double atTime)
    {
        var result = target.Rotation;
        var span = CollectionsMarshal.AsSpan(entries);

        for (var i = 0; i < span.Length; i++)
        {
            ref readonly var entry = ref span[i];

            if (entry.IsCompleted || entry.IsAborted || entry.IsCustom || entry.StartTime > atTime)
                continue;

            if (entry.BuiltInTarget != BuiltInTransformTarget.Rotation)
                continue;

            if (entry.EffectiveEndTime <= atTime)
            {
                result = entry.EndValue.AsSingle();
            }
            else if (entry.StartTime < atTime)
            {
                var start = entry.HasStartValue ? entry.StartValue.AsSingle() : result;
                result = Interpolation.ValueAt(atTime, start, entry.EndValue.AsSingle(), entry.StartTime, entry.EndTime, entry.Easing);
            }
        }

        return result;
    }

    public double GetLatestTransformEndTime(Drawable target)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (entries.Count == 0)
            return target.Time;

        var latest = double.NegativeInfinity;
        var span = CollectionsMarshal.AsSpan(entries);

        for (var i = 0; i < span.Length; i++)
        {
            var end = span[i].EffectiveEndTime;
            if (end > latest)
                latest = end;
        }

        return latest;
    }

    public void AddBuiltInTransform(
        Drawable target,
        BuiltInTransformTarget property,
        TransformValue endValue,
        double startTime,
        double duration,
        Easing easing = Easing.None,
        bool hasExplicitStartValue = false,
        TransformValue startValue = default,
        ITransformObserver? observer = null,
        int sequenceId = 0,
        double loopDelay = 0.0,
        int remainingLoops = 0)
    {
        validateScheduleParameters(target, startTime, duration, loopDelay, remainingLoops);
        BuiltInTransformHelper.ValidateTargetForDrawable(target, property);

        if ((uint)easing > (uint)Easing.OutPow10)
            throw new ArgumentOutOfRangeException(nameof(easing), easing, "Unsupported easing curve.");

        if (observer is null && sequenceId != 0 && sequenceTrackers is not null && sequenceTrackers.TryGetValue(sequenceId, out var tracker))
            observer = tracker;

        var flags = hasExplicitStartValue
            ? TransformEntryFlags.HasStartValue | TransformEntryFlags.ExplicitStartValue
            : TransformEntryFlags.None;

        var entry = new TransformEntry
        {
            StartTime = startTime,
            EndTime = startTime + duration,
            AbortTime = double.PositiveInfinity,
            LoopDelay = loopDelay,
            CreationOrder = ++nextCreationOrder,
            RemainingLoops = remainingLoops,
            SequenceId = sequenceId,
            StartValue = startValue,
            EndValue = endValue,
            CustomHandler = null,
            Observer = observer,
            BuiltInTarget = property,
            Easing = easing,
            Flags = flags,
        };

        addEntryCore(target, entry);
    }

    public void AddCustomTransform(
        Drawable target,
        CustomTransformHandler handler,
        double startTime,
        double duration,
        bool hasExplicitStartValue = false,
        ITransformObserver? observer = null,
        int sequenceId = 0,
        double loopDelay = 0.0,
        int remainingLoops = 0)
    {
        ArgumentNullException.ThrowIfNull(handler);
        validateScheduleParameters(target, startTime, duration, loopDelay, remainingLoops);

        if (observer is null && sequenceId != 0 && sequenceTrackers is not null && sequenceTrackers.TryGetValue(sequenceId, out var tracker))
            observer = tracker;

        var flags = hasExplicitStartValue
            ? TransformEntryFlags.HasStartValue | TransformEntryFlags.ExplicitStartValue
            : TransformEntryFlags.None;

        var entry = new TransformEntry
        {
            StartTime = startTime,
            EndTime = startTime + duration,
            AbortTime = double.PositiveInfinity,
            LoopDelay = loopDelay,
            CreationOrder = ++nextCreationOrder,
            RemainingLoops = remainingLoops,
            SequenceId = sequenceId,
            StartValue = default,
            EndValue = default,
            CustomHandler = handler,
            Observer = observer,
            BuiltInTarget = BuiltInTransformTarget.None,
            Easing = Easing.None,
            Flags = flags,
        };

        addEntryCore(target, entry);
    }

    public void ClearTransforms(Drawable target)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (isDisposed || entries.Count == 0)
            return;

        var span = CollectionsMarshal.AsSpan(entries);

        for (var i = 0; i < span.Length; i++)
        {
            ref var entry = ref span[i];

            if (!entry.IsCompleted && entry.Observer is not null && !entry.IsObserverNotified)
            {
                entry.Flags |= TransformEntryFlags.ObserverNotified;
                enqueueNotification(entry.Observer, isAbort: true);
            }
        }

        entries.Clear();
        drainNotifications(target);
        sequenceTrackers?.Clear();
    }

    public void ClearTransformsAfter(Drawable target, double time)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (double.IsNaN(time))
            throw new ArgumentOutOfRangeException(nameof(time), time, "Time must not be NaN.");

        if (isDisposed || entries.Count == 0)
            return;

        var currentTime = target.Time;
        var write = 0;
        var span = CollectionsMarshal.AsSpan(entries);

        for (var i = 0; i < span.Length; i++)
        {
            ref var entry = ref span[i];

            if (entry.StartTime >= time)
            {
                if (!entry.IsCompleted)
                {
                    if (entry.Observer is not null && !entry.IsObserverNotified)
                    {
                        entry.Flags |= TransformEntryFlags.ObserverNotified;
                        enqueueNotification(entry.Observer, isAbort: true);
                    }
                    else if (entry.SequenceId != 0)
                    {
                        cancelUnobservedSequenceInSpan(span, write, i + 1, entry.SequenceId);
                    }
                }

                continue;
            }

            // Cancel future loop iterations that would run after the cut-off time.
            entry.RemainingLoops = 0;

            if (!entry.IsCompleted && entry.EffectiveEndTime > time)
            {
                entry.AbortTime = Math.Min(entry.AbortTime, time);

                if (time <= currentTime)
                {
                    entry.Flags |= TransformEntryFlags.Completed | TransformEntryFlags.Aborted;

                    if (entry.Observer is not null && !entry.IsObserverNotified)
                    {
                        entry.Flags |= TransformEntryFlags.ObserverNotified;
                        enqueueNotification(entry.Observer, isAbort: true);
                    }
                    else if (entry.SequenceId != 0)
                    {
                        cancelUnobservedSequenceInSpan(span, write, i + 1, entry.SequenceId);
                    }

                    if (RemoveCompletedTransforms)
                        continue;
                }
            }

            if (write != i)
                span[write] = entry;

            write++;
        }

        if (write < entries.Count)
            entries.RemoveRange(write, entries.Count - write);

        drainNotifications(target);
    }

    public void FinishTransforms(Drawable target)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (isDisposed || entries.Count == 0)
            return;

        // Snapshot the current generation so transforms scheduled by completion callbacks
        // are not recursively finished in the same call.
        var maxCreationOrder = nextCreationOrder;
        var hasCompletedToRemove = false;
        var span = CollectionsMarshal.AsSpan(entries);

        for (var i = 0; i < span.Length; i++)
        {
            ref var entry = ref span[i];

            if (entry.CreationOrder > maxCreationOrder)
                continue;

            if (entry.IsCompleted)
            {
                if (RemoveCompletedTransforms)
                    hasCompletedToRemove = true;

                continue;
            }

            if (!entry.HasStartValue && (!RemoveCompletedTransforms || entry.HasAbortTime))
                entry.CaptureStartValue(target);

            if (entry.HasAbortTime)
            {
                entry.ApplyAt(target, entry.AbortTime);
                entry.RemainingLoops = 0;
                entry.Flags |= TransformEntryFlags.Applied | TransformEntryFlags.Completed | TransformEntryFlags.Aborted;

                if (entry.Observer is not null && !entry.IsObserverNotified)
                {
                    entry.Flags |= TransformEntryFlags.ObserverNotified;
                    enqueueNotification(entry.Observer, isAbort: true);
                }
            }
            else if (entry.RemainingLoops < 0)
            {
                // Infinite loops terminate deterministically at the end of the current iteration.
                entry.ApplyEndValue(target);
                entry.RemainingLoops = 0;
                entry.Flags |= TransformEntryFlags.Applied | TransformEntryFlags.Completed | TransformEntryFlags.Aborted;

                if (entry.Observer is not null && !entry.IsObserverNotified)
                {
                    entry.Flags |= TransformEntryFlags.ObserverNotified;
                    enqueueNotification(entry.Observer, isAbort: true);
                }
            }
            else
            {
                if (entry.RemainingLoops > 0)
                {
                    var shift = entry.RemainingLoops * entry.LoopDelay;
                    entry.StartTime += shift;
                    entry.EndTime += shift;
                    entry.RemainingLoops = 0;
                }

                entry.ApplyEndValue(target);
                entry.Flags |= TransformEntryFlags.Applied | TransformEntryFlags.Completed;

                if (entry.Observer is not null && !entry.IsObserverNotified)
                {
                    entry.Flags |= TransformEntryFlags.ObserverNotified;
                    enqueueNotification(entry.Observer, isAbort: false);
                }
            }

            if (RemoveCompletedTransforms)
                hasCompletedToRemove = true;
        }

        if (hasCompletedToRemove)
            compactCompletedEntries();

        drainNotifications(target);
    }

    public void OnUpdate(Drawable target)
    {
        if (isDisposed || entries.Count == 0)
            return;

        var currentTime = target.Time;

        if (!double.IsNaN(lastUpdateTime) && currentTime < lastUpdateTime)
            prepareRewind(target, currentTime);

        var hasCompletedToRemove = false;
        var i = 0;

        while (i < entries.Count)
        {
            var span = CollectionsMarshal.AsSpan(entries);
            ref var entry = ref span[i];

            if (entry.StartTime > currentTime)
                break;

            if (entry.IsCompleted)
            {
                if (RemoveCompletedTransforms)
                    hasCompletedToRemove = true;

                i++;
                continue;
            }

            var isZeroDuration = entry.EndTime == entry.StartTime;

            if (!entry.HasStartValue && (!isZeroDuration || !RemoveCompletedTransforms || entry.HasAbortTime))
                entry.CaptureStartValue(target);

            if (entry.HasAbortTime && currentTime >= entry.AbortTime)
            {
                entry.ApplyAt(target, entry.AbortTime);
                entry.RemainingLoops = 0;
                entry.Flags |= TransformEntryFlags.Applied | TransformEntryFlags.Completed | TransformEntryFlags.Aborted;

                if (entry.Observer is not null && !entry.IsObserverNotified)
                {
                    entry.Flags |= TransformEntryFlags.ObserverNotified;
                    enqueueNotification(entry.Observer, isAbort: true);
                }
                else if (entry.SequenceId != 0)
                {
                    CancelSequenceEntriesOnAbort(entry.SequenceId);
                }

                if (RemoveCompletedTransforms)
                    hasCompletedToRemove = true;

                i++;
                continue;
            }

            if (currentTime >= entry.EndTime)
            {
                entry.ApplyEndValue(target);
                entry.Flags |= TransformEntryFlags.Applied;

                if (entry.RemainingLoops != 0)
                {
                    advanceLoopEntry(ref entry, currentTime);
                    _ = repositionEntryRight(i);
                    continue;
                }

                entry.Flags |= TransformEntryFlags.Completed;

                if (entry.Observer is not null && !entry.IsObserverNotified)
                {
                    entry.Flags |= TransformEntryFlags.ObserverNotified;
                    enqueueNotification(entry.Observer, isAbort: false);
                }

                if (RemoveCompletedTransforms)
                    hasCompletedToRemove = true;

                i++;
                continue;
            }

            entry.ApplyAt(target, currentTime);
            entry.Flags |= TransformEntryFlags.Applied;
            i++;
        }

        if (hasCompletedToRemove)
            compactCompletedEntries();

        lastUpdateTime = currentTime;
        drainNotifications(target);
    }

    public void OnDispose(Drawable target)
    {
        isDisposed = true;
        hasActiveSequenceStartTime = false;
        activeScopeToken = 0;
        entries.Clear();
        pendingNotifications?.Clear();
        sequenceTrackers?.Clear();
    }

    private void addEntryCore(Drawable target, TransformEntry newEntry)
    {
        var currentTime = target.Time;
        var newTargetKey = newEntry.TargetKey;
        var newStartTime = newEntry.StartTime;
        var write = 0;
        var span = CollectionsMarshal.AsSpan(entries);

        for (var i = 0; i < span.Length; i++)
        {
            ref var existing = ref span[i];

            if (existing.ConflictsWith(in newTargetKey))
            {
                if (existing.EffectiveEndTime > newStartTime)
                {
                    if (existing.StartTime >= newStartTime)
                    {
                        if (!existing.IsCompleted)
                        {
                            if (existing.Observer is not null && !existing.IsObserverNotified)
                            {
                                existing.Flags |= TransformEntryFlags.ObserverNotified;
                                enqueueNotification(existing.Observer, isAbort: true);
                            }
                            else if (existing.SequenceId != 0)
                            {
                                cancelUnobservedSequenceInSpan(span, write, i + 1, existing.SequenceId);
                            }
                        }

                        continue;
                    }

                    existing.AbortTime = Math.Min(existing.AbortTime, newStartTime);
                    existing.RemainingLoops = 0;

                    if (newStartTime <= currentTime)
                    {
                        if (!existing.IsCompleted)
                        {
                            existing.Flags |= TransformEntryFlags.Completed | TransformEntryFlags.Aborted;

                            if (existing.Observer is not null && !existing.IsObserverNotified)
                            {
                                existing.Flags |= TransformEntryFlags.ObserverNotified;
                                enqueueNotification(existing.Observer, isAbort: true);
                            }
                            else if (existing.SequenceId != 0)
                            {
                                cancelUnobservedSequenceInSpan(span, write, i + 1, existing.SequenceId);
                            }
                        }

                        if (RemoveCompletedTransforms)
                            continue;
                    }
                }
                else if (existing.RemainingLoops != 0)
                {
                    // Prevent future loop iterations from overwriting the newly scheduled transform.
                    existing.RemainingLoops = 0;
                }
            }

            if (write != i)
                span[write] = existing;

            write++;
        }

        if (write < entries.Count)
            entries.RemoveRange(write, entries.Count - write);

        insertOrdered(newEntry);
        drainNotifications(target);
    }

    private void insertOrdered(in TransformEntry entry)
    {
        var count = entries.Count;

        if (count == 0)
        {
            entries.Add(entry);
            return;
        }

        var span = CollectionsMarshal.AsSpan(entries);

        if (compareOrder(in span[^1], in entry) <= 0)
        {
            entries.Add(entry);
            return;
        }

        var insertIndex = count - 1;

        while (insertIndex > 0 && compareOrder(in span[insertIndex - 1], in entry) > 0)
            insertIndex--;

        entries.Insert(insertIndex, entry);
    }

    private bool repositionEntryRight(int index)
    {
        var span = CollectionsMarshal.AsSpan(entries);
        var current = span[index];
        var nextIndex = index + 1;

        if (nextIndex >= span.Length || compareOrder(in current, in span[nextIndex]) <= 0)
            return false;

        while (nextIndex < span.Length && compareOrder(in current, in span[nextIndex]) > 0)
        {
            span[nextIndex - 1] = span[nextIndex];
            nextIndex++;
        }

        span[nextIndex - 1] = current;
        return true;
    }

    private void prepareRewind(Drawable target, double currentTime)
    {
        var span = CollectionsMarshal.AsSpan(entries);

        for (var i = span.Length - 1; i >= 0; i--)
        {
            ref var entry = ref span[i];

            if (entry.StartTime > currentTime)
            {
                if (entry.IsApplied && entry.HasStartValue)
                    entry.ApplyStartValue(target);

                entry.Flags &= ~(TransformEntryFlags.Applied | TransformEntryFlags.Completed | TransformEntryFlags.Aborted);
            }
            else if (entry.EffectiveEndTime > currentTime)
            {
                entry.Flags &= ~(TransformEntryFlags.Completed | TransformEntryFlags.Aborted);
            }
        }
    }

    private void compactCompletedEntries()
    {
        var write = 0;
        var span = CollectionsMarshal.AsSpan(entries);

        for (var i = 0; i < span.Length; i++)
        {
            if (span[i].IsCompleted)
                continue;

            if (write != i)
                span[write] = span[i];

            write++;
        }

        if (write < entries.Count)
            entries.RemoveRange(write, entries.Count - write);
    }

    private TransformSequenceScope pushSequenceScope(Drawable target, double newStartTime)
    {
        var hadPrevious = hasActiveSequenceStartTime;
        var previousStart = activeSequenceStartTime;
        var previousToken = activeScopeToken;
        var token = ++nextScopeToken;

        hasActiveSequenceStartTime = true;
        activeSequenceStartTime = newStartTime;
        activeScopeToken = token;

        return new TransformSequenceScope(target, this, hadPrevious, previousStart, token, previousToken);
    }

    private static void cancelUnobservedSequenceInSpan(Span<TransformEntry> span, int writtenCount, int nextReadIndex, int sequenceId)
    {
        for (var j = 0; j < writtenCount; j++)
        {
            ref var entry = ref span[j];
            if (entry.SequenceId == sequenceId)
                entry.RemainingLoops = 0;
        }

        for (var j = nextReadIndex; j < span.Length; j++)
        {
            ref var entry = ref span[j];
            if (entry.SequenceId == sequenceId)
                entry.RemainingLoops = 0;
        }
    }

    private void enqueueNotification(ITransformObserver observer, bool isAbort)
    {
        pendingNotifications ??= new List<PendingNotification>(2);
        pendingNotifications.Add(new PendingNotification(observer, isAbort));
    }

    private void drainNotifications(Drawable target)
    {
        if (isDrainingNotifications || pendingNotifications is null || pendingNotifications.Count == 0)
            return;

        isDrainingNotifications = true;

        try
        {
            var index = 0;

            while (index < pendingNotifications.Count)
            {
                var notification = pendingNotifications[index];
                pendingNotifications[index] = default;
                index++;

                if (isDisposed || target.IsDisposed)
                    break;

                if (notification.IsAbort)
                    notification.Observer.OnTransformAborted(target);
                else
                    notification.Observer.OnTransformCompleted(target);
            }
        }
        finally
        {
            pendingNotifications.Clear();
            isDrainingNotifications = false;
        }
    }

    private void validateScheduleParameters(Drawable target, double startTime, double duration, double loopDelay, int remainingLoops)
    {
        ArgumentNullException.ThrowIfNull(target);
        ObjectDisposedException.ThrowIf(isDisposed || target.IsDisposed, target);

        if (!double.IsFinite(startTime))
            throw new ArgumentOutOfRangeException(nameof(startTime), startTime, "Start time must be finite.");

        if (!double.IsFinite(duration) || duration < 0.0)
            throw new ArgumentOutOfRangeException(nameof(duration), duration, "Duration must be a finite non-negative number.");

        var endTime = startTime + duration;
        if (!double.IsFinite(endTime))
            throw new ArgumentOutOfRangeException(nameof(duration), duration, "Transform end time must be finite.");

        if (remainingLoops != 0 && (!double.IsFinite(loopDelay) || loopDelay <= 0.0))
            throw new ArgumentOutOfRangeException(nameof(loopDelay), loopDelay, "Loop delay must be finite and greater than zero when looping.");
    }

    private static void advanceLoopEntry(ref TransformEntry entry, double currentTime)
    {
        var behind = currentTime - entry.EndTime;
        var steps = behind >= 0.0
            ? (long)Math.Floor(behind / entry.LoopDelay) + 1L
            : 1L;

        if (entry.RemainingLoops > 0)
        {
            if (steps > entry.RemainingLoops)
                steps = entry.RemainingLoops;

            entry.RemainingLoops -= (int)steps;
        }

        var shift = steps * entry.LoopDelay;
        entry.StartTime += shift;
        entry.EndTime += shift;

        entry.Flags &= ~(TransformEntryFlags.Applied | TransformEntryFlags.Completed | TransformEntryFlags.Aborted);

        if (!entry.HasExplicitStartValue)
            entry.Flags &= ~TransformEntryFlags.HasStartValue;
    }

    private static int compareOrder(in TransformEntry left, in TransformEntry right)
    {
        var startComparison = left.StartTime.CompareTo(right.StartTime);
        return startComparison != 0
            ? startComparison
            : left.CreationOrder.CompareTo(right.CreationOrder);
    }

    private readonly struct PendingNotification
    {
        public readonly ITransformObserver Observer;
        public readonly bool IsAbort;

        public PendingNotification(ITransformObserver observer, bool isAbort)
        {
            Observer = observer;
            IsAbort = isAbort;
        }
    }
}
