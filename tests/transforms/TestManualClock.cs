using kajarlabs.Framework.Timing;

namespace kajarlabs.Framework.Scene.Transforms.Tests;

internal sealed class TestManualClock : IAdjustableClock
{
    public TestManualClock(bool start = true, double initialTime = 0.0)
    {
        IsRunning = start;
        CurrentTime = initialTime;
    }

    public double CurrentTime { get; private set; }

    public double Rate
    {
        get => field;
        set
        {
            if (!double.IsFinite(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "Clock rate must be finite.");

            field = value;
        }
    } = 1.0;

    public bool IsRunning { get; private set; }

    public void Start() => IsRunning = true;

    public void Stop() => IsRunning = false;

    public void Reset()
    {
        IsRunning = false;
        CurrentTime = 0.0;
    }

    public bool Seek(double position)
    {
        if (!double.IsFinite(position))
            return false;

        CurrentTime = position;
        return true;
    }

    public void Advance(double deltaMs)
    {
        if (IsRunning)
            CurrentTime += deltaMs * Rate;
    }
}
