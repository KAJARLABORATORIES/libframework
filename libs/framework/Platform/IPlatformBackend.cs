using Microsoft.Extensions.Logging;

namespace kajarlabs.Framework.Platform;

internal enum PlatformEventType
{
    Other = 0,
    Quit = 1,
    WindowCloseRequested = 2,
    WindowResized = 3,
    WindowPixelSizeChanged = 4,
    WindowMinimized = 5,
    WindowRestored = 6,
    WindowFocusGained = 7,
    WindowFocusLost = 8,
    WindowMouseEnter = 9,
    WindowMouseLeave = 10,
    MouseMotion = 11,
    MouseButtonDown = 12,
    MouseButtonUp = 13,
    MouseWheel = 14,
    KeyDown = 15,
    KeyUp = 16,
    TextInput = 17,
}

internal readonly record struct PlatformEvent(
    PlatformEventType Type,
    uint WindowId = 0,
    int Data1 = 0,
    int Data2 = 0,
    PlatformInputEvent Input = default);

internal interface IPlatformBackend
{
    IDisposable InstallLogRouter(ILoggerFactory loggerFactory);

    IDisposable InitializeVideo(string? videoDriverHint = null);

    bool IsDummyVideoDriver();

    nint CreateWindow(string title, int width, int height, bool resizable);

    void DestroyWindow(nint handle);

    uint GetWindowId(nint handle);

    void SetWindowTitle(nint handle, string title);

    void GetWindowSize(nint handle, out int width, out int height);

    bool GetWindowPixelSize(nint handle, out int width, out int height);

    void SetWindowSize(nint handle, int width, int height);

    void StartTextInput(nint handle);

    void StopTextInput(nint handle);

    bool PollEvent(out PlatformEvent platformEvent);
}
