namespace kajarlabs.Framework.Timing;

/// <summary>
/// A monotonic adjustable clock backed by a high-resolution <see cref="TimeProvider"/> timestamp source.
/// </summary>
public sealed class StopwatchClock : IAdjustableClock
{
    private readonly TimeProvider timeProvider;
    private readonly Lock gate = new();
    private readonly double millisecondsPerTick;

    private double seekOffset;
    private long referenceTimestamp;
    private double rate = 1.0;
    private bool isRunning;

    /// <summary>
    /// Initializes a new instance of the <see cref="StopwatchClock"/> class.
    /// </summary>
    /// <param name="start">Whether the clock should start running immediately.</param>
    public StopwatchClock(bool start = false)
        : this(TimeProvider.System, start)
    {
    }

    internal StopwatchClock(TimeProvider timeProvider, bool start = false)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.timeProvider = timeProvider;
        millisecondsPerTick = 1000.0 / timeProvider.TimestampFrequency;
        referenceTimestamp = timeProvider.GetTimestamp();
        isRunning = start;
    }

    /// <inheritdoc />
    public double CurrentTime
    {
        get
        {
            lock (gate)
                return isRunning ? seekOffset + getElapsedSinceReferenceMs() : seekOffset;
        }
    }

    /// <inheritdoc />
    public double Rate
    {
        get
        {
            lock (gate)
                return rate;
        }
        set
        {
            if (!double.IsFinite(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "Clock rate must be a finite number.");

            lock (gate)
            {
                if (value == rate)
                    return;

                if (isRunning)
                {
                    seekOffset += getElapsedSinceReferenceMs();
                    referenceTimestamp = timeProvider.GetTimestamp();
                }

                rate = value;
            }
        }
    }

    /// <inheritdoc />
    public bool IsRunning
    {
        get
        {
            lock (gate)
                return isRunning;
        }
    }

    /// <inheritdoc />
    public void Start()
    {
        lock (gate)
        {
            if (isRunning)
                return;

            referenceTimestamp = timeProvider.GetTimestamp();
            isRunning = true;
        }
    }

    /// <inheritdoc />
    public void Stop()
    {
        lock (gate)
        {
            if (!isRunning)
                return;

            seekOffset += getElapsedSinceReferenceMs();
            isRunning = false;
        }
    }

    /// <inheritdoc />
    public void Reset()
    {
        lock (gate)
        {
            seekOffset = 0;
            referenceTimestamp = timeProvider.GetTimestamp();
            isRunning = false;
        }
    }

    /// <inheritdoc />
    public bool Seek(double position)
    {
        if (!double.IsFinite(position))
            return false;

        lock (gate)
        {
            seekOffset = position;
            referenceTimestamp = timeProvider.GetTimestamp();
            return true;
        }
    }

    private double getElapsedSinceReferenceMs()
    {
        var now = timeProvider.GetTimestamp();
        return (now - referenceTimestamp) * millisecondsPerTick * rate;
    }
}
