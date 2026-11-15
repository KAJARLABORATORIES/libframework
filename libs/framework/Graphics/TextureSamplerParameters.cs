namespace kajarlabs.Framework.Graphics;

/// <summary>
/// Specifies the minification, magnification, and mipmap filtering mode used when sampling a texture.
/// </summary>
public enum TextureFilteringMode
{
    Linear = 0,
    Nearest = 1,
}

/// <summary>
/// Specifies how texture coordinates outside <c>[0, 1]</c> are handled when sampling a texture.
/// </summary>
public enum TextureWrapMode
{
    ClampToEdge = 0,
    Repeat = 1,
}

/// <summary>
/// Describes texture sampling parameters for sampler caching and draw batching.
/// </summary>
public readonly struct TextureSamplerParameters : IEquatable<TextureSamplerParameters>
{
    /// <summary>
    /// Gets a sampler configuration using linear filtering and clamp-to-edge addressing.
    /// </summary>
    public static readonly TextureSamplerParameters LINEAR_CLAMP = new(
        TextureFilteringMode.Linear,
        TextureWrapMode.ClampToEdge);

    /// <summary>
    /// Gets a sampler configuration using nearest-neighbor filtering and clamp-to-edge addressing.
    /// </summary>
    public static readonly TextureSamplerParameters NEAREST_CLAMP = new(
        TextureFilteringMode.Nearest,
        TextureWrapMode.ClampToEdge);

    /// <summary>
    /// Gets a sampler configuration using linear filtering and repeat addressing.
    /// </summary>
    public static readonly TextureSamplerParameters LINEAR_REPEAT = new(
        TextureFilteringMode.Linear,
        TextureWrapMode.Repeat);

    /// <summary>
    /// Gets a sampler configuration using nearest-neighbor filtering and repeat addressing.
    /// </summary>
    public static readonly TextureSamplerParameters NEAREST_REPEAT = new(
        TextureFilteringMode.Nearest,
        TextureWrapMode.Repeat);

    /// <summary>
    /// Initializes a new instance of the <see cref="TextureSamplerParameters"/> struct.
    /// </summary>
    public TextureSamplerParameters(TextureFilteringMode filtering, TextureWrapMode wrapMode)
    {
        Filtering = filtering;
        WrapMode = wrapMode;
    }

    /// <summary>
    /// Gets the filtering mode applied during sampling.
    /// </summary>
    public TextureFilteringMode Filtering { get; }

    /// <summary>
    /// Gets the texture coordinate wrapping mode applied during sampling.
    /// </summary>
    public TextureWrapMode WrapMode { get; }

    /// <summary>
    /// Determines whether two <see cref="TextureSamplerParameters"/> values are equal.
    /// </summary>
    public static bool operator ==(TextureSamplerParameters left, TextureSamplerParameters right) => left.Equals(right);

    /// <summary>
    /// Determines whether two <see cref="TextureSamplerParameters"/> values are not equal.
    /// </summary>
    public static bool operator !=(TextureSamplerParameters left, TextureSamplerParameters right) => !left.Equals(right);

    /// <inheritdoc />
    public bool Equals(TextureSamplerParameters other)
        => Filtering == other.Filtering && WrapMode == other.WrapMode;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is TextureSamplerParameters other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Filtering, WrapMode);
}
