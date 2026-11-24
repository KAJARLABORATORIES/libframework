using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace kajarlabs.Framework.Scene.Transforms;

internal sealed class DrawableTransformState : IDrawableExtension
{
    private static readonly Func<Drawable, DrawableTransformState> state_factory = static _ => new DrawableTransformState();

    private readonly List<TransformEntry> entries = [];
    private List<PendingNotification>? pendingNotifications;
    private long nextCreationOrder;
    private double lastUpdateTime = double.NaN;
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
                if (!entry.IsCompleted && entry.Observer is not null && !entry.IsObserverNotified)
                {
                    entry.Flags |= TransformEntryFlags.ObserverNotified;
                    enqueueNotification(entry.Observer, isAbort: true);
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
        entries.Clear();
        pendingNotifications?.Clear();
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
                        if (!existing.IsCompleted && existing.Observer is not null && !existing.IsObserverNotified)
                        {
                            existing.Flags |= TransformEntryFlags.ObserverNotified;
                            enqueueNotification(existing.Observer, isAbort: true);
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
