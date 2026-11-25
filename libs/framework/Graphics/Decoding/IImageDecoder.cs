namespace kajarlabs.Framework.Graphics.Decoding;

/// <summary>
/// Decodes encoded image streams or byte spans into straight (non-premultiplied) RGBA8 pixel buffers.
/// </summary>
internal interface IImageDecoder
{
    /// <summary>
    /// Decodes an image from <paramref name="stream"/> into RGBA8 pixels.
    /// </summary>
    /// <param name="stream">A readable stream containing encoded image bytes.</param>
    /// <returns>The decoded RGBA8 image.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="stream"/> is not readable.</exception>
    /// <exception cref="InvalidDataException">The stream is empty, corrupt, unsupported, or exceeds dimension limits.</exception>
    DecodedImage Decode(Stream stream);

    /// <summary>
    /// Decodes an image from <paramref name="encodedBytes"/> into RGBA8 pixels.
    /// </summary>
    /// <param name="encodedBytes">The encoded image bytes.</param>
    /// <returns>The decoded RGBA8 image.</returns>
    /// <exception cref="InvalidDataException">The data is empty, corrupt, unsupported, or exceeds dimension limits.</exception>
    DecodedImage Decode(ReadOnlySpan<byte> encodedBytes);
}
