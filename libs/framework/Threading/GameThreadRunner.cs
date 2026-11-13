namespace kajarlabs.Framework.Threading;

/// <summary>
/// Coordinates deterministic startup and shutdown ordering for the <see cref="InputThread"/>,
/// <see cref="UpdateThread"/>, and <see cref="DrawThread"/> trio.
/// </summary>
/// <remarks>
/// Startup order: <see cref="Input"/> → <see cref="Update"/> → <see cref="Draw"/>.
/// Shutdown order: <see cref="Draw"/> → <see cref="Update"/> → <see cref="Input"/>.
/// </remarks>
public sealed class GameThreadRunner : IDisposable
{
    private readonly Lock runnerLock = new();
    private readonly ManualResetEventSlim stoppedSignal = new(true);

    private int stopInProgress;
    private bool isInlineRun;
    private bool isRunning;
    private bool disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="GameThreadRunner"/> class, taking ownership of the threads.
    /// </summary>
    /// <param name="input">The input thread, or <see langword="null"/> to create a default <see cref="InputThread"/>.</param>
    /// <param name="update">The update thread, or <see langword="null"/> to create a default <see cref="UpdateThread"/>.</param>
    /// <param name="draw">The draw thread, or <see langword="null"/> to create a default <see cref="DrawThread"/>.</param>
    public GameThreadRunner(InputThread? input = null, UpdateThread? update = null, DrawThread? draw = null)
    {
        Input = input ?? new InputThread();
        Update = update ?? new UpdateThread();
        Draw = draw ?? new DrawThread();

        Input.ExceptionThrown += onThreadException;
        Update.ExceptionThrown += onThreadException;
        Draw.ExceptionThrown += onThreadException;
    }

    /// <summary>
    /// Gets the input thread.
    /// </summary>
    public InputThread Input { get; }

    /// <summary>
    /// Gets the update thread.
    /// </summary>
    public UpdateThread Update { get; }

    /// <summary>
    /// Gets the draw thread.
    /// </summary>
    public DrawThread Draw { get; }

    /// <summary>
    /// Gets a value indicating whether the runner is currently active.
    /// </summary>
    public bool IsRunning => Volatile.Read(ref isRunning);

    /// <summary>
    /// Starts all three threads on dedicated background threads in <c>Input → Update → Draw</c> order.
    /// </summary>
    public void Start()
    {
        lock (runnerLock)
        {
            ObjectDisposedException.ThrowIf(disposed, this);

            if (isRunning)
                throw new InvalidOperationException("The thread runner is already running.");

            isInlineRun = false;
            Volatile.Write(ref stopInProgress, 0);
            stoppedSignal.Reset();
            Volatile.Write(ref isRunning, true);
        }

        try
        {
            Input.Start();

            if (Volatile.Read(ref stopInProgress) == 0)
                Update.Start();

            if (Volatile.Read(ref stopInProgress) == 0)
                Draw.Start();
        }
        catch
        {
            stopBackgroundThreads();
            throw;
        }

        if (Volatile.Read(ref stopInProgress) != 0)
            stopBackgroundThreads();
    }

    /// <summary>
    /// Runs <see cref="Input"/> on the calling (main) thread while <see cref="Update"/> and <see cref="Draw"/>
    /// run on dedicated background threads, following <c>Input → Update → Draw</c> startup and
    /// <c>Draw → Update → Input</c> shutdown ordering.
    /// </summary>
    /// <param name="onStarted">An optional callback invoked on the main thread after all three threads have started.</param>
    public void Run(Action? onStarted = null)
    {
        lock (runnerLock)
        {
            ObjectDisposedException.ThrowIf(disposed, this);

            if (isRunning)
                throw new InvalidOperationException("The thread runner is already running.");

            isInlineRun = true;
            Volatile.Write(ref stopInProgress, 0);
            stoppedSignal.Reset();
            Volatile.Write(ref isRunning, true);
        }

        var inputPrepared = false;

        try
        {
            Input.PrepareInline();
            inputPrepared = true;

            Update.Start();
            Draw.Start();

            onStarted?.Invoke();
            Input.RunLoopInline();
        }
        finally
        {
            try
            {
                Draw.Stop();
                Update.Stop();

                if (inputPrepared)
                    Input.CompleteInlineExit();
            }
            finally
            {
                Volatile.Write(ref isRunning, false);
                stoppedSignal.Set();
            }
        }
    }

    /// <summary>
    /// Requests shutdown and waits for all threads to stop in <c>Draw → Update → Input</c> order.
    /// Safe to call repeatedly or from within any managed game thread without deadlocking.
    /// </summary>
    public void Stop()
    {
        bool inlineMode;

        lock (runnerLock)
        {
            if (disposed || !isRunning)
                return;

            inlineMode = isInlineRun;
        }

        if (inlineMode)
        {
            Input.RequestStop();

            if (!isOnManagedThread())
                stoppedSignal.Wait();

            return;
        }

        stopBackgroundThreads();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (disposed)
            return;

        Stop();

        lock (runnerLock)
        {
            if (disposed)
                return;

            disposed = true;
        }

        Input.ExceptionThrown -= onThreadException;
        Update.ExceptionThrown -= onThreadException;
        Draw.ExceptionThrown -= onThreadException;

        Draw.Dispose();
        Update.Dispose();
        Input.Dispose();
        stoppedSignal.Dispose();
    }

    private void stopBackgroundThreads()
    {
        if (Interlocked.CompareExchange(ref stopInProgress, 1, 0) == 0)
        {
            try
            {
                Draw.Stop();
                Update.Stop();
                Input.Stop();
            }
            finally
            {
                Volatile.Write(ref isRunning, false);
                stoppedSignal.Set();
            }
        }
        else if (!isOnManagedThread())
        {
            stoppedSignal.Wait();
        }
    }

    private bool isOnManagedThread()
        => Input.IsCurrentThread || Update.IsCurrentThread || Draw.IsCurrentThread;

    private void onThreadException(GameThread thread, Exception exception)
    {
        Stop();
    }
}
