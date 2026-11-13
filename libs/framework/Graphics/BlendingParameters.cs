namespace kajarlabs.Framework.Graphics;

/// <summary>
/// Specifies the scaling factor applied to source or destination color/alpha components during blending.
/// </summary>
public enum BlendFactor
{
    Zero,
    One,
    SourceColor,
    OneMinusSourceColor,
    DestinationColor,
    OneMinusDestinationColor,
    SourceAlpha,
    OneMinusSourceAlpha,
    DestinationAlpha,
    OneMinusDestinationAlpha,
    SourceAlphaSaturate,
}

/// <summary>
/// Specifies how weighted source and destination color/alpha components are combined.
/// </summary>
public enum BlendOperation
{
    Add,
    Subtract,
    ReverseSubtract,
    Min,
    Max,
}

/// <summary>
/// Describes color and alpha blending configuration for graphics pipeline state caching.
/// </summary>
public readonly struct BlendingParameters : IEquatable<BlendingParameters>
{
    /// <summary>
    /// Gets a configuration with blending disabled (source overwrites destination).
    /// </summary>
    public static readonly BlendingParameters NONE = new(
        BlendFactor.One,
        BlendFactor.Zero,
        BlendOperation.Add,
        BlendFactor.One,
        BlendFactor.Zero,
        BlendOperation.Add,
        isEnabled: false);

    /// <summary>
    /// Gets standard non-premultiplied alpha blending (<c>SrcAlpha</c> / <c>1 - SrcAlpha</c>).
    /// </summary>
    public static readonly BlendingParameters ALPHA = new(
        BlendFactor.SourceAlpha,
        BlendFactor.OneMinusSourceAlpha,
        BlendOperation.Add,
        BlendFactor.One,
        BlendFactor.OneMinusSourceAlpha,
        BlendOperation.Add,
        isEnabled: true);

    /// <summary>
    /// Gets premultiplied alpha blending (<c>One</c> / <c>1 - SrcAlpha</c>).
    /// </summary>
    public static readonly BlendingParameters PREMULTIPLIED_ALPHA = new(
        BlendFactor.One,
        BlendFactor.OneMinusSourceAlpha,
        BlendOperation.Add,
        BlendFactor.One,
        BlendFactor.OneMinusSourceAlpha,
        BlendOperation.Add,
        isEnabled: true);

    /// <summary>
    /// Gets additive color blending (<c>SrcAlpha</c> + <c>Dst</c>).
    /// </summary>
    public static readonly BlendingParameters ADDITIVE = new(
        BlendFactor.SourceAlpha,
        BlendFactor.One,
        BlendOperation.Add,
        BlendFactor.One,
        BlendFactor.One,
        BlendOperation.Add,
        isEnabled: true);

    /// <summary>
    /// Initializes a new instance of the <see cref="BlendingParameters"/> struct with separate color and alpha terms.
    /// </summary>
    public BlendingParameters(
        BlendFactor sourceColorFactor,
        BlendFactor destinationColorFactor,
        BlendOperation colorOperation,
        BlendFactor sourceAlphaFactor,
        BlendFactor destinationAlphaFactor,
        BlendOperation alphaOperation,
        bool isEnabled = true)
    {
        IsEnabled = isEnabled;
        SourceColorFactor = sourceColorFactor;
        DestinationColorFactor = destinationColorFactor;
        ColorOperation = colorOperation;
        SourceAlphaFactor = sourceAlphaFactor;
        DestinationAlphaFactor = destinationAlphaFactor;
        AlphaOperation = alphaOperation;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="BlendingParameters"/> struct using matching color and alpha factors.
    /// </summary>
    public BlendingParameters(
        BlendFactor sourceFactor,
        BlendFactor destinationFactor,
        BlendOperation operation = BlendOperation.Add,
        bool isEnabled = true)
        : this(
            sourceFactor,
            destinationFactor,
            operation,
            sourceFactor,
            destinationFactor,
            operation,
            isEnabled)
    {
    }

    /// <summary>
    /// Gets a value indicating whether blending is enabled.
    /// </summary>
    public bool IsEnabled { get; }

    /// <summary>
    /// Gets the blend factor applied to source RGB channels.
    /// </summary>
    public BlendFactor SourceColorFactor { get; }

    /// <summary>
    /// Gets the blend factor applied to destination RGB channels.
    /// </summary>
    public BlendFactor DestinationColorFactor { get; }

    /// <summary>
    /// Gets the blend operation combining source and destination RGB channels.
    /// </summary>
    public BlendOperation ColorOperation { get; }

    /// <summary>
    /// Gets the blend factor applied to the source alpha channel.
    /// </summary>
    public BlendFactor SourceAlphaFactor { get; }

    /// <summary>
    /// Gets the blend factor applied to the destination alpha channel.
    /// </summary>
    public BlendFactor DestinationAlphaFactor { get; }

    /// <summary>
    /// Gets the blend operation combining source and destination alpha channels.
    /// </summary>
    public BlendOperation AlphaOperation { get; }

    /// <summary>
    /// Determines whether two <see cref="BlendingParameters"/> values are equal.
    /// </summary>
    public static bool operator ==(BlendingParameters left, BlendingParameters right) => left.Equals(right);

    /// <summary>
    /// Determines whether two <see cref="BlendingParameters"/> values are not equal.
    /// </summary>
    public static bool operator !=(BlendingParameters left, BlendingParameters right) => !left.Equals(right);

    /// <inheritdoc />
    public bool Equals(BlendingParameters other)
        => IsEnabled == other.IsEnabled
            && SourceColorFactor == other.SourceColorFactor
            && DestinationColorFactor == other.DestinationColorFactor
            && ColorOperation == other.ColorOperation
            && SourceAlphaFactor == other.SourceAlphaFactor
            && DestinationAlphaFactor == other.DestinationAlphaFactor
            && AlphaOperation == other.AlphaOperation;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is BlendingParameters other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
        => HashCode.Combine(
            IsEnabled,
            SourceColorFactor,
            DestinationColorFactor,
            ColorOperation,
            SourceAlphaFactor,
            DestinationAlphaFactor,
            AlphaOperation);
}
