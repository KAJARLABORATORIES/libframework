using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using kajarlabs.Framework.Numerics;

namespace kajarlabs.Framework.Tests.Numerics;

public sealed class Color4Tests
{
    [Fact]
    public void PresetsHaveExpectedComponents()
    {
        Assert.Equal(new Color4(1f, 1f, 1f, 1f), Color4.WHITE);
        Assert.Equal(new Color4(0f, 0f, 0f, 1f), Color4.BLACK);
        Assert.Equal(new Color4(0f, 0f, 0f, 0f), Color4.TRANSPARENT);
        Assert.Equal(new Color4(1f, 0f, 0f, 1f), Color4.RED);
        Assert.Equal(new Color4(0f, 1f, 0f, 1f), Color4.GREEN);
        Assert.Equal(new Color4(0f, 0f, 1f, 1f), Color4.BLUE);
    }

    [Fact]
    public void ConversionsFromBytesAndRgbaProduceExpectedChannels()
    {
        var fromBytes = Color4.FromBytes(255, 128, 0, 64);
        Assert.Equal(1f, fromBytes.R, precision: 6);
        Assert.Equal(128f / 255f, fromBytes.G, precision: 6);
        Assert.Equal(0f, fromBytes.B, precision: 6);
        Assert.Equal(64f / 255f, fromBytes.A, precision: 6);

        var fromRgba = Color4.FromRgba(0xFF800040u);
        Assert.Equal(fromBytes, fromRgba);

        var vector = new Vector4(0.25f, 0.5f, 0.75f, 1f);
        Assert.Equal(vector, Color4.FromVector4(vector).ToVector4());
    }

    [Fact]
    public void AlphaPremultiplyAndLerpBehaveCorrectly()
    {
        var baseColor = new Color4(0.8f, 0.4f, 0.2f, 0.5f);

        Assert.Equal(new Color4(0.8f, 0.4f, 0.2f, 0.25f), baseColor.WithAlpha(0.25f));
        Assert.Equal(new Color4(0.8f, 0.4f, 0.2f, 0.25f), baseColor.MultiplyAlpha(0.5f));

        var premul = baseColor.Premultiply();
        Assert.Equal(0.4f, premul.R, precision: 6);
        Assert.Equal(0.2f, premul.G, precision: 6);
        Assert.Equal(0.1f, premul.B, precision: 6);
        Assert.Equal(0.5f, premul.A, precision: 6);

        var lerped = Color4.Lerp(Color4.BLACK, Color4.WHITE, 0.25f);
        Assert.Equal(new Color4(0.25f, 0.25f, 0.25f, 1f), lerped);

        var multiplied = new Color4(0.5f, 0.25f, 1f, 0.5f) * new Color4(0.5f, 2f, 0.5f, 0.5f);
        Assert.Equal(new Color4(0.25f, 0.5f, 0.5f, 0.25f), multiplied);
    }

    [Fact]
    public void NegativeZeroAndPositiveZeroHaveEqualValueAndHashCode()
    {
        var posZero = new Color4(0f, 0f, 0f, 0f);
        var negZero = new Color4(-0f, -0f, -0f, -0f);

        Assert.True(posZero == negZero);
        Assert.Equal(posZero, negZero);
        Assert.Equal(posZero.GetHashCode(), negZero.GetHashCode());
    }

    [Fact]
    public void MemoryLayoutIsSequentialSixteenBytesWithoutReferences()
    {
        Assert.Equal(16, Unsafe.SizeOf<Color4>());
        Assert.Equal(0, Marshal.OffsetOf<Color4>(nameof(Color4.R)));
        Assert.Equal(4, Marshal.OffsetOf<Color4>(nameof(Color4.G)));
        Assert.Equal(8, Marshal.OffsetOf<Color4>(nameof(Color4.B)));
        Assert.Equal(12, Marshal.OffsetOf<Color4>(nameof(Color4.A)));
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<Color4>());
    }
}
