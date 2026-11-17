namespace kajarlabs.Framework.Bindables;

/// <summary>
/// Carries the previous and updated values for a <see cref="IBindable{T}.ValueChanged"/> notification.
/// </summary>
public readonly struct ValueChangedEvent<T> : IEquatable<ValueChangedEvent<T>>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ValueChangedEvent{T}"/> struct.
    /// </summary>
    public ValueChangedEvent(T oldValue, T newValue)
    {
        OldValue = oldValue;
        NewValue = newValue;
    }

    /// <summary>
    /// Gets the value before the change.
    /// </summary>
    public T OldValue { get; }

    /// <summary>
    /// Gets the value after the change.
    /// </summary>
    public T NewValue { get; }

    /// <summary>
    /// Determines whether two <see cref="ValueChangedEvent{T}"/> values are equal.
    /// </summary>
    public static bool operator ==(ValueChangedEvent<T> left, ValueChangedEvent<T> right) => left.Equals(right);

    /// <summary>
    /// Determines whether two <see cref="ValueChangedEvent{T}"/> values are not equal.
    /// </summary>
    public static bool operator !=(ValueChangedEvent<T> left, ValueChangedEvent<T> right) => !left.Equals(right);

    /// <inheritdoc />
    public bool Equals(ValueChangedEvent<T> other)
        => EqualityComparer<T>.Default.Equals(OldValue, other.OldValue)
            && EqualityComparer<T>.Default.Equals(NewValue, other.NewValue);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is ValueChangedEvent<T> other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
        => HashCode.Combine(
            OldValue is null ? 0 : EqualityComparer<T>.Default.GetHashCode(OldValue),
            NewValue is null ? 0 : EqualityComparer<T>.Default.GetHashCode(NewValue));

    /// <inheritdoc />
    public override string ToString() => $"{OldValue} -> {NewValue}";
}
