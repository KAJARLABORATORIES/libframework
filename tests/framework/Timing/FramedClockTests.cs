using kajarlabs.Framework.Timing;

namespace kajarlabs.Framework.Tests.Timing;

public sealed class FramedClockTests
{
    [Fact]
    public void DefaultSourceIsRunningStopwatchClock()
    {
        var framed = new FramedClock();

        Assert.IsType<StopwatchClock>(framed.Source);
        Assert.True(framed.IsRunning);
        Assert.Equal(1.0, framed.Rate);
        Assert.Equal(0, framed.ElapsedFrameTime);
    }

    [Fact]
    public void CurrentTimeRemainsFrozenBetweenProcessFrameCalls()
    {
        var manual = new ManualClock(start: true);
        var framed = new FramedClock(manual);

        manual.Advance(16.0);
        Assert.Equal(0, framed.CurrentTime);
        Assert.Equal(0, framed.ElapsedFrameTime);

        framed.ProcessFrame();
        Assert.Equal(16.0, framed.CurrentTime, precision: 6);
        Assert.Equal(16.0, framed.ElapsedFrameTime, precision: 6);

        manual.Advance(8.0);
        Assert.Equal(16.0, framed.CurrentTime, precision: 6);
        Assert.Equal(16.0, framed.ElapsedFrameTime, precision: 6);
    }

    [Fact]
    public void TracksElapsedFrameTimeAcrossMultipleFrames()
    {
        var manual = new ManualClock(start: true);
        var framed = new FramedClock(manual);

        manual.Advance(10.0);
        framed.ProcessFrame();
        Assert.Equal(10.0, framed.CurrentTime, precision: 6);
        Assert.Equal(10.0, framed.ElapsedFrameTime, precision: 6);

        manual.Advance(16.5);
        framed.ProcessFrame();
        Assert.Equal(26.5, framed.CurrentTime, precision: 6);
        Assert.Equal(16.5, framed.ElapsedFrameTime, precision: 6);

        manual.Stop();
        manual.Advance(100.0);
        framed.ProcessFrame();
        Assert.Equal(26.5, framed.CurrentTime, precision: 6);
        Assert.Equal(0, framed.ElapsedFrameTime, precision: 6);
    }

    [Fact]
    public void ConstructingWithPreAdvancedSourceAvoidsInitialDeltaSpike()
    {
        var manual = new ManualClock(start: true, initialTime: 1000.0);
        var framed = new FramedClock(manual);

        Assert.Equal(1000.0, framed.CurrentTime, precision: 6);
        Assert.Equal(0, framed.ElapsedFrameTime);

        manual.Advance(16.0);
        framed.ProcessFrame();

        Assert.Equal(1016.0, framed.CurrentTime, precision: 6);
        Assert.Equal(16.0, framed.ElapsedFrameTime, precision: 6);
    }

    [Fact]
    public void DelegatesRateAndRunningStateToSource()
    {
        var manual = new ManualClock(start: false);
        var framed = new FramedClock(manual);

        Assert.False(framed.IsRunning);
        Assert.Equal(1.0, framed.Rate);

        manual.Start();
        manual.Rate = 1.5;

        Assert.True(framed.IsRunning);
        Assert.Equal(1.5, framed.Rate, precision: 6);

        manual.Advance(20.0);
        framed.ProcessFrame();

        Assert.Equal(30.0, framed.CurrentTime, precision: 6);
        Assert.Equal(30.0, framed.ElapsedFrameTime, precision: 6);
    }

    [Fact]
    public void ReflectsUnderlyingSeekOnNextFrame()
    {
        var manual = new ManualClock(start: true);
        var framed = new FramedClock(manual);

        manual.Advance(50.0);
        framed.ProcessFrame();

        manual.Seek(200.0);
        framed.ProcessFrame();
        Assert.Equal(200.0, framed.CurrentTime, precision: 6);
        Assert.Equal(150.0, framed.ElapsedFrameTime, precision: 6);

        manual.Seek(120.0);
        framed.ProcessFrame();
        Assert.Equal(120.0, framed.CurrentTime, precision: 6);
        Assert.Equal(-80.0, framed.ElapsedFrameTime, precision: 6);
    }

    [Fact]
    public void ChangeSourceSwitchesSourceAndAlignsSnapshot()
    {
        var first = new ManualClock(start: true, initialTime: 500.0);
        var second = new ManualClock(start: false, initialTime: 120.0);
        var framed = new FramedClock(first);

        first.Advance(16.0);
        framed.ProcessFrame();
        Assert.Equal(516.0, framed.CurrentTime, precision: 6);
        Assert.Equal(16.0, framed.ElapsedFrameTime, precision: 6);

        framed.ChangeSource(second);

        Assert.Same(second, framed.Source);
        Assert.False(framed.IsRunning);
        Assert.Equal(120.0, framed.CurrentTime, precision: 6);
        Assert.Equal(0, framed.ElapsedFrameTime);

        second.Start();
        second.Advance(25.0);
        framed.ProcessFrame();
        Assert.Equal(145.0, framed.CurrentTime, precision: 6);
        Assert.Equal(25.0, framed.ElapsedFrameTime, precision: 6);
    }

    [Fact]
    public void ChangeSourceThrowsWhenNull()
    {
        var framed = new FramedClock(new ManualClock());
        Assert.Throws<ArgumentNullException>(() => framed.ChangeSource(null!));
    }
}
