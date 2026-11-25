using kajarlabs.Framework.IO.Resources;

namespace kajarlabs.Framework.Tests.IO.Resources;

public sealed class ResourcePathTests
{
    [Theory]
    [InlineData("hero.png")]
    [InlineData("Textures/hero.png")]
    [InlineData("Textures/UI/button.hover.png")]
    [InlineData("a/.../b")]
    public void CanonicalPathReturnsSameStringInstance(string input)
    {
        var normalized = ResourcePath.TryNormalize(input);

        Assert.Same(input, normalized);
    }

    [Theory]
    [InlineData("a/../b", "b")]
    [InlineData("a//b", "a/b")]
    [InlineData("a\\b", "a/b")]
    [InlineData("a\\b\\", "a/b")]
    [InlineData("a/b/", "a/b")]
    [InlineData("./a/./b", "a/b")]
    [InlineData("a/b/..", "a")]
    [InlineData("Textures\\UI/../HUD//icon.png", "Textures/HUD/icon.png")]
    public void RelativePathsAreNormalizedToForwardSlashes(string input, string expected)
    {
        Assert.Equal(expected, ResourcePath.TryNormalize(input));
        Assert.Equal(expected, ResourcePath.NormalizeRequired(input));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\r\n")]
    [InlineData(".")]
    [InlineData("./.")]
    [InlineData("a/..")]
    [InlineData("a/b/../..")]
    public void EmptyOrRootCollapsingPathsReturnNullForLookupAndThrowForRequired(string input)
    {
        Assert.Null(ResourcePath.TryNormalize(input));
        Assert.Throws<ArgumentException>(() => ResourcePath.NormalizeRequired(input));
    }

    [Theory]
    [InlineData("..")]
    [InlineData("../x")]
    [InlineData("a/../../x")]
    [InlineData("a\\..\\..\\x")]
    [InlineData("a/b/../../..")]
    [InlineData("/etc/passwd")]
    [InlineData("\\windows")]
    [InlineData("/")]
    [InlineData("\\")]
    [InlineData("C:\\Windows")]
    [InlineData("C:x")]
    [InlineData("a/b:stream")]
    public void InvalidOrEscapingPathsThrowArgumentException(string path)
    {
        Assert.Throws<ArgumentException>(() => ResourcePath.TryNormalize(path));
        Assert.Throws<ArgumentException>(() => ResourcePath.NormalizeRequired(path));
    }

    [Fact]
    public void NullInputsThrowArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => ResourcePath.TryNormalize(null!));
        Assert.Throws<ArgumentNullException>(() => ResourcePath.NormalizeRequired(null!));
        Assert.Throws<ArgumentNullException>(() => ResourcePath.NormalizeExtension(null!));
    }

    [Theory]
    [InlineData("png", ".png")]
    [InlineData(".png", ".png")]
    [InlineData("tar.gz", ".tar.gz")]
    [InlineData(".tar.gz", ".tar.gz")]
    public void NormalizeExtensionEnsuresLeadingDot(string input, string expected)
    {
        Assert.Equal(expected, ResourcePath.NormalizeExtension(input));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(".")]
    [InlineData(".   ")]
    [InlineData("/png")]
    [InlineData("\\png")]
    [InlineData("a/b")]
    [InlineData("png:ads")]
    public void NormalizeExtensionRejectsInvalidExtensions(string input)
    {
        Assert.Throws<ArgumentException>(() => ResourcePath.NormalizeExtension(input));
    }
}
