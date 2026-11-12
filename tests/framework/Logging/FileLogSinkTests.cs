using kajarlabs.Framework.IO;
using kajarlabs.Framework.Logging;
using Microsoft.Extensions.Logging;

namespace kajarlabs.Framework.Tests.Logging;

public sealed class FileLogSinkTests : IDisposable
{
    private readonly string temp = Path.Combine(Path.GetTempPath(), "kajarlabs-" + Guid.NewGuid().ToString("N"));
    private readonly FileSystemStorage hostStorage;

    public FileLogSinkTests()
    {
        hostStorage = new FileSystemStorage(temp);
    }

    public void Dispose()
    {
        if (Directory.Exists(temp))
            Directory.Delete(temp, true);
    }

    [Fact]
    public void WritesSessionFileIntoLogsSubStorageAndFlushesOnDispose()
    {
        string createdFile;

        using (var sink = new FileLogSink(hostStorage))
        using (var provider = new BackgroundLoggerProvider([sink]))
        {
            createdFile = sink.FileName;
            var logger = provider.CreateLogger("kajarlabs.Tests.FileSink");
            logger.LogInformation("Session started");
        }

        var logs = hostStorage.GetStorageForDirectory("logs");
        Assert.True(logs.Exists(createdFile));

        var content = File.ReadAllText(logs.GetFullPath(createdFile));
        Assert.Contains("[INF] kajarlabs.Tests.FileSink: Session started", content);
    }

    [Fact]
    public void RetainsOnlyMostRecentNSessionFiles()
    {
        var start = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var created = new List<string>();

        for (var i = 0; i < 5; i++)
        {
            using var sink = new FileLogSink(hostStorage, maxRetainedFiles: 3, sessionStart: start.AddSeconds(i));
            created.Add(sink.FileName);
        }

        var logs = hostStorage.GetStorageForDirectory("logs");
        var remaining = logs.GetFiles("").OrderBy(f => f, StringComparer.Ordinal).ToList();

        Assert.Equal(3, remaining.Count);
        Assert.Equal(created.Skip(2), remaining);
    }

    [Fact]
    public void FlushPersistsQueuedMessagesBeforeDispose()
    {
        using var sink = new FileLogSink(hostStorage);
        using var provider = new BackgroundLoggerProvider([sink]);

        var logger = provider.CreateLogger("kajarlabs.Flush");
        logger.LogInformation("Persisted before shutdown");

        provider.Flush();

        var fullPath = hostStorage.GetStorageForDirectory("logs").GetFullPath(sink.FileName);
        using var readStream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(readStream);
        var content = reader.ReadToEnd();

        Assert.Contains("Persisted before shutdown", content);
    }

    [Fact]
    public void UnhandledExceptionHookLogsAndFlushesImmediately()
    {
        using var sink = new FileLogSink(hostStorage);
        using var provider = new BackgroundLoggerProvider([sink]);
        var logger = provider.CreateLogger("kajarlabs.Crash");
        using var hook = new UnhandledExceptionFlushHook(provider, logger);

        hook.HandleUnhandledException(new InvalidOperationException("fatal crash"));

        var fullPath = hostStorage.GetStorageForDirectory("logs").GetFullPath(sink.FileName);
        using var readStream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(readStream);
        var content = reader.ReadToEnd();

        Assert.Contains("[CRT] kajarlabs.Crash: Unhandled exception", content);
        Assert.Contains("System.InvalidOperationException: fatal crash", content);
    }
}
