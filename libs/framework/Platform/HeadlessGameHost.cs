using kajarlabs.Framework.Graphics.Rendering;

namespace kajarlabs.Framework.Platform;

/// <summary>
/// A <see cref="GameHost"/> configured for automated testing without a native window,
/// using the SDL <c>dummy</c> video driver and supporting bounded frame execution.
/// </summary>
public class HeadlessGameHost : GameHost
{
    private readonly string? ownedUserDataPath;

    private int targetFrames = -1;
    private long completedFrames;

    /// <summary>
    /// Initializes a new instance of the <see cref="HeadlessGameHost"/> class.
    /// </summary>
    /// <param name="options">
    /// Optional host configuration. When <see langword="null"/>, an isolated temporary user data directory
    /// is created and cleaned up on disposal, with console logging disabled.
    /// </param>
    /// <param name="useRealTimeThrottling">
    /// Whether the host threads throttle to their default real-time frequencies. Defaults to <see langword="false"/>
    /// so bounded test runs complete without artificial delays.
    /// </param>
    public HeadlessGameHost(HostOptions? options = null, bool useRealTimeThrottling = false)
        : this(options ?? createIsolatedOptions(), ownsUserDataPath: options is null, useRealTimeThrottling, SdlPlatformBackend.INSTANCE)
    {
    }

    internal HeadlessGameHost(
        HostOptions? options,
        bool ownsUserDataPath,
        bool useRealTimeThrottling,
        IPlatformBackend backend)
        : base(options, backend, gpuBackend: null)
    {
        ownedUserDataPath = ownsUserDataPath ? Options.UserDataPath : null;

        if (!useRealTimeThrottling)
        {
            Threads.Input.TargetFrequency = 0;
            Threads.Update.TargetFrequency = 0;
            Threads.Draw.TargetFrequency = 0;
        }
    }

    /// <summary>
    /// Gets the number of update frames completed during the current or most recent run.
    /// </summary>
    public long CompletedFrames => Volatile.Read(ref completedFrames);

    /// <inheritdoc />
    protected override bool ShouldCreateWindow => false;

    /// <inheritdoc />
    protected override bool ShouldRouteSdlLogs => false;

    /// <inheritdoc />
    protected override string? PreferredVideoDriver => "dummy";

    /// <summary>
    /// Runs the host for <paramref name="frameCount"/> update frames and then performs a clean shutdown.
    /// </summary>
    public void RunFrames(int frameCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(frameCount);
        runBounded(null, frameCount);
    }

    /// <summary>
    /// Runs the specified <paramref name="game"/> for <paramref name="frameCount"/> update frames
    /// and then performs a clean shutdown.
    /// </summary>
    public void RunFrames(Game game, int frameCount)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentOutOfRangeException.ThrowIfNegative(frameCount);
        runBounded(game, frameCount);
    }

    /// <inheritdoc />
    protected override void OnUpdateInitialize()
    {
        Interlocked.Exchange(ref completedFrames, 0);

        if (Volatile.Read(ref targetFrames) == 0)
        {
            Threads.Update.RequestStop();
            Exit();
        }
    }

    /// <inheritdoc />
    protected override void OnUpdateFrame()
    {
        var current = Interlocked.Increment(ref completedFrames);
        var limit = Volatile.Read(ref targetFrames);

        if (limit >= 0 && current >= limit)
        {
            Threads.Update.RequestStop();
            Exit();
        }
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing && ownedUserDataPath is not null && Directory.Exists(ownedUserDataPath))
        {
            try
            {
                Directory.Delete(ownedUserDataPath, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort cleanup of temporary test directory.
            }
        }
    }

    private static HostOptions createIsolatedOptions() => new()
    {
        UserDataPath = Path.Combine(Path.GetTempPath(), $"kajarlabs-headless-{Guid.NewGuid():N}"),
        EnableConsoleLogging = false,
        EnableFileLogging = true,
    };

    private void runBounded(Game? game, int frameCount)
    {
        if (IsRunning)
            throw new InvalidOperationException("The game host is already running.");

        Volatile.Write(ref targetFrames, frameCount);

        try
        {
            if (game is not null)
                Run(game);
            else
                Run();
        }
        finally
        {
            Volatile.Write(ref targetFrames, -1);
        }
    }
}
