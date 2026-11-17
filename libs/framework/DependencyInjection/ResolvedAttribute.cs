namespace kajarlabs.Framework.DependencyInjection;

/// <summary>
/// Marks a mutable field or property to be populated from the component's
/// <see cref="IReadOnlyDependencyCollection"/> during compile-time generated dependency resolution.
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class ResolvedAttribute : Attribute
{
    /// <summary>
    /// Gets or sets a value indicating whether resolution may succeed with <see langword="null"/>
    /// when the dependency is absent. When unset, nullability annotations determine whether
    /// resolution is optional or required.
    /// </summary>
    public bool CanBeNull { get; set; }
}
