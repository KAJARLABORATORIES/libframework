namespace kajarlabs.Framework.Input;

/// <summary>
/// Controls how a <see cref="KeyBindingContainer{TAction}"/> evaluates simultaneous or overlapping key presses.
/// </summary>
public enum SimultaneousBindingMode
{
    /// <summary>
    /// Triggers when a combination's keys are pressed without requiring exclusivity;
    /// activating a new binding automatically releases any previously active binding in the container.
    /// </summary>
    None = 0,

    /// <summary>
    /// Allows extra keys to be held and multiple matching bindings to remain active concurrently.
    /// </summary>
    All = 1,

    /// <summary>
    /// Triggers only when the exact <see cref="KeyCombination"/> is pressed with no extra keys, modifiers, or mouse buttons held.
    /// </summary>
    Unique = 2,
}

/// <summary>
/// Associates a <see cref="Input.KeyCombination"/> with a strongly-typed <typeparamref name="TAction"/>.
/// </summary>
/// <typeparam name="TAction">The action type dispatched when the binding triggers.</typeparam>
public readonly record struct KeyBinding<TAction>(KeyCombination KeyCombination, TAction Action)
{
    /// <summary>
    /// Initializes a new <see cref="KeyBinding{TAction}"/> from a single <see cref="InputKey"/> and <paramref name="action"/>.
    /// </summary>
    public KeyBinding(InputKey key, TAction action)
        : this(new KeyCombination(key), action)
    {
    }
}
