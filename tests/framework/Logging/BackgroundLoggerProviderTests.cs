using kajarlabs.Framework.Logging;
using Microsoft.Extensions.Logging;

namespace kajarlabs.Framework.Tests.Logging;

public sealed class BackgroundLoggerProviderTests
{
    [Fact]
    public void MessagesFromMultipleThreadsAllArriveInPerThreadOrderOnSingleBackgroundThread()
    {
        const int thread_count = 4;
        const int messages_per_thread = 200;

        var sink = new RecordingSink();
        var producerThreadIds = new int[thread_count];

        using (var factory = new FrameworkLoggerFactory())
        {
            factory.AddProvider(new BackgroundLoggerProvider([sink]));
            var logger = factory.CreateLogger("MultiThread");

            using var startGate = new ManualResetEventSlim(false);
            var threads = new Thread[thread_count];

            for (var t = 0; t < thread_count; t++)
            {
                var threadIndex = t;
                threads[t] = new Thread(() =>
                {
                    producerThreadIds[threadIndex] = Environment.CurrentManagedThreadId;
                    startGate.Wait();

                    for (var i = 0; i < messages_per_thread; i++)
                        logger.LogInformation("T{Thread}:{Index}", threadIndex, i);
                });
                threads[t].Start();
            }

            startGate.Set();

            foreach (var thread in threads)
                thread.Join();
        }

        Assert.Equal(thread_count * messages_per_thread, sink.Messages.Count);
        Assert.True(sink.Flushed);
        Assert.True(sink.Disposed);

        var writerThreadId = Assert.Single(sink.WriterThreadIds);
        Assert.DoesNotContain(writerThreadId, producerThreadIds);

        for (var t = 0; t < thread_count; t++)
        {
            var prefix = $"T{t}:";
            var perThread = sink.Messages
                .Where(m => m.Message.StartsWith(prefix, StringComparison.Ordinal))
                .Select(m => int.Parse(m.Message.AsSpan(prefix.Length)))
                .ToArray();

            Assert.Equal(Enumerable.Range(0, messages_per_thread), perThread);
        }
    }

    [Fact]
    public void PendingMessagesAreFlushedOnDispose()
    {
        var sink = new RecordingSink();

        using (var provider = new BackgroundLoggerProvider([sink]))
        {
            var logger = provider.CreateLogger("Shutdown");

            for (var i = 0; i < 50; i++)
                logger.LogInformation("msg-{Index}", i);
        }

        Assert.Equal(50, sink.Messages.Count);
        Assert.True(sink.Flushed);
        Assert.True(sink.Disposed);
    }

    [Fact]
    public void ConsoleLogSinkFormatsLevelCategoryMessageAndException()
    {
        using var output = new StringWriter();

        using (var provider = new BackgroundLoggerProvider([new ConsoleLogSink(output)]))
        {
            var logger = provider.CreateLogger("kajarlabs.Audio");
            logger.LogWarning(new InvalidOperationException("boom"), "Device {Name} failed", "default");
        }

        var written = output.ToString();
        Assert.Contains("[WRN] kajarlabs.Audio: Device default failed", written);
        Assert.Contains("System.InvalidOperationException: boom", written);
    }

    private sealed class RecordingSink : ILogSink
    {
        public List<LogMessage> Messages { get; } = [];
        public HashSet<int> WriterThreadIds { get; } = [];
        public bool Flushed { get; private set; }
        public bool Disposed { get; private set; }

        public void Write(in LogMessage message)
        {
            WriterThreadIds.Add(Environment.CurrentManagedThreadId);
            Messages.Add(message);
        }

        public void Flush() => Flushed = true;

        public void Dispose() => Disposed = true;
    }
}
