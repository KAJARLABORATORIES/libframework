using System.Diagnostics.CodeAnalysis;

namespace kajarlabs.Framework.DependencyInjection;

/// <summary>
/// A thread-safe, reflection-free dependency registry supporting optional parent fallback
/// and local shadowing for hierarchical component trees.
/// </summary>
[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "Established public API type name; renaming would be a breaking change.")]
public sealed class DependencyCollection : IReadOnlyDependencyCollection
{
    /// <summary>
    /// Gets a stateless empty <see cref="IReadOnlyDependencyCollection"/> instance.
    /// </summary>
    public static readonly IReadOnlyDependencyCollection EMPTY = new EmptyDependencyCollection();

    private readonly IReadOnlyDependencyCollection? parent;
    private readonly Lock syncLock = new();
    private readonly Dictionary<Type, object> dependencies = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="DependencyCollection"/> class.
    /// </summary>
    /// <param name="parent">Optional parent collection consulted when a dependency is not cached locally.</param>
    public DependencyCollection(IReadOnlyDependencyCollection? parent = null)
    {
        this.parent = parent;
    }

    /// <summary>
    /// Gets the number of dependencies cached directly in this collection (excluding ancestors).
    /// </summary>
    public int Count
    {
        get
        {
            lock (syncLock)
                return dependencies.Count;
        }
    }

    /// <summary>
    /// Caches <paramref name="instance"/> under the compile-time key <typeparamref name="T"/>.
    /// </summary>
    public void Cache<T>(T instance)
        where T : notnull
    {
        ArgumentNullException.ThrowIfNull(instance);
        cacheCore(typeof(T), instance);
    }

    /// <summary>
    /// Caches <paramref name="instance"/> under <paramref name="type"/> after validating assignability.
    /// </summary>
    public void CacheAs(Type type, object instance)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(instance);

        if (!type.IsInstanceOfType(instance))
        {
            throw new ArgumentException(
                $"Instance of type '{instance.GetType()}' is not assignable to dependency type '{type}'.",
                nameof(instance));
        }

        cacheCore(type, instance);
    }

    /// <inheritdoc />
    public bool TryGet<T>([MaybeNullWhen(false)] out T value)
    {
        if (tryGetLocal(typeof(T), out var found) && found is T typed)
        {
            value = typed;
            return true;
        }

        if (parent is not null)
            return parent.TryGet(out value);

        value = default;
        return false;
    }

    /// <inheritdoc />
    public T Get<T>()
        => TryGet<T>(out var value) ? value : throw new DependencyNotFoundException(typeof(T));

    /// <inheritdoc />
    public bool TryGet(Type type, [NotNullWhen(true)] out object? value)
    {
        ArgumentNullException.ThrowIfNull(type);

        if (tryGetLocal(type, out var found))
        {
            value = found;
            return true;
        }

        if (parent is not null)
            return parent.TryGet(type, out value);

        value = null;
        return false;
    }

    /// <inheritdoc />
    public object Get(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return TryGet(type, out var value) ? value : throw new DependencyNotFoundException(type);
    }

    private void cacheCore(Type type, object instance)
    {
        lock (syncLock)
        {
            if (!dependencies.TryAdd(type, instance))
            {
                throw new InvalidOperationException(
                    $"A dependency of type '{type}' is already cached in this {nameof(DependencyCollection)}.");
            }
        }
    }

    private bool tryGetLocal(Type type, [NotNullWhen(true)] out object? value)
    {
        lock (syncLock)
            return dependencies.TryGetValue(type, out value);
    }

    private sealed class EmptyDependencyCollection : IReadOnlyDependencyCollection
    {
        public bool TryGet<T>([MaybeNullWhen(false)] out T value)
        {
            value = default;
            return false;
        }

        public T Get<T>() => throw new DependencyNotFoundException(typeof(T));

        public bool TryGet(Type type, [NotNullWhen(true)] out object? value)
        {
            ArgumentNullException.ThrowIfNull(type);
            value = null;
            return false;
        }

        public object Get(Type type)
        {
            ArgumentNullException.ThrowIfNull(type);
            throw new DependencyNotFoundException(type);
        }
    }
}
