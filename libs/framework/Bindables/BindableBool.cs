namespace kajarlabs.Framework.Bindables;

/// <summary>
/// A <see cref="Bindable{T}"/> specialized for <see cref="bool"/> values with a <see cref="Toggle"/> helper.
/// </summary>
public class BindableBool : Bindable<bool>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="BindableBool"/> class.
    /// </summary>
    /// <param name="defaultValue">The initial <see cref="Bindable{T}.Value"/> and <see cref="Bindable{T}.Default"/>.</param>
    public BindableBool(bool defaultValue = false)
        : base(defaultValue)
    {
    }

    /// <summary>
    /// Inverts <see cref="Bindable{T}.Value"/>.
    /// </summary>
    public void Toggle() => Value = !Value;

    /// <inheritdoc />
    protected override Bindable<bool> CreateInstance() => new BindableBool();
}
