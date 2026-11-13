namespace kajarlabs.Framework.Timing;

/// <summary>
/// A clock that updates its time snapshot once per frame and exposes the elapsed frame delta.
/// </summary>
public interface IFrameBasedClock : IClock
{
    /// <summary>
    /// Gets the elapsed time in milliseconds between the previous frame and the current frame.
    /// </summary>
    double ElapsedFrameTime { get; }

    /// <summary>
    /// Advances the frame snapshot from the underlying time source and updates <see cref="ElapsedFrameTime"/>.
    /// </summary>
    void ProcessFrame();
}
