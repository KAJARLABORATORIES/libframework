using SDL3;

SDL.SetHint("SDL_VIDEO_DRIVER", "dummy");

if (!SDL.Init(SDL.InitFlags.Video))
{
    Console.Error.WriteLine($"SDL_Init failed: {SDL.GetError()}");
    return 1;
}

var version = SDL.GetVersion();
Console.WriteLine($"SDL {version / 1_000_000}.{version / 1_000 % 1_000}.{version % 1_000} initialized under NativeAOT.");

SDL.Quit();
return 0;
