using kajarlabs.Framework;
using kajarlabs.Framework.Platform;
using Microsoft.Extensions.Logging;

using var host = new GameHost(new HostOptions
{
    Organization = "kajarlabs",
    Application = "EmptyWindowSample",
    WindowTitle = "kajarlabs.Framework - Empty Window",
});

ConsoleCancelEventHandler onCancelKeyPress = (_, args) =>
{
    args.Cancel = true;
    host.Exit();
};

Console.CancelKeyPress += onCancelKeyPress;

try
{
    using var game = new EmptyWindowGame();
    host.Run(game);
}
finally
{
    Console.CancelKeyPress -= onCancelKeyPress;
}

internal sealed class EmptyWindowGame : Game
{
    protected override void Load()
    {
        Logger.LogInformation("Empty window opened. Log directory: {LogsPath}", Storage.GetFullPath("logs"));
    }

    protected override void Exiting()
    {
        Logger.LogInformation("Closing empty window sample.");
    }
}
