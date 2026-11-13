using System.Diagnostics;
using kajarlabs.Framework.Threading;

namespace kajarlabs.Framework.Tests.Threading;

public sealed class GameThreadTests
{
    [Fact]
    public void EachThreadHasDistinctClockSchedulerAndDefaultFrequencies()
    {
        using var input = new InputThread();
        using var update = new UpdateThread();
        using var draw = new DrawThread();

        Assert.Equal("Input", input.Name);
        Assert.Equal("Update", update.Name);
        Assert.Equal("Draw", draw.Name);

        Assert.Equal(InputThread.DEFAULT_FREQUENCY, input.TargetFrequency);
        Assert.Equal(UpdateThread.DEFAULT_FREQUENCY, update.TargetFrequency);
        Assert.Equal(DrawThread.DEFAULT_FREQUENCY, draw.TargetFrequency);

        Assert.NotSame(input.Clock, update.Clock);
        Assert.NotSame(update.Clock, draw.Clock);
        Assert.NotSame(input.Scheduler, update.Scheduler);
        Assert.NotSame(update.Scheduler, draw.Scheduler);
    }

    [Theory]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void TargetFrequencyRejectsInvalidValues(double invalidFrequency)
    {
        using var thread = new UpdateThread();

        Assert.Throws<ArgumentOutOfRangeException>(() => thread.TargetFrequency = invalidFrequency);
        Assert.Throws<ArgumentOutOfRangeException>(() => new UpdateThread(invalidFrequency));
    }

    [Fact]
    public void ScheduledWorkRunsOnOwningThread()
    {
        var callerThreadId = Environment.CurrentManagedThreadId;
        var executedThreadId = -1;
        using var executed = new ManualResetEventSlim(false);
        using var update = new UpdateThread(targetFrequency: 0);

        update.Start();

        try
        {
            var workerThreadId = update.ManagedThread!.ManagedThreadId;
            update.Scheduler.Add(() =>
            {
                executedThreadId = Environment.CurrentManagedThreadId;
                executed.Set();
            });

            Assert.True(executed.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
            Assert.Equal(workerThreadId, executedThreadId);
            Assert.NotEqual(callerThreadId, executedThreadId);
        }
        finally
        {
            update.Stop();
        }
    }

    [Fact]
    public void StopWakesLowFrequencyThreadImmediately()
    {
        using var firstFrameDone = new ManualResetEventSlim(false);
        using var thread = new UpdateThread(targetFrequency: 0.1, onNewFrame: _ => firstFrameDone.Set());

        thread.Start();
        Assert.True(firstFrameDone.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));

        var elapsed = Stopwatch.StartNew();
        thread.Stop();
        elapsed.Stop();

        Assert.Equal(GameThreadState.Stopped, thread.State);
        Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(2), $"Stop took {elapsed.Elapsed}, expected immediate wake-up.");
    }

    [Fact]
    public void StartAndStopRepeatedlyWithoutHangsOrLeakedThreads()
    {
        using var thread = new UpdateThread(targetFrequency: 0);

        for (var i = 0; i < 30; i++)
        {
            var targetFrames = thread.FrameCount + 1;
            using var frameReached = new ManualResetEventSlim(false);
            Thread? worker = null;

            thread.Start();
            worker = thread.ManagedThread;
            Assert.NotNull(worker);

            thread.Scheduler.Add(() => frameReached.Set());
            Assert.True(frameReached.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));

            thread.Stop();

            Assert.False(worker.IsAlive);
            Assert.Null(thread.ManagedThread);
            Assert.Equal(GameThreadState.Stopped, thread.State);
            Assert.True(thread.FrameCount >= targetFrames);
        }
    }

    [Fact]
    public void UpdateDrawAndInputRunAtIndependentRates()
    {
        using var drawTargetReached = new ManualResetEventSlim(false);
        var drawCount = 0;

        using var input = new InputThread(targetFrequency: 400);
        using var update = new UpdateThread(targetFrequency: 200);
        using var draw = new DrawThread(targetFrequency: 20, onNewFrame: _ =>
        {
            if (Interlocked.Increment(ref drawCount) >= 5)
                drawTargetReached.Set();
        });

        input.Start();
        update.Start();
        draw.Start();

        try
        {
            Assert.True(drawTargetReached.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        }
        finally
        {
            draw.Stop();
            update.Stop();
            input.Stop();
        }

        Assert.True(input.FrameCount > draw.FrameCount, $"Expected Input ({input.FrameCount}) > Draw ({draw.FrameCount}).");
        Assert.True(update.FrameCount > draw.FrameCount, $"Expected Update ({update.FrameCount}) > Draw ({draw.FrameCount}).");
    }

    [Fact]
    public void CallingStopFromInsideFrameDoesNotDeadlock()
    {
        using var stoppedFromSelf = new ManualResetEventSlim(false);
        UpdateThread? thread = null;

        thread = new UpdateThread(targetFrequency: 0, onNewFrame: _ =>
        {
            thread!.Stop();
            stoppedFromSelf.Set();
        });

        using (thread)
        {
            thread.Start();
            var worker = thread.ManagedThread!;

            Assert.True(stoppedFromSelf.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
            Assert.True(worker.Join(TimeSpan.FromSeconds(5)));
            thread.Stop();

            Assert.Equal(GameThreadState.Stopped, thread.State);
        }
    }

    [Fact]
    public void UnhandledExceptionDuringFrameStopsThreadAndReportsException()
    {
        using var exceptionReported = new ManualResetEventSlim(false);
        Exception? reported = null;

        using var thread = new UpdateThread(targetFrequency: 0, onNewFrame: _ => throw new InvalidOperationException("frame failure"));
        thread.ExceptionThrown += (_, ex) =>
        {
            reported = ex;
            exceptionReported.Set();
        };

        thread.Start();
        var worker = thread.ManagedThread!;

        Assert.True(exceptionReported.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Assert.True(worker.Join(TimeSpan.FromSeconds(5)));
        thread.Stop();

        Assert.IsType<InvalidOperationException>(reported);
        Assert.Same(reported, thread.UnhandledException);
        Assert.Equal(GameThreadState.Stopped, thread.State);
    }

    [Fact]
    public void StartupFailureInInitializeRethrowsAndLeavesNoAliveThread()
    {
        using var thread = new FailingInitializeThread();

        Assert.Throws<InvalidOperationException>(() => thread.Start());
        Assert.Equal(GameThreadState.Stopped, thread.State);
        Assert.Null(thread.ManagedThread);
    }

    private sealed class FailingInitializeThread : GameThread
    {
        public FailingInitializeThread()
            : base("Failing", targetFrequency: 0)
        {
        }

        protected override void OnInitialize()
        {
            throw new InvalidOperationException("init failed");
        }
    }
}
