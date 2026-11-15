using System.Numerics;

namespace kajarlabs.Framework.Scene;

/// <summary>
/// Specifies an alignment anchor or origin point relative to a bounding box.
/// </summary>
[Flags]
public enum Anchor
{
    /// <summary>
    /// Top-left corner.
    /// </summary>
    TopLeft = Top | Left,

    /// <summary>
    /// Top-center edge.
    /// </summary>
    TopCentre = Top | HorizontalCentre,

    /// <summary>
    /// Top-right corner.
    /// </summary>
    TopRight = Top | Right,

    /// <summary>
    /// Center-left edge.
    /// </summary>
    CentreLeft = VerticalCentre | Left,

    /// <summary>
    /// Exact center.
    /// </summary>
    Centre = VerticalCentre | HorizontalCentre,

    /// <summary>
    /// Center-right edge.
    /// </summary>
    CentreRight = VerticalCentre | Right,

    /// <summary>
    /// Bottom-left corner.
    /// </summary>
    BottomLeft = Bottom | Left,

    /// <summary>
    /// Bottom-center edge.
    /// </summary>
    BottomCentre = Bottom | HorizontalCentre,

    /// <summary>
    /// Bottom-right corner.
    /// </summary>
    BottomRight = Bottom | Right,

    /// <summary>
    /// Vertical top alignment flag.
    /// </summary>
    Top = 1 << 0,

    /// <summary>
    /// Vertical center alignment flag.
    /// </summary>
    VerticalCentre = 1 << 1,

    /// <summary>
    /// Vertical bottom alignment flag.
    /// </summary>
    Bottom = 1 << 2,

    /// <summary>
    /// Horizontal left alignment flag.
    /// </summary>
    Left = 1 << 3,

    /// <summary>
    /// Horizontal center alignment flag.
    /// </summary>
    HorizontalCentre = 1 << 4,

    /// <summary>
    /// Horizontal right alignment flag.
    /// </summary>
    Right = 1 << 5,

    /// <summary>
    /// Custom anchor/origin specified explicitly via coordinates.
    /// </summary>
    Custom = 1 << 6,
}

/// <summary>
/// Provides helper methods for converting <see cref="Anchor"/> flags to normalized coordinates.
/// </summary>
public static class AnchorExtensions
{
    private const Anchor vertical_mask = Anchor.Top | Anchor.VerticalCentre | Anchor.Bottom;
    private const Anchor horizontal_mask = Anchor.Left | Anchor.HorizontalCentre | Anchor.Right;

    /// <summary>
    /// Returns the normalized <c>[0, 1]</c> offset corresponding to <paramref name="anchor"/>,
    /// or <see cref="Vector2.Zero"/> for <see cref="Anchor.Custom"/>.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Thrown when conflicting directional flags are combined on the same axis.
    /// </exception>
    public static Vector2 GetRelativeOffset(this Anchor anchor)
    {
        if (anchor.HasFlag(Anchor.Custom))
            return Vector2.Zero;

        var horizontal = anchor & horizontal_mask;
        var vertical = anchor & vertical_mask;

        if (int.PopCount((int)horizontal) > 1)
            throw new ArgumentException($"Anchor '{anchor}' specifies multiple horizontal alignment flags.", nameof(anchor));

        if (int.PopCount((int)vertical) > 1)
            throw new ArgumentException($"Anchor '{anchor}' specifies multiple vertical alignment flags.", nameof(anchor));

        var x = horizontal switch
        {
            Anchor.HorizontalCentre => 0.5f,
            Anchor.Right => 1f,
            _ => 0f,
        };

        var y = vertical switch
        {
            Anchor.VerticalCentre => 0.5f,
            Anchor.Bottom => 1f,
            _ => 0f,
        };

        return new Vector2(x, y);
    }
}
