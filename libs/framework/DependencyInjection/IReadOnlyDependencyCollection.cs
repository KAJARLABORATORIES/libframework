using System.Diagnostics.CodeAnalysis;

namespace kajarlabs.Framework.DependencyInjection;

/// <summary>
/// Provides read-only hierarchical dependency lookup without runtime reflection.
/// </summary>
[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "Established public API type name; renaming would be a breaking change.")]
public interface IReadOnlyDependencyCollection
{
    /// <summary>
    /// Attempts to resolve a dependency registered under <typeparamref name="T"/> from this collection
    /// or its ancestor fallback chain.
    /// </summary>
    bool TryGet<T>([MaybeNullWhen(false)] out T value);

    /// <summary>
    /// Resolves a dependency registered under <typeparamref name="T"/> from this collection
    /// or its ancestor fallback chain, throwing <see cref="DependencyNotFoundException"/> when absent.
    /// </summary>
    T Get<T>();

    /// <summary>
    /// Attempts to resolve a dependency registered under <paramref name="type"/> from this collection
    /// or its ancestor fallback chain.
    /// </summary>
    bool TryGet(Type type, [NotNullWhen(true)] out object? value);

    /// <summary>
    /// Resolves a dependency registered under <paramref name="type"/> from this collection
    /// or its ancestor fallback chain, throwing <see cref="DependencyNotFoundException"/> when absent.
    /// </summary>
    object Get(Type type);
}
