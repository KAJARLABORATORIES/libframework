using kajarlabs.Framework.Platform;

namespace kajarlabs.Framework.Input;

/// <summary>
/// An <see cref="InputManager"/> that optionally forwards input events received from an ancestor
/// <see cref="InputManager"/> into its own descendant subtree.
/// </summary>
public class PassThroughInputManager : InputManager
{
    private bool receivedEventThisFrame;

    /// <summary>
    /// Initializes a new instance of the <see cref="PassThroughInputManager"/> class.
    /// </summary>
    public PassThroughInputManager()
    {
        base.UseParentInput = true;
    }

    /// <summary>
    /// Gets or sets a value indicating whether this manager accepts and forwards input from an ancestor <see cref="InputManager"/>.
    /// </summary>
    public new bool UseParentInput
    {
        get => base.UseParentInput;
        set
        {
            if (base.UseParentInput == value)
                return;

            base.UseParentInput = value;
            Invalidate(Scene.Invalidation.DrawNode);
        }
    }

    /// <inheritdoc />
    public override bool HandlePositionalInput => base.UseParentInput;

    /// <inheritdoc />
    public override bool HandleNonPositionalInput => base.UseParentInput;

    /// <inheritdoc />
    protected override bool OnHover(HoverEvent e)
    {
        if (!base.UseParentInput)
            return false;

        receivedEventThisFrame = true;
        SyncWindowFocusFromParent(e.CurrentState.WindowHasFocus);
        return UpdateHoverFromParent(e.ScreenSpaceMousePosition);
    }

    /// <inheritdoc />
    protected override void OnHoverLost(HoverLostEvent e)
    {
        if (!base.UseParentInput)
            return;

        receivedEventThisFrame = true;
        HandlePlatformInputEvent(PlatformInputEvent.WindowMouseLeave());
    }

    /// <inheritdoc />
    protected override bool OnMouseMove(MouseMoveEvent e)
    {
        if (!base.UseParentInput)
            return false;

        receivedEventThisFrame = true;
        SyncWindowFocusFromParent(e.CurrentState.WindowHasFocus);
        return HandlePlatformInputEvent(PlatformInputEvent.MouseMotion(e.ScreenSpaceMousePosition, e.ScreenSpaceDelta));
    }

    /// <inheritdoc />
    protected override bool OnMouseDown(MouseDownEvent e)
    {
        if (!base.UseParentInput)
            return false;

        receivedEventThisFrame = true;
        SyncWindowFocusFromParent(e.CurrentState.WindowHasFocus);
        return HandlePlatformInputEvent(PlatformInputEvent.MouseButtonEvent(e.Button, isDown: true, e.ScreenSpaceMousePosition));
    }

    /// <inheritdoc />
    protected override bool OnMouseUp(MouseUpEvent e)
    {
        if (!base.UseParentInput)
            return false;

        receivedEventThisFrame = true;
        SyncWindowFocusFromParent(e.CurrentState.WindowHasFocus);
        return HandlePlatformInputEvent(PlatformInputEvent.MouseButtonEvent(e.Button, isDown: false, e.ScreenSpaceMousePosition));
    }

    /// <inheritdoc />
    protected override bool OnScroll(ScrollEvent e)
    {
        if (!base.UseParentInput)
            return false;

        receivedEventThisFrame = true;
        SyncWindowFocusFromParent(e.CurrentState.WindowHasFocus);
        return HandlePlatformInputEvent(PlatformInputEvent.MouseWheel(e.ScrollDelta, e.ScreenSpaceMousePosition, e.IsPrecise));
    }

    /// <inheritdoc />
    protected override bool OnKeyDown(KeyDownEvent e)
    {
        if (!base.UseParentInput)
            return false;

        receivedEventThisFrame = true;
        SyncWindowFocusFromParent(e.CurrentState.WindowHasFocus);
        return HandlePlatformInputEvent(PlatformInputEvent.KeyEvent(e.Key, isDown: true, e.Modifiers, e.Repeat));
    }

    /// <inheritdoc />
    protected override bool OnKeyUp(KeyUpEvent e)
    {
        if (!base.UseParentInput)
            return false;

        receivedEventThisFrame = true;
        SyncWindowFocusFromParent(e.CurrentState.WindowHasFocus);
        return HandlePlatformInputEvent(PlatformInputEvent.KeyEvent(e.Key, isDown: false, e.Modifiers));
    }

    /// <inheritdoc />
    protected override bool OnTextInput(TextInputEvent e)
    {
        if (!base.UseParentInput)
            return false;

        receivedEventThisFrame = true;
        SyncWindowFocusFromParent(e.CurrentState.WindowHasFocus);
        return HandlePlatformInputEvent(PlatformInputEvent.TextInput(e.Text));
    }

    /// <inheritdoc />
    protected override void UpdateAfterChildren()
    {
        if (base.UseParentInput && !receivedEventThisFrame)
            BeginInputFrame();

        receivedEventThisFrame = false;
        base.UpdateAfterChildren();
    }
}
