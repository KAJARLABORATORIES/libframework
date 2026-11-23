using System.Diagnostics.CodeAnalysis;

namespace kajarlabs.Framework.Threading;

/// <summary>
/// Represents the lifecycle state of a <see cref="ScheduledDelegate"/>.
/// </summary>
public enum ScheduledDelegateState
{
    /// <summary>
    /// The delegate is waiting to run on the owning thread.
    /// </summary>
    Waiting,

    /// <summary>
    /// The delegate is currently executing on the owning thread.
    /// </summary>
    Running,

    /// <summary>
    /// The delegate has completed execution.
    /// </summary>
    Completed,

    /// <summary>
    /// The delegate was cancelled before completion.
    /// </summary>
    Cancelled,
}

/// <summary>
/// A handle to an action scheduled on a <see cref="Scheduler"/>.
/// </summary>
[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "Established public API type name; renaming would be a breaking change.")]
public sealed class ScheduledDelegate
{
    private readonly Action task;
    private int state;

    internal ScheduledDelegate(Action task, double executionTime, double repeatInterval, bool isDelayed)
    {
        this.task = task;
        ExecutionTime = executionTime;
        RepeatInterval = repeatInterval;
        IsDelayed = isDelayed;
    }

    /// <summary>
    /// Gets the current execution state of this scheduled delegate.
    /// </summary>
    public ScheduledDelegateState State => (ScheduledDelegateState)Volatile.Read(ref state);

    /// <summary>
    /// Gets a value indicating whether this scheduled delegate has completed.
    /// </summary>
    public bool Completed => State == ScheduledDelegateState.Completed;

    /// <summary>
    /// Gets a value indicating whether this scheduled delegate has been cancelled.
    /// </summary>
    public bool Cancelled => State == ScheduledDelegateState.Cancelled;

    internal double ExecutionTime { get; set; }

    internal double RepeatInterval { get; }

    internal bool IsDelayed { get; }

    internal bool IsRepeating => RepeatInterval >= 0;

    internal long Sequence { get; set; }

    /// <summary>
    /// Cancels this scheduled delegate so it will not run (or repeat) again.
    /// </summary>
    public void Cancel()
    {
        while (true)
        {
            var current = Volatile.Read(ref state);

            if (current is (int)ScheduledDelegateState.Completed or (int)ScheduledDelegateState.Cancelled)
                return;

            if (Interlocked.CompareExchange(ref state, (int)ScheduledDelegateState.Cancelled, current) == current)
                return;
        }
    }

    internal bool RunTask()
    {
        if (Interlocked.CompareExchange(ref state, (int)ScheduledDelegateState.Running, (int)ScheduledDelegateState.Waiting)
            != (int)ScheduledDelegateState.Waiting)
        {
            return false;
        }

        var completedNormally = false;

        try
        {
            task();
            completedNormally = true;
            return true;
        }
        finally
        {
            var nextState = IsRepeating && completedNormally
                ? (int)ScheduledDelegateState.Waiting
                : (int)ScheduledDelegateState.Completed;

            Interlocked.CompareExchange(ref state, nextState, (int)ScheduledDelegateState.Running);
        }
    }
}
