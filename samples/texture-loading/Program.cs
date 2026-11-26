using kajarlabs.Framework.Platform;
using kajarlabs.Framework.Samples.TextureLoading;

TimeSpan? autoExitAfter = null;

for (var i = 0; i < args.Length; i++)
{
    if (args[i] == "--duration" && i + 1 < args.Length && double.TryParse(args[i + 1], out var seconds) && seconds > 0)
    {
        autoExitAfter = TimeSpan.FromSeconds(seconds);
        i++;
    }
}

using var host = new GameHost(new HostOptions
{
    Organization = "kajarlabs",
    Application = "TextureLoadingSample",
    WindowTitle = "kajarlabs.Framework - Texture Loading (Embedded, Disk Override, Async & Samplers)",
    Resizable = true,
});

ConsoleCancelEventHandler onCancelKeyPress = (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    host.Exit();
};

Console.CancelKeyPress += onCancelKeyPress;

try
{
    using var game = new TextureLoadingGame(autoExitAfter);
    host.Run(game);
}
finally
{
    Console.CancelKeyPress -= onCancelKeyPress;
}
