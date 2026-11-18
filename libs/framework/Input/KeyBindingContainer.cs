using System.Numerics;
using kajarlabs.Framework.Scene;

namespace kajarlabs.Framework.Input;

/// <summary>
/// A <see cref="Container"/> that maps <see cref="KeyCombination"/> inputs to strongly-typed
/// <typeparamref name="TAction"/> events and dispatches them to descendant
/// <see cref="IKeyBindingHandler{TAction}"/> components without runtime reflection.
/// </summary>
/// <typeparam name="TAction">The strongly-typed action enum or value type.</typeparam>
public class KeyBindingContainer<TAction> : Container
{
    private readonly List<KeyBinding<TAction>> sortedBindings = [];
    private readonly List<(Drawable Drawable, IKeyBindingHandler<TAction> Handler)> handlerTargets = [];
    private readonly List<ActiveBinding> activeBindings = [];

    private IReadOnlyList<KeyBinding<TAction>> bindings = [];
    private bool bindingsInitialized;
    private bool hasPositionalBindings;

    /// <summary>
    /// Initializes a new instance of the <see cref="KeyBindingContainer{TAction}"/> class.
    /// </summary>
    public KeyBindingContainer(SimultaneousBindingMode simultaneousMode = SimultaneousBindingMode.None)
    {
        SimultaneousMode = simultaneousMode;
    }

    /// <summary>
    /// Gets the simultaneous key-binding matching mode used by this container.
    /// </summary>
    public SimultaneousBindingMode SimultaneousMode { get; }

    /// <summary>
    /// Gets the default key bindings populated when <see cref="KeyBindings"/> is not explicitly assigned.
    /// </summary>
    public virtual IEnumerable<KeyBinding<TAction>> DefaultKeyBindings => [];

    /// <summary>
    /// Gets or sets the active list of key bindings evaluated by this container.
    /// </summary>
    public IReadOnlyList<KeyBinding<TAction>> KeyBindings
    {
        get
        {
            ensureBindingsInitialized();
            return bindings;
        }
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            setBindingsCore(value);
        }
    }

    /// <inheritdoc />
    public override bool HandleNonPositionalInput => true;

    /// <inheritdoc />
    public override bool HandlePositionalInput
    {
        get
        {
            ensureBindingsInitialized();
            return hasPositionalBindings;
        }
    }

    /// <inheritdoc />
    protected override void Load()
    {
        base.Load();
        ensureBindingsInitialized();
    }

    /// <inheritdoc />
    protected override bool OnKeyDown(KeyDownEvent e)
    {
        if (e.Repeat)
            return false;

        return tryTriggerBindings(InputKey.FromKey(e.Key), e.CurrentState, e.Repeat, positional: false);
    }

    /// <inheritdoc />
    protected override bool OnKeyUp(KeyUpEvent e)
    {
        releaseInvalidatedBindings(e.CurrentState);
        return false;
    }

    /// <inheritdoc />
    protected override bool OnMouseDown(MouseDownEvent e)
        => tryTriggerBindings(InputKey.FromMouseButton(e.Button), e.CurrentState, repeat: false, positional: true);

    /// <inheritdoc />
    protected override bool OnMouseUp(MouseUpEvent e)
    {
        releaseInvalidatedBindings(e.CurrentState);
        return false;
    }

    /// <inheritdoc />
    protected override bool OnScroll(ScrollEvent e)
    {
        var handled = false;
        var state = e.CurrentState;

        if (e.ScrollDelta.Y > 0f)
            handled |= tryTriggerBindings(InputKey.MouseWheelUp, in state, repeat: false, positional: true);
        else if (e.ScrollDelta.Y < 0f)
            handled |= tryTriggerBindings(InputKey.MouseWheelDown, in state, repeat: false, positional: true);

        if (e.ScrollDelta.X > 0f)
            handled |= tryTriggerBindings(InputKey.MouseWheelRight, in state, repeat: false, positional: true);
        else if (e.ScrollDelta.X < 0f)
            handled |= tryTriggerBindings(InputKey.MouseWheelLeft, in state, repeat: false, positional: true);

        return handled;
    }

    /// <inheritdoc />
    protected override void UpdateAfterChildren()
    {
        base.UpdateAfterChildren();

        if (activeBindings.Count == 0)
            return;

        var manager = findEnclosingInputManager();

        if (manager is not null)
        {
            var state = manager.CurrentState;
            releaseInvalidatedBindings(in state);
        }
        else
        {
            releaseDetachedOrHiddenBindings(InputState.EMPTY);
        }
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing && !IsDisposed)
        {
            releaseAllActiveBindings(InputState.EMPTY);
            handlerTargets.Clear();
        }

        base.Dispose(disposing);
    }

    private void ensureBindingsInitialized()
    {
        if (bindingsInitialized)
            return;

        setBindingsCore(DefaultKeyBindings);
    }

    private void setBindingsCore(IEnumerable<KeyBinding<TAction>> source)
    {
        releaseAllActiveBindings(InputState.EMPTY);

        var rawList = new List<KeyBinding<TAction>>();
        var anyPositional = false;

        foreach (var binding in source)
        {
            if (binding.KeyCombination.Count == 0)
                continue;

            rawList.Add(binding);

            if (binding.KeyCombination.ContainsPositionalKeys)
                anyPositional = true;
        }

        bindings = rawList.ToArray();
        hasPositionalBindings = anyPositional;
        bindingsInitialized = true;

        sortedBindings.Clear();
        sortedBindings.AddRange(rawList);

        // Stable insertion sort descending by combination length so chords (e.g., Ctrl+S) precede single keys (S).
        for (var i = 1; i < sortedBindings.Count; i++)
        {
            var current = sortedBindings[i];
            var currentLen = current.KeyCombination.Count;
            var j = i - 1;

            while (j >= 0 && sortedBindings[j].KeyCombination.Count < currentLen)
            {
                sortedBindings[j + 1] = sortedBindings[j];
                j--;
            }

            sortedBindings[j + 1] = current;
        }
    }

    private bool tryTriggerBindings(InputKey triggeredKey, in InputState state, bool repeat, bool positional)
    {
        ensureBindingsInitialized();

        if (sortedBindings.Count == 0)
            return false;

        collectHandlers(positional, state.Mouse.Position);

        if (handlerTargets.Count == 0)
            return false;

        var requireExact = SimultaneousMode == SimultaneousBindingMode.Unique;

        for (var i = 0; i < sortedBindings.Count; i++)
        {
            var binding = sortedBindings[i];

            if (!binding.KeyCombination.Matches(triggeredKey, in state, requireExact))
                continue;

            for (var j = 0; j < handlerTargets.Count; j++)
            {
                var (targetDrawable, handler) = handlerTargets[j];

                if (isBindingActive(binding, targetDrawable))
                    continue;

                var pressEvent = new KeyBindingPressEvent<TAction>(
                    state,
                    binding.Action,
                    binding.KeyCombination,
                    repeat,
                    targetDrawable);

                if (!handler.OnPressed(pressEvent))
                    continue;

                if (SimultaneousMode is SimultaneousBindingMode.None or SimultaneousBindingMode.Unique)
                    releaseAllActiveBindings(in state);

                if (triggeredKey.IsScroll)
                {
                    handler.OnReleased(new KeyBindingReleaseEvent<TAction>(
                        state,
                        binding.Action,
                        binding.KeyCombination,
                        targetDrawable));
                }
                else
                {
                    activeBindings.Add(new ActiveBinding(binding, handler, targetDrawable));
                }

                return true;
            }
        }

        return false;
    }

    private void collectHandlers(bool positional, Vector2 screenSpacePosition)
    {
        handlerTargets.Clear();
        collectHandlersRecursive(this, positional, screenSpacePosition);
    }

    private void collectHandlersRecursive(Drawable current, bool positional, Vector2 screenSpacePosition)
    {
        if (current.IsDisposed || current.LoadState == LoadState.Loading || !current.IsPresent)
            return;

        if (!ReferenceEquals(current, this) && current is InputManager nested && !nested.UseParentInput)
            return;

        if (current is CompositeDrawable composite)
        {
            if (positional && composite.Masking && !composite.ReceivePositionalInputAt(screenSpacePosition))
                return;

            var children = composite.InternalChildren;

            for (var i = children.Count - 1; i >= 0; i--)
                collectHandlersRecursive(children[i], positional, screenSpacePosition);
        }

        if (current is IKeyBindingHandler<TAction> handler)
        {
            if (!positional || ReferenceEquals(current, this) || current.ReceivePositionalInputAt(screenSpacePosition))
                handlerTargets.Add((current, handler));
        }
    }

    private bool isBindingActive(KeyBinding<TAction> binding, Drawable targetDrawable)
    {
        for (var i = 0; i < activeBindings.Count; i++)
        {
            var active = activeBindings[i];

            if (ReferenceEquals(active.HandlerDrawable, targetDrawable) && active.Binding.Equals(binding))
                return true;
        }

        return false;
    }

    private void releaseInvalidatedBindings(in InputState state)
    {
        for (var i = activeBindings.Count - 1; i >= 0; i--)
        {
            var active = activeBindings[i];

            if (isHandlerEligible(active.HandlerDrawable)
                && active.Binding.KeyCombination.IsPressed(in state))
            {
                continue;
            }

            activeBindings.RemoveAt(i);

            if (!active.HandlerDrawable.IsDisposed)
            {
                active.Handler.OnReleased(new KeyBindingReleaseEvent<TAction>(
                    state,
                    active.Binding.Action,
                    active.Binding.KeyCombination,
                    active.HandlerDrawable));
            }
        }
    }

    private void releaseDetachedOrHiddenBindings(in InputState state)
    {
        for (var i = activeBindings.Count - 1; i >= 0; i--)
        {
            var active = activeBindings[i];

            if (isHandlerEligible(active.HandlerDrawable))
                continue;

            activeBindings.RemoveAt(i);

            if (!active.HandlerDrawable.IsDisposed)
            {
                active.Handler.OnReleased(new KeyBindingReleaseEvent<TAction>(
                    state,
                    active.Binding.Action,
                    active.Binding.KeyCombination,
                    active.HandlerDrawable));
            }
        }
    }

    private void releaseAllActiveBindings(in InputState state)
    {
        while (activeBindings.Count > 0)
        {
            var lastIndex = activeBindings.Count - 1;
            var active = activeBindings[lastIndex];
            activeBindings.RemoveAt(lastIndex);

            if (!active.HandlerDrawable.IsDisposed)
            {
                active.Handler.OnReleased(new KeyBindingReleaseEvent<TAction>(
                    state,
                    active.Binding.Action,
                    active.Binding.KeyCombination,
                    active.HandlerDrawable));
            }
        }
    }

    private bool isHandlerEligible(Drawable handlerDrawable)
    {
        for (Drawable? current = handlerDrawable; current is not null; current = current.Parent)
        {
            if (current.IsDisposed || current.LoadState == LoadState.Loading || !current.IsPresent)
                return false;

            if (ReferenceEquals(current, this))
                return true;

            if (current is InputManager nested && !nested.UseParentInput)
                return false;
        }

        return false;
    }

    private InputManager? findEnclosingInputManager()
    {
        for (var current = Parent; current is not null; current = current.Parent)
        {
            if (current is InputManager manager)
                return manager;
        }

        return null;
    }

    private readonly record struct ActiveBinding(
        KeyBinding<TAction> Binding,
        IKeyBindingHandler<TAction> Handler,
        Drawable HandlerDrawable);
}
