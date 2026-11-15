using kajarlabs.Framework.Numerics;

namespace kajarlabs.Framework.Graphics.Rendering;

/// <summary>
/// Provides the minimal platform-agnostic rendering surface exposed by a <see cref="Platform.GameHost"/>
/// and <see cref="Game"/>.
/// </summary>
public interface IRenderer
{
    /// <summary>
    /// Gets the current render target viewport in pixels.
    /// </summary>
    Viewport Viewport { get; }

    /// <summary>
    /// Gets the built-in 1x1 opaque white texture used for solid or tinted untextured primitives.
    /// </summary>
    ITexture WhitePixel { get; }

    /// <summary>
    /// Creates a 2D RGBA8 texture with the specified pixel dimensions.
    /// </summary>
    /// <param name="width">The texture width in pixels (must be positive).</param>
    /// <param name="height">The texture height in pixels (must be positive).</param>
    ITexture CreateTexture(int width, int height);

    /// <summary>
    /// Clears the current frame color target to the specified <paramref name="color"/>.
    /// </summary>
    /// <param name="color">The RGBA clear color.</param>
    void Clear(Color4 color);
}
