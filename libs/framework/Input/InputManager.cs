using System.Numerics;
using kajarlabs.Framework.Platform;
using kajarlabs.Framework.Scene;

namespace kajarlabs.Framework.Input;

/// <summary>
/// Manages input state snapshots, positional/non-positional event dispatch, hover/drag/click lifecycles,
/// and focus transitions across its descendant <see cref="Drawable"/> subtree.
/// </summary>
public class InputManager : Container
{
    private readonly List<Drawable> positionalQueue = [];
    private readonly List<Drawable> nonPositionalQueue = [];
    private readonly List<Drawable> hoveredDrawables = [];
    private readonly List<Drawable> newHoveredDrawables = [];
    private readonly List<Drawable> tempTargets = [];
    private readonly HashSet<Drawable> previousHoveredSet = [];
    private readonly HashSet<Drawable> newHoveredSet = [];
    private readonly HashSet<Drawable> hoverBlockingSet = [];
    private readonly Dictionary<MouseButton, ButtonDownState> pressedButtons = [];
    private readonly Dictionary<MouseButton, LastClickState> lastClicks = [];

    private GameWindow? boundWindow;
    private bool lastRequestedTextInput;
    private int focusChangeVersion;
    private long lastHoverSubTreeInvalidationId = -1;

    /// <summary>
    /// Gets the current immutable input snapshot observed by this <see cref="InputManager"/>.
    /// </summary>
    public InputState CurrentState { get; private set; } = InputState.EMPTY;

    /// <summary>
    /// Gets the <see cref="Drawable"/> currently holding keyboard input focus, or <see langword="null"/> if none.
    /// </summary>
    public Drawable? FocusedDrawable { get; private set; }

    /// <summary>
    /// Gets the currently hovered drawables ordered front-to-back.
    /// </summary>
    public IReadOnlyList<Drawable> HoveredDrawables => hoveredDrawables;

    /// <summary>
    /// Gets or sets the minimum screen-space distance in pixels required before a pointer press transitions into a drag.
    /// </summary>
    public float DragDistanceThreshold { get; set; } = 10f;

    /// <summary>
    /// Gets or sets the maximum elapsed clock time in milliseconds between consecutive clicks to qualify as a double-click.
    /// </summary>
    public double DoubleClickTimeThreshold { get; set; } = 250.0;

    /// <summary>
    /// Gets or sets the maximum screen-space distance in pixels between consecutive clicks to qualify as a double-click.
    /// </summary>
    public float DoubleClickDistanceThreshold { get; set; } = 10f;

    /// <summary>
    /// Gets a value indicating whether this <see cref="InputManager"/> receives input events from a parent <see cref="InputManager"/>.
    /// </summary>
    public bool UseParentInput { get; protected set; }

    /// <inheritdoc />
    public override bool HandlePositionalInput => UseParentInput;

    /// <inheritdoc />
    public override bool HandleNonPositionalInput => UseParentInput;

    /// <inheritdoc />
    public override bool BuildPositionalInputQueue(Vector2 screenSpacePosition, List<Drawable> queue)
    {
        ArgumentNullException.ThrowIfNull(queue);

        if (!UseParentInput || IsDisposed || LoadState == LoadState.Loading || !IsPresent)
            return false;

        if (ReceivePositionalInputAt(screenSpacePosition))
            queue.Add(this);

        return true;
    }

    /// <inheritdoc />
    public override bool BuildNonPositionalInputQueue(List<Drawable> queue)
    {
        ArgumentNullException.ThrowIfNull(queue);

        if (!UseParentInput || IsDisposed || LoadState == LoadState.Loading || !IsPresent)
            return false;

        queue.Add(this);
        return true;
    }

    /// <summary>
    /// Transitions keyboard focus to <paramref name="potentialFocusTarget"/> (or clears focus when <see langword="null"/>).
    /// </summary>
    /// <returns><see langword="true"/> if focus is now held by <paramref name="potentialFocusTarget"/>; otherwise <see langword="false"/>.</returns>
    public bool ChangeFocus(Drawable? potentialFocusTarget)
    {
        if (ReferenceEquals(FocusedDrawable, potentialFocusTarget))
            return true;

        if (potentialFocusTarget is not null && !canHoldFocus(potentialFocusTarget))
            return false;

        focusChangeVersion++;

        var previous = FocusedDrawable;
        FocusedDrawable = null;

        if (previous is not null)
        {
            previous.HasFocus = false;

            if (!previous.IsDisposed)
                previous.TriggerEvent(new FocusLostEvent(CurrentState, potentialFocusTarget));
        }

        // If a re-entrant ChangeFocus occurred during OnFocusLost, honour the newer focus assignment.
        if (FocusedDrawable is not null)
            return ReferenceEquals(FocusedDrawable, potentialFocusTarget);

        FocusedDrawable = potentialFocusTarget;

        if (potentialFocusTarget is not null)
        {
            potentialFocusTarget.HasFocus = true;
            potentialFocusTarget.TriggerEvent(new FocusEvent(CurrentState, previous));
        }

        syncWindowTextInput();
        return ReferenceEquals(FocusedDrawable, potentialFocusTarget);
    }

    /// <summary>
    /// Searches the subtree rooted at <paramref name="triggerSource"/> (or this <see cref="InputManager"/> when <see langword="null"/>)
    /// in front-to-back order for the highest-priority drawable with <see cref="Drawable.RequestsFocus"/> set, and focuses it.
    /// </summary>
    /// <returns><see langword="true"/> if a focusable drawable requested focus and acquired it; otherwise <see langword="false"/>.</returns>
    public bool TriggerFocusContention(Drawable? triggerSource = null)
    {
        var root = triggerSource ?? this;

        if (!ReferenceEquals(root, this) && !isAttachedToThis(root))
            return false;

        var candidate = findFocusContentionCandidate(root, isRoot: true);
        return candidate is not null && ChangeFocus(candidate);
    }

    internal void HandlePlatformInputEvents(IReadOnlyList<PlatformInputEvent> events)
    {
        BeginInputFrame();

        for (var i = 0; i < events.Count; i++)
        {
            var evt = events[i];
            HandlePlatformInputEvent(in evt);
        }
    }

    internal void BeginInputFrame()
    {
        var clearedMouse = CurrentState.Mouse.WithClearedDeltas();

        if (clearedMouse != CurrentState.Mouse)
            CurrentState = CurrentState.WithMouse(clearedMouse);
    }

    internal bool HandlePlatformInputEvent(in PlatformInputEvent inputEvent)
    {
        switch (inputEvent.Type)
        {
            case PlatformInputEventType.MouseMotion:
                return handleMouseMotion(inputEvent.Position, inputEvent.Delta);

            case PlatformInputEventType.MouseButtonDown:
                return handleMouseButtonDown(inputEvent.MouseButton, inputEvent.HasPosition ? inputEvent.Position : null);

            case PlatformInputEventType.MouseButtonUp:
                return handleMouseButtonUp(inputEvent.MouseButton, inputEvent.HasPosition ? inputEvent.Position : null);

            case PlatformInputEventType.MouseWheel:
                return handleMouseWheel(
                    inputEvent.ScrollDelta,
                    inputEvent.HasPosition ? inputEvent.Position : null,
                    inputEvent.IsPrecise);

            case PlatformInputEventType.KeyDown:
                return handleKeyDown(inputEvent.Key, inputEvent.Modifiers, inputEvent.IsRepeat);

            case PlatformInputEventType.KeyUp:
                return handleKeyUp(inputEvent.Key, inputEvent.Modifiers);

            case PlatformInputEventType.TextInput:
                return !string.IsNullOrEmpty(inputEvent.Text) && handleTextInput(inputEvent.Text);

            case PlatformInputEventType.WindowFocusGained:
                CurrentState = CurrentState.WithWindowHasFocus(true);
                return false;

            case PlatformInputEventType.WindowFocusLost:
                handleWindowFocusLost();
                return false;

            case PlatformInputEventType.WindowMouseEnter:
                return false;

            case PlatformInputEventType.WindowMouseLeave:
                handleWindowMouseLeave();
                return false;

            default:
                return false;
        }
    }

    internal void SyncWindowFocusFromParent(bool hasFocus)
    {
        if (CurrentState.WindowHasFocus == hasFocus)
            return;

        if (hasFocus)
            CurrentState = CurrentState.WithWindowHasFocus(true);
        else
            handleWindowFocusLost();
    }

    internal bool UpdateHoverFromParent(Vector2 screenSpacePosition)
    {
        if (!CurrentState.Mouse.IsPositionValid || CurrentState.Mouse.Position != screenSpacePosition)
            CurrentState = CurrentState.WithMouse(CurrentState.Mouse.WithPosition(screenSpacePosition, updateDelta: false));

        return updateHover();
    }

    /// <inheritdoc />
    protected override void Load()
    {
        base.Load();
        Dependencies.TryGet(out boundWindow);
        syncWindowTextInput();
    }

    /// <inheritdoc />
    protected override void UpdateAfterChildren()
    {
        base.UpdateAfterChildren();

        if (FocusedDrawable is { } focused)
        {
            if (!canHoldFocus(focused))
                ChangeFocus(null);
            else
                syncWindowTextInput();
        }

        validateActiveDrags();

        if (CurrentState.Mouse.IsPositionValid && lastHoverSubTreeInvalidationId != SubTreeInvalidationID)
            updateHover();
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing && !IsDisposed)
        {
            ChangeFocus(null);
            clearHover();
            endAllDragsWithoutClick();
            pressedButtons.Clear();
            lastClicks.Clear();
            positionalQueue.Clear();
            nonPositionalQueue.Clear();
            tempTargets.Clear();
            boundWindow = null;
        }

        base.Dispose(disposing);
    }

    private bool handleMouseMotion(Vector2 position, Vector2 explicitDelta)
    {
        var nextMouse = CurrentState.Mouse.WithPosition(position);

        if (nextMouse.Delta == Vector2.Zero && explicitDelta != Vector2.Zero)
            nextMouse = nextMouse.WithDelta(explicitDelta);

        CurrentState = CurrentState.WithMouse(nextMouse);

        updateHover();

        var moveConsumed = false;
        buildOwnPositionalQueue(CurrentState.Mouse.Position, positionalQueue);

        if (positionalQueue.Count > 0)
        {
            var moveEvent = new MouseMoveEvent(CurrentState);

            for (var i = 0; i < positionalQueue.Count; i++)
            {
                if (positionalQueue[i].TriggerEvent(moveEvent))
                {
                    moveConsumed = true;
                    break;
                }
            }
        }

        updateActiveDrags();
        return moveConsumed;
    }

    private bool handleMouseButtonDown(MouseButton button, Vector2? position)
    {
        var mouse = CurrentState.Mouse;

        if (position is { } pos)
            mouse = mouse.WithPosition(pos);

        mouse = mouse.WithButtonDown(button);
        CurrentState = CurrentState.WithMouse(mouse);

        if (position.HasValue)
            updateHover();

        if (!pressedButtons.TryGetValue(button, out var downState))
        {
            downState = new ButtonDownState();
            pressedButtons[button] = downState;
        }
        else
        {
            if (downState.DraggedDrawable is { } oldDragged)
            {
                oldDragged.IsDragged = false;
                downState.DraggedDrawable = null;
            }

            downState.MouseDownTargets.Clear();
        }

        downState.ScreenSpaceDownPosition = CurrentState.Mouse.Position;

        Drawable? focusCandidate = null;
        var consumed = false;
        var focusVersionBefore = focusChangeVersion;

        if (CurrentState.Mouse.IsPositionValid)
        {
            buildOwnPositionalQueue(CurrentState.Mouse.Position, positionalQueue);

            if (positionalQueue.Count > 0)
            {
                var downEvent = new MouseDownEvent(CurrentState, button);

                for (var i = 0; i < positionalQueue.Count; i++)
                {
                    var target = positionalQueue[i];
                    downState.MouseDownTargets.Add(target);

                    if (button == MouseButton.Left && focusCandidate is null && target.AcceptsFocus)
                        focusCandidate = target;

                    if (target.TriggerEvent(downEvent))
                    {
                        consumed = true;
                        break;
                    }
                }
            }
        }

        if (button == MouseButton.Left && focusChangeVersion == focusVersionBefore)
            ChangeFocus(focusCandidate);

        return consumed;
    }

    private bool handleMouseButtonUp(MouseButton button, Vector2? position)
    {
        var mouse = CurrentState.Mouse;

        if (position is { } pos)
            mouse = mouse.WithPosition(pos);

        mouse = mouse.WithButtonUp(button);
        CurrentState = CurrentState.WithMouse(mouse);

        if (position.HasValue)
            updateHover();

        if (!pressedButtons.Remove(button, out var downState))
            return false;

        var consumed = false;

        if (downState.MouseDownTargets.Count > 0)
        {
            var upEvent = new MouseUpEvent(CurrentState, button, downState.ScreenSpaceDownPosition);

            for (var i = 0; i < downState.MouseDownTargets.Count; i++)
            {
                var target = downState.MouseDownTargets[i];

                if (target.IsDisposed || !isAttachedToThis(target))
                    continue;

                if (target.TriggerEvent(upEvent))
                {
                    consumed = true;
                    break;
                }
            }
        }

        if (downState.DraggedDrawable is { } dragged)
        {
            dragged.IsDragged = false;

            if (!dragged.IsDisposed && isAttachedToThis(dragged))
                dragged.TriggerEvent(new DragEndEvent(CurrentState, button, downState.ScreenSpaceDownPosition));

            return consumed;
        }

        if (downState.HasDragged || !CurrentState.Mouse.IsPositionValid)
            return consumed;

        tempTargets.Clear();

        for (var i = 0; i < downState.MouseDownTargets.Count; i++)
        {
            var target = downState.MouseDownTargets[i];

            if (!target.IsDisposed
                && target.LoadState != LoadState.Loading
                && target.IsPresent
                && isAttachedToThis(target)
                && target.ReceivePositionalInputAt(CurrentState.Mouse.Position))
            {
                tempTargets.Add(target);
            }
        }

        if (tempTargets.Count == 0)
            return consumed;

        var doubleClickHandled = false;

        if (lastClicks.TryGetValue(button, out var previousClick)
            && Time - previousClick.Time <= DoubleClickTimeThreshold
            && Vector2.DistanceSquared(CurrentState.Mouse.Position, previousClick.Position) <= DoubleClickDistanceThreshold * DoubleClickDistanceThreshold)
        {
            var doubleClickEvent = new DoubleClickEvent(CurrentState, button, downState.ScreenSpaceDownPosition);

            for (var i = 0; i < tempTargets.Count; i++)
            {
                if (tempTargets[i].TriggerEvent(doubleClickEvent))
                {
                    doubleClickHandled = true;
                    consumed = true;
                    break;
                }
            }
        }

        if (doubleClickHandled)
        {
            lastClicks.Remove(button);
        }
        else
        {
            var clickEvent = new ClickEvent(CurrentState, button, downState.ScreenSpaceDownPosition);

            for (var i = 0; i < tempTargets.Count; i++)
            {
                if (tempTargets[i].TriggerEvent(clickEvent))
                {
                    consumed = true;
                    break;
                }
            }

            lastClicks[button] = new LastClickState(Time, CurrentState.Mouse.Position);
        }

        tempTargets.Clear();
        return consumed;
    }

    private bool handleMouseWheel(Vector2 scrollDelta, Vector2? position, bool isPrecise)
    {
        var mouse = CurrentState.Mouse;

        if (position is { } pos)
            mouse = mouse.WithPosition(pos);

        mouse = mouse.WithScrollDelta(scrollDelta);
        CurrentState = CurrentState.WithMouse(mouse);

        if (position.HasValue)
            updateHover();

        if (!CurrentState.Mouse.IsPositionValid)
            return false;

        buildOwnPositionalQueue(CurrentState.Mouse.Position, positionalQueue);

        if (positionalQueue.Count == 0)
            return false;

        var scrollEvent = new ScrollEvent(CurrentState, scrollDelta, isPrecise);

        for (var i = 0; i < positionalQueue.Count; i++)
        {
            if (positionalQueue[i].TriggerEvent(scrollEvent))
                return true;
        }

        return false;
    }

    private bool handleKeyDown(Key key, KeyModifiers modifiers, bool isRepeat)
    {
        var kb = CurrentState.Keyboard.WithKeyDown(key);

        if (modifiers != KeyModifiers.None)
            kb = kb.WithModifiers(kb.Modifiers | modifiers);

        CurrentState = CurrentState.WithKeyboard(kb);

        var keyDownEvent = new KeyDownEvent(CurrentState, key, isRepeat);

        if (FocusedDrawable is { } focused && canHoldFocus(focused))
        {
            if (focused.TriggerEvent(keyDownEvent))
                return true;
        }

        buildOwnNonPositionalQueue(nonPositionalQueue);

        for (var i = 0; i < nonPositionalQueue.Count; i++)
        {
            if (nonPositionalQueue[i].TriggerEvent(keyDownEvent))
                return true;
        }

        return false;
    }

    private bool handleKeyUp(Key key, KeyModifiers modifiers)
    {
        var kb = CurrentState.Keyboard.WithKeyUp(key);
        var physicalModifiers = computePhysicalModifiers(kb);
        var effectiveModifiers = physicalModifiers | (modifiers & ~key.ToKeyModifiers());
        kb = kb.WithModifiers(effectiveModifiers);

        CurrentState = CurrentState.WithKeyboard(kb);

        var keyUpEvent = new KeyUpEvent(CurrentState, key);

        if (FocusedDrawable is { } focused && canHoldFocus(focused))
        {
            if (focused.TriggerEvent(keyUpEvent))
                return true;
        }

        buildOwnNonPositionalQueue(nonPositionalQueue);

        for (var i = 0; i < nonPositionalQueue.Count; i++)
        {
            if (nonPositionalQueue[i].TriggerEvent(keyUpEvent))
                return true;
        }

        return false;
    }

    private bool handleTextInput(string text)
    {
        var textEvent = new TextInputEvent(CurrentState, text);

        if (FocusedDrawable is { } focused && canHoldFocus(focused))
        {
            if (focused.TriggerEvent(textEvent))
                return true;
        }

        buildOwnNonPositionalQueue(nonPositionalQueue);

        for (var i = 0; i < nonPositionalQueue.Count; i++)
        {
            if (nonPositionalQueue[i].TriggerEvent(textEvent))
                return true;
        }

        return false;
    }

    private void handleWindowFocusLost()
    {
        CurrentState = CurrentState.WithWindowHasFocus(false);

        if (CurrentState.Keyboard.AnyKeyDown)
        {
            var pressedKeys = CurrentState.Keyboard.GetPressedKeys();

            for (var i = 0; i < pressedKeys.Length; i++)
                handleKeyUp(pressedKeys[i], KeyModifiers.None);
        }

        if (CurrentState.Keyboard.Modifiers != KeyModifiers.None)
            CurrentState = CurrentState.WithKeyboard(KeyboardState.EMPTY);

        endAllDragsWithoutClick();
        lastClicks.Clear();
        ChangeFocus(null);
    }

    private void handleWindowMouseLeave()
    {
        CurrentState = CurrentState.WithMouse(CurrentState.Mouse.WithPositionInvalid());
        clearHover();
    }

    private bool updateHover()
    {
        lastHoverSubTreeInvalidationId = SubTreeInvalidationID;

        if (!CurrentState.Mouse.IsPositionValid)
        {
            clearHover();
            return false;
        }

        buildOwnPositionalQueue(CurrentState.Mouse.Position, positionalQueue);

        previousHoveredSet.Clear();
        for (var i = 0; i < hoveredDrawables.Count; i++)
            previousHoveredSet.Add(hoveredDrawables[i]);

        newHoveredDrawables.Clear();
        newHoveredSet.Clear();

        var blocked = false;
        var hoverEvent = new HoverEvent(CurrentState);

        for (var i = 0; i < positionalQueue.Count; i++)
        {
            var target = positionalQueue[i];

            if (!newHoveredSet.Add(target))
                continue;

            newHoveredDrawables.Add(target);

            if (previousHoveredSet.Contains(target))
            {
                if (hoverBlockingSet.Contains(target))
                {
                    blocked = true;
                    break;
                }

                continue;
            }

            target.IsHovered = true;

            if (target.TriggerEvent(hoverEvent))
            {
                hoverBlockingSet.Add(target);
                blocked = true;
                break;
            }
        }

        if (hoveredDrawables.Count > 0)
        {
            var hoverLostEvent = new HoverLostEvent(CurrentState);

            for (var i = 0; i < hoveredDrawables.Count; i++)
            {
                var previous = hoveredDrawables[i];

                if (newHoveredSet.Contains(previous))
                    continue;

                hoverBlockingSet.Remove(previous);
                previous.IsHovered = false;

                if (!previous.IsDisposed)
                    previous.TriggerEvent(hoverLostEvent);
            }
        }

        hoveredDrawables.Clear();
        hoveredDrawables.AddRange(newHoveredDrawables);
        newHoveredDrawables.Clear();
        previousHoveredSet.Clear();
        newHoveredSet.Clear();

        return blocked;
    }

    private void clearHover()
    {
        if (hoveredDrawables.Count == 0)
        {
            hoverBlockingSet.Clear();
            return;
        }

        var hoverLostEvent = new HoverLostEvent(CurrentState);

        for (var i = 0; i < hoveredDrawables.Count; i++)
        {
            var target = hoveredDrawables[i];
            target.IsHovered = false;

            if (!target.IsDisposed)
                target.TriggerEvent(hoverLostEvent);
        }

        hoveredDrawables.Clear();
        hoverBlockingSet.Clear();
    }

    private void updateActiveDrags()
    {
        if (pressedButtons.Count == 0)
            return;

        var thresholdSq = DragDistanceThreshold > 0f ? DragDistanceThreshold * DragDistanceThreshold : 0f;

        foreach (var (button, downState) in pressedButtons)
        {
            if (downState.DraggedDrawable is null)
            {
                var movedSq = Vector2.DistanceSquared(CurrentState.Mouse.Position, downState.ScreenSpaceDownPosition);
                var exceededThreshold = thresholdSq > 0f
                    ? movedSq >= thresholdSq
                    : CurrentState.Mouse.Position != downState.ScreenSpaceDownPosition;

                if (!exceededThreshold)
                    continue;

                var dragStartEvent = new DragStartEvent(CurrentState, button, downState.ScreenSpaceDownPosition);

                for (var i = 0; i < downState.MouseDownTargets.Count; i++)
                {
                    var candidate = downState.MouseDownTargets[i];

                    if (candidate.IsDisposed || !candidate.IsPresent || !isAttachedToThis(candidate))
                        continue;

                    if (candidate.TriggerEvent(dragStartEvent))
                    {
                        downState.DraggedDrawable = candidate;
                        downState.HasDragged = true;
                        candidate.IsDragged = true;
                        break;
                    }
                }
            }
            else if (downState.DraggedDrawable is { } activeDragged)
            {
                if (activeDragged.IsDisposed || !activeDragged.IsPresent || !isAttachedToThis(activeDragged))
                {
                    endDrag(button, downState);
                    continue;
                }

                activeDragged.TriggerEvent(new DragEvent(CurrentState, button, downState.ScreenSpaceDownPosition));
            }
        }
    }

    private void validateActiveDrags()
    {
        if (pressedButtons.Count == 0)
            return;

        foreach (var (button, downState) in pressedButtons)
        {
            if (downState.DraggedDrawable is { } activeDragged
                && (activeDragged.IsDisposed || !activeDragged.IsPresent || !isAttachedToThis(activeDragged)))
            {
                endDrag(button, downState);
            }
        }
    }

    private void endDrag(MouseButton button, ButtonDownState downState)
    {
        if (downState.DraggedDrawable is not { } dragged)
            return;

        downState.DraggedDrawable = null;
        dragged.IsDragged = false;

        if (!dragged.IsDisposed)
            dragged.TriggerEvent(new DragEndEvent(CurrentState, button, downState.ScreenSpaceDownPosition));
    }

    private void endAllDragsWithoutClick()
    {
        if (pressedButtons.Count == 0)
            return;

        Span<MouseButton> buttons = stackalloc MouseButton[8];
        var count = 0;

        foreach (var button in pressedButtons.Keys)
        {
            if (count < buttons.Length)
                buttons[count++] = button;
        }

        for (var i = 0; i < count; i++)
        {
            var button = buttons[i];

            if (!pressedButtons.Remove(button, out var downState))
                continue;

            CurrentState = CurrentState.WithMouse(CurrentState.Mouse.WithButtonUp(button));

            var upEvent = new MouseUpEvent(CurrentState, button, downState.ScreenSpaceDownPosition);

            for (var j = 0; j < downState.MouseDownTargets.Count; j++)
            {
                var target = downState.MouseDownTargets[j];

                if (!target.IsDisposed && isAttachedToThis(target))
                    target.TriggerEvent(upEvent);
            }

            endDrag(button, downState);
        }
    }

    private void buildOwnPositionalQueue(Vector2 screenSpacePosition, List<Drawable> queue)
    {
        queue.Clear();

        if (IsDisposed || LoadState == LoadState.Loading || !IsPresent)
            return;

        if (Masking && !ReceivePositionalInputAt(screenSpacePosition))
            return;

        EnsureChildrenSorted();
        var children = InternalChildren;

        for (var i = children.Count - 1; i >= 0; i--)
            children[i].BuildPositionalInputQueue(screenSpacePosition, queue);
    }

    private void buildOwnNonPositionalQueue(List<Drawable> queue)
    {
        queue.Clear();

        if (IsDisposed || LoadState == LoadState.Loading || !IsPresent)
            return;

        EnsureChildrenSorted();
        var children = InternalChildren;

        for (var i = children.Count - 1; i >= 0; i--)
            children[i].BuildNonPositionalInputQueue(queue);
    }

    private bool canHoldFocus(Drawable target)
        => target.AcceptsFocus
            && !target.IsDisposed
            && target.LoadState != LoadState.Loading
            && target.IsPresent
            && isAttachedToThis(target);

    private bool isAttachedToThis(Drawable target)
    {
        for (var current = target.Parent; current is not null; current = current.Parent)
        {
            if (ReferenceEquals(current, this))
                return true;

            if (current is InputManager nested && !nested.UseParentInput)
                return false;
        }

        return false;
    }

    private Drawable? findFocusContentionCandidate(Drawable current, bool isRoot)
    {
        if (current.IsDisposed || current.LoadState == LoadState.Loading || !current.IsPresent)
            return null;

        if (!isRoot && current is InputManager nested && !nested.UseParentInput)
            return null;

        if (current is CompositeDrawable composite)
        {
            var children = composite.InternalChildren;

            for (var i = children.Count - 1; i >= 0; i--)
            {
                var found = findFocusContentionCandidate(children[i], isRoot: false);

                if (found is not null)
                    return found;
            }
        }

        if (!ReferenceEquals(current, this) && current.AcceptsFocus && current.RequestsFocus)
            return current;

        return null;
    }

    private void syncWindowTextInput()
    {
        var shouldActivate = FocusedDrawable is { RequestsTextInput: true } focused && canHoldFocus(focused);

        if (boundWindow is null || shouldActivate == lastRequestedTextInput)
            return;

        lastRequestedTextInput = shouldActivate;

        if (shouldActivate)
            boundWindow.StartTextInput();
        else
            boundWindow.StopTextInput();
    }

    private static KeyModifiers computePhysicalModifiers(KeyboardState kb)
    {
        var mods = KeyModifiers.None;

        if (kb.IsKeyDown(Key.ShiftLeft) || kb.IsKeyDown(Key.ShiftRight))
            mods |= KeyModifiers.Shift;

        if (kb.IsKeyDown(Key.ControlLeft) || kb.IsKeyDown(Key.ControlRight))
            mods |= KeyModifiers.Control;

        if (kb.IsKeyDown(Key.AltLeft) || kb.IsKeyDown(Key.AltRight))
            mods |= KeyModifiers.Alt;

        if (kb.IsKeyDown(Key.SuperLeft) || kb.IsKeyDown(Key.SuperRight))
            mods |= KeyModifiers.Super;

        return mods;
    }

    private sealed class ButtonDownState
    {
        public Vector2 ScreenSpaceDownPosition { get; set; }

        public List<Drawable> MouseDownTargets { get; } = [];

        public Drawable? DraggedDrawable { get; set; }

        public bool HasDragged { get; set; }
    }

    private readonly record struct LastClickState(double Time, Vector2 Position);
}
