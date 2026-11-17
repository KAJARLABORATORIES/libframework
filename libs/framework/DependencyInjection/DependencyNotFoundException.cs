namespace kajarlabs.Framework.DependencyInjection;

/// <summary>
/// Exception thrown when a required dependency type cannot be resolved from an
/// <see cref="IReadOnlyDependencyCollection"/> hierarchy.
/// </summary>
public sealed class DependencyNotFoundException : InvalidOperationException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DependencyNotFoundException"/> class.
    /// </summary>
    public DependencyNotFoundException(Type dependencyType)
        : base($"Dependency of type '{dependencyType?.FullName ?? dependencyType?.Name ?? "<null>"}' could not be resolved.")
    {
        ArgumentNullException.ThrowIfNull(dependencyType);
        DependencyType = dependencyType;
    }

    /// <summary>
    /// Gets the missing dependency <see cref="Type"/> that could not be resolved.
    /// </summary>
    public Type DependencyType { get; }
}
