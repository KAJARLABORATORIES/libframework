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
    /// Clears the current frame color target to the specified <paramref name="color"/>.
    /// </summary>
    /// <param name="color">The RGBA clear color.</param>
    void Clear(Color4 color);
}
