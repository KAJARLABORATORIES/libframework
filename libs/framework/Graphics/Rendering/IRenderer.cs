using System.Numerics;
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
    /// Gets the number of indexed draw calls submitted during the most recently completed frame.
    /// </summary>
    int LastFrameDrawCallCount { get; }

    /// <summary>
    /// Gets the number of quads queued and submitted during the most recently completed frame.
    /// </summary>
    int LastFrameQuadCount { get; }

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

    /// <summary>
    /// Sets the active blending configuration for subsequent draw operations in the current frame.
    /// </summary>
    void SetBlending(BlendingParameters blending);

    /// <summary>
    /// Sets the active texture sampler configuration for subsequent draw operations in the current frame.
    /// </summary>
    void SetSampler(TextureSamplerParameters sampler);

    /// <summary>
    /// Sets the active scissor rectangle in pixel coordinates, or <see langword="null"/> to restore
    /// the full active viewport bounds.
    /// </summary>
    void SetScissor(RectangleI? scissor);

    /// <summary>
    /// Sets the active orthographic or custom vertex projection matrix, or <see langword="null"/>
    /// to restore the default projection matching <see cref="Viewport"/>.
    /// </summary>
    void SetProjection(Matrix4x4? projection);

    /// <summary>
    /// Queues a textured, color-tinted 2D quad covering the entire texture using full <c>[0, 1]</c> UVs.
    /// </summary>
    void DrawQuad(ITexture texture, RectangleF destination, Color4 color);

    /// <summary>
    /// Queues a textured, color-tinted 2D quad with explicit normalized UV coordinates.
    /// </summary>
    void DrawQuad(ITexture texture, RectangleF destination, RectangleF textureCoords, Color4 color);

    /// <summary>
    /// Queues a textured, color-tinted 2D transformed <see cref="Quad"/> covering the entire texture
    /// using full <c>[0, 1]</c> UVs.
    /// </summary>
    void DrawQuad(ITexture texture, Quad destination, Color4 color);

    /// <summary>
    /// Queues a textured, color-tinted 2D transformed <see cref="Quad"/> with explicit normalized UV coordinates.
    /// </summary>
    void DrawQuad(ITexture texture, Quad destination, RectangleF textureCoords, Color4 color);
}
