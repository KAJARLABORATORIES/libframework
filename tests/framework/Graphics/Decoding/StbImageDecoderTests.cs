using kajarlabs.Framework.Graphics.Decoding;

namespace kajarlabs.Framework.Tests.Graphics.Decoding;

public sealed class StbImageDecoderTests
{
    private const string fixture_prefix = "kajarlabs.Framework.Tests.Graphics.Decoding.Fixtures.";

    [Fact]
    public void DecodesPngPreservingStraightAlphaRgba8()
    {
        var decoder = new StbImageDecoder();

        using var stream = openFixture("sample-rgba.png");
        var decoded = decoder.Decode(stream);

        Assert.Equal(2, decoded.Width);
        Assert.Equal(2, decoded.Height);
        Assert.Equal(
            [
                255, 0, 0, 255,
                0, 255, 0, 128,
                0, 0, 255, 64,
                255, 255, 0, 0,
            ],
            decoded.Pixels.ToArray());
    }

    [Fact]
    public void DecodesJpegToOpaqueRgba8()
    {
        var decoder = new StbImageDecoder();

        using var stream = openFixture("sample-rgb.jpg");
        var decoded = decoder.Decode(stream);

        Assert.Equal(4, decoded.Width);
        Assert.Equal(4, decoded.Height);
        Assert.Equal(4 * 4 * 4, decoded.Pixels.Length);

        var pixels = decoded.Pixels;

        for (var i = 0; i < pixels.Length; i += 4)
        {
            Assert.InRange(pixels[i + 0], 235, 245);
            Assert.InRange(pixels[i + 1], 115, 125);
            Assert.InRange(pixels[i + 2], 35, 45);
            Assert.Equal(255, pixels[i + 3]);
        }
    }

    [Fact]
    public void DecodesBmpToTopDownOpaqueRgba8()
    {
        var decoder = new StbImageDecoder();

        using var stream = openFixture("sample-rgb.bmp");
        var decoded = decoder.Decode(stream);

        Assert.Equal(2, decoded.Width);
        Assert.Equal(2, decoded.Height);
        Assert.Equal(
            [
                255, 10, 20, 255,
                30, 255, 40, 255,
                50, 60, 255, 255,
                200, 150, 100, 255,
            ],
            decoded.Pixels.ToArray());
    }

    [Fact]
    public void DecodesTgaPreservingStraightAlphaRgba8()
    {
        var decoder = new StbImageDecoder();

        using var stream = openFixture("sample-rgba.tga");
        var decoded = decoder.Decode(stream);

        Assert.Equal(2, decoded.Width);
        Assert.Equal(2, decoded.Height);
        Assert.Equal(
            [
                250, 20, 30, 255,
                40, 240, 50, 180,
                60, 70, 230, 90,
                120, 130, 140, 0,
            ],
            decoded.Pixels.ToArray());
    }

    [Theory]
    [InlineData("sample-rgba.png", 2, 2)]
    [InlineData("sample-rgb.jpg", 4, 4)]
    [InlineData("sample-rgb.bmp", 2, 2)]
    [InlineData("sample-rgba.tga", 2, 2)]
    public void DecodesFromNonSeekableChunkedStreamAndSpanOverload(string fixtureName, int expectedWidth, int expectedHeight)
    {
        var decoder = new StbImageDecoder();
        var rawBytes = readFixtureBytes(fixtureName);

        using var nonSeekable = new NonSeekableChunkedStream(rawBytes, maxChunkSize: 7);
        var fromNonSeekable = decoder.Decode(nonSeekable);
        var fromSpan = decoder.Decode(rawBytes.AsSpan());

        Assert.Equal(expectedWidth, fromNonSeekable.Width);
        Assert.Equal(expectedHeight, fromNonSeekable.Height);
        Assert.Equal(fromSpan.Pixels.ToArray(), fromNonSeekable.Pixels.ToArray());
    }

    [Fact]
    public void DecodesFromSeekableStreamAtNonZeroStartPosition()
    {
        var decoder = new StbImageDecoder();
        var rawPng = readFixtureBytes("sample-rgba.png");

        var prefixed = new byte[16 + rawPng.Length];
        Array.Fill(prefixed, (byte)0xAA, 0, 16);
        Buffer.BlockCopy(rawPng, 0, prefixed, 16, rawPng.Length);

        using var stream = new MemoryStream(prefixed, writable: false);
        stream.Position = 16;

        var decoded = decoder.Decode(stream);

        Assert.Equal(2, decoded.Width);
        Assert.Equal(2, decoded.Height);
        Assert.Equal(255, decoded.Pixels[0]);
        Assert.Equal(128, decoded.Pixels[7]);
    }

    [Fact]
    public void ValidatesArgumentsAndRejectsEmptyCorruptAndTruncatedInputs()
    {
        var decoder = new StbImageDecoder();

        Assert.Throws<ArgumentNullException>(() => decoder.Decode((Stream)null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => new StbImageDecoder(maxDimension: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new StbImageDecoder(maxEncodedBytes: 0));

        using var unreadable = new UnreadableStream();
        Assert.Throws<ArgumentException>(() => decoder.Decode(unreadable));

        Assert.Throws<InvalidDataException>(() => decoder.Decode(ReadOnlySpan<byte>.Empty));

        using (var emptySeekable = new MemoryStream([], writable: false))
            Assert.Throws<InvalidDataException>(() => decoder.Decode(emptySeekable));

        using (var emptyNonSeekable = new NonSeekableChunkedStream([], maxChunkSize: 4))
            Assert.Throws<InvalidDataException>(() => decoder.Decode(emptyNonSeekable));

        byte[] garbage = [0x01, 0x02, 0x03, 0x04, 0x05, 0xFF, 0xFE, 0xFD, 0xAA, 0xBB];
        Assert.Throws<InvalidDataException>(() => decoder.Decode(garbage));

        using (var garbageNonSeekable = new NonSeekableChunkedStream(garbage, maxChunkSize: 3))
            Assert.Throws<InvalidDataException>(() => decoder.Decode(garbageNonSeekable));

        // Truncated PNG header/payload must fail cleanly with InvalidDataException.
        var fullPng = readFixtureBytes("sample-rgba.png");
        var truncatedPng = fullPng[..(fullPng.Length / 2)];
        Assert.Throws<InvalidDataException>(() => decoder.Decode(truncatedPng));
    }

    [Fact]
    public void RejectsImagesExceedingDimensionOrEncodedSizeLimitsBeforePixelAllocation()
    {
        var pngBytes = readFixtureBytes("sample-rgba.png");

        // Enforce custom max dimension (1x1 rejects 2x2 fixture).
        var tinyDimDecoder = new StbImageDecoder(maxDimension: 1);
        Assert.Throws<InvalidDataException>(() => tinyDimDecoder.Decode(pngBytes));

        // Enforce max encoded byte size on both seekable, non-seekable, and span inputs.
        var tinySizeDecoder = new StbImageDecoder(maxEncodedBytes: pngBytes.Length - 1);
        Assert.Throws<InvalidDataException>(() => tinySizeDecoder.Decode(pngBytes));

        using (var seekable = new MemoryStream(pngBytes, writable: false))
            Assert.Throws<InvalidDataException>(() => tinySizeDecoder.Decode(seekable));

        using (var nonSeekable = new NonSeekableChunkedStream(pngBytes, maxChunkSize: 5))
            Assert.Throws<InvalidDataException>(() => tinySizeDecoder.Decode(nonSeekable));

        // Crafted PNG IHDR claiming 20000x20000 (1.6 GB RGBA8) must be rejected by ImageInfo header check
        // under default 16384 limit without attempting pixel allocation.
        var bombHeader = createOversizedPngHeader(20000, 20000);
        var defaultDecoder = new StbImageDecoder();
        Assert.Throws<InvalidDataException>(() => defaultDecoder.Decode(bombHeader));
    }

    [Fact]
    public void DecodedImageConstructorValidatesDimensionsAndBufferLength()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DecodedImage(0, 1, []));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DecodedImage(1, -1, []));
        Assert.Throws<ArgumentNullException>(() => new DecodedImage(1, 1, null!));
        Assert.Throws<ArgumentException>(() => new DecodedImage(2, 2, new byte[12]));
    }

    [Fact]
    public async Task ConcurrentDecodingAcrossAllFormatsIsThreadSafe()
    {
        var decoder = new StbImageDecoder();
        var fixtures = new (string Name, int Width, int Height, byte[] Bytes)[]
        {
            ("sample-rgba.png", 2, 2, readFixtureBytes("sample-rgba.png")),
            ("sample-rgb.jpg", 4, 4, readFixtureBytes("sample-rgb.jpg")),
            ("sample-rgb.bmp", 2, 2, readFixtureBytes("sample-rgb.bmp")),
            ("sample-rgba.tga", 2, 2, readFixtureBytes("sample-rgba.tga")),
        };

        var ct = TestContext.Current.CancellationToken;
        var tasks = new List<Task>();

        for (var worker = 0; worker < 8; worker++)
        {
            var workerIndex = worker;
            tasks.Add(Task.Run(() =>
            {
                for (var i = 0; i < 40; i++)
                {
                    var (Name, Width, Height, Bytes) = fixtures[(workerIndex + i) % fixtures.Length];

                    using Stream stream = i % 2 == 0
                        ? new MemoryStream(Bytes, writable: false)
                        : new NonSeekableChunkedStream(Bytes, maxChunkSize: 11);

                    var decoded = decoder.Decode(stream);
                    Assert.Equal(Width, decoded.Width);
                    Assert.Equal(Height, decoded.Height);
                    Assert.Equal(Width * Height * 4, decoded.Pixels.Length);
                }
            }, ct));
        }

        await Task.WhenAll(tasks);
    }

    private static Stream openFixture(string name)
        => typeof(StbImageDecoderTests).Assembly.GetManifestResourceStream(fixture_prefix + name)
           ?? throw new InvalidOperationException($"Missing embedded image fixture '{name}'.");

    private static byte[] readFixtureBytes(string name)
    {
        using var stream = openFixture(name);
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    private static byte[] createOversizedPngHeader(int width, int height)
    {
        var bytes = readFixtureBytes("sample-rgba.png");
        // PNG IHDR width is at bytes 16..19 (big-endian), height at 20..23 (big-endian).
        bytes[16] = (byte)(width >> 24);
        bytes[17] = (byte)(width >> 16);
        bytes[18] = (byte)(width >> 8);
        bytes[19] = (byte)width;
        bytes[20] = (byte)(height >> 24);
        bytes[21] = (byte)(height >> 16);
        bytes[22] = (byte)(height >> 8);
        bytes[23] = (byte)height;
        return bytes;
    }

    private sealed class NonSeekableChunkedStream : Stream
    {
        private readonly byte[] data;
        private readonly int maxChunkSize;
        private int position;

        public NonSeekableChunkedStream(byte[] data, int maxChunkSize)
        {
            this.data = data;
            this.maxChunkSize = maxChunkSize;
        }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var remaining = data.Length - position;

            if (remaining <= 0)
                return 0;

            var toRead = Math.Min(Math.Min(count, maxChunkSize), remaining);
            Buffer.BlockCopy(data, position, buffer, offset, toRead);
            position += toRead;
            return toRead;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class UnreadableStream : Stream
    {
        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => 0;

        public override long Position
        {
            get => 0;
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
