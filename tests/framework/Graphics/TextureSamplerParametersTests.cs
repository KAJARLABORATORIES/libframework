using kajarlabs.Framework.Graphics;

namespace kajarlabs.Framework.Tests.Graphics;

public sealed class TextureSamplerParametersTests
{
    [Fact]
    public void EqualityAndPresetsCompareFilteringAndWrapMode()
    {
        var linearClamp = new TextureSamplerParameters(TextureFilteringMode.Linear, TextureWrapMode.ClampToEdge);
        var nearestClamp = new TextureSamplerParameters(TextureFilteringMode.Nearest, TextureWrapMode.ClampToEdge);
        var linearRepeat = new TextureSamplerParameters(TextureFilteringMode.Linear, TextureWrapMode.Repeat);
        var nearestRepeat = new TextureSamplerParameters(TextureFilteringMode.Nearest, TextureWrapMode.Repeat);

        Assert.Equal(TextureSamplerParameters.LINEAR_CLAMP, linearClamp);
        Assert.Equal(TextureSamplerParameters.LINEAR_CLAMP.GetHashCode(), linearClamp.GetHashCode());
        Assert.True(TextureSamplerParameters.LINEAR_CLAMP == linearClamp);

        Assert.Equal(TextureSamplerParameters.NEAREST_CLAMP, nearestClamp);
        Assert.Equal(TextureSamplerParameters.LINEAR_REPEAT, linearRepeat);
        Assert.Equal(TextureSamplerParameters.NEAREST_REPEAT, nearestRepeat);

        Assert.NotEqual(linearClamp, nearestClamp);
        Assert.True(linearClamp != linearRepeat);
        Assert.NotEqual(nearestClamp, nearestRepeat);
    }
}
