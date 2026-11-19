using System.Numerics;
using System.Runtime.InteropServices;
using kajarlabs.Framework.Input;
using kajarlabs.Framework.Platform;
using SDL3;

namespace kajarlabs.Framework.Tests.Platform;

public sealed class SdlPlatformBackendInputTests
{
    [Fact]
    public void TranslatesMouseMotionEvent()
    {
        var sdlEvent = new SDL.Event
        {
            Type = (uint)SDL.EventType.MouseMotion,
            Motion = new SDL.MouseMotionEvent
            {
                Type = SDL.EventType.MouseMotion,
                WindowID = 7,
                X = 123.5f,
                Y = 456.25f,
                XRel = -4.5f,
                YRel = 12f,
            },
        };

        var translated = SdlPlatformBackend.TranslateSdlEvent(in sdlEvent);

        Assert.Equal(PlatformEventType.MouseMotion, translated.Type);
        Assert.Equal(7u, translated.WindowId);
        Assert.Equal(PlatformInputEventType.MouseMotion, translated.Input.Type);
        Assert.True(translated.Input.HasPosition);
        Assert.Equal(new Vector2(123.5f, 456.25f), translated.Input.Position);
        Assert.Equal(new Vector2(-4.5f, 12f), translated.Input.Delta);
    }

    [Theory]
    [InlineData((byte)SDL.ButtonLeft, true, MouseButton.Left, PlatformEventType.MouseButtonDown, PlatformInputEventType.MouseButtonDown)]
    [InlineData((byte)SDL.ButtonMiddle, false, MouseButton.Middle, PlatformEventType.MouseButtonUp, PlatformInputEventType.MouseButtonUp)]
    [InlineData((byte)SDL.ButtonRight, true, MouseButton.Right, PlatformEventType.MouseButtonDown, PlatformInputEventType.MouseButtonDown)]
    [InlineData((byte)SDL.ButtonX1, false, MouseButton.Button4, PlatformEventType.MouseButtonUp, PlatformInputEventType.MouseButtonUp)]
    [InlineData((byte)SDL.ButtonX2, true, MouseButton.Button5, PlatformEventType.MouseButtonDown, PlatformInputEventType.MouseButtonDown)]
    internal void TranslatesMouseButtonEvents(
        byte sdlButton,
        bool isDown,
        MouseButton expectedButton,
        PlatformEventType expectedPlatformType,
        PlatformInputEventType expectedInputType)
    {
        var eventType = isDown ? SDL.EventType.MouseButtonDown : SDL.EventType.MouseButtonUp;
        var sdlEvent = new SDL.Event
        {
            Type = (uint)eventType,
            Button = new SDL.MouseButtonEvent
            {
                Type = eventType,
                WindowID = 11,
                Button = sdlButton,
                Down = isDown,
                X = 300f,
                Y = 200f,
            },
        };

        var translated = SdlPlatformBackend.TranslateSdlEvent(in sdlEvent);

        Assert.Equal(expectedPlatformType, translated.Type);
        Assert.Equal(11u, translated.WindowId);
        Assert.Equal(expectedInputType, translated.Input.Type);
        Assert.Equal(expectedButton, translated.Input.MouseButton);
        Assert.True(translated.Input.HasPosition);
        Assert.Equal(new Vector2(300f, 200f), translated.Input.Position);
    }

    [Fact]
    public void UnknownMouseButtonTranslatesToOther()
    {
        var sdlEvent = new SDL.Event
        {
            Type = (uint)SDL.EventType.MouseButtonDown,
            Button = new SDL.MouseButtonEvent
            {
                Type = SDL.EventType.MouseButtonDown,
                WindowID = 1,
                Button = 99,
                Down = true,
            },
        };

        var translated = SdlPlatformBackend.TranslateSdlEvent(in sdlEvent);

        Assert.Equal(PlatformEventType.Other, translated.Type);
    }

    [Theory]
    [InlineData(0f, 2f, SDL.MouseWheelDirection.Normal, false)]
    [InlineData(0.25f, -1.5f, SDL.MouseWheelDirection.Flipped, true)]
    public void TranslatesMouseWheelEvent(float x, float y, SDL.MouseWheelDirection direction, bool expectedPrecise)
    {
        var sdlEvent = new SDL.Event
        {
            Type = (uint)SDL.EventType.MouseWheel,
            Wheel = new SDL.MouseWheelEvent
            {
                Type = SDL.EventType.MouseWheel,
                WindowID = 13,
                X = x,
                Y = y,
                Direction = direction,
                MouseX = 64f,
                MouseY = 128f,
            },
        };

        var translated = SdlPlatformBackend.TranslateSdlEvent(in sdlEvent);

        Assert.Equal(PlatformEventType.MouseWheel, translated.Type);
        Assert.Equal(13u, translated.WindowId);
        Assert.Equal(PlatformInputEventType.MouseWheel, translated.Input.Type);
        Assert.Equal(new Vector2(x, y), translated.Input.ScrollDelta);
        Assert.Equal(new Vector2(64f, 128f), translated.Input.Position);
        Assert.True(translated.Input.HasPosition);
        Assert.Equal(expectedPrecise, translated.Input.IsPrecise);
    }

    [Theory]
    [InlineData(SDL.Scancode.A, Key.A)]
    [InlineData(SDL.Scancode.Alpha9, Key.Number9)]
    [InlineData(SDL.Scancode.F12, Key.F12)]
    [InlineData(SDL.Scancode.F24, Key.F24)]
    [InlineData(SDL.Scancode.LShift, Key.ShiftLeft)]
    [InlineData(SDL.Scancode.RGUI, Key.SuperRight)]
    [InlineData(SDL.Scancode.Pageup, Key.PageUp)]
    [InlineData(SDL.Scancode.Return, Key.Enter)]
    [InlineData(SDL.Scancode.Escape, Key.Escape)]
    [InlineData(SDL.Scancode.Capslock, Key.CapsLock)]
    [InlineData(SDL.Scancode.NumLockClear, Key.NumLock)]
    [InlineData(SDL.Scancode.Printscreen, Key.PrintScreen)]
    [InlineData(SDL.Scancode.Application, Key.Menu)]
    [InlineData(SDL.Scancode.KpEnter, Key.KeypadEnter)]
    [InlineData(SDL.Scancode.KpPeriod, Key.KeypadDecimal)]
    [InlineData(SDL.Scancode.MediaPlayPause, Key.MediaPlayPause)]
    public void TranslatesKeyboardScancodesModifiersAndRepeat(SDL.Scancode scancode, Key expectedKey)
    {
        var downEvent = new SDL.Event
        {
            Type = (uint)SDL.EventType.KeyDown,
            Key = new SDL.KeyboardEvent
            {
                Type = SDL.EventType.KeyDown,
                WindowID = 5,
                Scancode = scancode,
                Mod = SDL.Keymod.LShift | SDL.Keymod.RCtrl,
                Down = true,
                Repeat = true,
            },
        };

        var translatedDown = SdlPlatformBackend.TranslateSdlEvent(in downEvent);

        Assert.Equal(PlatformEventType.KeyDown, translatedDown.Type);
        Assert.Equal(5u, translatedDown.WindowId);
        Assert.Equal(PlatformInputEventType.KeyDown, translatedDown.Input.Type);
        Assert.Equal(expectedKey, translatedDown.Input.Key);
        Assert.Equal(KeyModifiers.Shift | KeyModifiers.Control, translatedDown.Input.Modifiers);
        Assert.True(translatedDown.Input.IsRepeat);

        var upEvent = new SDL.Event
        {
            Type = (uint)SDL.EventType.KeyUp,
            Key = new SDL.KeyboardEvent
            {
                Type = SDL.EventType.KeyUp,
                WindowID = 5,
                Scancode = scancode,
                Mod = SDL.Keymod.LAlt | SDL.Keymod.RGUI,
                Down = false,
                Repeat = true,
            },
        };

        var translatedUp = SdlPlatformBackend.TranslateSdlEvent(in upEvent);

        Assert.Equal(PlatformEventType.KeyUp, translatedUp.Type);
        Assert.Equal(PlatformInputEventType.KeyUp, translatedUp.Input.Type);
        Assert.Equal(expectedKey, translatedUp.Input.Key);
        Assert.Equal(KeyModifiers.Alt | KeyModifiers.Super, translatedUp.Input.Modifiers);
        Assert.False(translatedUp.Input.IsRepeat);
    }

    [Fact]
    public void TranslatesKeycodeFallbackWhenScancodeIsUnknown()
    {
        var sdlEvent = new SDL.Event
        {
            Type = (uint)SDL.EventType.KeyDown,
            Key = new SDL.KeyboardEvent
            {
                Type = SDL.EventType.KeyDown,
                WindowID = 2,
                Scancode = SDL.Scancode.Unknown,
                Key = SDL.Keycode.Space,
                Down = true,
            },
        };

        var translated = SdlPlatformBackend.TranslateSdlEvent(in sdlEvent);

        Assert.Equal(PlatformEventType.KeyDown, translated.Type);
        Assert.Equal(Key.Space, translated.Input.Key);
    }

    [Fact]
    public void TranslatesTextInputImmediatelyDecodingUtf8NativePointer()
    {
        const string expected_text = "Olá 🌍";
        var nativeUtf8 = Marshal.StringToCoTaskMemUTF8(expected_text);
        PlatformEvent translated;

        try
        {
            var sdlEvent = new SDL.Event
            {
                Type = (uint)SDL.EventType.TextInput,
                Text = new SDL.TextInputEvent
                {
                    Type = SDL.EventType.TextInput,
                    WindowID = 9,
                    Text = nativeUtf8,
                },
            };

            translated = SdlPlatformBackend.TranslateSdlEvent(in sdlEvent);
        }
        finally
        {
            Marshal.FreeCoTaskMem(nativeUtf8);
        }

        Assert.Equal(PlatformEventType.TextInput, translated.Type);
        Assert.Equal(9u, translated.WindowId);
        Assert.Equal(PlatformInputEventType.TextInput, translated.Input.Type);
        Assert.Equal(expected_text, translated.Input.Text);
    }

    [Fact]
    public void EmptyOrNullTextInputTranslatesToOther()
    {
        var emptyPtr = Marshal.StringToCoTaskMemUTF8(string.Empty);

        try
        {
            var nullEvent = new SDL.Event
            {
                Type = (uint)SDL.EventType.TextInput,
                Text = new SDL.TextInputEvent { Type = SDL.EventType.TextInput, WindowID = 1, Text = nint.Zero },
            };

            var emptyEvent = new SDL.Event
            {
                Type = (uint)SDL.EventType.TextInput,
                Text = new SDL.TextInputEvent { Type = SDL.EventType.TextInput, WindowID = 1, Text = emptyPtr },
            };

            Assert.Equal(PlatformEventType.Other, SdlPlatformBackend.TranslateSdlEvent(in nullEvent).Type);
            Assert.Equal(PlatformEventType.Other, SdlPlatformBackend.TranslateSdlEvent(in emptyEvent).Type);
        }
        finally
        {
            Marshal.FreeCoTaskMem(emptyPtr);
        }
    }

    [Theory]
    [InlineData(SDL.EventType.WindowFocusGained, PlatformEventType.WindowFocusGained, PlatformInputEventType.WindowFocusGained)]
    [InlineData(SDL.EventType.WindowFocusLost, PlatformEventType.WindowFocusLost, PlatformInputEventType.WindowFocusLost)]
    [InlineData(SDL.EventType.WindowMouseEnter, PlatformEventType.WindowMouseEnter, PlatformInputEventType.WindowMouseEnter)]
    [InlineData(SDL.EventType.WindowMouseLeave, PlatformEventType.WindowMouseLeave, PlatformInputEventType.WindowMouseLeave)]
    internal void TranslatesWindowFocusAndCursorEvents(
        SDL.EventType sdlType,
        PlatformEventType expectedPlatformType,
        PlatformInputEventType expectedInputType)
    {
        var sdlEvent = new SDL.Event
        {
            Type = (uint)sdlType,
            Window = new SDL.WindowEvent
            {
                Type = sdlType,
                WindowID = 19,
            },
        };

        var translated = SdlPlatformBackend.TranslateSdlEvent(in sdlEvent);

        Assert.Equal(expectedPlatformType, translated.Type);
        Assert.Equal(19u, translated.WindowId);
        Assert.Equal(expectedInputType, translated.Input.Type);
    }

    [Fact]
    public void TranslatesWindowExposedEvent()
    {
        var sdlEvent = new SDL.Event
        {
            Type = (uint)SDL.EventType.WindowExposed,
            Window = new SDL.WindowEvent
            {
                Type = SDL.EventType.WindowExposed,
                WindowID = 23,
                Data1 = 1,
                Data2 = 0,
            },
        };

        var translated = SdlPlatformBackend.TranslateSdlEvent(in sdlEvent);

        Assert.Equal(PlatformEventType.WindowExposed, translated.Type);
        Assert.Equal(23u, translated.WindowId);
        Assert.Equal(1, translated.Data1);
        Assert.Equal(0, translated.Data2);
    }
}
