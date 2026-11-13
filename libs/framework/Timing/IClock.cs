namespace kajarlabs.Framework.Timing;

/// <summary>
/// A read-only clock providing the current time, playback rate, and running state.
/// </summary>
public interface IClock
{
    /// <summary>
    /// Gets the current time of this clock in milliseconds.
    /// </summary>
    double CurrentTime { get; }

    /// <summary>
    /// Gets the rate multiplier at which time advances relative to real time (1.0 is normal speed).
    /// </summary>
    double Rate { get; }

    /// <summary>
    /// Gets a value indicating whether this clock is currently running.
    /// </summary>
    bool IsRunning { get; }
}
