using System.Runtime.CompilerServices;

namespace kajarlabs.Framework.Scene.Transforms;

[Flags]
internal enum TransformEntryFlags : byte
{
    None = 0,
    HasStartValue = 1 << 0,
    ExplicitStartValue = 1 << 1,
    Applied = 1 << 2,
    Completed = 1 << 3,
    Aborted = 1 << 4,
    ObserverNotified = 1 << 5,
}

internal struct TransformEntry
{
    public double StartTime;
    public double EndTime;
    public double AbortTime;
    public double LoopDelay;
    public long CreationOrder;
    public int RemainingLoops;
    public int SequenceId;
    public TransformValue StartValue;
    public TransformValue EndValue;
    public CustomTransformHandler? CustomHandler;
    public ITransformObserver? Observer;
    public BuiltInTransformTarget BuiltInTarget;
    public Easing Easing;
    public TransformEntryFlags Flags;

    public readonly bool HasAbortTime => !double.IsPositiveInfinity(AbortTime);

    public readonly double EffectiveEndTime => AbortTime < EndTime ? AbortTime : EndTime;

    public readonly bool IsCustom => CustomHandler is not null;

    public readonly bool HasStartValue => (Flags & TransformEntryFlags.HasStartValue) != 0;

    public readonly bool HasExplicitStartValue => (Flags & TransformEntryFlags.ExplicitStartValue) != 0;

    public readonly bool IsApplied => (Flags & TransformEntryFlags.Applied) != 0;

    public readonly bool IsCompleted => (Flags & TransformEntryFlags.Completed) != 0;

    public readonly bool IsAborted => (Flags & TransformEntryFlags.Aborted) != 0;

    public readonly bool IsObserverNotified => (Flags & TransformEntryFlags.ObserverNotified) != 0;

    public readonly TransformTargetKey TargetKey
        => CustomHandler is not null
            ? CustomHandler.TargetKey
            : TransformTargetKey.FromBuiltIn(BuiltInTarget);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly bool ConflictsWith(in TransformTargetKey other)
    {
        if (CustomHandler is not null)
            return CustomHandler.TargetKey.ConflictsWith(in other);

        return other.BuiltInMask != BuiltInTransformMask.None
            && (BuiltInTransformHelper.GetMask(BuiltInTarget) & other.BuiltInMask) != BuiltInTransformMask.None;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void CaptureStartValue(Drawable target)
    {
        if (CustomHandler is not null)
            CustomHandler.ReadIntoStartValue(target);
        else
            StartValue = BuiltInTransformHelper.ReadValue(target, BuiltInTarget);

        Flags |= TransformEntryFlags.HasStartValue;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly void ApplyStartValue(Drawable target)
    {
        if (CustomHandler is not null)
            CustomHandler.ApplyStartValue(target);
        else
            BuiltInTransformHelper.ApplyValue(target, BuiltInTarget, in StartValue);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly void ApplyEndValue(Drawable target)
    {
        if (CustomHandler is not null)
            CustomHandler.ApplyEndValue(target);
        else
            BuiltInTransformHelper.ApplyValue(target, BuiltInTarget, in EndValue);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly void ApplyAt(Drawable target, double time)
    {
        if (CustomHandler is not null)
            CustomHandler.Apply(target, time, StartTime, EndTime);
        else
            BuiltInTransformHelper.ApplyInterpolated(target, BuiltInTarget, in StartValue, in EndValue, time, StartTime, EndTime, Easing);
    }
}
