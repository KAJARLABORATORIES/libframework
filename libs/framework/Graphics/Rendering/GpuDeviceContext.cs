using Microsoft.Extensions.Logging;

namespace kajarlabs.Framework.Graphics.Rendering;

/// <summary>
/// Manages the lifetime of the <c>SDL_GPUDevice</c>, window swapchain claim/presentation
/// on <c>InputThread</c>, and framework-owned offscreen color targets rendered on <c>DrawThread</c>.
/// </summary>
internal sealed partial class GpuDeviceContext : IDisposable
{
    public const GpuShaderFormats SUPPORTED_SHADER_FORMAT_MASK =
        GpuShaderFormats.SpirV | GpuShaderFormats.Dxil | GpuShaderFormats.Msl;

    private const int slot_count = 3;

    private readonly IGpuBackend backend;
    private readonly ILogger logger;
    private readonly Lock stateLock = new();
    private readonly OffscreenSlot[] slots = new OffscreenSlot[slot_count];

    private nint device;
    private nint windowHandle;
    private GpuDeviceLifecycleState lifecycleState = GpuDeviceLifecycleState.Uninitialized;
    private int targetWidth;
    private int targetHeight;
    private bool isMinimized;
    private bool loggedInactiveSurface;
    private int shutdownStarted;

    public GpuDeviceContext(IGpuBackend backend, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(backend);
        ArgumentNullException.ThrowIfNull(logger);

        this.backend = backend;
        this.logger = logger;
    }

    public GpuDeviceLifecycleState LifecycleState
    {
        get
        {
            lock (stateLock)
                return lifecycleState;
        }
    }

    public nint DeviceHandle
    {
        get
        {
            lock (stateLock)
                return device;
        }
    }

    public string? DeviceDriver { get; private set; }

    public GpuShaderFormats SupportedShaderFormats { get; private set; } = GpuShaderFormats.None;

    public GpuTextureFormat SwapchainFormat { get; private set; } = GpuTextureFormat.INVALID;

    public GpuTextureFormat OffscreenColorFormat { get; private set; } = GpuTextureFormat.INVALID;

    public GpuPresentMode ActivePresentMode { get; private set; } = GpuPresentMode.VSync;

    public (int Width, int Height) SurfaceSize
    {
        get
        {
            lock (stateLock)
                return (targetWidth, targetHeight);
        }
    }

    /// <summary>
    /// Creates the GPU device, claims the window handle, and configures swapchain parameters.
    /// Must be called on the window-creating thread (<c>InputThread</c>).
    /// </summary>
    public void Initialize(nint windowHandle, bool debugMode = false, string? preferredDriver = null)
    {
        if (windowHandle == nint.Zero)
            throw new ArgumentException("Window handle must not be zero.", nameof(windowHandle));

        lock (stateLock)
        {
            if (lifecycleState != GpuDeviceLifecycleState.Uninitialized)
                throw new InvalidOperationException($"Cannot initialize GPU device from state {lifecycleState}.");
        }

        var createdDevice = backend.CreateDevice(SUPPORTED_SHADER_FORMAT_MASK, debugMode, preferredDriver);

        if (createdDevice == nint.Zero)
            throw new InvalidOperationException("Failed to create SDL GPU device.");

        var claimed = false;

        try
        {
            var driver = backend.GetDeviceDriver(createdDevice) ?? "unknown";
            var formats = backend.GetSupportedShaderFormats(createdDevice);

            if (!backend.ClaimWindow(createdDevice, windowHandle))
                throw new InvalidOperationException("Failed to claim window for SDL GPU device.");

            claimed = true;

            var composition = GpuSwapchainComposition.Sdr;

            if (!backend.WindowSupportsSwapchainComposition(createdDevice, windowHandle, composition))
                throw new InvalidOperationException("Window does not support SDR swapchain composition.");

            var presentMode = backend.WindowSupportsPresentMode(createdDevice, windowHandle, GpuPresentMode.Mailbox)
                ? GpuPresentMode.Mailbox
                : GpuPresentMode.VSync;

            if (!backend.SetSwapchainParameters(createdDevice, windowHandle, composition, presentMode))
                throw new InvalidOperationException("Failed to configure GPU swapchain parameters.");

            var scFormat = backend.GetSwapchainTextureFormat(createdDevice, windowHandle);
            var colorFormat = backend.ResolveOffscreenColorFormat(createdDevice, scFormat);

            if (colorFormat == GpuTextureFormat.INVALID)
                throw new InvalidOperationException("Failed to resolve a valid offscreen color target format.");

            var minimized = backend.IsWindowMinimized(windowHandle);
            var hasSize = backend.GetWindowPixelSize(windowHandle, out var pxWidth, out var pxHeight);

            lock (stateLock)
            {
                if (lifecycleState != GpuDeviceLifecycleState.Uninitialized)
                    throw new InvalidOperationException($"GPU device state changed concurrently to {lifecycleState}.");

                device = createdDevice;
                this.windowHandle = windowHandle;
                DeviceDriver = driver;
                SupportedShaderFormats = formats;
                SwapchainFormat = scFormat;
                OffscreenColorFormat = colorFormat;
                ActivePresentMode = presentMode;
                isMinimized = minimized;
                targetWidth = hasSize && pxWidth > 0 ? pxWidth : 0;
                targetHeight = hasSize && pxHeight > 0 ? pxHeight : 0;
                loggedInactiveSurface = false;
                lifecycleState = GpuDeviceLifecycleState.Initialized;
            }

            logDeviceInitialized(logger, driver, formats, presentMode);
        }
        catch
        {
            if (claimed)
                backend.ReleaseWindow(createdDevice, windowHandle);

            backend.DestroyDevice(createdDevice);
            throw;
        }
    }

    /// <summary>
    /// Updates the desired offscreen render target dimensions. Safe to call on resize events.
    /// </summary>
    public void HandleResize(int width, int height)
    {
        nint currentWindow;

        lock (stateLock)
        {
            if (lifecycleState != GpuDeviceLifecycleState.Initialized)
                return;

            currentWindow = windowHandle;
        }

        var windowMinimized = backend.IsWindowMinimized(currentWindow);

        lock (stateLock)
        {
            if (lifecycleState != GpuDeviceLifecycleState.Initialized)
                return;

            targetWidth = width > 0 ? width : 0;
            targetHeight = height > 0 ? height : 0;
            isMinimized = targetWidth == 0 || targetHeight == 0 || windowMinimized;

            if (!isMinimized)
                loggedInactiveSurface = false;
        }
    }

    /// <summary>
    /// Queries the platform window for current pixel dimensions and minimized state.
    /// Must be called on the window-creating thread (<c>InputThread</c>).
    /// </summary>
    public void RefreshSurfaceMetrics()
    {
        nint currentWindow;

        lock (stateLock)
        {
            if (lifecycleState != GpuDeviceLifecycleState.Initialized)
                return;

            currentWindow = windowHandle;
        }

        var minimized = backend.IsWindowMinimized(currentWindow);
        var hasSize = backend.GetWindowPixelSize(currentWindow, out var pxWidth, out var pxHeight);

        lock (stateLock)
        {
            if (lifecycleState != GpuDeviceLifecycleState.Initialized)
                return;

            isMinimized = minimized;
            targetWidth = hasSize && pxWidth > 0 ? pxWidth : 0;
            targetHeight = hasSize && pxHeight > 0 ? pxHeight : 0;

            if (!isMinimized && targetWidth > 0 && targetHeight > 0)
                loggedInactiveSurface = false;
        }
    }

    /// <summary>
    /// Acquires a command buffer and an offscreen color render target for <c>DrawThread</c>.
    /// Returns <see langword="false"/> when the device is not ready or the window is minimized/zero-sized.
    /// </summary>
    public bool BeginOffscreenFrame(out GpuOffscreenFrame frame)
    {
        frame = default;

        nint currentDevice;
        GpuTextureFormat colorFormat;
        int desiredWidth;
        int desiredHeight;
        int slotIndex;
        nint fenceToWait;
        nint existingTexture;
        int existingWidth;
        int existingHeight;

        lock (stateLock)
        {
            if (lifecycleState != GpuDeviceLifecycleState.Initialized)
                return false;

            if (isMinimized || targetWidth <= 0 || targetHeight <= 0)
            {
                logSurfaceInactiveOnce();
                return false;
            }

            currentDevice = device;
            colorFormat = OffscreenColorFormat;
            desiredWidth = targetWidth;
            desiredHeight = targetHeight;

            slotIndex = selectAvailableSlotLocked(currentDevice, out fenceToWait);

            if (slotIndex < 0)
                return false;

            slots[slotIndex].State = SlotState.Drawing;
            existingTexture = slots[slotIndex].Texture;
            existingWidth = slots[slotIndex].Width;
            existingHeight = slots[slotIndex].Height;
        }

        if (fenceToWait != nint.Zero)
        {
            Span<nint> fences = [fenceToWait];
            backend.WaitForFences(currentDevice, fences);
            backend.ReleaseFence(currentDevice, fenceToWait);
        }

        var targetTexture = existingTexture;

        if (targetTexture == nint.Zero || existingWidth != desiredWidth || existingHeight != desiredHeight)
        {
            if (targetTexture != nint.Zero)
                backend.ReleaseTexture(currentDevice, targetTexture);

            var description = new GpuTextureDescription(
                (uint)desiredWidth,
                (uint)desiredHeight,
                colorFormat,
                GpuTextureUsage.ColorTarget | GpuTextureUsage.Sampler);

            targetTexture = backend.CreateTexture(currentDevice, in description);

            if (targetTexture == nint.Zero)
            {
                lock (stateLock)
                {
                    slots[slotIndex].Texture = nint.Zero;
                    slots[slotIndex].Width = 0;
                    slots[slotIndex].Height = 0;
                    slots[slotIndex].State = SlotState.Available;
                }

                throw new InvalidOperationException(
                    $"Failed to create offscreen color texture ({desiredWidth}x{desiredHeight}).");
            }

            lock (stateLock)
            {
                slots[slotIndex].Texture = targetTexture;
                slots[slotIndex].Width = desiredWidth;
                slots[slotIndex].Height = desiredHeight;
            }
        }

        var commandBuffer = backend.AcquireCommandBuffer(currentDevice);

        if (commandBuffer == nint.Zero)
        {
            lock (stateLock)
                slots[slotIndex].State = SlotState.Available;

            throw new InvalidOperationException("Failed to acquire GPU command buffer for draw frame.");
        }

        frame = new GpuOffscreenFrame(slotIndex, commandBuffer, targetTexture, desiredWidth, desiredHeight);
        return true;
    }

    /// <summary>
    /// Submits the draw command buffer and publishes the completed offscreen frame for <c>InputThread</c>.
    /// </summary>
    public void EndOffscreenFrame(in GpuOffscreenFrame frame)
        => EndOffscreenFrame(in frame, acquireFence: false, out _);

    /// <summary>
    /// Submits the draw command buffer (optionally acquiring a completion fence) and publishes
    /// the completed offscreen frame for <c>InputThread</c>.
    /// </summary>
    public void EndOffscreenFrame(in GpuOffscreenFrame frame, bool acquireFence, out nint submittedFence)
    {
        submittedFence = nint.Zero;

        if (frame.CommandBuffer == nint.Zero)
            throw new ArgumentException("Frame command buffer must not be zero.", nameof(frame));

        if (acquireFence)
            submittedFence = backend.SubmitCommandBufferAndAcquireFence(frame.CommandBuffer);
        else
            backend.SubmitCommandBuffer(frame.CommandBuffer);

        lock (stateLock)
        {
            if (lifecycleState != GpuDeviceLifecycleState.Initialized)
            {
                if ((uint)frame.SlotIndex < slot_count && slots[frame.SlotIndex].State == SlotState.Drawing)
                    slots[frame.SlotIndex].State = SlotState.Available;

                return;
            }

            for (var i = 0; i < slot_count; i++)
            {
                if (i != frame.SlotIndex && slots[i].State == SlotState.Published)
                    slots[i].State = SlotState.Available;
            }

            slots[frame.SlotIndex].State = SlotState.Published;
        }
    }

    /// <summary>
    /// Cancels an in-progress draw command buffer without publishing the slot.
    /// </summary>
    public void CancelOffscreenFrame(in GpuOffscreenFrame frame)
    {
        if (frame.CommandBuffer != nint.Zero)
            backend.CancelCommandBuffer(frame.CommandBuffer);

        lock (stateLock)
        {
            if ((uint)frame.SlotIndex < slot_count && slots[frame.SlotIndex].State == SlotState.Drawing)
                slots[frame.SlotIndex].State = SlotState.Available;
        }
    }

    /// <summary>
    /// Non-blockingly presents the latest published offscreen frame onto the window swapchain.
    /// Must be called on the window-creating thread (<c>InputThread</c>).
    /// </summary>
    public bool PresentLatestFrame()
    {
        nint currentDevice;
        nint currentWindow;

        lock (stateLock)
        {
            if (lifecycleState != GpuDeviceLifecycleState.Initialized)
                return false;

            currentDevice = device;
            currentWindow = windowHandle;
        }

        var windowMinimized = backend.IsWindowMinimized(currentWindow);
        var slotIndex = -1;
        nint sourceTexture;
        int sourceWidth;
        int sourceHeight;

        lock (stateLock)
        {
            if (lifecycleState != GpuDeviceLifecycleState.Initialized)
                return false;

            var effectiveMinimized = windowMinimized || targetWidth <= 0 || targetHeight <= 0;
            isMinimized = effectiveMinimized;

            if (!effectiveMinimized)
                loggedInactiveSurface = false;

            if (effectiveMinimized)
            {
                logSurfaceInactiveOnce();
                return false;
            }

            for (var i = 0; i < slot_count; i++)
            {
                if (slots[i].State == SlotState.Published)
                {
                    slotIndex = i;
                    break;
                }
            }

            if (slotIndex < 0)
                return false;

            sourceTexture = slots[slotIndex].Texture;
            sourceWidth = slots[slotIndex].Width;
            sourceHeight = slots[slotIndex].Height;

            if (sourceTexture == nint.Zero || sourceWidth <= 0 || sourceHeight <= 0)
            {
                slots[slotIndex].State = SlotState.Available;
                return false;
            }

            slots[slotIndex].State = SlotState.Presenting;
        }

        var commandBuffer = backend.AcquireCommandBuffer(currentDevice);

        if (commandBuffer == nint.Zero)
        {
            restoreUnpresentedSlot(slotIndex);
            return false;
        }

        var acquired = backend.AcquireSwapchainTexture(
            commandBuffer,
            currentWindow,
            out var swapchainTexture,
            out var swapchainWidth,
            out var swapchainHeight);

        if (!acquired || swapchainTexture == nint.Zero || swapchainWidth == 0 || swapchainHeight == 0)
        {
            // Submit an empty command buffer rather than cancelling after AcquireSwapchainTexture.
            backend.SubmitCommandBuffer(commandBuffer);
            restoreUnpresentedSlot(slotIndex);
            return false;
        }

        backend.BlitTexture(
            commandBuffer,
            sourceTexture,
            (uint)sourceWidth,
            (uint)sourceHeight,
            swapchainTexture,
            swapchainWidth,
            swapchainHeight);

        var fence = backend.SubmitCommandBufferAndAcquireFence(commandBuffer);

        lock (stateLock)
        {
            slots[slotIndex].PresentFence = fence;
            slots[slotIndex].State = SlotState.Available;
        }

        return true;
    }

    public void WaitForIdle()
    {
        nint currentDevice;

        lock (stateLock)
        {
            if (lifecycleState != GpuDeviceLifecycleState.Initialized)
                return;

            currentDevice = device;
        }

        if (currentDevice != nint.Zero)
            backend.WaitForIdle(currentDevice);
    }

    /// <summary>
    /// Waits for GPU idle, releases offscreen targets and fences, releases the window, and destroys the device.
    /// Must be called on <c>InputThread</c> after <c>DrawThread</c> has stopped and before <c>GameWindow.Destroy()</c>.
    /// </summary>
    public void Shutdown()
    {
        if (Interlocked.Exchange(ref shutdownStarted, 1) != 0)
            return;

        nint deviceToDestroy;
        nint windowToRelease;

        lock (stateLock)
        {
            if (lifecycleState == GpuDeviceLifecycleState.Uninitialized)
            {
                lifecycleState = GpuDeviceLifecycleState.Disposed;
                return;
            }

            lifecycleState = GpuDeviceLifecycleState.ShuttingDown;
            deviceToDestroy = device;
            windowToRelease = windowHandle;
        }

        try
        {
            if (deviceToDestroy != nint.Zero)
            {
                logDeviceShuttingDown(logger);
                backend.WaitForIdle(deviceToDestroy);

                for (var i = 0; i < slot_count; i++)
                {
                    if (slots[i].PresentFence != nint.Zero)
                    {
                        backend.ReleaseFence(deviceToDestroy, slots[i].PresentFence);
                        slots[i].PresentFence = nint.Zero;
                    }

                    if (slots[i].Texture != nint.Zero)
                    {
                        backend.ReleaseTexture(deviceToDestroy, slots[i].Texture);
                        slots[i].Texture = nint.Zero;
                    }

                    slots[i].Width = 0;
                    slots[i].Height = 0;
                    slots[i].State = SlotState.Available;
                }

                if (windowToRelease != nint.Zero)
                    backend.ReleaseWindow(deviceToDestroy, windowToRelease);

                backend.DestroyDevice(deviceToDestroy);
            }
        }
        finally
        {
            lock (stateLock)
            {
                device = nint.Zero;
                windowHandle = nint.Zero;
                DeviceDriver = null;
                SupportedShaderFormats = GpuShaderFormats.None;
                SwapchainFormat = GpuTextureFormat.INVALID;
                OffscreenColorFormat = GpuTextureFormat.INVALID;
                ActivePresentMode = GpuPresentMode.VSync;
                targetWidth = 0;
                targetHeight = 0;
                lifecycleState = GpuDeviceLifecycleState.Disposed;
            }
        }
    }

    public void Dispose() => Shutdown();

    private int selectAvailableSlotLocked(nint currentDevice, out nint fenceToWait)
    {
        fenceToWait = nint.Zero;
        var fallbackIndex = -1;

        for (var i = 0; i < slot_count; i++)
        {
            if (slots[i].State != SlotState.Available)
                continue;

            var fence = slots[i].PresentFence;

            if (fence == nint.Zero)
                return i;

            if (backend.QueryFence(currentDevice, fence))
            {
                backend.ReleaseFence(currentDevice, fence);
                slots[i].PresentFence = nint.Zero;
                return i;
            }

            if (fallbackIndex < 0)
                fallbackIndex = i;
        }

        if (fallbackIndex >= 0)
        {
            fenceToWait = slots[fallbackIndex].PresentFence;
            slots[fallbackIndex].PresentFence = nint.Zero;
        }

        return fallbackIndex;
    }

    private void restoreUnpresentedSlot(int slotIndex)
    {
        lock (stateLock)
        {
            if ((uint)slotIndex >= slot_count)
                return;

            if (lifecycleState != GpuDeviceLifecycleState.Initialized)
            {
                slots[slotIndex].State = SlotState.Available;
                return;
            }

            var hasNewerPublished = false;

            for (var i = 0; i < slot_count; i++)
            {
                if (i != slotIndex && slots[i].State == SlotState.Published)
                {
                    hasNewerPublished = true;
                    break;
                }
            }

            slots[slotIndex].State = hasNewerPublished ? SlotState.Available : SlotState.Published;
        }
    }

    private void logSurfaceInactiveOnce()
    {
        if (loggedInactiveSurface)
            return;

        loggedInactiveSurface = true;
        logSurfaceInactive(logger);
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Initialized SDL_GPU device using driver '{Driver}' (shader formats: {ShaderFormats}, present mode: {PresentMode})")]
    private static partial void logDeviceInitialized(
        ILogger logger,
        string driver,
        GpuShaderFormats shaderFormats,
        GpuPresentMode presentMode);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Skipping GPU render/present while window is minimized or zero-sized")]
    private static partial void logSurfaceInactive(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Shutting down SDL_GPU device")]
    private static partial void logDeviceShuttingDown(ILogger logger);

    private enum SlotState
    {
        Available = 0,
        Drawing = 1,
        Published = 2,
        Presenting = 3,
    }

    private struct OffscreenSlot
    {
        public nint Texture;
        public int Width;
        public int Height;
        public nint PresentFence;
        public SlotState State;
    }
}
