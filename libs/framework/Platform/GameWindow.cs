using kajarlabs.Framework.Threading;

namespace kajarlabs.Framework.Platform;

/// <summary>
/// Represents the platform window owned by a <see cref="GameHost"/>.
/// </summary>
public sealed class GameWindow
{
    private readonly IPlatformBackend backend;
    private readonly Scheduler inputScheduler;
    private readonly Lock stateLock = new();

    private nint handle;
    private string title;
    private int width;
    private int height;

    internal GameWindow(
        IPlatformBackend backend,
        Scheduler inputScheduler,
        string title,
        int width,
        int height)
    {
        ArgumentNullException.ThrowIfNull(backend);
        ArgumentNullException.ThrowIfNull(inputScheduler);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(width, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(height, 0);

        this.backend = backend;
        this.inputScheduler = inputScheduler;
        this.title = title;
        this.width = width;
        this.height = height;
    }

    /// <summary>
    /// Gets or sets the window title.
    /// </summary>
    public string Title
    {
        get
        {
            lock (stateLock)
                return title;
        }
        set
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);

            nint currentHandle;

            lock (stateLock)
            {
                title = value;
                currentHandle = handle;
            }

            if (currentHandle != nint.Zero)
            {
                if (inputScheduler.IsCurrentThread)
                    applyTitleIfCreated(value);
                else
                    inputScheduler.Add(() => applyTitleIfCreated(value));
            }
        }
    }

    /// <summary>
    /// Gets the current width of the window in screen coordinates.
    /// </summary>
    public int Width => Volatile.Read(ref width);

    /// <summary>
    /// Gets the current height of the window in screen coordinates.
    /// </summary>
    public int Height => Volatile.Read(ref height);

    /// <summary>
    /// Gets a value indicating whether the native window handle is currently created.
    /// </summary>
    public bool IsCreated => Volatile.Read(ref handle) != nint.Zero;

    /// <summary>
    /// Occurs when the operating system or user requests that the window be closed.
    /// </summary>
    public event Action? CloseRequested;

    /// <summary>
    /// Occurs when the window is resized.
    /// </summary>
    public event Action<int, int>? Resized;

    internal nint Handle => Volatile.Read(ref handle);

    internal uint Id { get; private set; }

    /// <summary>
    /// Requests a new window size in screen coordinates.
    /// </summary>
    public void SetSize(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(width, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(height, 0);

        nint currentHandle;

        lock (stateLock)
        {
            Volatile.Write(ref this.width, width);
            Volatile.Write(ref this.height, height);
            currentHandle = handle;
        }

        if (currentHandle != nint.Zero)
        {
            if (inputScheduler.IsCurrentThread)
                applySizeIfCreated(width, height);
            else
                inputScheduler.Add(() => applySizeIfCreated(width, height));
        }
    }

    internal void Create(bool resizable)
    {
        string initialTitle;
        int initialWidth;
        int initialHeight;

        lock (stateLock)
        {
            if (handle != nint.Zero)
                throw new InvalidOperationException("Window has already been created.");

            initialTitle = title;
            initialWidth = width;
            initialHeight = height;
        }

        var createdHandle = backend.CreateWindow(initialTitle, initialWidth, initialHeight, resizable);
        var createdId = backend.GetWindowId(createdHandle);

        backend.GetWindowSize(createdHandle, out var actualWidth, out var actualHeight);

        lock (stateLock)
        {
            Volatile.Write(ref handle, createdHandle);
            Id = createdId;

            if (actualWidth > 0)
                Volatile.Write(ref width, actualWidth);

            if (actualHeight > 0)
                Volatile.Write(ref height, actualHeight);
        }
    }

    internal void Destroy()
    {
        nint handleToDestroy;

        lock (stateLock)
        {
            handleToDestroy = handle;
            Volatile.Write(ref handle, nint.Zero);
            Id = 0;
        }

        if (handleToDestroy != nint.Zero)
            backend.DestroyWindow(handleToDestroy);
    }

    internal void ProcessEvent(in PlatformEvent platformEvent)
    {
        if (platformEvent.WindowId != Id)
            return;

        switch (platformEvent.Type)
        {
            case PlatformEventType.WindowCloseRequested:
                CloseRequested?.Invoke();
                break;

            case PlatformEventType.WindowResized:
                if (platformEvent.Data1 > 0 && platformEvent.Data2 > 0)
                {
                    Volatile.Write(ref width, platformEvent.Data1);
                    Volatile.Write(ref height, platformEvent.Data2);
                    Resized?.Invoke(platformEvent.Data1, platformEvent.Data2);
                }
                break;
        }
    }

    private void applyTitleIfCreated(string newTitle)
    {
        nint currentHandle;

        lock (stateLock)
            currentHandle = handle;

        if (currentHandle != nint.Zero)
            backend.SetWindowTitle(currentHandle, newTitle);
    }

    private void applySizeIfCreated(int newWidth, int newHeight)
    {
        nint currentHandle;

        lock (stateLock)
            currentHandle = handle;

        if (currentHandle != nint.Zero)
            backend.SetWindowSize(currentHandle, newWidth, newHeight);
    }
}
