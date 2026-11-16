namespace kajarlabs.Framework.Threading;

/// <summary>
/// A lock-free single-producer, single-consumer triple buffer for non-blocking handoff of frame snapshots
/// from an update thread to a draw thread.
/// </summary>
public sealed class TripleBuffer<T>
{
    private const int index_mask = 0x3;
    private const int dirty_bit = 0x4;

    private readonly T?[] slots = new T?[3];

    private int writeIndex;
    private int readIndex = 1;
    private int sharedState = 2;
    private bool hasPublished;

    /// <summary>
    /// Gets the slot index (<c>0..2</c>) currently owned exclusively by the producer thread.
    /// </summary>
    public int WriteIndex => Volatile.Read(ref writeIndex);

    /// <summary>
    /// Gets the slot index (<c>0..2</c>) currently owned exclusively by the consumer thread.
    /// </summary>
    public int ReadIndex => Volatile.Read(ref readIndex);

    /// <summary>
    /// Stores <paramref name="value"/> into the current producer slot and publishes it for the consumer,
    /// advancing <see cref="WriteIndex"/> to the next non-active slot.
    /// </summary>
    public void PublishWrite(T value)
    {
        var currentWrite = writeIndex;
        slots[currentWrite] = value;

        var previous = Interlocked.Exchange(ref sharedState, currentWrite | dirty_bit);
        writeIndex = previous & index_mask;
    }

    /// <summary>
    /// Acquires the latest published snapshot without blocking, or re-returns the most recently acquired snapshot
    /// if no new frame has been published since the last call.
    /// </summary>
    /// <returns><see langword="false"/> if no frame has been published yet; otherwise <see langword="true"/>.</returns>
    public bool TryAcquireRead(out T? value)
    {
        if ((Volatile.Read(ref sharedState) & dirty_bit) != 0)
        {
            var previous = Interlocked.Exchange(ref sharedState, readIndex);
            readIndex = previous & index_mask;
            hasPublished = true;
        }

        if (!hasPublished)
        {
            value = default;
            return false;
        }

        value = slots[readIndex];
        return true;
    }

    /// <summary>
    /// Resets buffer indices and clears all stored slot references. Must only be called when neither producer nor consumer is active.
    /// </summary>
    public void Clear()
    {
        Array.Clear(slots);
        writeIndex = 0;
        readIndex = 1;
        Volatile.Write(ref sharedState, 2);
        hasPublished = false;
    }
}
