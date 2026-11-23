namespace kajarlabs.Framework.Scene.Transforms;

/// <summary>
/// Evaluates an easing curve over normalized progress in <c>[0, 1]</c>.
/// </summary>
/// <remarks>
/// Implementations intended for per-frame transform evaluation should be <see langword="readonly"/> value types
/// so generic callers constrained to <see langword="struct"/>, <see cref="IEasingFunction"/> evaluate without boxing.
/// </remarks>
public interface IEasingFunction
{
    /// <summary>
    /// Applies the easing curve to the normalized progress <paramref name="time"/>.
    /// </summary>
    /// <param name="time">Normalized progress in <c>[0, 1]</c>.</param>
    /// <returns>The eased progress value.</returns>
    double ApplyEasing(double time);
}
