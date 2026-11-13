using kajarlabs.Framework.Platform;
using Microsoft.Extensions.Logging;

namespace kajarlabs.Framework.Tests.Platform;

public sealed class HostOptionsTests
{
    [Fact]
    public void DefaultsArePopulated()
    {
        var options = new HostOptions();

        Assert.Equal(HostOptions.DEFAULT_ORGANIZATION, options.Organization);
        Assert.Equal(HostOptions.DEFAULT_APPLICATION, options.Application);
        Assert.Null(options.UserDataPath);
        Assert.Null(options.WindowTitle);
        Assert.Equal(HostOptions.DEFAULT_WINDOW_WIDTH, options.WindowWidth);
        Assert.Equal(HostOptions.DEFAULT_WINDOW_HEIGHT, options.WindowHeight);
        Assert.True(options.Resizable);
        Assert.Equal(LogLevel.Information, options.MinimumLogLevel);
        Assert.Equal(HostOptions.DEFAULT_MAX_RETAINED_LOG_FILES, options.MaxRetainedLogFiles);
        Assert.True(options.EnableConsoleLogging);
        Assert.True(options.EnableFileLogging);
        Assert.Equal(HostOptions.DEFAULT_APPLICATION, options.ResolveWindowTitle());
    }

    [Fact]
    public void ResolveWindowTitlePrefersExplicitTitleOverApplication()
    {
        var options = new HostOptions
        {
            Application = "SampleApp",
            WindowTitle = "Custom Title",
        };

        Assert.Equal("Custom Title", options.ResolveWindowTitle());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidateRejectsInvalidOrganizationOrApplication(string invalid)
    {
        Assert.Throws<ArgumentException>(() => new HostOptions { Organization = invalid }.Validate());
        Assert.Throws<ArgumentException>(() => new HostOptions { Application = invalid }.Validate());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ValidateRejectsNonPositiveWindowDimensions(int dimension)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new HostOptions { WindowWidth = dimension }.Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new HostOptions { WindowHeight = dimension }.Validate());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void ValidateRejectsInvalidMaxRetainedLogFiles(int count)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new HostOptions { MaxRetainedLogFiles = count }.Validate());
    }
}
