namespace kajarlabs.Framework.Timing;

/// <summary>
/// A clock whose running state, position, and rate can be controlled externally.
/// </summary>
public interface IAdjustableClock : IClock
{
    /// <summary>
    /// Gets or sets the rate multiplier at which time advances relative to real time.
    /// </summary>
    new double Rate { get; set; }

    /// <summary>
    /// Starts or resumes the clock.
    /// </summary>
    void Start();

    /// <summary>
    /// Stops or pauses the clock without altering <see cref="IClock.CurrentTime"/>.
    /// </summary>
    void Stop();

    /// <summary>
    /// Stops the clock and resets <see cref="IClock.CurrentTime"/> to zero.
    /// </summary>
    void Reset();

    /// <summary>
    /// Seeks the clock to the specified position in milliseconds.
    /// </summary>
    /// <param name="position">The target time in milliseconds.</param>
    /// <returns><see langword="true"/> if the seek succeeded; otherwise, <see langword="false"/>.</returns>
    bool Seek(double position);
}
