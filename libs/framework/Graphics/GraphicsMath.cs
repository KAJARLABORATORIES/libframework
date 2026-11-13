using System.Numerics;

namespace kajarlabs.Framework.Graphics;

/// <summary>
/// Provides math helpers for 2D rendering and projection matrices targeting the SDL GPU clip-space convention.
/// </summary>
public static class GraphicsMath
{
    /// <summary>
    /// Creates an orthographic projection matrix mapping top-left screen coordinates <c>(0, 0)</c>..<c>(width, height)</c>
    /// to SDL GPU clip space (<c>X</c> in <c>[-1, 1]</c>, <c>Y</c> up in <c>[-1, 1]</c>, depth in <c>[0, 1]</c>).
    /// </summary>
    /// <remarks>
    /// The returned <see cref="Matrix4x4"/> follows <see cref="System.Numerics"/> row-vector convention
    /// (<c>Vector4.Transform(v, matrix)</c>), matching HLSL row-vector multiplication <c>mul(float4(pos, 0, 1), projection)</c>.
    /// </remarks>
    public static Matrix4x4 CreateOrthographicProjection(
        float width,
        float height,
        float nearPlane = 0f,
        float farPlane = 1f)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(width, 0f);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(height, 0f);

        return CreateOrthographicProjectionOffCenter(0f, width, height, 0f, nearPlane, farPlane);
    }

    /// <summary>
    /// Creates an orthographic projection matrix from explicit clipping plane bounds using SDL GPU
    /// <c>[0, 1]</c> depth range and <see cref="System.Numerics"/> row-vector layout.
    /// </summary>
    public static Matrix4x4 CreateOrthographicProjectionOffCenter(
        float left,
        float right,
        float bottom,
        float top,
        float nearPlane = 0f,
        float farPlane = 1f)
    {
        if (left == right)
            throw new ArgumentException("Left and right planes must differ.", nameof(right));

        if (bottom == top)
            throw new ArgumentException("Bottom and top planes must differ.", nameof(top));

        if (nearPlane == farPlane)
            throw new ArgumentException("Near and far planes must differ.", nameof(farPlane));

        return Matrix4x4.CreateOrthographicOffCenterLeftHanded(left, right, bottom, top, nearPlane, farPlane);
    }

    /// <summary>
    /// Creates an orthographic projection matrix matching the dimensions of <paramref name="viewport"/>.
    /// </summary>
    public static Matrix4x4 CreateOrthographicProjection(Viewport viewport)
        => CreateOrthographicProjection(viewport.Width, viewport.Height);
}
