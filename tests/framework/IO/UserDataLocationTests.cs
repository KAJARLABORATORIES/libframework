using kajarlabs.Framework.IO;

namespace kajarlabs.Framework.Tests.IO;

public sealed class UserDataLocationTests : IDisposable
{
    private readonly string temp = Path.Combine(Path.GetTempPath(), "kajarlabs-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(temp))
            Directory.Delete(temp, true);
    }

    [Fact]
    public void OverridePathCreatesMissingDirectoryAndSkipsSdl()
    {
        var overridePath = Path.Combine(temp, "nested", "data") + Path.DirectorySeparatorChar;
        var sdlCalled = false;

        var resolved = UserDataLocation.Resolve(
            "KAJAR",
            "TestApp",
            overridePath,
            (_, _) =>
            {
                sdlCalled = true;
                return "should-not-be-used";
            });

        Assert.False(sdlCalled);
        Assert.True(Directory.Exists(resolved));
        Assert.Equal(Path.TrimEndingDirectorySeparator(Path.GetFullPath(overridePath)), resolved);
    }

    [Fact]
    public void OverridePathWorksWhenDirectoryAlreadyExists()
    {
        var overridePath = Path.Combine(temp, "existing");
        Directory.CreateDirectory(overridePath);

        var resolved = UserDataLocation.Resolve(
            "KAJAR",
            "TestApp",
            overridePath,
            (_, _) => throw new InvalidOperationException("SDL should not be called."));

        Assert.True(Directory.Exists(resolved));
        Assert.Equal(Path.GetFullPath(overridePath), resolved);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void DefaultLocationUsesGetPrefPathAndTrimsTrailingSeparator(string? overridePath)
    {
        string? capturedOrg = null;
        string? capturedApp = null;
        var expected = Path.Combine(temp, "pref");

        var resolved = UserDataLocation.Resolve(
            "KAJAR",
            "TestApp",
            overridePath,
            (org, app) =>
            {
                capturedOrg = org;
                capturedApp = app;
                return expected + Path.DirectorySeparatorChar;
            });

        Assert.Equal("KAJAR", capturedOrg);
        Assert.Equal("TestApp", capturedApp);
        Assert.Equal(expected, resolved);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void NullOrEmptyPrefPathThrowsInvalidOperationException(string? prefPath)
    {
        Assert.Throws<InvalidOperationException>(() =>
            UserDataLocation.Resolve("KAJAR", "TestApp", null, (_, _) => prefPath));
    }

    [Theory]
    [InlineData(null, "TestApp")]
    [InlineData("", "TestApp")]
    [InlineData("   ", "TestApp")]
    [InlineData("KAJAR", null)]
    [InlineData("KAJAR", "")]
    [InlineData("KAJAR", "   ")]
    public void InvalidOrganizationOrApplicationThrowsArgumentException(string? organization, string? application)
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            UserDataLocation.Resolve(organization!, application!, null, (_, _) => temp));
    }
}
