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

internal sealed partial class EmptyWindowGame : Game
{
    protected override void Load()
    {
        if (Logger.IsEnabled(LogLevel.Information))
        {
            var logsPath = Storage.GetFullPath("logs");
            logWindowOpened(Logger, logsPath);
        }
    }

    protected override void Exiting()
    {
        logSampleClosing(Logger);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Empty window opened. Log directory: {LogsPath}")]
    private static partial void logWindowOpened(ILogger logger, string logsPath);

    [LoggerMessage(Level = LogLevel.Information, Message = "Closing empty window sample.")]
    private static partial void logSampleClosing(ILogger logger);
}
