namespace kajarlabs.Framework.DependencyInjection;

/// <summary>
/// Marks a class, field, or property to be automatically registered into the component's
/// <see cref="DependencyCollection"/> during compile-time generated dependency caching.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = true, Inherited = false)]
public sealed class CachedAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CachedAttribute"/> class.
    /// </summary>
    /// <param name="asType">
    /// Optional interface or base <see cref="Type"/> under which the instance is registered.
    /// When <see langword="null"/>, the concrete class or member type is used.
    /// </param>
    public CachedAttribute(Type? asType = null)
    {
        AsType = asType;
    }

    /// <summary>
    /// Gets the explicit registration <see cref="Type"/>, or <see langword="null"/> to use the target's own type.
    /// </summary>
    public Type? AsType { get; }
}
