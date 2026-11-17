namespace kajarlabs.Framework.Scene;

/// <summary>
/// Represents the lifecycle loading state of a <see cref="Drawable"/>.
/// </summary>
public enum LoadState
{
    /// <summary>
    /// The drawable has not started loading.
    /// </summary>
    NotLoaded,

    /// <summary>
    /// The drawable is currently resolving dependencies and executing <see cref="Drawable.Load()"/>.
    /// </summary>
    Loading,

    /// <summary>
    /// The drawable has completed <see cref="Drawable.Load()"/> and is awaiting <see cref="Drawable.LoadComplete"/> on the update thread.
    /// </summary>
    Ready,

    /// <summary>
    /// The drawable has completed both <see cref="Drawable.Load()"/> and <see cref="Drawable.LoadComplete"/>.
    /// </summary>
    Loaded,
}
