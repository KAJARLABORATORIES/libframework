using System.Numerics;
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
    private int pixelWidth;
    private int pixelHeight;
    private bool hasFocus;
    private bool isMouseInside;
    private bool textInputRequested;
    private bool isTextInputActive;

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
        pixelWidth = width;
        pixelHeight = height;
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
    /// Gets the current width of the window client area in physical pixels.
    /// </summary>
    public int PixelWidth => Volatile.Read(ref pixelWidth);

    /// <summary>
    /// Gets the current height of the window client area in physical pixels.
    /// </summary>
    public int PixelHeight => Volatile.Read(ref pixelHeight);

    /// <summary>
    /// Gets a value indicating whether the native window handle is currently created.
    /// </summary>
    public bool IsCreated => Volatile.Read(ref handle) != nint.Zero;

    /// <summary>
    /// Gets a value indicating whether the window currently has keyboard input focus.
    /// </summary>
    public bool HasFocus => Volatile.Read(ref hasFocus);

    /// <summary>
    /// Gets a value indicating whether SDL text input is currently active for this window.
    /// </summary>
    public bool IsTextInputActive => Volatile.Read(ref isTextInputActive);

    /// <summary>
    /// Occurs when the operating system or user requests that the window be closed.
    /// </summary>
    public event Action? CloseRequested;

    /// <summary>
    /// Occurs when the window is resized in logical screen coordinates.
    /// </summary>
    public event Action<int, int>? Resized;

    /// <summary>
    /// Occurs when the window client area size changes in physical pixels.
    /// </summary>
    public event Action<int, int>? PixelSizeChanged;

    /// <summary>
    /// Occurs when the window gains or loses keyboard input focus.
    /// </summary>
    public event Action<bool>? FocusChanged;

    internal nint Handle => Volatile.Read(ref handle);

    internal uint Id { get; private set; }

    internal bool IsMouseInside => Volatile.Read(ref isMouseInside);

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

    /// <summary>
    /// Enables text input events for this window. Safe to call from any thread.
    /// </summary>
    public void StartTextInput() => setTextInputRequested(true);

    /// <summary>
    /// Disables text input events for this window. Safe to call from any thread.
    /// </summary>
    public void StopTextInput() => setTextInputRequested(false);

    internal Vector2 GetViewportScale()
    {
        var logicalWidth = Volatile.Read(ref width);
        var logicalHeight = Volatile.Read(ref height);
        var physicalWidth = Volatile.Read(ref pixelWidth);
        var physicalHeight = Volatile.Read(ref pixelHeight);

        var scaleX = logicalWidth > 0 && physicalWidth > 0 ? physicalWidth / (float)logicalWidth : 1f;
        var scaleY = logicalHeight > 0 && physicalHeight > 0 ? physicalHeight / (float)logicalHeight : 1f;
        return new Vector2(scaleX, scaleY);
    }

    internal Vector2 ScaleToViewport(Vector2 logicalValue) => logicalValue * GetViewportScale();

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
        var hasPixelSize = backend.GetWindowPixelSize(createdHandle, out var actualPixelWidth, out var actualPixelHeight);
        bool startTextOnCreate;

        lock (stateLock)
        {
            Volatile.Write(ref handle, createdHandle);
            Id = createdId;

            if (actualWidth > 0)
                Volatile.Write(ref width, actualWidth);

            if (actualHeight > 0)
                Volatile.Write(ref height, actualHeight);

            var resolvedWidth = Volatile.Read(ref width);
            var resolvedHeight = Volatile.Read(ref height);

            Volatile.Write(ref pixelWidth, hasPixelSize && actualPixelWidth > 0 ? actualPixelWidth : resolvedWidth);
            Volatile.Write(ref pixelHeight, hasPixelSize && actualPixelHeight > 0 ? actualPixelHeight : resolvedHeight);

            startTextOnCreate = textInputRequested;

            if (startTextOnCreate)
                Volatile.Write(ref isTextInputActive, true);
        }

        if (startTextOnCreate)
            backend.StartTextInput(createdHandle);
    }

    internal void Destroy()
    {
        nint handleToDestroy;
        bool shouldStopTextInput;

        lock (stateLock)
        {
            handleToDestroy = handle;
            shouldStopTextInput = textInputRequested || isTextInputActive;
            textInputRequested = false;
            Volatile.Write(ref isTextInputActive, false);
            Volatile.Write(ref hasFocus, false);
            Volatile.Write(ref isMouseInside, false);
            Volatile.Write(ref handle, nint.Zero);
            Id = 0;
        }

        if (handleToDestroy != nint.Zero)
        {
            if (shouldStopTextInput)
                backend.StopTextInput(handleToDestroy);

            backend.DestroyWindow(handleToDestroy);
        }
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

            case PlatformEventType.WindowPixelSizeChanged:
                if (platformEvent.Data1 > 0 && platformEvent.Data2 > 0)
                {
                    Volatile.Write(ref pixelWidth, platformEvent.Data1);
                    Volatile.Write(ref pixelHeight, platformEvent.Data2);
                    PixelSizeChanged?.Invoke(platformEvent.Data1, platformEvent.Data2);
                }
                break;

            case PlatformEventType.WindowFocusGained:
                Volatile.Write(ref hasFocus, true);
                FocusChanged?.Invoke(true);
                break;

            case PlatformEventType.WindowFocusLost:
                Volatile.Write(ref hasFocus, false);
                FocusChanged?.Invoke(false);
                break;

            case PlatformEventType.WindowMouseEnter:
                Volatile.Write(ref isMouseInside, true);
                break;

            case PlatformEventType.WindowMouseLeave:
                Volatile.Write(ref isMouseInside, false);
                break;
        }
    }

    private void setTextInputRequested(bool requested)
    {
        nint currentHandle;

        lock (stateLock)
        {
            textInputRequested = requested;
            currentHandle = handle;
        }

        if (currentHandle != nint.Zero)
        {
            if (inputScheduler.IsCurrentThread)
                syncTextInputIfCreated();
            else
                inputScheduler.Add(syncTextInputIfCreated);
        }
    }

    private void syncTextInputIfCreated()
    {
        nint currentHandle;
        bool desired;
        bool current;

        lock (stateLock)
        {
            currentHandle = handle;

            if (currentHandle == nint.Zero)
                return;

            desired = textInputRequested;
            current = isTextInputActive;

            if (desired == current)
                return;

            Volatile.Write(ref isTextInputActive, desired);
        }

        if (desired)
            backend.StartTextInput(currentHandle);
        else
            backend.StopTextInput(currentHandle);
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
