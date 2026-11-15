using kajarlabs.Framework.Numerics;

namespace kajarlabs.Framework.Graphics.Rendering;

internal static class TextureValidation
{
    public const int BYTES_PER_PIXEL = 4;

    public static void ValidateDimensions(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(width, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(height, 0);
    }

    public static int ValidateUpload(int textureWidth, int textureHeight, RectangleI region, int bufferLength)
    {
        if (region.X < 0)
            throw new ArgumentOutOfRangeException(nameof(region), region.X, "Region X coordinate must be non-negative.");

        if (region.Y < 0)
            throw new ArgumentOutOfRangeException(nameof(region), region.Y, "Region Y coordinate must be non-negative.");

        if (region.Width <= 0)
            throw new ArgumentOutOfRangeException(nameof(region), region.Width, "Region width must be positive.");

        if (region.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(region), region.Height, "Region height must be positive.");

        if ((long)region.X + region.Width > textureWidth || (long)region.Y + region.Height > textureHeight)
        {
            throw new ArgumentOutOfRangeException(
                nameof(region),
                region,
                $"Upload region ({region.X}, {region.Y}, {region.Width}, {region.Height}) exceeds texture bounds ({textureWidth}x{textureHeight}).");
        }

        var expectedBytes = checked((long)region.Width * region.Height * BYTES_PER_PIXEL);

        if (bufferLength != expectedBytes)
        {
            throw new ArgumentException(
                $"Pixel buffer length {bufferLength} does not match expected RGBA8 byte count {expectedBytes} for region {region.Width}x{region.Height}.",
                nameof(bufferLength));
        }

        return (int)expectedBytes;
    }
}
