using System.Buffers;
using kajarlabs.Framework.Graphics.Rendering;
using StbImageNext;

namespace kajarlabs.Framework.Graphics.Decoding;

/// <summary>
/// Internal <see cref="IImageDecoder"/> backed by <c>StbImageNext</c> supporting PNG, JPEG, BMP, and TGA
/// decoding into straight (non-premultiplied) RGBA8 buffers.
/// </summary>
internal sealed class StbImageDecoder : IImageDecoder
{
    public const int DEFAULT_MAX_DIMENSION = 16384;
    public const int DEFAULT_MAX_ENCODED_BYTES = 64 * 1024 * 1024;

    private const int initial_buffer_size = 4096;

    private readonly int maxDimension;
    private readonly int maxEncodedBytes;

    public StbImageDecoder(
        int maxDimension = DEFAULT_MAX_DIMENSION,
        int maxEncodedBytes = DEFAULT_MAX_ENCODED_BYTES)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maxDimension, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maxEncodedBytes, 0);

        this.maxDimension = maxDimension;
        this.maxEncodedBytes = maxEncodedBytes;
    }

    public DecodedImage Decode(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (!stream.CanRead)
            throw new ArgumentException("Image stream must be readable.", nameof(stream));

        if (stream.CanSeek)
        {
            var startPosition = stream.Position;
            var remaining = stream.Length - startPosition;

            if (remaining <= 0)
                throw new InvalidDataException("Image stream is empty.");

            if (remaining > maxEncodedBytes)
            {
                throw new InvalidDataException(
                    $"Image stream length {remaining} exceeds maximum allowed encoded size of {maxEncodedBytes} bytes.");
            }

            if (startPosition == 0)
                return decodeFromSeekableStream(stream);

            using var offsetStream = new OffsetStream(stream, startPosition);
            return decodeFromSeekableStream(offsetStream);
        }

        return decodeFromNonSeekableStream(stream);
    }

    public DecodedImage Decode(ReadOnlySpan<byte> encodedBytes)
    {
        if (encodedBytes.IsEmpty)
            throw new InvalidDataException("Image data is empty.");

        if (encodedBytes.Length > maxEncodedBytes)
        {
            throw new InvalidDataException(
                $"Image data length {encodedBytes.Length} exceeds maximum allowed encoded size of {maxEncodedBytes} bytes.");
        }

        var rentedBuffer = ArrayPool<byte>.Shared.Rent(encodedBytes.Length);

        try
        {
            encodedBytes.CopyTo(rentedBuffer);
            using var bufferedStream = new MemoryStream(rentedBuffer, 0, encodedBytes.Length, writable: false);
            return decodeFromSeekableStream(bufferedStream);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rentedBuffer);
        }
    }

    private DecodedImage decodeFromNonSeekableStream(Stream stream)
    {
        var rentedBuffer = ArrayPool<byte>.Shared.Rent(Math.Min(initial_buffer_size, maxEncodedBytes));

        try
        {
            var totalRead = 0;

            while (true)
            {
                if (totalRead == maxEncodedBytes)
                {
                    if (stream.ReadByte() < 0)
                        break;

                    throw new InvalidDataException(
                        $"Image stream exceeds maximum allowed encoded size of {maxEncodedBytes} bytes.");
                }

                if (totalRead == rentedBuffer.Length)
                {
                    var nextSize = (int)Math.Min((long)rentedBuffer.Length * 2, maxEncodedBytes);
                    var nextBuffer = ArrayPool<byte>.Shared.Rent(nextSize);
                    Buffer.BlockCopy(rentedBuffer, 0, nextBuffer, 0, totalRead);
                    ArrayPool<byte>.Shared.Return(rentedBuffer);
                    rentedBuffer = nextBuffer;
                }

                var remainingAllowance = Math.Min(rentedBuffer.Length - totalRead, maxEncodedBytes - totalRead);
                var bytesRead = stream.Read(rentedBuffer.AsSpan(totalRead, remainingAllowance));

                if (bytesRead == 0)
                    break;

                totalRead += bytesRead;
            }

            if (totalRead == 0)
                throw new InvalidDataException("Image stream is empty.");

            using var bufferedStream = new MemoryStream(rentedBuffer, 0, totalRead, writable: false);
            return decodeFromSeekableStream(bufferedStream);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rentedBuffer);
        }
    }

    private DecodedImage decodeFromSeekableStream(Stream stream)
    {
        ImageInfo header;

        try
        {
            header = ImageInfo.FromStream(stream);
        }
        catch (Exception ex) when (isDecodeFailure(ex))
        {
            throw new InvalidDataException("Failed to inspect image header.", ex);
        }

        validateDimensions(header.Width, header.Height);

        stream.Position = 0;

        ImageResult? result;

        try
        {
            result = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
        }
        catch (Exception ex) when (isDecodeFailure(ex))
        {
            throw new InvalidDataException("Failed to decode image data.", ex);
        }

        if (result?.Data is null)
            throw new InvalidDataException("Image decoder returned no pixel data.");

        validateDimensions(result.Width, result.Height);

        var expectedBytes = checked((long)result.Width * result.Height * TextureValidation.BYTES_PER_PIXEL);

        if (result.Data.Length != expectedBytes)
        {
            throw new InvalidDataException(
                $"Decoded RGBA8 buffer length {result.Data.Length} does not match expected {expectedBytes} bytes for {result.Width}x{result.Height} image.");
        }

        return new DecodedImage(result.Width, result.Height, result.Data);
    }

    private void validateDimensions(int width, int height)
    {
        if (width <= 0 || height <= 0)
            throw new InvalidDataException($"Decoded image dimensions ({width}x{height}) must be positive.");

        if (width > maxDimension || height > maxDimension)
        {
            throw new InvalidDataException(
                $"Image dimensions ({width}x{height}) exceed maximum supported dimension of {maxDimension}.");
        }

        var byteCount = (long)width * height * TextureValidation.BYTES_PER_PIXEL;

        if (byteCount > Array.MaxLength)
        {
            throw new InvalidDataException(
                $"Image dimensions ({width}x{height}) require {byteCount} RGBA8 bytes, exceeding maximum buffer capacity.");
        }
    }

    private static bool isDecodeFailure(Exception ex)
        => ex is not OutOfMemoryException
           and not InvalidDataException
           and (not IOException or EndOfStreamException);

    /// <summary>
    /// Non-owning seekable view over a stream starting at a non-zero origin so <c>StbImageNext</c>'s internal
    /// <c>Seek(0, SeekOrigin.Begin)</c> calls rewind to the image start rather than byte 0 of the outer stream.
    /// </summary>
    private sealed class OffsetStream : Stream
    {
        private readonly Stream inner;
        private readonly long origin;

        public OffsetStream(Stream inner, long origin)
        {
            this.inner = inner;
            this.origin = origin;
        }

        public override bool CanRead => true;

        public override bool CanSeek => true;

        public override bool CanWrite => false;

        public override long Length => inner.Length - origin;

        public override long Position
        {
            get => inner.Position - origin;
            set
            {
                if (value < 0)
                    throw new IOException("Attempted to seek before the start of the image stream.");

                inner.Position = origin + value;
            }
        }

        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);

        public override int Read(Span<byte> buffer) => inner.Read(buffer);

        public override int ReadByte() => inner.ReadByte();

        public override long Seek(long offset, SeekOrigin origin)
        {
            long target;

            switch (origin)
            {
                case SeekOrigin.Begin:
                    target = this.origin + offset;
                    break;

                case SeekOrigin.Current:
                    target = inner.Position + offset;
                    break;

                case SeekOrigin.End:
                    target = inner.Length + offset;
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(origin), origin, "Invalid seek origin.");
            }

            if (target < this.origin)
                throw new IOException("Attempted to seek before the start of the image stream.");

            return inner.Seek(target, SeekOrigin.Begin) - this.origin;
        }

        public override void Flush()
        {
        }

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
