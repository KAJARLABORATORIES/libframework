using System.Collections.Concurrent;
using kajarlabs.Framework.Threading;
using kajarlabs.Framework.Timing;

namespace kajarlabs.Framework.Tests.Threading;

public sealed class GameThreadRunnerTests
{
    [Fact]
    public void StartAndStopFollowDefinedOrder()
    {
        var events = new ConcurrentQueue<string>();

        using var input = new RecordingInputThread(events, targetFrequency: 0);
        using var update = new RecordingUpdateThread(events, targetFrequency: 0);
        using var draw = new RecordingDrawThread(events, targetFrequency: 0);
        using var runner = new GameThreadRunner(input, update, draw);

        runner.Start();
        Assert.True(runner.IsRunning);

        using var allRan = new ManualResetEventSlim(false);
        draw.Scheduler.Add(() => allRan.Set());
        Assert.True(allRan.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));

        runner.Stop();
        Assert.False(runner.IsRunning);

        Assert.Equal(
            ["Input:Start", "Update:Start", "Draw:Start", "Draw:Stop", "Update:Stop", "Input:Stop"],
            events.ToArray());
    }

    [Fact]
    public async Task RunExecutesInputOnMainThreadAndFollowsStartupShutdownOrder()
    {
        var events = new ConcurrentQueue<string>();
        var callerThreadId = Environment.CurrentManagedThreadId;
        var inputThreadId = -1;
        var updateThreadId = -1;
        var drawThreadId = -1;

        using var input = new RecordingInputThread(events, targetFrequency: 0, onFrame: _ => inputThreadId = Environment.CurrentManagedThreadId);
        using var update = new RecordingUpdateThread(events, targetFrequency: 0, onFrame: _ => updateThreadId = Environment.CurrentManagedThreadId);
        using var draw = new RecordingDrawThread(events, targetFrequency: 0, onFrame: _ => drawThreadId = Environment.CurrentManagedThreadId);
        using var runner = new GameThreadRunner(input, update, draw);

        using var allExecuted = new CountdownEvent(3);
        input.Scheduler.Add(() => allExecuted.Signal());
        update.Scheduler.Add(() => allExecuted.Signal());
        draw.Scheduler.Add(() => allExecuted.Signal());

        Task? stopper = null;

        runner.Run(onStarted: () =>
        {
            stopper = Task.Run(() =>
            {
                allExecuted.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
                runner.Stop();
            }, TestContext.Current.CancellationToken);
        });

        if (stopper is not null)
            await stopper;

        Assert.False(runner.IsRunning);
        Assert.Equal(callerThreadId, inputThreadId);
        Assert.NotEqual(callerThreadId, updateThreadId);
        Assert.NotEqual(callerThreadId, drawThreadId);
        Assert.NotEqual(updateThreadId, drawThreadId);

        Assert.Equal(
            ["Input:Start", "Update:Start", "Draw:Start", "Draw:Stop", "Update:Stop", "Input:Stop"],
            events.ToArray());
    }

    [Fact]
    public void RepeatedStartAndStopCyclesLeaveNoLeakedThreads()
    {
        using var runner = new GameThreadRunner(
            new InputThread(targetFrequency: 0),
            new UpdateThread(targetFrequency: 0),
            new DrawThread(targetFrequency: 0));

        for (var i = 0; i < 20; i++)
        {
            using var cycleReady = new CountdownEvent(3);

            runner.Start();

            var inputWorker = runner.Input.ManagedThread!;
            var updateWorker = runner.Update.ManagedThread!;
            var drawWorker = runner.Draw.ManagedThread!;

            runner.Input.Scheduler.Add(() => cycleReady.Signal());
            runner.Update.Scheduler.Add(() => cycleReady.Signal());
            runner.Draw.Scheduler.Add(() => cycleReady.Signal());

            Assert.True(cycleReady.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));

            runner.Stop();

            Assert.False(runner.IsRunning);
            Assert.False(inputWorker.IsAlive);
            Assert.False(updateWorker.IsAlive);
            Assert.False(drawWorker.IsAlive);
        }
    }

    [Fact]
    public void StartupFailureRollsBackAlreadyStartedThreadsInReverseOrder()
    {
        var events = new ConcurrentQueue<string>();

        using var input = new RecordingInputThread(events, targetFrequency: 0);
        using var update = new RecordingUpdateThread(events, targetFrequency: 0);
        using var draw = new RecordingDrawThread(events, targetFrequency: 0, throwOnInitialize: true);
        using var runner = new GameThreadRunner(input, update, draw);

        Assert.Throws<InvalidOperationException>(() => runner.Start());
        Assert.False(runner.IsRunning);
        Assert.Equal(GameThreadState.Stopped, input.State);
        Assert.Equal(GameThreadState.Stopped, update.State);
        Assert.Equal(GameThreadState.Stopped, draw.State);

        Assert.Equal(
            ["Input:Start", "Update:Start", "Update:Stop", "Input:Stop"],
            events.ToArray());
    }

    [Fact]
    public void UnhandledExceptionOnWorkerThreadTriggersCleanRunnerShutdownInRunMode()
    {
        var events = new ConcurrentQueue<string>();

        using var input = new RecordingInputThread(events, targetFrequency: 0);
        using var update = new RecordingUpdateThread(events, targetFrequency: 0, onFrame: _ => throw new InvalidOperationException("worker crash"));
        using var draw = new RecordingDrawThread(events, targetFrequency: 0);
        using var runner = new GameThreadRunner(input, update, draw);

        runner.Run();

        var recorded = events.ToArray();
        Assert.False(runner.IsRunning);
        Assert.IsType<InvalidOperationException>(update.UnhandledException);
        Assert.Contains("Draw:Stop", recorded);
        Assert.Contains("Update:Stop", recorded);
        Assert.Equal("Input:Stop", recorded[^1]);
    }

    [Fact]
    public void TwoRunnersExecuteIndependentlyInParallel()
    {
        using var runnerA = new GameThreadRunner(
            new InputThread(targetFrequency: 0),
            new UpdateThread(targetFrequency: 0),
            new DrawThread(targetFrequency: 0));
        using var runnerB = new GameThreadRunner(
            new InputThread(targetFrequency: 0),
            new UpdateThread(targetFrequency: 0),
            new DrawThread(targetFrequency: 0));

        using var readyA = new CountdownEvent(3);
        using var readyB = new CountdownEvent(3);

        runnerA.Start();
        runnerB.Start();

        try
        {
            runnerA.Input.Scheduler.Add(() => readyA.Signal());
            runnerA.Update.Scheduler.Add(() => readyA.Signal());
            runnerA.Draw.Scheduler.Add(() => readyA.Signal());

            runnerB.Input.Scheduler.Add(() => readyB.Signal());
            runnerB.Update.Scheduler.Add(() => readyB.Signal());
            runnerB.Draw.Scheduler.Add(() => readyB.Signal());

            Assert.True(readyA.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
            Assert.True(readyB.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        }
        finally
        {
            runnerA.Stop();
            runnerB.Stop();
        }

        Assert.False(runnerA.IsRunning);
        Assert.False(runnerB.IsRunning);
    }

    private sealed class RecordingInputThread : InputThread
    {
        private readonly ConcurrentQueue<string> events;
        private readonly Action<IFrameBasedClock>? onFrame;

        public RecordingInputThread(
            ConcurrentQueue<string> events,
            double targetFrequency,
            Action<IFrameBasedClock>? onFrame = null)
            : base(targetFrequency)
        {
            this.events = events;
            this.onFrame = onFrame;
        }

        protected override void OnInitialize() => events.Enqueue("Input:Start");

        protected override void OnNewFrame() => onFrame?.Invoke(Clock);

        protected override void OnExit() => events.Enqueue("Input:Stop");
    }

    private sealed class RecordingUpdateThread : UpdateThread
    {
        private readonly ConcurrentQueue<string> events;
        private readonly Action<IFrameBasedClock>? onFrame;

        public RecordingUpdateThread(
            ConcurrentQueue<string> events,
            double targetFrequency,
            Action<IFrameBasedClock>? onFrame = null)
            : base(targetFrequency)
        {
            this.events = events;
            this.onFrame = onFrame;
        }

        protected override void OnInitialize() => events.Enqueue("Update:Start");

        protected override void OnNewFrame() => onFrame?.Invoke(Clock);

        protected override void OnExit() => events.Enqueue("Update:Stop");
    }

    private sealed class RecordingDrawThread : DrawThread
    {
        private readonly ConcurrentQueue<string> events;
        private readonly Action<IFrameBasedClock>? onFrame;
        private readonly bool throwOnInitialize;

        public RecordingDrawThread(
            ConcurrentQueue<string> events,
            double targetFrequency,
            Action<IFrameBasedClock>? onFrame = null,
            bool throwOnInitialize = false)
            : base(targetFrequency)
        {
            this.events = events;
            this.onFrame = onFrame;
            this.throwOnInitialize = throwOnInitialize;
        }

        protected override void OnInitialize()
        {
            if (throwOnInitialize)
                throw new InvalidOperationException("Draw initialization failed.");

            events.Enqueue("Draw:Start");
        }

        protected override void OnNewFrame() => onFrame?.Invoke(Clock);

        protected override void OnExit() => events.Enqueue("Draw:Stop");
    }
}
