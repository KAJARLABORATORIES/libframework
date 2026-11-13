using kajarlabs.Framework.Logging;
using Microsoft.Extensions.Logging;
using SDL3;

namespace kajarlabs.Framework.Platform;

internal sealed class SdlPlatformBackend : IPlatformBackend
{
    public static readonly SdlPlatformBackend INSTANCE = new();

    private static readonly Lock sdl_lock = new();
    private static int video_ref_count;
    private static bool applied_video_driver_hint;

    private SdlPlatformBackend()
    {
    }

    public IDisposable InstallLogRouter(ILoggerFactory loggerFactory) => new SdlLogRouter(loggerFactory);

    public IDisposable InitializeVideo(string? videoDriverHint = null)
    {
        lock (sdl_lock)
        {
            if (video_ref_count == 0)
            {
                if (!string.IsNullOrWhiteSpace(videoDriverHint))
                {
                    SDL.SetHint("SDL_VIDEO_DRIVER", videoDriverHint);
                    applied_video_driver_hint = true;
                }

                if (!SDL.InitSubSystem(SDL.InitFlags.Video))
                {
                    var error = SDL.GetError();

                    if (applied_video_driver_hint)
                    {
                        SDL.ResetHint("SDL_VIDEO_DRIVER");
                        applied_video_driver_hint = false;
                    }

                    throw new InvalidOperationException($"SDL video initialization failed: {error}");
                }
            }

            video_ref_count++;
            return new VideoSession();
        }
    }

    public nint CreateWindow(string title, int width, int height, bool resizable)
    {
        var flags = resizable ? SDL.WindowFlags.Resizable : default;
        var handle = SDL.CreateWindow(title, width, height, flags);

        if (handle == nint.Zero)
            throw new InvalidOperationException($"SDL window creation failed: {SDL.GetError()}");

        return handle;
    }

    public void DestroyWindow(nint handle)
    {
        if (handle != nint.Zero)
            SDL.DestroyWindow(handle);
    }

    public uint GetWindowId(nint handle) => SDL.GetWindowID(handle);

    public void SetWindowTitle(nint handle, string title) => SDL.SetWindowTitle(handle, title);

    public void GetWindowSize(nint handle, out int width, out int height) => SDL.GetWindowSize(handle, out width, out height);

    public void SetWindowSize(nint handle, int width, int height) => SDL.SetWindowSize(handle, width, height);

    public bool PollEvent(out PlatformEvent platformEvent)
    {
        if (!SDL.PollEvent(out var sdlEvent))
        {
            platformEvent = default;
            return false;
        }

        platformEvent = (SDL.EventType)sdlEvent.Type switch
        {
            SDL.EventType.Quit => new PlatformEvent(PlatformEventType.Quit),
            SDL.EventType.WindowCloseRequested => new PlatformEvent(
                PlatformEventType.WindowCloseRequested,
                sdlEvent.Window.WindowID),
            SDL.EventType.WindowResized => new PlatformEvent(
                PlatformEventType.WindowResized,
                sdlEvent.Window.WindowID,
                sdlEvent.Window.Data1,
                sdlEvent.Window.Data2),
            _ => new PlatformEvent(PlatformEventType.Other),
        };

        return true;
    }

    private sealed class VideoSession : IDisposable
    {
        private int disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0)
                return;

            lock (sdl_lock)
            {
                if (video_ref_count <= 0)
                    return;

                video_ref_count--;

                if (video_ref_count == 0)
                {
                    SDL.QuitSubSystem(SDL.InitFlags.Video);

                    if (SDL.WasInit((SDL.InitFlags)0) == 0)
                        SDL.Quit();

                    if (applied_video_driver_hint)
                    {
                        SDL.ResetHint("SDL_VIDEO_DRIVER");
                        applied_video_driver_hint = false;
                    }
                }
            }
        }
    }
}
