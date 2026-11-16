using kajarlabs.Framework.Threading;

namespace kajarlabs.Framework.Tests.Threading;

public sealed class TripleBufferTests
{
    [Fact]
    public void TryAcquireReadReturnsFalseBeforeFirstPublishAndReReturnsLatestAfter()
    {
        var buffer = new TripleBuffer<string>();

        Assert.Equal(0, buffer.WriteIndex);
        Assert.Equal(1, buffer.ReadIndex);
        Assert.False(buffer.TryAcquireRead(out var initial));
        Assert.Null(initial);

        buffer.PublishWrite("frame-1");
        Assert.Equal(2, buffer.WriteIndex);

        Assert.True(buffer.TryAcquireRead(out var firstRead));
        Assert.Equal("frame-1", firstRead);
        Assert.NotEqual(buffer.WriteIndex, buffer.ReadIndex);

        // Second read without an intervening write re-returns the latest snapshot safely.
        Assert.True(buffer.TryAcquireRead(out var repeatRead));
        Assert.Equal("frame-1", repeatRead);
    }

    [Fact]
    public void FasterProducerDropsIntermediateFramesWithoutOverwritingReaderSlot()
    {
        var buffer = new TripleBuffer<int>();

        buffer.PublishWrite(1);
        Assert.True(buffer.TryAcquireRead(out var read1));
        Assert.Equal(1, read1);

        var activeReadIndex = buffer.ReadIndex;

        // Producer publishes multiple frames without consumer reading; writer never steals activeReadIndex.
        for (var i = 2; i <= 10; i++)
        {
            Assert.NotEqual(activeReadIndex, buffer.WriteIndex);
            buffer.PublishWrite(i);
            Assert.NotEqual(activeReadIndex, buffer.WriteIndex);
        }

        Assert.True(buffer.TryAcquireRead(out var latest));
        Assert.Equal(10, latest);

        buffer.Clear();
        Assert.False(buffer.TryAcquireRead(out _));
    }

    [Fact]
    public async Task ConcurrentSingleProducerSingleConsumerPreservesMonotonicConsistentSnapshots()
    {
        var buffer = new TripleBuffer<FrameSnapshot>();
        using var startBarrier = new Barrier(2);
        const int total_frames = 20_000;
        long maxObserved = -1;
        Exception? readerError = null;

        var readerTask = Task.Run(() =>
        {
            try
            {
                startBarrier.SignalAndWait(TestContext.Current.CancellationToken);
                long lastSeq = -1;

                while (lastSeq < total_frames)
                {
                    if (!buffer.TryAcquireRead(out var snapshot) || snapshot is null)
                        continue;

                    if (snapshot.Sequence != snapshot.MirroredSequence)
                        throw new InvalidOperationException($"Torn snapshot read: {snapshot.Sequence} != {snapshot.MirroredSequence}");

                    if (snapshot.Sequence < lastSeq)
                        throw new InvalidOperationException($"Out-of-order snapshot: {snapshot.Sequence} < {lastSeq}");

                    lastSeq = snapshot.Sequence;
                    maxObserved = lastSeq;
                }
            }
            catch (Exception ex)
            {
                readerError = ex;
            }
        }, TestContext.Current.CancellationToken);

        startBarrier.SignalAndWait(TestContext.Current.CancellationToken);

        for (long i = 1; i <= total_frames; i++)
            buffer.PublishWrite(new FrameSnapshot(i, i));

        await readerTask.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Null(readerError);
        Assert.Equal(total_frames, maxObserved);
    }

    private sealed class FrameSnapshot
    {
        public FrameSnapshot(long sequence, long mirroredSequence)
        {
            Sequence = sequence;
            MirroredSequence = mirroredSequence;
        }

        public long Sequence { get; }

        public long MirroredSequence { get; }
    }
}
