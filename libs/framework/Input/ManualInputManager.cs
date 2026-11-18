using System.Numerics;
using kajarlabs.Framework.Platform;

namespace kajarlabs.Framework.Input;

/// <summary>
/// A deterministic, self-contained <see cref="InputManager"/> for testing scene trees with synchronous
/// synthetic input helpers and <see cref="PassThroughInputManager.UseParentInput"/> disabled by default.
/// </summary>
public class ManualInputManager : PassThroughInputManager
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ManualInputManager"/> class.
    /// </summary>
    public ManualInputManager()
    {
        UseParentInput = false;
    }

    /// <summary>
    /// Synchronously moves the pointer to <paramref name="screenSpacePosition"/>.
    /// </summary>
    public void MoveMouseTo(Vector2 screenSpacePosition)
    {
        var delta = CurrentState.Mouse.IsPositionValid
            ? screenSpacePosition - CurrentState.Mouse.Position
            : Vector2.Zero;

        BeginInputFrame();
        HandlePlatformInputEvent(PlatformInputEvent.MouseMotion(screenSpacePosition, delta));
    }

    /// <summary>
    /// Synchronously presses <paramref name="button"/> at the current pointer position.
    /// </summary>
    public void PressButton(MouseButton button)
    {
        BeginInputFrame();
        Vector2? position = CurrentState.Mouse.IsPositionValid ? CurrentState.Mouse.Position : null;
        HandlePlatformInputEvent(PlatformInputEvent.MouseButtonEvent(button, isDown: true, position));
    }

    /// <summary>
    /// Synchronously releases <paramref name="button"/> at the current pointer position.
    /// </summary>
    public void ReleaseButton(MouseButton button)
    {
        BeginInputFrame();
        Vector2? position = CurrentState.Mouse.IsPositionValid ? CurrentState.Mouse.Position : null;
        HandlePlatformInputEvent(PlatformInputEvent.MouseButtonEvent(button, isDown: false, position));
    }

    /// <summary>
    /// Synchronously clicks <paramref name="button"/>, optionally moving to <paramref name="screenSpacePosition"/> first.
    /// </summary>
    public void Click(MouseButton button, Vector2? screenSpacePosition = null)
    {
        if (screenSpacePosition is { } pos)
            MoveMouseTo(pos);

        PressButton(button);
        ReleaseButton(button);
    }

    /// <summary>
    /// Synchronously dispatches a scroll wheel event with <paramref name="scrollDelta"/> at the current pointer position.
    /// </summary>
    public void Scroll(Vector2 scrollDelta, bool isPrecise = false)
    {
        BeginInputFrame();
        Vector2? position = CurrentState.Mouse.IsPositionValid ? CurrentState.Mouse.Position : null;
        HandlePlatformInputEvent(PlatformInputEvent.MouseWheel(scrollDelta, position, isPrecise));
    }

    /// <summary>
    /// Synchronously simulates the pointer leaving the window bounds.
    /// </summary>
    public void MouseLeave()
    {
        BeginInputFrame();
        HandlePlatformInputEvent(PlatformInputEvent.WindowMouseLeave());
    }

    /// <summary>
    /// Synchronously presses <paramref name="key"/> with optional <paramref name="modifiers"/> and <paramref name="repeat"/> state.
    /// </summary>
    public void PressKey(Key key, KeyModifiers modifiers = KeyModifiers.None, bool repeat = false)
    {
        BeginInputFrame();
        HandlePlatformInputEvent(PlatformInputEvent.KeyEvent(key, isDown: true, modifiers, repeat));
    }

    /// <summary>
    /// Synchronously releases <paramref name="key"/>.
    /// </summary>
    public void ReleaseKey(Key key, KeyModifiers modifiers = KeyModifiers.None)
    {
        BeginInputFrame();
        HandlePlatformInputEvent(PlatformInputEvent.KeyEvent(key, isDown: false, modifiers));
    }

    /// <summary>
    /// Synchronously delivers committed UTF-8 <paramref name="text"/> input.
    /// </summary>
    public void InputText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (text.Length == 0)
            return;

        BeginInputFrame();
        HandlePlatformInputEvent(PlatformInputEvent.TextInput(text));
    }

    /// <summary>
    /// Synchronously updates window focus state, releasing focus, drags, and held keys when focus is lost.
    /// </summary>
    public void SetWindowFocus(bool hasFocus)
    {
        BeginInputFrame();
        HandlePlatformInputEvent(PlatformInputEvent.WindowFocus(hasFocus));
    }
}
