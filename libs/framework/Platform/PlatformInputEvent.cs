using System.Numerics;
using kajarlabs.Framework.Input;

namespace kajarlabs.Framework.Platform;

internal enum PlatformInputEventType
{
    MouseMotion = 0,
    MouseButtonDown = 1,
    MouseButtonUp = 2,
    MouseWheel = 3,
    KeyDown = 4,
    KeyUp = 5,
    TextInput = 6,
    WindowFocusGained = 7,
    WindowFocusLost = 8,
    WindowMouseEnter = 9,
    WindowMouseLeave = 10,
}

internal readonly record struct PlatformInputEvent
{
    public PlatformInputEventType Type { get; init; }

    public Vector2 Position { get; init; }

    public Vector2 Delta { get; init; }

    public Vector2 ScrollDelta { get; init; }

    public bool HasPosition { get; init; }

    public MouseButton MouseButton { get; init; }

    public Key Key { get; init; }

    public KeyModifiers Modifiers { get; init; }

    public bool IsRepeat { get; init; }

    public bool IsPrecise { get; init; }

    public string? Text { get; init; }

    public static PlatformInputEvent MouseMotion(Vector2 position, Vector2 delta)
        => new()
        {
            Type = PlatformInputEventType.MouseMotion,
            Position = position,
            Delta = delta,
            HasPosition = true,
        };

    public static PlatformInputEvent MouseButtonEvent(MouseButton button, bool isDown, Vector2? position)
        => new()
        {
            Type = isDown ? PlatformInputEventType.MouseButtonDown : PlatformInputEventType.MouseButtonUp,
            MouseButton = button,
            Position = position ?? Vector2.Zero,
            HasPosition = position.HasValue,
        };

    public static PlatformInputEvent MouseWheel(Vector2 scrollDelta, Vector2? position, bool isPrecise = false)
        => new()
        {
            Type = PlatformInputEventType.MouseWheel,
            ScrollDelta = scrollDelta,
            Position = position ?? Vector2.Zero,
            HasPosition = position.HasValue,
            IsPrecise = isPrecise,
        };

    public static PlatformInputEvent KeyEvent(Key key, bool isDown, KeyModifiers modifiers = KeyModifiers.None, bool isRepeat = false)
        => new()
        {
            Type = isDown ? PlatformInputEventType.KeyDown : PlatformInputEventType.KeyUp,
            Key = key,
            Modifiers = modifiers,
            IsRepeat = isDown && isRepeat,
        };

    public static PlatformInputEvent TextInput(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return new()
        {
            Type = PlatformInputEventType.TextInput,
            Text = text,
        };
    }

    public static PlatformInputEvent WindowFocus(bool hasFocus)
        => new()
        {
            Type = hasFocus ? PlatformInputEventType.WindowFocusGained : PlatformInputEventType.WindowFocusLost,
        };

    public static PlatformInputEvent WindowMouseEnter()
        => new()
        {
            Type = PlatformInputEventType.WindowMouseEnter,
        };

    public static PlatformInputEvent WindowMouseLeave()
        => new()
        {
            Type = PlatformInputEventType.WindowMouseLeave,
        };

    public PlatformInputEvent WithScaledPointer(Vector2 scale)
    {
        if (scale == Vector2.One)
            return this;

        return this with
        {
            Position = HasPosition ? Position * scale : Position,
            Delta = Type == PlatformInputEventType.MouseMotion ? Delta * scale : Delta,
        };
    }
}
