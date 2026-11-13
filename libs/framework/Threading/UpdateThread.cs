using kajarlabs.Framework.Timing;

namespace kajarlabs.Framework.Threading;

/// <summary>
/// The game thread responsible for scene updates and simulation logic.
/// </summary>
public class UpdateThread : GameThread
{
    /// <summary>
    /// The default target frequency in Hz for the update thread.
    /// </summary>
    public const double DEFAULT_FREQUENCY = 240.0;

    /// <summary>
    /// Initializes a new instance of the <see cref="UpdateThread"/> class.
    /// </summary>
    /// <param name="targetFrequency">The target frequency in Hz, or <c>0</c> for uncapped execution.</param>
    /// <param name="onNewFrame">An optional callback invoked on each frame.</param>
    /// <param name="sourceClock">An optional underlying clock source.</param>
    public UpdateThread(
        double targetFrequency = DEFAULT_FREQUENCY,
        Action<IFrameBasedClock>? onNewFrame = null,
        IClock? sourceClock = null)
        : base("Update", targetFrequency, onNewFrame, sourceClock)
    {
    }
}
