using System.Runtime.ExceptionServices;
using kajarlabs.Framework.Timing;

namespace kajarlabs.Framework.Threading;

/// <summary>
/// Base class for a game execution thread owning its own <see cref="IFrameBasedClock"/>,
/// <see cref="Scheduler"/>, and target frame frequency.
/// </summary>
public abstract class GameThread : IDisposable
{
    private readonly TimeProvider timeProvider;
    private readonly Lock lifecycleLock = new();
    private readonly ManualResetEventSlim startedSignal = new(false);
    private readonly ManualResetEventSlim stopSignal = new(false);
    private readonly ManualResetEventSlim exitedSignal = new(true);
    private readonly Action<IFrameBasedClock>? onNewFrame;

    private Thread? managedThread;
    private int state = (int)GameThreadState.Stopped;
    private long frameCount;
    private double targetFrequency;
    private Exception? startupException;
    private Exception? unhandledException;
    private bool isInline;
    private bool disposed;
    private long loopStartTimestamp;
    private double nextFrameTimeMs;

    /// <summary>
    /// Initializes a new instance of the <see cref="GameThread"/> class.
    /// </summary>
    /// <param name="name">The display and thread name.</param>
    /// <param name="targetFrequency">The target update frequency in Hz, or <c>0</c> for uncapped execution.</param>
    /// <param name="onNewFrame">An optional callback invoked once per frame on the owning thread.</param>
    /// <param name="sourceClock">An optional underlying clock source, or <see langword="null"/> to use a running <see cref="StopwatchClock"/>.</param>
    protected GameThread(
        string name,
        double targetFrequency = 0,
        Action<IFrameBasedClock>? onNewFrame = null,
        IClock? sourceClock = null)
        : this(name, targetFrequency, onNewFrame, sourceClock, TimeProvider.System)
    {
    }

    internal GameThread(
        string name,
        double targetFrequency,
        Action<IFrameBasedClock>? onNewFrame,
        IClock? sourceClock,
        TimeProvider timeProvider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(timeProvider);
        validateFrequency(targetFrequency);

        Name = name;
        this.targetFrequency = targetFrequency;
        this.onNewFrame = onNewFrame;
        this.timeProvider = timeProvider;
        Clock = new FramedClock(sourceClock ?? new StopwatchClock(start: true));
        Scheduler = new Scheduler(Clock);
    }

    /// <summary>
    /// Gets the name of this thread.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the frame-based clock owned by this thread.
    /// </summary>
    public IFrameBasedClock Clock { get; }

    /// <summary>
    /// Gets the thread-affine scheduler bound to this thread.
    /// </summary>
    public Scheduler Scheduler { get; }

    /// <summary>
    /// Gets the current lifecycle state of this thread.
    /// </summary>
    public GameThreadState State => (GameThreadState)Volatile.Read(ref state);

    /// <summary>
    /// Gets the total number of frames processed by this thread across its runs.
    /// </summary>
    public long FrameCount => Volatile.Read(ref frameCount);

    /// <summary>
    /// Gets a value indicating whether the calling thread is the owning thread.
    /// </summary>
    public bool IsCurrentThread => Scheduler.IsCurrentThread;

    /// <summary>
    /// Gets the unhandled exception that terminated the most recent run, if any.
    /// </summary>
    public Exception? UnhandledException => Volatile.Read(ref unhandledException);

    /// <summary>
    /// Gets or sets the target frame rate in Hz. Set to <c>0</c> to run without throttling.
    /// </summary>
    public double TargetFrequency
    {
        get => Volatile.Read(ref targetFrequency);
        set
        {
            validateFrequency(value);
            Volatile.Write(ref targetFrequency, value);
        }
    }

    /// <summary>
    /// Occurs when an unhandled exception escapes the thread's frame loop.
    /// </summary>
    public event Action<GameThread, Exception>? ExceptionThrown;

    internal Thread? ManagedThread => Volatile.Read(ref managedThread);

    /// <summary>
    /// Starts this game thread on a new dedicated background OS thread and waits until it is initialized.
    /// </summary>
    public void Start()
    {
        Thread worker;

        lock (lifecycleLock)
        {
            ObjectDisposedException.ThrowIf(disposed, this);

            var current = State;
            if (current != GameThreadState.Stopped)
                throw new InvalidOperationException($"Cannot start game thread '{Name}' from state {current}.");

            startupException = null;
            unhandledException = null;
            isInline = false;
            startedSignal.Reset();
            stopSignal.Reset();
            exitedSignal.Reset();
            Volatile.Write(ref state, (int)GameThreadState.Starting);

            worker = new Thread(runDedicatedThread)
            {
                Name = Name,
                IsBackground = true,
            };

            Volatile.Write(ref managedThread, worker);
            worker.Start();
        }

        startedSignal.Wait();

        var initFailure = Volatile.Read(ref startupException);
        if (initFailure is not null)
        {
            worker.Join();
            Volatile.Write(ref managedThread, null);
            ExceptionDispatchInfo.Capture(initFailure).Throw();
        }
    }

    /// <summary>
    /// Runs this game thread inline on the calling thread until <see cref="RequestStop"/> or <see cref="Stop"/> is called.
    /// </summary>
    public void Run()
    {
        PrepareInline();

        try
        {
            RunLoopInline();
        }
        finally
        {
            CompleteInlineExit();
        }
    }

    /// <summary>
    /// Signals the thread to stop without blocking for completion.
    /// </summary>
    public void RequestStop()
    {
        lock (lifecycleLock)
        {
            if (disposed)
                return;

            var current = State;
            if (current is GameThreadState.Stopped or GameThreadState.Stopping)
            {
                stopSignal.Set();
                return;
            }

            Volatile.Write(ref state, (int)GameThreadState.Stopping);
            stopSignal.Set();
        }
    }

    /// <summary>
    /// Requests the thread to stop and waits until it has completely exited.
    /// </summary>
    public void Stop()
    {
        Thread? threadToJoin;
        bool inlineMode;

        lock (lifecycleLock)
        {
            if (disposed || State == GameThreadState.Stopped)
                return;

            if (State != GameThreadState.Stopping)
                Volatile.Write(ref state, (int)GameThreadState.Stopping);

            stopSignal.Set();
            threadToJoin = Volatile.Read(ref managedThread);
            inlineMode = isInline;
        }

        if (inlineMode)
        {
            if (!IsCurrentThread)
                exitedSignal.Wait();

            return;
        }

        if (threadToJoin is not null && !IsCurrentThread)
        {
            threadToJoin.Join();
            Volatile.Write(ref managedThread, null);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    internal void PrepareInline()
    {
        lock (lifecycleLock)
        {
            ObjectDisposedException.ThrowIf(disposed, this);

            var current = State;
            if (current != GameThreadState.Stopped)
                throw new InvalidOperationException($"Cannot start game thread '{Name}' inline from state {current}.");

            startupException = null;
            unhandledException = null;
            isInline = true;
            startedSignal.Reset();
            stopSignal.Reset();
            exitedSignal.Reset();
            Volatile.Write(ref managedThread, Thread.CurrentThread);
            Volatile.Write(ref state, (int)GameThreadState.Starting);
        }

        try
        {
            initializeOnCurrentThread();
        }
        catch (Exception ex)
        {
            handleUnhandledException(ex, notifyListeners: false);
            cleanupOnCurrentThread(runExitHook: false);
            throw;
        }
    }

    internal void RunLoopInline()
    {
        try
        {
            runFrameLoop();
        }
        catch (Exception ex)
        {
            handleUnhandledException(ex, notifyListeners: true);
            throw;
        }
    }

    internal void CompleteInlineExit()
    {
        cleanupOnCurrentThread(runExitHook: true);
    }

    internal void ProcessSingleFrame()
    {
        performFrame();
    }

    /// <summary>
    /// Called once on the owning thread before the frame loop begins.
    /// </summary>
    protected virtual void OnInitialize()
    {
    }

    /// <summary>
    /// Called once per frame on the owning thread after <see cref="Clock"/> and <see cref="Scheduler"/> update.
    /// </summary>
    protected virtual void OnNewFrame()
    {
    }

    /// <summary>
    /// Called once on the owning thread when the frame loop exits.
    /// </summary>
    protected virtual void OnExit()
    {
    }

    /// <summary>
    /// Releases resources used by this <see cref="GameThread"/>.
    /// </summary>
    protected virtual void Dispose(bool disposing)
    {
        if (!disposing)
            return;

        Stop();

        lock (lifecycleLock)
        {
            if (disposed)
                return;

            disposed = true;
            startedSignal.Dispose();
            stopSignal.Dispose();
            exitedSignal.Dispose();
        }
    }

    private static void validateFrequency(double frequency)
    {
        if (!double.IsFinite(frequency) || frequency < 0)
            throw new ArgumentOutOfRangeException(nameof(frequency), frequency, "Target frequency must be a finite non-negative number.");
    }

    private void runDedicatedThread()
    {
        var initialized = false;

        try
        {
            initializeOnCurrentThread();
            initialized = true;
            runFrameLoop();
        }
        catch (Exception ex)
        {
            if (!initialized)
                Volatile.Write(ref startupException, ex);

            handleUnhandledException(ex, notifyListeners: initialized);
        }
        finally
        {
            cleanupOnCurrentThread(runExitHook: initialized);
        }
    }

    private void initializeOnCurrentThread()
    {
        Scheduler.SetCurrentThread();
        loopStartTimestamp = timeProvider.GetTimestamp();
        nextFrameTimeMs = 0;

        OnInitialize();

        lock (lifecycleLock)
        {
            if (State == GameThreadState.Starting && !stopSignal.IsSet)
                Volatile.Write(ref state, (int)GameThreadState.Running);
        }

        startedSignal.Set();
    }

    private void runFrameLoop()
    {
        while (!stopSignal.IsSet)
        {
            var frameStartMs = getElapsedMilliseconds(loopStartTimestamp, timeProvider.GetTimestamp());

            performFrame();

            if (stopSignal.IsSet)
                break;

            throttleFrame(frameStartMs);
        }
    }

    private void performFrame()
    {
        Clock.ProcessFrame();
        Scheduler.Update();
        OnNewFrame();
        onNewFrame?.Invoke(Clock);
        Interlocked.Increment(ref frameCount);
    }

    private void throttleFrame(double frameStartMs)
    {
        var freq = TargetFrequency;
        if (freq <= 0)
            return;

        var frameDurationMs = 1000.0 / freq;
        nextFrameTimeMs = Math.Min(nextFrameTimeMs + frameDurationMs, frameStartMs + frameDurationMs);

        if (nextFrameTimeMs < frameStartMs)
            nextFrameTimeMs = frameStartMs;

        while (!stopSignal.IsSet)
        {
            var nowMs = getElapsedMilliseconds(loopStartTimestamp, timeProvider.GetTimestamp());
            var remainingMs = nextFrameTimeMs - nowMs;

            if (remainingMs <= 0)
                break;

            if (remainingMs >= 2.0)
            {
                var waitMs = (int)Math.Floor(remainingMs - 1.0);
                if (waitMs > 0)
                    stopSignal.Wait(waitMs);
            }
            else
            {
                Thread.Yield();
            }
        }
    }

    private double getElapsedMilliseconds(long startTimestamp, long endTimestamp)
        => (endTimestamp - startTimestamp) * (1000.0 / timeProvider.TimestampFrequency);

    private void handleUnhandledException(Exception exception, bool notifyListeners)
    {
        Volatile.Write(ref unhandledException, exception);
        RequestStop();

        if (notifyListeners)
            ExceptionThrown?.Invoke(this, exception);
    }

    private void cleanupOnCurrentThread(bool runExitHook)
    {
        try
        {
            if (runExitHook)
                OnExit();
        }
        finally
        {
            lock (lifecycleLock)
            {
                if (isInline)
                    Volatile.Write(ref managedThread, null);

                Volatile.Write(ref state, (int)GameThreadState.Stopped);
                startedSignal.Set();
                exitedSignal.Set();
            }
        }
    }
}
