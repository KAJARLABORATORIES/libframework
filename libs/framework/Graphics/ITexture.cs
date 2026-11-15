using kajarlabs.Framework.Numerics;

namespace kajarlabs.Framework.Graphics;

/// <summary>
/// Represents a 2D RGBA8 texture owned by a renderer.
/// </summary>
public interface ITexture : IDisposable
{
    /// <summary>
    /// Gets the width of this texture in pixels.
    /// </summary>
    int Width { get; }

    /// <summary>
    /// Gets the height of this texture in pixels.
    /// </summary>
    int Height { get; }

    /// <summary>
    /// Uploads tightly packed RGBA8 pixel data covering the entire texture.
    /// </summary>
    /// <param name="rgba8Pixels">Pixel bytes of length <c>Width * Height * 4</c>.</param>
    void SetData(ReadOnlySpan<byte> rgba8Pixels);

    /// <summary>
    /// Uploads tightly packed RGBA8 pixel data into a rectangular sub-region of this texture.
    /// </summary>
    /// <param name="region">The destination pixel rectangle within this texture.</param>
    /// <param name="rgba8Pixels">Pixel bytes of length <c>region.Width * region.Height * 4</c>.</param>
    void SetData(RectangleI region, ReadOnlySpan<byte> rgba8Pixels);
}
