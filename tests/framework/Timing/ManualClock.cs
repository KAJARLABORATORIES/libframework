using kajarlabs.Framework.Timing;

namespace kajarlabs.Framework.Tests.Timing;

/// <summary>
/// A deterministic, manually advanced <see cref="IAdjustableClock"/> for testing.
/// </summary>
public sealed class ManualClock : IAdjustableClock
{
    public ManualClock(bool start = false, double initialTime = 0)
    {
        IsRunning = start;
        CurrentTime = initialTime;
    }

    public double CurrentTime { get; private set; }

    public double Rate
    {
        get;
        set
        {
            if (!double.IsFinite(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "Clock rate must be a finite number.");

            field = value;
        }
    } = 1.0;

    public bool IsRunning { get; private set; }

    public void Start()
    {
        IsRunning = true;
    }

    public void Stop()
    {
        IsRunning = false;
    }

    public void Reset()
    {
        IsRunning = false;
        CurrentTime = 0;
    }

    public bool Seek(double position)
    {
        if (!double.IsFinite(position))
            return false;

        CurrentTime = position;
        return true;
    }

    /// <summary>
    /// Advances the clock by <paramref name="deltaMs"/> scaled by <see cref="Rate"/> if the clock is running.
    /// </summary>
    public void Advance(double deltaMs)
    {
        if (IsRunning)
            CurrentTime += deltaMs * Rate;
    }
}
