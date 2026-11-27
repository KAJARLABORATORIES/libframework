using System.Numerics;
using System.Runtime.CompilerServices;
using kajarlabs.Framework.Graphics;
using kajarlabs.Framework.Graphics.Rendering;

namespace kajarlabs.Framework.Tests.Graphics;

public sealed class MsdfRenderParametersTests
{
    [Fact]
    public void ConstructorStoresValuesAndComputesNonSquareUnitRange()
    {
        var parameters = new MsdfRenderParameters(distanceRange: 4f, atlasWidth: 512, atlasHeight: 256, distanceBias: 0.05f);

        Assert.Equal(4f, parameters.DistanceRange);
        Assert.Equal(512, parameters.AtlasWidth);
        Assert.Equal(256, parameters.AtlasHeight);
        Assert.Equal(0.05f, parameters.DistanceBias);
        Assert.Equal(new Vector2(4f / 512f, 4f / 256f), parameters.UnitRange);
        Assert.True(parameters.IsValid);

        var uniform = parameters.ToFragmentUniform();
        Assert.Equal(new Vector2(4f / 512f, 4f / 256f), uniform.UnitRange);
        Assert.Equal(0.05f, uniform.DistanceBias);
        Assert.Equal(0f, uniform.Reserved);
    }

    [Fact]
    public void EqualityAndHashCodeMatchStructuralValuesIncludingNegativeZeroBias()
    {
        var a = new MsdfRenderParameters(4f, 512, 512, 0f);
        var b = new MsdfRenderParameters(4f, 512, 512, -0f);
        var differentBias = new MsdfRenderParameters(4f, 512, 512, 0.1f);
        var differentAtlas = new MsdfRenderParameters(4f, 1024, 512, 0f);

        Assert.Equal(a, b);
        Assert.True(a == b);
        Assert.False(a != b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.True(a.Equals((object)b));

        Assert.NotEqual(a, differentBias);
        Assert.True(a != differentBias);
        Assert.NotEqual(a, differentAtlas);
        Assert.False(a.Equals("not-msdf"));
    }

    [Theory]
    [InlineData(0f, 256, 256, 0f)]
    [InlineData(-4f, 256, 256, 0f)]
    [InlineData(float.NaN, 256, 256, 0f)]
    [InlineData(float.PositiveInfinity, 256, 256, 0f)]
    [InlineData(4f, 0, 256, 0f)]
    [InlineData(4f, -16, 256, 0f)]
    [InlineData(4f, 256, 0, 0f)]
    [InlineData(4f, 256, -16, 0f)]
    [InlineData(4f, 256, 256, float.NaN)]
    [InlineData(4f, 256, 256, float.NegativeInfinity)]
    public void ConstructorRejectsInvalidArguments(float distanceRange, int atlasWidth, int atlasHeight, float distanceBias)
        => Assert.Throws<ArgumentOutOfRangeException>(() => new MsdfRenderParameters(distanceRange, atlasWidth, atlasHeight, distanceBias));

    [Fact]
    public void DefaultValueIsInvalidAndRejectedByValidate()
    {
        MsdfRenderParameters uninitialized = default;

        Assert.False(uninitialized.IsValid);
        Assert.Equal(Vector2.Zero, uninitialized.UnitRange);
        Assert.Throws<ArgumentException>(() => uninitialized.Validate("parameters"));
    }

    [Fact]
    public void StructLayoutsAreUnmanagedAndUniformIsSixteenBytes()
    {
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<MsdfRenderParameters>());
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<GpuMsdfFragmentUniform>());
        Assert.Equal(16, Unsafe.SizeOf<GpuMsdfFragmentUniform>());
    }
}
