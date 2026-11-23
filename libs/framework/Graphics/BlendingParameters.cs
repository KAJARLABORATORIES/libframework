namespace kajarlabs.Framework.Graphics;

/// <summary>
/// Specifies the scaling factor applied to source or destination color/alpha components during blending.
/// </summary>
public enum BlendFactor
{
    /// <summary>
    /// Scales components by <c>0</c>.
    /// </summary>
    Zero,

    /// <summary>
    /// Scales components by <c>1</c>.
    /// </summary>
    One,

    /// <summary>
    /// Scales components by the source color channels (<c>SrcColor</c>).
    /// </summary>
    SourceColor,

    /// <summary>
    /// Scales components by the inverse source color channels (<c>1 - SrcColor</c>).
    /// </summary>
    OneMinusSourceColor,

    /// <summary>
    /// Scales components by the destination color channels (<c>DstColor</c>).
    /// </summary>
    DestinationColor,

    /// <summary>
    /// Scales components by the inverse destination color channels (<c>1 - DstColor</c>).
    /// </summary>
    OneMinusDestinationColor,

    /// <summary>
    /// Scales components by the source alpha channel (<c>SrcAlpha</c>).
    /// </summary>
    SourceAlpha,

    /// <summary>
    /// Scales components by the inverse source alpha channel (<c>1 - SrcAlpha</c>).
    /// </summary>
    OneMinusSourceAlpha,

    /// <summary>
    /// Scales components by the destination alpha channel (<c>DstAlpha</c>).
    /// </summary>
    DestinationAlpha,

    /// <summary>
    /// Scales components by the inverse destination alpha channel (<c>1 - DstAlpha</c>).
    /// </summary>
    OneMinusDestinationAlpha,

    /// <summary>
    /// Scales RGB components by <c>min(SrcAlpha, 1 - DstAlpha)</c> and alpha by <c>1</c>.
    /// </summary>
    SourceAlphaSaturate,
}

/// <summary>
/// Specifies how weighted source and destination color/alpha components are combined.
/// </summary>
public enum BlendOperation
{
    /// <summary>
    /// Adds weighted source and destination components (<c>Src + Dst</c>).
    /// </summary>
    Add,

    /// <summary>
    /// Subtracts weighted destination from source components (<c>Src - Dst</c>).
    /// </summary>
    Subtract,

    /// <summary>
    /// Subtracts weighted source from destination components (<c>Dst - Src</c>).
    /// </summary>
    ReverseSubtract,

    /// <summary>
    /// Takes the component-wise minimum of source and destination (<c>min(Src, Dst)</c>).
    /// </summary>
    Min,

    /// <summary>
    /// Takes the component-wise maximum of source and destination (<c>max(Src, Dst)</c>).
    /// </summary>
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
