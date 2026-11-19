using System.Numerics;
using System.Runtime.ExceptionServices;
using kajarlabs.Framework.Graphics;
using kajarlabs.Framework.Graphics.Rendering;
using kajarlabs.Framework.IO;
using kajarlabs.Framework.Logging;
using kajarlabs.Framework.Threading;
using Microsoft.Extensions.Logging;

namespace kajarlabs.Framework.Platform;

/// <summary>
/// Owns the application lifecycle, platform window, SDL initialization, user data storage,
/// logging pipeline, and game execution threads.
/// </summary>
public partial class GameHost : IDisposable
{
    private readonly IPlatformBackend backend;
    private readonly IGpuBackend? gpuBackend;
    private readonly FrameworkLoggerFactory loggerFactory;
    private readonly BackgroundLoggerProvider? backgroundLoggerProvider;
    private readonly UnhandledExceptionFlushHook? unhandledExceptionHook;
    private readonly HostInputThread inputThread;
    private readonly HostUpdateThread updateThread;
    private readonly HostDrawThread drawThread;
    private readonly Lock lifecycleLock = new();
    private readonly Lock inputQueueLock = new();
    private readonly ManualResetEventSlim runStoppedSignal = new(true);
    private readonly PlatformEventWatchCallback eventWatchCallback;

    private List<PlatformInputEvent> pendingInputEvents = [];
    private List<PlatformInputEvent> swapInputEvents = [];
    private IHostRenderer activeRenderer;
    private IDisposable? eventWatch;
    private Game? activeGame;
    private Exception? unhandledThreadException;
    private int runThreadId = -1;
    private int exitRequested;
    private int exitingNotified;
    private int inEventWatch;
    private bool isRunning;
    private bool disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="GameHost"/> class.
    /// </summary>
    /// <param name="options">Optional configuration for storage, logging, and window creation.</param>
    public GameHost(HostOptions? options = null)
        : this(options, SdlPlatformBackend.INSTANCE, SdlGpuBackend.INSTANCE)
    {
    }

    internal GameHost(HostOptions? options, IPlatformBackend backend)
        : this(options, backend, gpuBackend: null)
    {
    }

    internal GameHost(HostOptions? options, IPlatformBackend backend, IGpuBackend? gpuBackend)
    {
        ArgumentNullException.ThrowIfNull(backend);

        Options = options ?? new HostOptions();
        Options.Validate();

        this.backend = backend;
        this.gpuBackend = gpuBackend;
        eventWatchCallback = onPlatformEventWatch;
        activeRenderer = new NullRenderer(Options.WindowWidth, Options.WindowHeight);

        var userDataPath = UserDataLocation.Resolve(Options.Organization, Options.Application, Options.UserDataPath);
        Storage = new FileSystemStorage(userDataPath);

        loggerFactory = new FrameworkLoggerFactory();

        var sinks = new List<ILogSink>(2);

        if (Options.EnableConsoleLogging)
            sinks.Add(new ConsoleLogSink());

        if (Options.EnableFileLogging)
            sinks.Add(new FileLogSink(Storage, Options.MaxRetainedLogFiles));

        if (sinks.Count > 0)
        {
            backgroundLoggerProvider = new BackgroundLoggerProvider(sinks, Options.MinimumLogLevel);
            loggerFactory.AddProvider(backgroundLoggerProvider);
        }

        Logger = loggerFactory.CreateLogger<GameHost>();

        if (backgroundLoggerProvider is not null)
            unhandledExceptionHook = new UnhandledExceptionFlushHook(backgroundLoggerProvider, Logger);

        inputThread = new HostInputThread(this);
        updateThread = new HostUpdateThread(this);
        drawThread = new HostDrawThread(this);

        inputThread.ExceptionThrown += onThreadException;
        updateThread.ExceptionThrown += onThreadException;
        drawThread.ExceptionThrown += onThreadException;

        Threads = new GameThreadRunner(inputThread, updateThread, drawThread);
    }

    /// <summary>
    /// Gets the configuration options used by this host.
    /// </summary>
    public HostOptions Options { get; }

    /// <summary>
    /// Gets the root user data storage created by this host.
    /// </summary>
    public Storage Storage { get; }

    /// <summary>
    /// Gets the logger factory owned by this host.
    /// </summary>
    public ILoggerFactory LoggerFactory => loggerFactory;

    /// <summary>
    /// Gets the host logger.
    /// </summary>
    public ILogger Logger { get; }

    /// <summary>
    /// Gets the window created by this host, or <see langword="null"/> if no window is created.
    /// </summary>
    public GameWindow? Window { get; private set; }

    /// <summary>
    /// Gets the renderer owned by this host.
    /// </summary>
    public IRenderer Renderer => Volatile.Read(ref activeRenderer);

    /// <summary>
    /// Gets the thread runner coordinating the <c>Input</c>, <c>Update</c>, and <c>Draw</c> threads.
    /// </summary>
    public GameThreadRunner Threads { get; }

    /// <summary>
    /// Gets a value indicating whether the host is currently running.
    /// </summary>
    public bool IsRunning => Volatile.Read(ref isRunning);

    /// <summary>
    /// Gets a value indicating whether <see cref="Exit"/> has been requested for the current run.
    /// </summary>
    public bool IsExitRequested => Volatile.Read(ref exitRequested) != 0;

    /// <summary>
    /// Gets a value indicating whether this host creates a platform window.
    /// </summary>
    protected virtual bool ShouldCreateWindow => true;

    /// <summary>
    /// Gets a value indicating whether this host installs SDL log routing.
    /// </summary>
    protected virtual bool ShouldRouteSdlLogs => true;

    /// <summary>
    /// Gets an optional SDL video driver hint applied when initializing the video subsystem.
    /// </summary>
    protected virtual string? PreferredVideoDriver => null;

    /// <summary>
    /// Runs the host lifecycle on the calling (main) thread until <see cref="Exit"/> is called
    /// or the window is closed.
    /// </summary>
    public void Run() => runCore(null);

    /// <summary>
    /// Runs the specified <paramref name="game"/> on the calling (main) thread until <see cref="Exit"/>
    /// is called or the window is closed.
    /// </summary>
    public void Run(Game game)
    {
        ArgumentNullException.ThrowIfNull(game);
        runCore(game);
    }

    /// <summary>
    /// Requests a clean shutdown of the host. Safe to call repeatedly or from any thread.
    /// </summary>
    public void Exit()
    {
        if (!IsRunning)
            return;

        if (Interlocked.Exchange(ref exitRequested, 1) != 0)
            return;

        if (Interlocked.Exchange(ref exitingNotified, 1) == 0)
        {
            try
            {
                OnExiting();
                activeGame?.InvokeExiting();
            }
            catch (Exception ex)
            {
                logExitingHookError(Logger, ex);
            }
        }

        Threads.Stop();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Called before SDL video initialization to allow subclasses to configure platform hints.
    /// </summary>
    protected virtual void ConfigureSdlVideo()
    {
    }

    /// <summary>
    /// Called once on the main thread after all host threads have started.
    /// </summary>
    protected virtual void OnStart()
    {
    }

    /// <summary>
    /// Called once per frame on the input (main) thread after SDL events have been pumped.
    /// </summary>
    protected virtual void OnInputFrame()
    {
    }

    /// <summary>
    /// Called once on the update thread after <see cref="Game.Load"/> has executed.
    /// </summary>
    protected virtual void OnUpdateInitialize()
    {
    }

    /// <summary>
    /// Called once per frame on the update thread.
    /// </summary>
    protected virtual void OnUpdateFrame()
    {
    }

    /// <summary>
    /// Called once per frame on the draw thread.
    /// </summary>
    protected virtual void OnDrawFrame()
    {
    }

    /// <summary>
    /// Called when the host begins shutting down.
    /// </summary>
    protected virtual void OnExiting()
    {
    }

    /// <summary>
    /// Releases resources owned by this <see cref="GameHost"/>.
    /// </summary>
    protected virtual void Dispose(bool disposing)
    {
        if (!disposing)
            return;

        lock (lifecycleLock)
        {
            if (disposed)
                return;

            disposed = true;
        }

        Exit();

        if (Volatile.Read(ref runThreadId) != Environment.CurrentManagedThreadId
            && !inputThread.IsCurrentThread
            && !updateThread.IsCurrentThread
            && !drawThread.IsCurrentThread)
        {
            runStoppedSignal.Wait();
        }

        inputThread.ExceptionThrown -= onThreadException;
        updateThread.ExceptionThrown -= onThreadException;
        drawThread.ExceptionThrown -= onThreadException;

        Threads.Dispose();
        Volatile.Read(ref activeRenderer).Dispose();
        unhandledExceptionHook?.Dispose();
        loggerFactory.Dispose();
        runStoppedSignal.Dispose();
    }

    private void runCore(Game? game)
    {
        lock (lifecycleLock)
        {
            ObjectDisposedException.ThrowIf(disposed, this);

            if (isRunning)
                throw new InvalidOperationException("The game host is already running.");

            game?.Attach(this);
            activeGame = game;
            Volatile.Write(ref exitRequested, 0);
            Volatile.Write(ref exitingNotified, 0);
            Volatile.Write(ref unhandledThreadException, null);

            Window = ShouldCreateWindow
                ? new GameWindow(
                    backend,
                    inputThread.Scheduler,
                    Options.ResolveWindowTitle(),
                    Options.WindowWidth,
                    Options.WindowHeight)
                : null;

            Volatile.Write(ref runThreadId, Environment.CurrentManagedThreadId);
            runStoppedSignal.Reset();
            Volatile.Write(ref isRunning, true);
        }

        IDisposable? logRouter = null;
        IDisposable? videoSession = null;
        Exception? inlineException = null;

        try
        {
            if (ShouldRouteSdlLogs)
                logRouter = backend.InstallLogRouter(loggerFactory);

            logHostStarting(Logger, Options.Organization, Options.Application);

            ConfigureSdlVideo();
            videoSession = backend.InitializeVideo(PreferredVideoDriver);

            var isDummyVideo = backend.IsDummyVideoDriver();

            if (ShouldCreateWindow && gpuBackend is not null && isDummyVideo)
                logDummyVideoFallback(Logger);

            IHostRenderer runRenderer = ShouldCreateWindow && gpuBackend is not null && !isDummyVideo
                ? new SdlGpuRenderer(gpuBackend, Logger, Options.WindowWidth, Options.WindowHeight)
                : new NullRenderer(Options.WindowWidth, Options.WindowHeight);

            Volatile.Write(ref activeRenderer, runRenderer);

            Threads.Run(onStarted: OnStart);
        }
        catch (Exception ex)
        {
            inlineException = ex;
            logUnhandledHostException(Logger, ex);
            throw;
        }
        finally
        {
            unregisterEventWatch();

            if (Window is { } window)
                window.Resized -= onWindowResized;

            try
            {
                Volatile.Read(ref activeRenderer).Shutdown();
            }
            catch (Exception ex)
            {
                logRendererCleanupError(Logger, ex);
            }

            try
            {
                Window?.Destroy();
            }
            catch (Exception ex)
            {
                logWindowCleanupError(Logger, ex);
            }

            videoSession?.Dispose();
            logHostStopped(Logger);
            logRouter?.Dispose();
            backgroundLoggerProvider?.Flush();

            lock (lifecycleLock)
            {
                activeGame = null;
                Volatile.Write(ref runThreadId, -1);
                Volatile.Write(ref isRunning, false);
                runStoppedSignal.Set();
            }

            var workerException = Volatile.Read(ref unhandledThreadException);

            if (inlineException is null && workerException is not null)
                ExceptionDispatchInfo.Capture(workerException).Throw();
        }
    }

    internal void ForwardResize(int width, int height)
        => Volatile.Read(ref activeRenderer).Resize(width, height);

    internal void EnqueueInputEvent(in PlatformInputEvent inputEvent)
    {
        lock (inputQueueLock)
            pendingInputEvents.Add(inputEvent);
    }

    internal int DrainInputEvents(List<PlatformInputEvent> destination)
    {
        ArgumentNullException.ThrowIfNull(destination);

        List<PlatformInputEvent> batch;

        lock (inputQueueLock)
        {
            if (pendingInputEvents.Count == 0)
                return 0;

            batch = pendingInputEvents;
            pendingInputEvents = swapInputEvents;
            swapInputEvents = batch;
        }

        destination.AddRange(batch);
        var count = batch.Count;
        batch.Clear();
        return count;
    }

    private void discardPendingInputEvents()
    {
        lock (inputQueueLock)
            pendingInputEvents.Clear();
    }

    private void onWindowResized(int width, int height)
        => Volatile.Read(ref activeRenderer).Resize(width, height);

    private void registerEventWatch()
    {
        var registration = backend.AddEventWatch(eventWatchCallback);
        Interlocked.Exchange(ref eventWatch, registration)?.Dispose();
    }

    private void unregisterEventWatch()
        => Interlocked.Exchange(ref eventWatch, null)?.Dispose();

    private void onPlatformEventWatch(in PlatformEvent platformEvent)
    {
        if (IsExitRequested || Volatile.Read(ref disposed) || !inputThread.IsCurrentThread)
            return;

        if (Window is not { } window || platformEvent.WindowId != window.Id)
            return;

        if (Interlocked.CompareExchange(ref inEventWatch, 1, 0) != 0)
            return;

        try
        {
            switch (platformEvent.Type)
            {
                case PlatformEventType.WindowPixelSizeChanged:
                    Volatile.Read(ref activeRenderer).Resize(platformEvent.Data1, platformEvent.Data2);
                    break;

                case PlatformEventType.WindowExposed:
                    Volatile.Read(ref activeRenderer).Present();
                    break;
            }
        }
        catch (Exception ex)
        {
            // Never allow managed exceptions to unwind across native SDL/OS modal loop frames.
            onThreadException(inputThread, ex);
            Exit();
        }
        finally
        {
            Volatile.Write(ref inEventWatch, 0);
        }
    }

    private void pumpEvents()
    {
        while (backend.PollEvent(out var platformEvent))
        {
            switch (platformEvent.Type)
            {
                case PlatformEventType.Quit:
                    Exit();
                    break;

                case PlatformEventType.WindowCloseRequested:
                    Window?.ProcessEvent(in platformEvent);
                    Exit();
                    break;

                case PlatformEventType.WindowPixelSizeChanged:
                    if (Window is { } pixelWindow && platformEvent.WindowId == pixelWindow.Id)
                    {
                        pixelWindow.ProcessEvent(in platformEvent);
                        Volatile.Read(ref activeRenderer).Resize(platformEvent.Data1, platformEvent.Data2);
                    }
                    break;

                case PlatformEventType.WindowMinimized:
                    if (Window is { } minimizedWindow && platformEvent.WindowId == minimizedWindow.Id)
                        Volatile.Read(ref activeRenderer).Resize(0, 0);
                    break;

                case PlatformEventType.WindowRestored:
                    if (Window is { } restoredWindow && platformEvent.WindowId == restoredWindow.Id)
                        Volatile.Read(ref activeRenderer).RefreshSurface();
                    break;

                case PlatformEventType.WindowFocusGained:
                case PlatformEventType.WindowFocusLost:
                case PlatformEventType.WindowMouseEnter:
                case PlatformEventType.WindowMouseLeave:
                    pumpWindowInputStateEvent(in platformEvent);
                    break;

                case PlatformEventType.MouseMotion:
                case PlatformEventType.MouseButtonDown:
                case PlatformEventType.MouseButtonUp:
                case PlatformEventType.MouseWheel:
                    pumpPointerEvent(in platformEvent);
                    break;

                case PlatformEventType.KeyDown:
                case PlatformEventType.KeyUp:
                    if (isTargetedAtActiveWindow(platformEvent.WindowId))
                    {
                        var keyInput = platformEvent.Input;
                        EnqueueInputEvent(in keyInput);
                    }
                    break;

                case PlatformEventType.TextInput:
                    if (isTargetedAtActiveWindow(platformEvent.WindowId)
                        && !string.IsNullOrEmpty(platformEvent.Input.Text))
                    {
                        var textInput = platformEvent.Input;
                        EnqueueInputEvent(in textInput);
                    }
                    break;

                default:
                    Window?.ProcessEvent(in platformEvent);
                    break;
            }
        }
    }

    private bool isTargetedAtActiveWindow(uint windowId)
        => Window is not { } window || windowId == window.Id;

    private void pumpWindowInputStateEvent(in PlatformEvent platformEvent)
    {
        if (Window is { } window)
        {
            if (platformEvent.WindowId != window.Id)
                return;

            window.ProcessEvent(in platformEvent);
        }

        var inputEvent = platformEvent.Type switch
        {
            PlatformEventType.WindowFocusGained => PlatformInputEvent.WindowFocus(true),
            PlatformEventType.WindowFocusLost => PlatformInputEvent.WindowFocus(false),
            PlatformEventType.WindowMouseEnter => PlatformInputEvent.WindowMouseEnter(),
            PlatformEventType.WindowMouseLeave => PlatformInputEvent.WindowMouseLeave(),
            _ => platformEvent.Input,
        };

        EnqueueInputEvent(in inputEvent);
    }

    private void pumpPointerEvent(in PlatformEvent platformEvent)
    {
        var scale = Vector2.One;

        if (Window is { } window)
        {
            if (platformEvent.WindowId != window.Id)
                return;

            scale = window.GetViewportScale();
        }

        var scaledEvent = platformEvent.Input.WithScaledPointer(scale);
        EnqueueInputEvent(in scaledEvent);
    }

    private void onThreadException(GameThread thread, Exception exception)
    {
        Interlocked.CompareExchange(ref unhandledThreadException, exception, null);
        logUnhandledThreadException(Logger, thread.Name, exception);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Starting GameHost for {Organization}/{Application}")]
    private static partial void logHostStarting(ILogger logger, string organization, string application);

    [LoggerMessage(Level = LogLevel.Information, Message = "GameHost stopped")]
    private static partial void logHostStopped(ILogger logger);

    [LoggerMessage(Level = LogLevel.Critical, Message = "Unhandled exception in GameHost")]
    private static partial void logUnhandledHostException(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Critical, Message = "Unhandled exception on thread {ThreadName}")]
    private static partial void logUnhandledThreadException(ILogger logger, string threadName, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error in exiting hook")]
    private static partial void logExitingHookError(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error destroying window during cleanup")]
    private static partial void logWindowCleanupError(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error shutting down renderer during cleanup")]
    private static partial void logRendererCleanupError(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Using NullRenderer because SDL video driver is 'dummy'")]
    private static partial void logDummyVideoFallback(ILogger logger);

    private sealed class HostInputThread : InputThread
    {
        private readonly GameHost host;

        public HostInputThread(GameHost host)
        {
            this.host = host;
        }

        protected override void OnInitialize()
        {
            if (host.Window is { } window)
            {
                window.Create(host.Options.Resizable);
                window.Resized += host.onWindowResized;
                Volatile.Read(ref host.activeRenderer).Initialize(window.Handle);
                host.registerEventWatch();
            }
        }

        protected override void OnNewFrame()
        {
            host.pumpEvents();
            Volatile.Read(ref host.activeRenderer).Present();
            host.OnInputFrame();
        }

        protected override void OnExit()
        {
            host.unregisterEventWatch();

            if (host.Window is { } window)
                window.Resized -= host.onWindowResized;

            Volatile.Read(ref host.activeRenderer).Shutdown();
            host.Window?.Destroy();
        }
    }

    private sealed class HostUpdateThread : UpdateThread
    {
        private readonly GameHost host;

        public HostUpdateThread(GameHost host)
        {
            this.host = host;
        }

        protected override void OnInitialize()
        {
            host.activeGame?.InvokeLoad();
            host.OnUpdateInitialize();
        }

        protected override void OnNewFrame()
        {
            if (host.IsExitRequested)
                return;

            if (host.activeGame is { } game)
                game.InvokeUpdate();
            else
                host.discardPendingInputEvents();

            host.OnUpdateFrame();
        }
    }

    private sealed class HostDrawThread : DrawThread
    {
        private readonly GameHost host;

        public HostDrawThread(GameHost host)
        {
            this.host = host;
        }

        protected override void OnNewFrame()
        {
            if (host.IsExitRequested)
                return;

            var renderer = Volatile.Read(ref host.activeRenderer);
            renderer.BeginFrame();
            var faulted = false;

            try
            {
                host.activeGame?.InvokeDraw();
                host.OnDrawFrame();
            }
            catch
            {
                faulted = true;
                throw;
            }
            finally
            {
                renderer.EndFrame(faulted);
            }
        }
    }
}
