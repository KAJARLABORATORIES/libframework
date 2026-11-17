namespace kajarlabs.Framework.Bindables;

/// <summary>
/// Non-generic read-only and lifecycle contract for bindable values.
/// </summary>
public interface IBindable
{
    /// <summary>
    /// Occurs when <see cref="Disabled"/> changes.
    /// </summary>
    event Action<bool>? DisabledChanged;

    /// <summary>
    /// Gets a value indicating whether mutations to this bindable are disabled.
    /// </summary>
    bool Disabled { get; }

    /// <summary>
    /// Gets a value indicating whether the current value equals the default value.
    /// </summary>
    bool IsDefault { get; }

    /// <summary>
    /// Subscribes <paramref name="onChange"/> to <see cref="DisabledChanged"/>,
    /// optionally invoking it immediately with the current <see cref="Disabled"/> state.
    /// </summary>
    void BindDisabledChanged(Action<bool> onChange, bool runOnceImmediately = false);

    /// <summary>
    /// Removes all event subscribers attached to this bindable without disconnecting bound peers.
    /// </summary>
    void UnbindEvents();

    /// <summary>
    /// Disconnects all peer bindables bound to this instance while keeping event subscribers intact.
    /// </summary>
    void UnbindBindings();

    /// <summary>
    /// Removes all event subscribers and disconnects all bound peer bindables.
    /// </summary>
    void UnbindAll();
}

/// <summary>
/// Read-only reactive bindable interface exposing value and disabled notifications
/// and creating bound copies without permitting direct value mutation.
/// </summary>
public interface IBindable<T> : IBindable
{
    /// <summary>
    /// Occurs when <see cref="Value"/> changes.
    /// </summary>
    event Action<ValueChangedEvent<T>>? ValueChanged;

    /// <summary>
    /// Gets the current value.
    /// </summary>
    T Value { get; }

    /// <summary>
    /// Gets the default value.
    /// </summary>
    T Default { get; }

    /// <summary>
    /// Subscribes <paramref name="onChange"/> to <see cref="ValueChanged"/>,
    /// optionally invoking it immediately with the current <see cref="Value"/>.
    /// </summary>
    void BindValueChanged(Action<ValueChangedEvent<T>> onChange, bool runOnceImmediately = false);

    /// <summary>
    /// Binds this instance bidirectionally to <paramref name="them"/>.
    /// </summary>
    void BindTo(IBindable<T> them);

    /// <summary>
    /// Creates a new independent bindable instance of the same type bound to this instance.
    /// </summary>
    IBindable<T> GetBoundCopy();
}
