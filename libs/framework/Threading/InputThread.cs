using kajarlabs.Framework.Timing;

namespace kajarlabs.Framework.Threading;

/// <summary>
/// The game thread responsible for platform window events and input polling.
/// Designed to run on the process main thread when required by the platform (such as macOS SDL).
/// </summary>
public class InputThread : GameThread
{
    /// <summary>
    /// The default target frequency in Hz for the input thread.
    /// </summary>
    public const double DEFAULT_FREQUENCY = 1000.0;

    /// <summary>
    /// Initializes a new instance of the <see cref="InputThread"/> class.
    /// </summary>
    /// <param name="targetFrequency">The target frequency in Hz, or <c>0</c> for uncapped execution.</param>
    /// <param name="onNewFrame">An optional callback invoked on each frame.</param>
    /// <param name="sourceClock">An optional underlying clock source.</param>
    public InputThread(
        double targetFrequency = DEFAULT_FREQUENCY,
        Action<IFrameBasedClock>? onNewFrame = null,
        IClock? sourceClock = null)
        : base("Input", targetFrequency, onNewFrame, sourceClock)
    {
    }
}
