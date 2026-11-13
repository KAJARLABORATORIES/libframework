using System.Diagnostics;
using kajarlabs.Framework.Timing;

namespace kajarlabs.Framework.Threading;

/// <summary>
/// A thread-affine work scheduler that accepts work from any thread and executes it on its owning thread.
/// </summary>
public sealed class Scheduler
{
    private readonly IClock clock;
    private readonly Lock queueLock = new();
    private readonly Queue<ScheduledDelegate> incomingTasks = new();
    private readonly PriorityQueue<ScheduledDelegate, (double ExecutionTime, long Sequence)> timedTasks = new();
    private readonly Queue<ScheduledDelegate> tasksToRun = new();

    private int owningThreadId;
    private long nextSequence;

    /// <summary>
    /// Initializes a new instance of the <see cref="Scheduler"/> class bound to the current thread.
    /// </summary>
    /// <param name="clock">
    /// The clock used to evaluate delayed tasks, or <see langword="null"/> to create a running <see cref="StopwatchClock"/>.
    /// </param>
    public Scheduler(IClock? clock = null)
    {
        this.clock = clock ?? new StopwatchClock(start: true);
        owningThreadId = Environment.CurrentManagedThreadId;
    }

    /// <summary>
    /// Gets a value indicating whether the calling thread is the owning thread of this scheduler.
    /// </summary>
    public bool IsCurrentThread => Environment.CurrentManagedThreadId == Volatile.Read(ref owningThreadId);

    /// <summary>
    /// Gets the number of pending tasks currently queued on this scheduler.
    /// </summary>
    public int PendingTasksCount
    {
        get
        {
            ensureCurrentThread();

            lock (queueLock)
                return timedTasks.Count + incomingTasks.Count + tasksToRun.Count;
        }
    }

    /// <summary>
    /// Rebinds this scheduler's thread affinity to <paramref name="thread"/>, or to the calling thread when <see langword="null"/>.
    /// </summary>
    /// <param name="thread">The thread that will own this scheduler.</param>
    public void SetCurrentThread(Thread? thread = null)
    {
        Volatile.Write(ref owningThreadId, (thread ?? Thread.CurrentThread).ManagedThreadId);
    }

    /// <summary>
    /// Schedules <paramref name="task"/> to run on the owning thread during the next <see cref="Update"/>.
    /// </summary>
    /// <param name="task">The action to execute.</param>
    /// <returns>A handle that can be used to inspect or cancel the scheduled work.</returns>
    public ScheduledDelegate Add(Action task)
    {
        ArgumentNullException.ThrowIfNull(task);

        var scheduled = new ScheduledDelegate(task, executionTime: 0, repeatInterval: -1, isDelayed: false);
        enqueueIncoming(scheduled);
        return scheduled;
    }

    /// <summary>
    /// Schedules <paramref name="task"/> to run on the owning thread after <paramref name="timeUntilRun"/> milliseconds.
    /// </summary>
    /// <param name="task">The action to execute.</param>
    /// <param name="timeUntilRun">The delay in milliseconds relative to the scheduler's clock.</param>
    /// <param name="repeat">Whether the action should repeat every <paramref name="timeUntilRun"/> milliseconds until cancelled.</param>
    /// <returns>A handle that can be used to inspect or cancel the scheduled work.</returns>
    public ScheduledDelegate AddDelayed(Action task, double timeUntilRun, bool repeat = false)
    {
        ArgumentNullException.ThrowIfNull(task);

        if (!double.IsFinite(timeUntilRun))
            throw new ArgumentOutOfRangeException(nameof(timeUntilRun), timeUntilRun, "Delay must be a finite number.");

        if (repeat && timeUntilRun < 0)
            throw new ArgumentOutOfRangeException(nameof(timeUntilRun), timeUntilRun, "Repeating delay cannot be negative.");

        var repeatInterval = repeat ? timeUntilRun : -1;
        var scheduled = new ScheduledDelegate(task, executionTime: clock.CurrentTime + timeUntilRun, repeatInterval, isDelayed: true);
        enqueueIncoming(scheduled);
        return scheduled;
    }

    /// <summary>
    /// Executes all due tasks on the owning thread.
    /// </summary>
    /// <returns>The number of tasks executed during this update.</returns>
    public int Update()
    {
        ensureCurrentThread();

        var currentTime = clock.CurrentTime;
        drainIncomingTasks();

        while (timedTasks.TryPeek(out var next, out var priority) && priority.ExecutionTime <= currentTime)
        {
            timedTasks.Dequeue();

            if (!next.Cancelled)
                tasksToRun.Enqueue(next);
        }

        var executedCount = 0;

        while (tasksToRun.TryDequeue(out var scheduled))
        {
            if (scheduled.Cancelled)
                continue;

            try
            {
                if (scheduled.RunTask())
                    executedCount++;
            }
            finally
            {
                if (scheduled.IsRepeating && scheduled.State == ScheduledDelegateState.Waiting)
                {
                    scheduled.ExecutionTime = currentTime + scheduled.RepeatInterval;
                    scheduled.Sequence = Interlocked.Increment(ref nextSequence);
                    timedTasks.Enqueue(scheduled, (scheduled.ExecutionTime, scheduled.Sequence));
                }
            }
        }

        return executedCount;
    }

    /// <summary>
    /// Cancels and clears all pending tasks queued on this scheduler.
    /// </summary>
    public void CancelPendingTasks()
    {
        ensureCurrentThread();

        lock (queueLock)
        {
            while (incomingTasks.TryDequeue(out var incoming))
                incoming.Cancel();
        }

        while (timedTasks.TryDequeue(out var timed, out _))
            timed.Cancel();

        while (tasksToRun.TryDequeue(out var queued))
            queued.Cancel();
    }

    private void enqueueIncoming(ScheduledDelegate scheduled)
    {
        lock (queueLock)
        {
            scheduled.Sequence = Interlocked.Increment(ref nextSequence);
            incomingTasks.Enqueue(scheduled);
        }
    }

    private void drainIncomingTasks()
    {
        lock (queueLock)
        {
            while (incomingTasks.TryDequeue(out var scheduled))
            {
                if (scheduled.Cancelled)
                    continue;

                if (scheduled.IsDelayed)
                    timedTasks.Enqueue(scheduled, (scheduled.ExecutionTime, scheduled.Sequence));
                else
                    tasksToRun.Enqueue(scheduled);
            }
        }
    }

    [Conditional("DEBUG")]
    private void ensureCurrentThread()
    {
        if (!IsCurrentThread)
        {
            throw new InvalidOperationException(
                $"Thread-affine {nameof(Scheduler)} state accessed from thread {Environment.CurrentManagedThreadId}, expected owning thread {Volatile.Read(ref owningThreadId)}.");
        }
    }
}
