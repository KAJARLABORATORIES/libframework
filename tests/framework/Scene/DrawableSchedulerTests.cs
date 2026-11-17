using kajarlabs.Framework.DependencyInjection;
using kajarlabs.Framework.Scene;
using kajarlabs.Framework.Tests.Timing;
using kajarlabs.Framework.Timing;

namespace kajarlabs.Framework.Tests.Scene;

public sealed class DrawableSchedulerTests
{
    [Fact]
    public void SchedulerIsDrainedInUpdateSubTreeAfterLoadCompleteAndBeforeUpdate()
    {
        var log = new List<string>();
        using var drawable = new ScheduledLifecycleDrawable(log);

        drawable.Schedule(() => log.Add("pre-load-scheduled"));
        drawable.Load(DependencyCollection.EMPTY);

        Assert.Equal(["Load"], log);
        log.Clear();

        Assert.True(drawable.UpdateSubTree());
        Assert.Equal(
            [
                "LoadComplete",
                "pre-load-scheduled",
                "scheduled-in-load",
                "scheduled-in-load-complete",
                "Update",
            ],
            log);

        log.Clear();
        Assert.True(drawable.UpdateSubTree());
        Assert.Equal(["scheduled-in-update", "Update"], log);
    }

    [Fact]
    public void ScheduleDelayedUsesInheritedAndCustomClock()
    {
        var parentSource = new ManualClock(start: true, initialTime: 100);
        var customSource = new ManualClock(start: true, initialTime: 500);

        using var parent = new Container { Clock = new FramedClock(parentSource) };
        var inheritedChild = new Drawable();
        var customChild = new Drawable { Clock = new FramedClock(customSource) };

        parent.AddRange([inheritedChild, customChild]);
        parent.Load(DependencyCollection.EMPTY);

        var inheritedRuns = 0;
        var repeatingRuns = 0;
        var customRuns = 0;

        inheritedChild.ScheduleDelayed(() => inheritedRuns++, 50);
        var repeatingHandle = inheritedChild.ScheduleDelayed(() => repeatingRuns++, 25, repeat: true);
        customChild.ScheduleDelayed(() => customRuns++, 40);

        Assert.True(parent.UpdateSubTree());
        Assert.Equal(0, inheritedRuns);
        Assert.Equal(0, repeatingRuns);
        Assert.Equal(0, customRuns);

        parentSource.Advance(25);
        Assert.True(parent.UpdateSubTree());
        Assert.Equal(0, inheritedRuns);
        Assert.Equal(1, repeatingRuns);
        Assert.Equal(0, customRuns);

        parentSource.Advance(25);
        customSource.Advance(40);
        Assert.True(parent.UpdateSubTree());
        Assert.Equal(1, inheritedRuns);
        Assert.Equal(2, repeatingRuns);
        Assert.Equal(1, customRuns);

        repeatingHandle.Cancel();
        parentSource.Advance(50);
        Assert.True(parent.UpdateSubTree());
        Assert.Equal(2, repeatingRuns);
    }

    [Fact]
    public void CrossThreadScheduledWorkRebindsThreadAffinityDuringUpdateSubTree()
    {
        using var drawable = new Drawable();
        var mainThreadId = Environment.CurrentManagedThreadId;
        var executedOnThreadId = -1;

        var worker = new Thread(() =>
        {
            drawable.Schedule(() => executedOnThreadId = Environment.CurrentManagedThreadId);
        });

        worker.Start();
        worker.Join();

        Assert.Equal(-1, executedOnThreadId);
        Assert.True(drawable.UpdateSubTree());
        Assert.Equal(mainThreadId, executedOnThreadId);
    }

    [Fact]
    public void DisposeCancelsPendingAndRepeatingTasksFromAnyThreadAndPostDisposeScheduleIsCancelled()
    {
        var manualClock = new ManualClock(start: true);
        var drawable = new Drawable { Clock = new FramedClock(manualClock) };

        var ranImmediate = false;
        var ranDelayed = false;
        var repeatCount = 0;

        var immediateHandle = drawable.Schedule(() => ranImmediate = true);
        var delayedHandle = drawable.ScheduleDelayed(() => ranDelayed = true, 50);
        var repeatingHandle = drawable.ScheduleDelayed(() => repeatCount++, 10, repeat: true);

        // Bind scheduler affinity to a different thread first, then advance clock and drain one repeat.
        var ownerThread = new Thread(() =>
        {
            manualClock.Advance(10);
            drawable.UpdateSubTree();
            drawable.Schedule(() => ranImmediate = true);
        });

        ownerThread.Start();
        ownerThread.Join();

        Assert.True(ranImmediate);
        Assert.Equal(1, repeatCount);

        ranImmediate = false;
        var secondImmediate = drawable.Schedule(() => ranImmediate = true);

        // Dispose from the test thread (not ownerThread) must cancel all tasks without throwing.
        drawable.Dispose();

        Assert.True(drawable.IsDisposed);
        Assert.True(immediateHandle.Completed);
        Assert.True(delayedHandle.Cancelled);
        Assert.True(repeatingHandle.Cancelled);
        Assert.True(secondImmediate.Cancelled);
        Assert.Equal(0, drawable.Scheduler.PendingTasksCount);

        manualClock.Advance(100);
        Assert.False(drawable.UpdateSubTree());
        Assert.False(ranImmediate);
        Assert.False(ranDelayed);
        Assert.Equal(1, repeatCount);

        var postDisposeHandle = drawable.Schedule(() => ranImmediate = true);
        var postDisposeDelayed = drawable.ScheduleDelayed(() => ranDelayed = true, 10);
        Assert.True(postDisposeHandle.Cancelled);
        Assert.True(postDisposeDelayed.Cancelled);
    }

    private sealed class ScheduledLifecycleDrawable(List<string> log) : Drawable
    {
        private bool scheduledInUpdate;

        protected override void Load()
        {
            log.Add("Load");
            Schedule(() => log.Add("scheduled-in-load"));
        }

        protected override void LoadComplete()
        {
            log.Add("LoadComplete");
            Schedule(() => log.Add("scheduled-in-load-complete"));
        }

        protected override void Update()
        {
            log.Add("Update");

            if (!scheduledInUpdate)
            {
                scheduledInUpdate = true;
                Schedule(() => log.Add("scheduled-in-update"));
            }
        }
    }
}
