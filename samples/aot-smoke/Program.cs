using System.Text;
using kajarlabs.Framework.IO;
using kajarlabs.Framework.Threading;
using kajarlabs.Framework.Timing;
using SDL3;

SDL.SetHint("SDL_VIDEO_DRIVER", "dummy");

if (!SDL.Init(SDL.InitFlags.Video))
{
    Console.Error.WriteLine($"SDL_Init failed: {SDL.GetError()}");
    return 1;
}

try
{
    var version = SDL.GetVersion();
    Console.WriteLine($"SDL {version / 1_000_000}.{version / 1_000 % 1_000}.{version % 1_000} initialized under NativeAOT.");

    var tempRoot = Path.Combine(Path.GetTempPath(), $"kajarlabs-aot-smoke-{Guid.NewGuid():N}");

    try
    {
        var storage = new FileSystemStorage(tempRoot);
        var probeFile = "probe.txt";
        var payload = "aot-smoke";

        using (var writeStream = storage.GetStream(probeFile, FileAccess.Write, FileMode.Create))
        {
            var bytes = Encoding.UTF8.GetBytes(payload);
            writeStream.Write(bytes, 0, bytes.Length);
        }

        if (!storage.Exists(probeFile))
            throw new InvalidOperationException("FileSystemStorage failed to persist smoke probe file.");

        using (var reader = new StreamReader(storage.GetStream(probeFile, FileAccess.Read, FileMode.Open), Encoding.UTF8))
        {
            if (!string.Equals(reader.ReadToEnd(), payload, StringComparison.Ordinal))
                throw new InvalidOperationException("FileSystemStorage returned unexpected file content.");
        }

        storage.Delete(probeFile);
    }
    finally
    {
        if (Directory.Exists(tempRoot))
            Directory.Delete(tempRoot, recursive: true);
    }

    var stopwatch = new StopwatchClock(start: true);
    var framedClock = new FramedClock(stopwatch);
    framedClock.ProcessFrame();

    var localScheduler = new Scheduler(framedClock);
    var localExecuted = false;
    localScheduler.Add(() => localExecuted = true);
    localScheduler.Update();

    if (!localExecuted)
        throw new InvalidOperationException("Scheduler failed to execute scheduled task.");

    using var runner = new GameThreadRunner();
    using var threadExecuted = new ManualResetEventSlim(false);
    runner.Start();
    runner.Update.Scheduler.Add(() => threadExecuted.Set());

    if (!threadExecuted.Wait(TimeSpan.FromSeconds(5)))
        throw new TimeoutException("GameThreadRunner.Update failed to execute scheduled task within timeout.");

    runner.Stop();

    Console.WriteLine("kajarlabs.Framework subsystems verified under NativeAOT.");
    return 0;
}
finally
{
    SDL.Quit();
}
