using kajarlabs.Framework.Timing;

namespace kajarlabs.Framework.Threading;

/// <summary>
/// The game thread responsible for rendering frames.
/// </summary>
public class DrawThread : GameThread
{
    /// <summary>
    /// The default target frequency in Hz for the draw thread.
    /// </summary>
    public const double DEFAULT_FREQUENCY = 120.0;

    /// <summary>
    /// Initializes a new instance of the <see cref="DrawThread"/> class.
    /// </summary>
    /// <param name="targetFrequency">The target frequency in Hz, or <c>0</c> for uncapped execution.</param>
    /// <param name="onNewFrame">An optional callback invoked on each frame.</param>
    /// <param name="sourceClock">An optional underlying clock source.</param>
    public DrawThread(
        double targetFrequency = DEFAULT_FREQUENCY,
        Action<IFrameBasedClock>? onNewFrame = null,
        IClock? sourceClock = null)
        : base("Draw", targetFrequency, onNewFrame, sourceClock)
    {
    }
}
