using kajarlabs.Framework.Graphics.Rendering;

namespace kajarlabs.Framework.Graphics.Decoding;

/// <summary>
/// Represents a decoded 2D image stored as straight (non-premultiplied) RGBA8 pixels in top-to-bottom row order.
/// </summary>
internal readonly struct DecodedImage
{
    private readonly byte[] pixels;

    public DecodedImage(int width, int height, byte[] pixels)
    {
        TextureValidation.ValidateDimensions(width, height);
        ArgumentNullException.ThrowIfNull(pixels);

        var expectedBytes = checked((long)width * height * TextureValidation.BYTES_PER_PIXEL);

        if (pixels.Length != expectedBytes)
        {
            throw new ArgumentException(
                $"Pixel buffer length {pixels.Length} does not match expected RGBA8 byte count {expectedBytes} for {width}x{height} image.",
                nameof(pixels));
        }

        Width = width;
        Height = height;
        this.pixels = pixels;
    }

    public int Width { get; }

    public int Height { get; }

    public ReadOnlySpan<byte> Pixels => pixels;
}
