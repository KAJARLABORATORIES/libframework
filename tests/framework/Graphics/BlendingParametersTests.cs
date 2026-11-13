using kajarlabs.Framework.Graphics;

namespace kajarlabs.Framework.Tests.Graphics;

public sealed class BlendingParametersTests
{
    [Fact]
    public void PresetsMatchExpectedStates()
    {
        Assert.False(BlendingParameters.NONE.IsEnabled);

        Assert.True(BlendingParameters.ALPHA.IsEnabled);
        Assert.Equal(BlendFactor.SourceAlpha, BlendingParameters.ALPHA.SourceColorFactor);
        Assert.Equal(BlendFactor.OneMinusSourceAlpha, BlendingParameters.ALPHA.DestinationColorFactor);
        Assert.Equal(BlendOperation.Add, BlendingParameters.ALPHA.ColorOperation);
        Assert.Equal(BlendFactor.One, BlendingParameters.ALPHA.SourceAlphaFactor);
        Assert.Equal(BlendFactor.OneMinusSourceAlpha, BlendingParameters.ALPHA.DestinationAlphaFactor);
        Assert.Equal(BlendOperation.Add, BlendingParameters.ALPHA.AlphaOperation);

        Assert.True(BlendingParameters.PREMULTIPLIED_ALPHA.IsEnabled);
        Assert.Equal(BlendFactor.One, BlendingParameters.PREMULTIPLIED_ALPHA.SourceColorFactor);
        Assert.Equal(BlendFactor.OneMinusSourceAlpha, BlendingParameters.PREMULTIPLIED_ALPHA.DestinationColorFactor);

        Assert.True(BlendingParameters.ADDITIVE.IsEnabled);
        Assert.Equal(BlendFactor.SourceAlpha, BlendingParameters.ADDITIVE.SourceColorFactor);
        Assert.Equal(BlendFactor.One, BlendingParameters.ADDITIVE.DestinationColorFactor);
    }

    [Fact]
    public void ValueEqualityAndDictionaryKeyLookupWork()
    {
        var manualAlpha = new BlendingParameters(
            BlendFactor.SourceAlpha,
            BlendFactor.OneMinusSourceAlpha,
            BlendOperation.Add,
            BlendFactor.One,
            BlendFactor.OneMinusSourceAlpha,
            BlendOperation.Add,
            isEnabled: true);

        Assert.True(BlendingParameters.ALPHA == manualAlpha);
        Assert.Equal(BlendingParameters.ALPHA, manualAlpha);
        Assert.Equal(BlendingParameters.ALPHA.GetHashCode(), manualAlpha.GetHashCode());
        Assert.True(BlendingParameters.ALPHA != BlendingParameters.ADDITIVE);

        var cache = new Dictionary<BlendingParameters, string>
        {
            [BlendingParameters.ALPHA] = "alpha",
            [BlendingParameters.ADDITIVE] = "additive",
            [BlendingParameters.NONE] = "none",
        };

        Assert.Equal("alpha", cache[manualAlpha]);
        Assert.Equal("additive", cache[BlendingParameters.ADDITIVE]);
        Assert.Equal("none", cache[BlendingParameters.NONE]);
    }
}
