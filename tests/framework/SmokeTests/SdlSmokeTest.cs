using SDL3;

namespace kajarlabs.Framework.Tests;

[Collection("SDL")]
public class SdlSmokeTests
{
    [Fact]
    public void NativeLibraryLoadsAndIsSdl3()
    {
        var version = SDL.GetVersion();
        var major = version / 1_000_000;
        var minor = version / 1_000 % 1_000;

        Assert.Equal(3, major);
        Assert.True(minor >= 4, $"SDL 3.{minor} is older than expected.");
    }

    [Fact]
    public void VideoInitializesHeadless()
    {
        SDL.SetHint("SDL_VIDEO_DRIVER", "dummy");
        Assert.True(SDL.Init(SDL.InitFlags.Video), SDL.GetError());
        SDL.Quit();
    }
}
