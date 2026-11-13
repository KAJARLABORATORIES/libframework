using kajarlabs.Framework.Timing;

namespace kajarlabs.Framework.Tests.Timing;

public sealed class StopwatchClockTests
{
    [Fact]
    public void InitialStateReflectsStartParameter()
    {
        var time = new ManualTimeProvider();

        var stopped = new StopwatchClock(time);
        Assert.False(stopped.IsRunning);
        Assert.Equal(0, stopped.CurrentTime);
        Assert.Equal(1.0, stopped.Rate);

        var started = new StopwatchClock(time, start: true);
        Assert.True(started.IsRunning);
        Assert.Equal(0, started.CurrentTime);
        Assert.Equal(1.0, started.Rate);
    }

    [Fact]
    public void AdvancesOnlyWhileRunning()
    {
        var time = new ManualTimeProvider();
        var clock = new StopwatchClock(time);

        time.AdvanceMilliseconds(100);
        Assert.Equal(0, clock.CurrentTime);

        clock.Start();
        Assert.True(clock.IsRunning);
        time.AdvanceMilliseconds(250);
        Assert.Equal(250, clock.CurrentTime, precision: 6);

        clock.Stop();
        Assert.False(clock.IsRunning);
        time.AdvanceMilliseconds(500);
        Assert.Equal(250, clock.CurrentTime, precision: 6);

        clock.Start();
        time.AdvanceMilliseconds(50);
        Assert.Equal(300, clock.CurrentTime, precision: 6);
    }

    [Fact]
    public void StartAndStopAreIdempotent()
    {
        var time = new ManualTimeProvider();
        var clock = new StopwatchClock(time, start: true);

        time.AdvanceMilliseconds(100);
        clock.Start();
        time.AdvanceMilliseconds(50);
        Assert.Equal(150, clock.CurrentTime, precision: 6);

        clock.Stop();
        time.AdvanceMilliseconds(200);
        clock.Stop();
        Assert.Equal(150, clock.CurrentTime, precision: 6);
    }

    [Fact]
    public void ResetStopsClockAndZeroesCurrentTime()
    {
        var time = new ManualTimeProvider();
        var clock = new StopwatchClock(time, start: true);

        time.AdvanceMilliseconds(400);
        clock.Reset();

        Assert.False(clock.IsRunning);
        Assert.Equal(0, clock.CurrentTime);

        time.AdvanceMilliseconds(100);
        Assert.Equal(0, clock.CurrentTime);
    }

    [Fact]
    public void SeekMovesPositionWhenStoppedAndWhenRunning()
    {
        var time = new ManualTimeProvider();
        var clock = new StopwatchClock(time);

        Assert.True(clock.Seek(1250));
        Assert.Equal(1250, clock.CurrentTime, precision: 6);

        clock.Start();
        time.AdvanceMilliseconds(100);
        Assert.Equal(1350, clock.CurrentTime, precision: 6);

        Assert.True(clock.Seek(-500));
        Assert.Equal(-500, clock.CurrentTime, precision: 6);

        time.AdvanceMilliseconds(200);
        Assert.Equal(-300, clock.CurrentTime, precision: 6);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void SeekReturnsFalseForNonFinitePositions(double invalidPosition)
    {
        var time = new ManualTimeProvider();
        var clock = new StopwatchClock(time, start: true);
        time.AdvanceMilliseconds(42);

        Assert.False(clock.Seek(invalidPosition));
        Assert.Equal(42, clock.CurrentTime, precision: 6);
    }

    [Fact]
    public void ChangingRateWhileRunningPreservesCurrentTimeAndScalesSubsequentElapsedTime()
    {
        var time = new ManualTimeProvider();
        var clock = new StopwatchClock(time, start: true);

        time.AdvanceMilliseconds(100);
        Assert.Equal(100, clock.CurrentTime, precision: 6);

        clock.Rate = 2.0;
        Assert.Equal(100, clock.CurrentTime, precision: 6);

        time.AdvanceMilliseconds(50);
        Assert.Equal(200, clock.CurrentTime, precision: 6);

        clock.Rate = 0.0;
        time.AdvanceMilliseconds(500);
        Assert.Equal(200, clock.CurrentTime, precision: 6);

        clock.Rate = -0.5;
        time.AdvanceMilliseconds(100);
        Assert.Equal(150, clock.CurrentTime, precision: 6);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void SettingNonFiniteRateThrows(double invalidRate)
    {
        var clock = new StopwatchClock(new ManualTimeProvider());
        Assert.Throws<ArgumentOutOfRangeException>(() => clock.Rate = invalidRate);
    }

    [Fact]
    public void DefaultConstructorUsesSystemTimeProvider()
    {
        var clock = new StopwatchClock();

        Assert.False(clock.IsRunning);
        Assert.Equal(0, clock.CurrentTime);
        Assert.Equal(1.0, clock.Rate);
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private long timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => timestamp;

        public void AdvanceMilliseconds(double milliseconds)
        {
            timestamp += (long)Math.Round(milliseconds * TimeSpan.TicksPerMillisecond);
        }
    }
}
