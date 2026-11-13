namespace kajarlabs.Framework.Threading;

/// <summary>
/// Represents the lifecycle state of a <see cref="GameThread"/>.
/// </summary>
public enum GameThreadState
{
    /// <summary>
    /// The thread is not running.
    /// </summary>
    Stopped,

    /// <summary>
    /// The thread is initializing before entering its frame loop.
    /// </summary>
    Starting,

    /// <summary>
    /// The thread is actively executing frames.
    /// </summary>
    Running,

    /// <summary>
    /// A stop has been requested and the thread is shutting down.
    /// </summary>
    Stopping,
}
