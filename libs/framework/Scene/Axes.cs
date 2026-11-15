namespace kajarlabs.Framework.Scene;

/// <summary>
/// Specifies one or both 2D layout axes.
/// </summary>
[Flags]
public enum Axes
{
    /// <summary>
    /// Neither axis.
    /// </summary>
    None = 0,

    /// <summary>
    /// The horizontal axis.
    /// </summary>
    X = 1 << 0,

    /// <summary>
    /// The vertical axis.
    /// </summary>
    Y = 1 << 1,

    /// <summary>
    /// Both horizontal and vertical axes.
    /// </summary>
    Both = X | Y,
}
