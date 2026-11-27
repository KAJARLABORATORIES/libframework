using System.Numerics;
using System.Runtime.InteropServices;
using kajarlabs.Framework.Graphics.Rendering;

namespace kajarlabs.Framework.Graphics;

/// <summary>
/// Describes atlas-level parameters required by the built-in multi-channel signed distance field (MSDF)
/// fragment shader when rendering resolution-independent text quads.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public readonly struct MsdfRenderParameters : IEquatable<MsdfRenderParameters>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MsdfRenderParameters"/> struct.
    /// </summary>
    /// <param name="distanceRange">
    /// The signed distance field pixel range (<c>-pxrange</c>) baked into the MSDF atlas. Must be finite and positive.
    /// </param>
    /// <param name="atlasWidth">The width of the MSDF atlas texture in pixels. Must be positive.</param>
    /// <param name="atlasHeight">The height of the MSDF atlas texture in pixels. Must be positive.</param>
    /// <param name="distanceBias">
    /// Optional threshold bias added to the reconstructed signed distance before coverage evaluation
    /// (<c>0</c> by default, positive values thicken strokes and negative values thin them). Must be finite.
    /// </param>
    public MsdfRenderParameters(float distanceRange, int atlasWidth, int atlasHeight, float distanceBias = 0f)
    {
        if (!float.IsFinite(distanceRange) || distanceRange <= 0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(distanceRange),
                distanceRange,
                "MSDF distance range must be finite and positive.");
        }

        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(atlasWidth, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(atlasHeight, 0);

        if (!float.IsFinite(distanceBias))
        {
            throw new ArgumentOutOfRangeException(
                nameof(distanceBias),
                distanceBias,
                "MSDF distance bias must be finite.");
        }

        DistanceRange = distanceRange;
        AtlasWidth = atlasWidth;
        AtlasHeight = atlasHeight;
        DistanceBias = distanceBias == 0f ? 0f : distanceBias;
    }

    /// <summary>
    /// Gets the signed distance field pixel range baked into the MSDF atlas.
    /// </summary>
    public float DistanceRange { get; }

    /// <summary>
    /// Gets the width of the MSDF atlas texture in pixels.
    /// </summary>
    public int AtlasWidth { get; }

    /// <summary>
    /// Gets the height of the MSDF atlas texture in pixels.
    /// </summary>
    public int AtlasHeight { get; }

    /// <summary>
    /// Gets the optional threshold bias applied to the reconstructed signed distance.
    /// </summary>
    public float DistanceBias { get; }

    /// <summary>
    /// Gets the normalized distance range in UV space (<c>(DistanceRange / AtlasWidth, DistanceRange / AtlasHeight)</c>).
    /// </summary>
    public Vector2 UnitRange => IsValid
        ? new Vector2(DistanceRange / AtlasWidth, DistanceRange / AtlasHeight)
        : Vector2.Zero;

    internal bool IsValid
        => float.IsFinite(DistanceRange)
            && DistanceRange > 0f
            && AtlasWidth > 0
            && AtlasHeight > 0
            && float.IsFinite(DistanceBias);

    /// <summary>
    /// Determines whether two <see cref="MsdfRenderParameters"/> values are equal.
    /// </summary>
    public static bool operator ==(MsdfRenderParameters left, MsdfRenderParameters right) => left.Equals(right);

    /// <summary>
    /// Determines whether two <see cref="MsdfRenderParameters"/> values are not equal.
    /// </summary>
    public static bool operator !=(MsdfRenderParameters left, MsdfRenderParameters right) => !left.Equals(right);

    /// <inheritdoc />
    public bool Equals(MsdfRenderParameters other)
        => DistanceRange == other.DistanceRange
            && AtlasWidth == other.AtlasWidth
            && AtlasHeight == other.AtlasHeight
            && DistanceBias == other.DistanceBias;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is MsdfRenderParameters other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(DistanceRange, AtlasWidth, AtlasHeight, DistanceBias);

    internal void Validate(string paramName)
    {
        if (!IsValid)
        {
            throw new ArgumentException(
                "MSDF render parameters must have positive finite DistanceRange, positive atlas dimensions, and finite DistanceBias.",
                paramName);
        }
    }

    internal GpuMsdfFragmentUniform ToFragmentUniform()
        => new(new Vector2(DistanceRange / AtlasWidth, DistanceRange / AtlasHeight), DistanceBias, 0f);
}
