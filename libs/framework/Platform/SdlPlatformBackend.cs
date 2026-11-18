using System.Numerics;
using System.Runtime.InteropServices;
using kajarlabs.Framework.Input;
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

    public bool IsDummyVideoDriver()
        => string.Equals(SDL.GetCurrentVideoDriver(), "dummy", StringComparison.OrdinalIgnoreCase);

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

    public bool GetWindowPixelSize(nint handle, out int width, out int height)
        => SDL.GetWindowSizeInPixels(handle, out width, out height);

    public void SetWindowSize(nint handle, int width, int height) => SDL.SetWindowSize(handle, width, height);

    public void StartTextInput(nint handle)
    {
        if (handle != nint.Zero)
            SDL.StartTextInput(handle);
    }

    public void StopTextInput(nint handle)
    {
        if (handle != nint.Zero)
            SDL.StopTextInput(handle);
    }

    public bool PollEvent(out PlatformEvent platformEvent)
    {
        if (!SDL.PollEvent(out var sdlEvent))
        {
            platformEvent = default;
            return false;
        }

        platformEvent = TranslateSdlEvent(in sdlEvent);
        return true;
    }

    internal static PlatformEvent TranslateSdlEvent(in SDL.Event sdlEvent)
        => (SDL.EventType)sdlEvent.Type switch
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
            SDL.EventType.WindowPixelSizeChanged => new PlatformEvent(
                PlatformEventType.WindowPixelSizeChanged,
                sdlEvent.Window.WindowID,
                sdlEvent.Window.Data1,
                sdlEvent.Window.Data2),
            SDL.EventType.WindowMinimized => new PlatformEvent(
                PlatformEventType.WindowMinimized,
                sdlEvent.Window.WindowID),
            SDL.EventType.WindowRestored => new PlatformEvent(
                PlatformEventType.WindowRestored,
                sdlEvent.Window.WindowID),
            SDL.EventType.WindowFocusGained => new PlatformEvent(
                PlatformEventType.WindowFocusGained,
                sdlEvent.Window.WindowID,
                Input: PlatformInputEvent.WindowFocus(true)),
            SDL.EventType.WindowFocusLost => new PlatformEvent(
                PlatformEventType.WindowFocusLost,
                sdlEvent.Window.WindowID,
                Input: PlatformInputEvent.WindowFocus(false)),
            SDL.EventType.WindowMouseEnter => new PlatformEvent(
                PlatformEventType.WindowMouseEnter,
                sdlEvent.Window.WindowID,
                Input: PlatformInputEvent.WindowMouseEnter()),
            SDL.EventType.WindowMouseLeave => new PlatformEvent(
                PlatformEventType.WindowMouseLeave,
                sdlEvent.Window.WindowID,
                Input: PlatformInputEvent.WindowMouseLeave()),
            SDL.EventType.MouseMotion => new PlatformEvent(
                PlatformEventType.MouseMotion,
                sdlEvent.Motion.WindowID,
                Input: PlatformInputEvent.MouseMotion(
                    new Vector2(sdlEvent.Motion.X, sdlEvent.Motion.Y),
                    new Vector2(sdlEvent.Motion.XRel, sdlEvent.Motion.YRel))),
            SDL.EventType.MouseButtonDown => translateMouseButton(in sdlEvent.Button, isDown: true),
            SDL.EventType.MouseButtonUp => translateMouseButton(in sdlEvent.Button, isDown: false),
            SDL.EventType.MouseWheel => translateMouseWheel(in sdlEvent.Wheel),
            SDL.EventType.KeyDown => translateKey(in sdlEvent.Key, isDown: true),
            SDL.EventType.KeyUp => translateKey(in sdlEvent.Key, isDown: false),
            SDL.EventType.TextInput => translateTextInput(in sdlEvent.Text),
            _ => new PlatformEvent(PlatformEventType.Other),
        };

    private static PlatformEvent translateMouseButton(in SDL.MouseButtonEvent buttonEvent, bool isDown)
    {
        MouseButton? mapped = buttonEvent.Button switch
        {
            SDL.ButtonLeft => MouseButton.Left,
            SDL.ButtonMiddle => MouseButton.Middle,
            SDL.ButtonRight => MouseButton.Right,
            SDL.ButtonX1 => MouseButton.Button4,
            SDL.ButtonX2 => MouseButton.Button5,
            _ => null,
        };

        if (mapped is not { } button)
            return new PlatformEvent(PlatformEventType.Other);

        var type = isDown ? PlatformEventType.MouseButtonDown : PlatformEventType.MouseButtonUp;
        var position = new Vector2(buttonEvent.X, buttonEvent.Y);
        return new PlatformEvent(type, buttonEvent.WindowID, Input: PlatformInputEvent.MouseButtonEvent(button, isDown, position));
    }

    private static PlatformEvent translateMouseWheel(in SDL.MouseWheelEvent wheelEvent)
    {
        var delta = new Vector2(wheelEvent.X, wheelEvent.Y);
        var isPrecise = wheelEvent.X != MathF.Truncate(wheelEvent.X) || wheelEvent.Y != MathF.Truncate(wheelEvent.Y);
        var position = new Vector2(wheelEvent.MouseX, wheelEvent.MouseY);

        return new PlatformEvent(
            PlatformEventType.MouseWheel,
            wheelEvent.WindowID,
            Input: PlatformInputEvent.MouseWheel(delta, position, isPrecise));
    }

    private static PlatformEvent translateKey(in SDL.KeyboardEvent keyEvent, bool isDown)
    {
        var key = mapScancode(keyEvent.Scancode);

        if (key == Key.Unknown)
            key = mapKeycodeFallback(keyEvent.Key);

        var modifiers = mapModifiers(keyEvent.Mod);
        var type = isDown ? PlatformEventType.KeyDown : PlatformEventType.KeyUp;
        var repeat = isDown && keyEvent.Repeat;

        return new PlatformEvent(
            type,
            keyEvent.WindowID,
            Input: PlatformInputEvent.KeyEvent(key, isDown, modifiers, repeat));
    }

    private static PlatformEvent translateTextInput(in SDL.TextInputEvent textEvent)
    {
        // Decode UTF-8 immediately on InputThread so native SDL pointers never cross thread boundaries.
        var text = textEvent.Text != nint.Zero ? Marshal.PtrToStringUTF8(textEvent.Text) : null;

        if (string.IsNullOrEmpty(text))
            return new PlatformEvent(PlatformEventType.Other);

        return new PlatformEvent(
            PlatformEventType.TextInput,
            textEvent.WindowID,
            Input: PlatformInputEvent.TextInput(text));
    }

    private static KeyModifiers mapModifiers(SDL.Keymod mod)
    {
        var result = KeyModifiers.None;

        if ((mod & SDL.Keymod.Shift) != 0)
            result |= KeyModifiers.Shift;

        if ((mod & SDL.Keymod.Ctrl) != 0)
            result |= KeyModifiers.Control;

        if ((mod & SDL.Keymod.Alt) != 0)
            result |= KeyModifiers.Alt;

        if ((mod & SDL.Keymod.GUI) != 0)
            result |= KeyModifiers.Super;

        return result;
    }

    private static Key mapScancode(SDL.Scancode scancode) => scancode switch
    {
        SDL.Scancode.A => Key.A,
        SDL.Scancode.B => Key.B,
        SDL.Scancode.C => Key.C,
        SDL.Scancode.D => Key.D,
        SDL.Scancode.E => Key.E,
        SDL.Scancode.F => Key.F,
        SDL.Scancode.G => Key.G,
        SDL.Scancode.H => Key.H,
        SDL.Scancode.I => Key.I,
        SDL.Scancode.J => Key.J,
        SDL.Scancode.K => Key.K,
        SDL.Scancode.L => Key.L,
        SDL.Scancode.M => Key.M,
        SDL.Scancode.N => Key.N,
        SDL.Scancode.O => Key.O,
        SDL.Scancode.P => Key.P,
        SDL.Scancode.Q => Key.Q,
        SDL.Scancode.R => Key.R,
        SDL.Scancode.S => Key.S,
        SDL.Scancode.T => Key.T,
        SDL.Scancode.U => Key.U,
        SDL.Scancode.V => Key.V,
        SDL.Scancode.W => Key.W,
        SDL.Scancode.X => Key.X,
        SDL.Scancode.Y => Key.Y,
        SDL.Scancode.Z => Key.Z,

        SDL.Scancode.Alpha0 => Key.Number0,
        SDL.Scancode.Alpha1 => Key.Number1,
        SDL.Scancode.Alpha2 => Key.Number2,
        SDL.Scancode.Alpha3 => Key.Number3,
        SDL.Scancode.Alpha4 => Key.Number4,
        SDL.Scancode.Alpha5 => Key.Number5,
        SDL.Scancode.Alpha6 => Key.Number6,
        SDL.Scancode.Alpha7 => Key.Number7,
        SDL.Scancode.Alpha8 => Key.Number8,
        SDL.Scancode.Alpha9 => Key.Number9,

        SDL.Scancode.F1 => Key.F1,
        SDL.Scancode.F2 => Key.F2,
        SDL.Scancode.F3 => Key.F3,
        SDL.Scancode.F4 => Key.F4,
        SDL.Scancode.F5 => Key.F5,
        SDL.Scancode.F6 => Key.F6,
        SDL.Scancode.F7 => Key.F7,
        SDL.Scancode.F8 => Key.F8,
        SDL.Scancode.F9 => Key.F9,
        SDL.Scancode.F10 => Key.F10,
        SDL.Scancode.F11 => Key.F11,
        SDL.Scancode.F12 => Key.F12,
        SDL.Scancode.F13 => Key.F13,
        SDL.Scancode.F14 => Key.F14,
        SDL.Scancode.F15 => Key.F15,
        SDL.Scancode.F16 => Key.F16,
        SDL.Scancode.F17 => Key.F17,
        SDL.Scancode.F18 => Key.F18,
        SDL.Scancode.F19 => Key.F19,
        SDL.Scancode.F20 => Key.F20,
        SDL.Scancode.F21 => Key.F21,
        SDL.Scancode.F22 => Key.F22,
        SDL.Scancode.F23 => Key.F23,
        SDL.Scancode.F24 => Key.F24,

        SDL.Scancode.LShift => Key.ShiftLeft,
        SDL.Scancode.RShift => Key.ShiftRight,
        SDL.Scancode.LCtrl => Key.ControlLeft,
        SDL.Scancode.RCtrl => Key.ControlRight,
        SDL.Scancode.LAlt => Key.AltLeft,
        SDL.Scancode.RAlt => Key.AltRight,
        SDL.Scancode.LGUI => Key.SuperLeft,
        SDL.Scancode.RGUI => Key.SuperRight,

        SDL.Scancode.Left => Key.Left,
        SDL.Scancode.Right => Key.Right,
        SDL.Scancode.Up => Key.Up,
        SDL.Scancode.Down => Key.Down,
        SDL.Scancode.Home => Key.Home,
        SDL.Scancode.End => Key.End,
        SDL.Scancode.Pageup => Key.PageUp,
        SDL.Scancode.Pagedown => Key.PageDown,
        SDL.Scancode.Insert => Key.Insert,
        SDL.Scancode.Delete => Key.Delete,

        SDL.Scancode.Backspace => Key.Backspace,
        SDL.Scancode.Tab => Key.Tab,
        SDL.Scancode.Return or SDL.Scancode.Return2 => Key.Enter,
        SDL.Scancode.Space => Key.Space,
        SDL.Scancode.Escape => Key.Escape,

        SDL.Scancode.Grave => Key.Grave,
        SDL.Scancode.Minus => Key.Minus,
        SDL.Scancode.Equals => Key.Equals,
        SDL.Scancode.Leftbracket => Key.BracketLeft,
        SDL.Scancode.Rightbracket => Key.BracketRight,
        SDL.Scancode.Backslash => Key.Backslash,
        SDL.Scancode.Semicolon => Key.Semicolon,
        SDL.Scancode.Apostrophe => Key.Apostrophe,
        SDL.Scancode.Comma => Key.Comma,
        SDL.Scancode.Period => Key.Period,
        SDL.Scancode.Slash => Key.Slash,

        SDL.Scancode.Capslock => Key.CapsLock,
        SDL.Scancode.NumLockClear => Key.NumLock,
        SDL.Scancode.Scrolllock => Key.ScrollLock,

        SDL.Scancode.Printscreen => Key.PrintScreen,
        SDL.Scancode.Pause => Key.Pause,
        SDL.Scancode.Application or SDL.Scancode.Menu => Key.Menu,

        SDL.Scancode.Kp0 => Key.Keypad0,
        SDL.Scancode.Kp1 => Key.Keypad1,
        SDL.Scancode.Kp2 => Key.Keypad2,
        SDL.Scancode.Kp3 => Key.Keypad3,
        SDL.Scancode.Kp4 => Key.Keypad4,
        SDL.Scancode.Kp5 => Key.Keypad5,
        SDL.Scancode.Kp6 => Key.Keypad6,
        SDL.Scancode.Kp7 => Key.Keypad7,
        SDL.Scancode.Kp8 => Key.Keypad8,
        SDL.Scancode.Kp9 => Key.Keypad9,
        SDL.Scancode.KpPeriod or SDL.Scancode.KpDecimal => Key.KeypadDecimal,
        SDL.Scancode.KpDivide => Key.KeypadDivide,
        SDL.Scancode.KpMultiply => Key.KeypadMultiply,
        SDL.Scancode.KpMinus => Key.KeypadSubtract,
        SDL.Scancode.KpPlus => Key.KeypadAdd,
        SDL.Scancode.KpEnter => Key.KeypadEnter,
        SDL.Scancode.KpEquals => Key.KeypadEquals,

        SDL.Scancode.Mute => Key.Mute,
        SDL.Scancode.VolumeDown => Key.VolumeDown,
        SDL.Scancode.VolumeUp => Key.VolumeUp,
        SDL.Scancode.MediaPlayPause => Key.MediaPlayPause,
        SDL.Scancode.MediaStop => Key.MediaStop,
        SDL.Scancode.MediaNextTrack => Key.MediaNext,
        SDL.Scancode.MediaPreviousTrack => Key.MediaPrevious,

        _ => Key.Unknown,
    };

    private static Key mapKeycodeFallback(SDL.Keycode keycode) => keycode switch
    {
        SDL.Keycode.A => Key.A,
        SDL.Keycode.B => Key.B,
        SDL.Keycode.C => Key.C,
        SDL.Keycode.D => Key.D,
        SDL.Keycode.E => Key.E,
        SDL.Keycode.F => Key.F,
        SDL.Keycode.G => Key.G,
        SDL.Keycode.H => Key.H,
        SDL.Keycode.I => Key.I,
        SDL.Keycode.J => Key.J,
        SDL.Keycode.K => Key.K,
        SDL.Keycode.L => Key.L,
        SDL.Keycode.M => Key.M,
        SDL.Keycode.N => Key.N,
        SDL.Keycode.O => Key.O,
        SDL.Keycode.P => Key.P,
        SDL.Keycode.Q => Key.Q,
        SDL.Keycode.R => Key.R,
        SDL.Keycode.S => Key.S,
        SDL.Keycode.T => Key.T,
        SDL.Keycode.U => Key.U,
        SDL.Keycode.V => Key.V,
        SDL.Keycode.W => Key.W,
        SDL.Keycode.X => Key.X,
        SDL.Keycode.Y => Key.Y,
        SDL.Keycode.Z => Key.Z,

        SDL.Keycode.Alpha0 => Key.Number0,
        SDL.Keycode.Alpha1 => Key.Number1,
        SDL.Keycode.Alpha2 => Key.Number2,
        SDL.Keycode.Alpha3 => Key.Number3,
        SDL.Keycode.Alpha4 => Key.Number4,
        SDL.Keycode.Alpha5 => Key.Number5,
        SDL.Keycode.Alpha6 => Key.Number6,
        SDL.Keycode.Alpha7 => Key.Number7,
        SDL.Keycode.Alpha8 => Key.Number8,
        SDL.Keycode.Alpha9 => Key.Number9,

        SDL.Keycode.F1 => Key.F1,
        SDL.Keycode.F2 => Key.F2,
        SDL.Keycode.F3 => Key.F3,
        SDL.Keycode.F4 => Key.F4,
        SDL.Keycode.F5 => Key.F5,
        SDL.Keycode.F6 => Key.F6,
        SDL.Keycode.F7 => Key.F7,
        SDL.Keycode.F8 => Key.F8,
        SDL.Keycode.F9 => Key.F9,
        SDL.Keycode.F10 => Key.F10,
        SDL.Keycode.F11 => Key.F11,
        SDL.Keycode.F12 => Key.F12,

        SDL.Keycode.LShift => Key.ShiftLeft,
        SDL.Keycode.RShift => Key.ShiftRight,
        SDL.Keycode.LCtrl => Key.ControlLeft,
        SDL.Keycode.RCtrl => Key.ControlRight,
        SDL.Keycode.LAlt => Key.AltLeft,
        SDL.Keycode.RAlt => Key.AltRight,
        SDL.Keycode.LGUI => Key.SuperLeft,
        SDL.Keycode.RGUI => Key.SuperRight,

        SDL.Keycode.Left => Key.Left,
        SDL.Keycode.Right => Key.Right,
        SDL.Keycode.Up => Key.Up,
        SDL.Keycode.Down => Key.Down,
        SDL.Keycode.Home => Key.Home,
        SDL.Keycode.End => Key.End,
        SDL.Keycode.Pageup => Key.PageUp,
        SDL.Keycode.Pagedown => Key.PageDown,
        SDL.Keycode.Insert => Key.Insert,
        SDL.Keycode.Delete => Key.Delete,

        SDL.Keycode.Backspace => Key.Backspace,
        SDL.Keycode.Tab => Key.Tab,
        SDL.Keycode.Return or SDL.Keycode.Return2 => Key.Enter,
        SDL.Keycode.Space => Key.Space,
        SDL.Keycode.Escape => Key.Escape,

        _ => Key.Unknown,
    };

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
