using System.Globalization;
using System.Text;
using kajarlabs.Framework.IO;

namespace kajarlabs.Framework.Logging;

/// <summary>
/// An <see cref="ILogSink"/> that writes one session log file per run into the <c>logs</c> sub-storage
/// and retains only the most recent session files.
/// </summary>
internal sealed class FileLogSink : ILogSink
{
    private readonly StreamWriter writer;
    private bool disposed;

    public FileLogSink(Storage hostStorage, int maxRetainedFiles = 10, DateTimeOffset? sessionStart = null)
    {
        ArgumentNullException.ThrowIfNull(hostStorage);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxRetainedFiles, 1);

        var logsStorage = hostStorage.GetStorageForDirectory("logs");
        var stream = createSessionStream(logsStorage, sessionStart ?? DateTimeOffset.UtcNow, out var fileName);
        FileName = fileName;

        writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        pruneOldFiles(logsStorage, maxRetainedFiles);
    }

    /// <summary>
    /// The file name created for this session inside the <c>logs</c> sub-storage.
    /// </summary>
    public string FileName { get; }

    public void Write(in LogMessage message)
    {
        if (disposed)
            return;

        LogMessageFormatter.WriteTo(writer, in message);
    }

    public void Flush()
    {
        if (disposed)
            return;

        writer.Flush();
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        writer.Flush();
        writer.Dispose();
    }

    private static Stream createSessionStream(Storage logsStorage, DateTimeOffset sessionStart, out string fileName)
    {
        var timestamp = sessionStart.ToUniversalTime().ToString("yyyy-MM-dd_HH-mm-ss-fff", CultureInfo.InvariantCulture);

        for (var attempt = 0; ; attempt++)
        {
            fileName = attempt == 0 ? $"{timestamp}.log" : $"{timestamp}-{attempt}.log";

            try
            {
                return logsStorage.GetStream(fileName, FileAccess.Write, FileMode.CreateNew);
            }
            catch (IOException) when (logsStorage.Exists(fileName))
            {
                // Another session started within the same millisecond; retry with a unique suffix.
            }
        }
    }

    private static void pruneOldFiles(Storage logsStorage, int maxRetainedFiles)
    {
        var logFiles = logsStorage.GetFiles("")
            .Where(file => file.EndsWith(".log", StringComparison.OrdinalIgnoreCase))
            .OrderBy(file => file, StringComparer.Ordinal)
            .ToList();

        var excess = logFiles.Count - maxRetainedFiles;

        for (var i = 0; i < excess; i++)
            logsStorage.Delete(logFiles[i]);
    }
}
