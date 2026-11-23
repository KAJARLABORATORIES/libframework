namespace kajarlabs.Framework.Scene;

/// <summary>
/// Represents a per-drawable extension component hooked into <see cref="Drawable"/>'s update and disposal lifecycle.
/// </summary>
public interface IDrawableExtension
{
    /// <summary>
    /// Invoked once per frame on the update thread during <see cref="Drawable.UpdateSubTree"/>,
    /// after scheduled callbacks and before <see cref="Drawable.Update"/>.
    /// </summary>
    /// <param name="target">The owning <see cref="Drawable"/>.</param>
    void OnUpdate(Drawable target);

    /// <summary>
    /// Invoked when the owning <see cref="Drawable"/> is disposed or when this extension is removed.
    /// </summary>
    /// <param name="target">The owning <see cref="Drawable"/>.</param>
    void OnDispose(Drawable target);
}
