namespace kajarlabs.Framework.Timing;

/// <summary>
/// A frame-based clock that snapshots an underlying <see cref="IClock"/> once per <see cref="ProcessFrame"/> call.
/// </summary>
public sealed class FramedClock : IFrameBasedClock
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FramedClock"/> class.
    /// </summary>
    /// <param name="source">
    /// The underlying clock to sample on each frame, or <see langword="null"/> to create a running <see cref="StopwatchClock"/>.
    /// </param>
    public FramedClock(IClock? source = null)
    {
        Source = source ?? new StopwatchClock(start: true);
        CurrentTime = Source.CurrentTime;
    }

    /// <summary>
    /// Gets the underlying clock sampled by this framed clock.
    /// </summary>
    public IClock Source { get; private set; }

    /// <inheritdoc />
    public double CurrentTime { get; private set; }

    /// <inheritdoc />
    public double ElapsedFrameTime { get; private set; }

    /// <inheritdoc />
    public double Rate => Source.Rate;

    /// <inheritdoc />
    public bool IsRunning => Source.IsRunning;

    /// <inheritdoc />
    public void ProcessFrame()
    {
        var lastTime = CurrentTime;
        CurrentTime = Source.CurrentTime;
        ElapsedFrameTime = CurrentTime - lastTime;
    }

    /// <summary>
    /// Switches the underlying clock source and aligns the frame snapshot to the new source's current time.
    /// </summary>
    /// <param name="newSource">The new clock source.</param>
    public void ChangeSource(IClock newSource)
    {
        ArgumentNullException.ThrowIfNull(newSource);

        Source = newSource;
        CurrentTime = newSource.CurrentTime;
        ElapsedFrameTime = 0;
    }
}
