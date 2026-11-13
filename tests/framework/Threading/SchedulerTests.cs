using kajarlabs.Framework.Tests.Timing;
using kajarlabs.Framework.Threading;

namespace kajarlabs.Framework.Tests.Threading;

public sealed class SchedulerTests
{
    [Fact]
    public void ImmediateTasksRunOnlyOnUpdateInFifoOrder()
    {
        var clock = new ManualClock(start: true);
        var scheduler = new Scheduler(clock);
        var executed = new List<int>();

        var first = scheduler.Add(() => executed.Add(1));
        var second = scheduler.Add(() => executed.Add(2));
        var third = scheduler.Add(() => executed.Add(3));

        Assert.Empty(executed);
        Assert.Equal(ScheduledDelegateState.Waiting, first.State);

        var count = scheduler.Update();

        Assert.Equal(3, count);
        Assert.Equal([1, 2, 3], executed);
        Assert.True(first.Completed);
        Assert.True(second.Completed);
        Assert.True(third.Completed);
    }

    [Fact]
    public void DelayedTasksExecuteAccordingToManualClock()
    {
        var clock = new ManualClock(start: true);
        var scheduler = new Scheduler(clock);
        var executed = new List<string>();

        scheduler.AddDelayed(() => executed.Add("late"), 200);
        scheduler.AddDelayed(() => executed.Add("first"), 100);
        scheduler.AddDelayed(() => executed.Add("second"), 100);

        Assert.Equal(0, scheduler.Update());
        Assert.Empty(executed);

        clock.Advance(99);
        Assert.Equal(0, scheduler.Update());
        Assert.Empty(executed);

        clock.Advance(1);
        Assert.Equal(2, scheduler.Update());
        Assert.Equal(["first", "second"], executed);

        clock.Advance(100);
        Assert.Equal(1, scheduler.Update());
        Assert.Equal(["first", "second", "late"], executed);
    }

    [Fact]
    public void CancelledTaskDoesNotRun()
    {
        var clock = new ManualClock(start: true);
        var scheduler = new Scheduler(clock);
        var ran = false;

        var immediate = scheduler.Add(() => ran = true);
        var delayed = scheduler.AddDelayed(() => ran = true, 50);

        immediate.Cancel();
        delayed.Cancel();

        Assert.True(immediate.Cancelled);
        Assert.True(delayed.Cancelled);

        clock.Advance(100);
        Assert.Equal(0, scheduler.Update());
        Assert.False(ran);
    }

    [Fact]
    public void RepeatingTaskRunsEachIntervalUntilCancelled()
    {
        var clock = new ManualClock(start: true);
        var scheduler = new Scheduler(clock);
        var executions = 0;

        ScheduledDelegate? handle = null;
        handle = scheduler.AddDelayed(() =>
        {
            executions++;

            if (executions == 3)
                handle!.Cancel();
        }, 50, repeat: true);

        clock.Advance(49);
        Assert.Equal(0, scheduler.Update());

        clock.Advance(1);
        Assert.Equal(1, scheduler.Update());
        Assert.Equal(1, executions);
        Assert.Equal(ScheduledDelegateState.Waiting, handle.State);

        // Even if multiple intervals elapse, a repeating task executes once per Update.
        clock.Advance(200);
        Assert.Equal(1, scheduler.Update());
        Assert.Equal(2, executions);

        clock.Advance(50);
        Assert.Equal(1, scheduler.Update());
        Assert.Equal(3, executions);
        Assert.True(handle.Cancelled);

        clock.Advance(100);
        Assert.Equal(0, scheduler.Update());
        Assert.Equal(3, executions);
    }

    [Fact]
    public void TaskScheduledDuringUpdateRunsOnNextUpdate()
    {
        var clock = new ManualClock(start: true);
        var scheduler = new Scheduler(clock);
        var log = new List<string>();

        scheduler.Add(() =>
        {
            log.Add("outer");
            scheduler.Add(() => log.Add("inner"));
        });

        Assert.Equal(1, scheduler.Update());
        Assert.Equal(["outer"], log);

        Assert.Equal(1, scheduler.Update());
        Assert.Equal(["outer", "inner"], log);
    }

    [Fact]
    public void CancelPendingTasksCancelsQueuedImmediateAndDelayedWork()
    {
        var clock = new ManualClock(start: true);
        var scheduler = new Scheduler(clock);
        var executed = 0;

        var a = scheduler.Add(() => executed++);
        var b = scheduler.AddDelayed(() => executed++, 50);

        Assert.Equal(2, scheduler.PendingTasksCount);

        scheduler.CancelPendingTasks();

        Assert.True(a.Cancelled);
        Assert.True(b.Cancelled);
        Assert.Equal(0, scheduler.PendingTasksCount);

        clock.Advance(100);
        Assert.Equal(0, scheduler.Update());
        Assert.Equal(0, executed);
    }

    [Fact]
    public void ExceptionInTaskDoesNotRerunFailedTaskAndPreservesRemainingTasks()
    {
        var clock = new ManualClock(start: true);
        var scheduler = new Scheduler(clock);
        var ranAfter = false;

        var failing = scheduler.Add(() => throw new InvalidOperationException("boom"));
        var trailing = scheduler.Add(() => ranAfter = true);

        Assert.Throws<InvalidOperationException>(() => scheduler.Update());
        Assert.True(failing.Completed);
        Assert.Equal(ScheduledDelegateState.Waiting, trailing.State);

        Assert.Equal(1, scheduler.Update());
        Assert.True(ranAfter);
        Assert.True(trailing.Completed);
    }

    [Fact]
    public void ValidationRejectsNullOrInvalidDelays()
    {
        var scheduler = new Scheduler(new ManualClock());

        Assert.Throws<ArgumentNullException>(() => scheduler.Add(null!));
        Assert.Throws<ArgumentNullException>(() => scheduler.AddDelayed(null!, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => scheduler.AddDelayed(() => { }, double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => scheduler.AddDelayed(() => { }, double.PositiveInfinity));
        Assert.Throws<ArgumentOutOfRangeException>(() => scheduler.AddDelayed(() => { }, -10, repeat: true));
    }

    [Fact]
    public void WorkScheduledFromOtherThreadsRunsOnOwningThread()
    {
        var clock = new ManualClock(start: true);
        var scheduler = new Scheduler(clock);
        var owningThreadId = Environment.CurrentManagedThreadId;

        const int worker_count = 4;
        const int tasks_per_worker = 50;
        var executedThreadIds = new List<int>(worker_count * tasks_per_worker);
        var workers = new Thread[worker_count];
        using var allEnqueued = new CountdownEvent(worker_count);

        for (var w = 0; w < worker_count; w++)
        {
            workers[w] = new Thread(() =>
            {
                Assert.False(scheduler.IsCurrentThread);

                for (var i = 0; i < tasks_per_worker; i++)
                {
                    if (i % 2 == 0)
                        scheduler.Add(() => executedThreadIds.Add(Environment.CurrentManagedThreadId));
                    else
                        scheduler.AddDelayed(() => executedThreadIds.Add(Environment.CurrentManagedThreadId), 10);
                }

                allEnqueued.Signal();
            });
            workers[w].Start();
        }

        Assert.True(allEnqueued.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));

        foreach (var worker in workers)
            worker.Join();

        clock.Advance(10);
        var executed = scheduler.Update();

        Assert.Equal(worker_count * tasks_per_worker, executed);
        Assert.Equal(worker_count * tasks_per_worker, executedThreadIds.Count);
        Assert.All(executedThreadIds, id => Assert.Equal(owningThreadId, id));
    }

    [Fact]
    public void SetCurrentThreadRebindsOwningThread()
    {
        var clock = new ManualClock(start: true);
        var scheduler = new Scheduler(clock);
        var executedOnThreadId = -1;

        var thread = new Thread(() =>
        {
            scheduler.SetCurrentThread();
            Assert.True(scheduler.IsCurrentThread);

            scheduler.Add(() => executedOnThreadId = Environment.CurrentManagedThreadId);
            scheduler.Update();
        });

        thread.Start();
        thread.Join();

        Assert.Equal(thread.ManagedThreadId, executedOnThreadId);
        Assert.False(scheduler.IsCurrentThread);
    }

#if DEBUG
    [Fact]
    public void AccessingThreadAffineStateFromWrongThreadThrowsInDebug()
    {
        var scheduler = new Scheduler(new ManualClock());
        Exception? updateException = null;
        Exception? countException = null;
        Exception? cancelException = null;

        var worker = new Thread(() =>
        {
            updateException = Record.Exception(() => scheduler.Update());
            countException = Record.Exception(() => _ = scheduler.PendingTasksCount);
            cancelException = Record.Exception(() => scheduler.CancelPendingTasks());
        });

        worker.Start();
        worker.Join();

        Assert.IsType<InvalidOperationException>(updateException);
        Assert.IsType<InvalidOperationException>(countException);
        Assert.IsType<InvalidOperationException>(cancelException);
    }
#endif
}
