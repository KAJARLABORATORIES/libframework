using System.Numerics;
using kajarlabs.Framework.Timing;

namespace kajarlabs.Framework.Scene.Transforms.Tests;

public sealed class TransformSequenceScopeTests
{
    [Fact]
    public void NestedDelayedAndAbsoluteScopesScheduleAtExpectedTimesAndRestoreOuterStartTimes()
    {
        var manualClock = new TestManualClock(initialTime: 50);
        var clock = new FramedClock(manualClock);
        using var box = new Box { Clock = clock, Alpha = 0f, X = 0f, Y = 0f, Rotation = 0f, Scale = Vector2.One };

        using (box.BeginDelayedSequence(100)) // 150
        {
            var xSeq = box.MoveToX(100f, 50);
            Assert.Equal(150, xSeq.OriginTime);

            using (box.BeginDelayedSequence(25)) // 175
            {
                var ySeq = box.MoveToY(200f, 50);
                Assert.Equal(175, ySeq.OriginTime);
            }

            using (box.BeginAbsoluteSequence(500)) // 500
            {
                var rotSeq = box.RotateTo(90f, 100);
                Assert.Equal(500, rotSeq.OriginTime);

                using (box.BeginDelayedSequence(50)) // 550
                {
                    var scaleSeq = box.ScaleTo(2f, 50);
                    Assert.Equal(550, scaleSeq.OriginTime);
                }
            }

            // Outer BeginDelayedSequence(100) -> 150 must be restored.
            var alphaSeq = box.FadeIn(50);
            Assert.Equal(150, alphaSeq.OriginTime);
        }

        // Default Drawable.Time (50) must be restored after leaving all scopes.
        var rootSeq = box.FadeTo(0.5f, 100);
        Assert.Equal(50, rootSeq.OriginTime);
    }

    [Fact]
    public void ScopeRestoresOnExceptionAndHandlesDoubleDisposeAndTargetDisposedSafely()
    {
        var manualClock = new TestManualClock(initialTime: 10);
        var clock = new FramedClock(manualClock);
        using var box = new Box { Clock = clock };

        try
        {
            using (box.BeginAbsoluteSequence(1000))
                throw new InvalidOperationException("simulated failure");
        }
        catch (InvalidOperationException)
        {
        }

        Assert.Equal(10, box.FadeIn(50).OriginTime);

        // Double-dispose of an inner scope must not clobber outer scope.
        using (box.BeginAbsoluteSequence(500))
        {
            var inner = box.BeginDelayedSequence(100);
            inner.Dispose();
            inner.Dispose();
            Assert.Equal(500, box.MoveToX(10f, 50).OriginTime);
        }

        // Default uninitialized scope Dispose is a safe no-op.
        TransformSequenceScope defaultScope = default;
        defaultScope.Dispose();

        // Disposal of target inside an active scope must not throw when scope exits.
        var disposableBox = new Box { Clock = clock };
        using (disposableBox.BeginDelayedSequence(50))
        {
            disposableBox.Dispose();
        }

        Assert.Throws<ObjectDisposedException>(() => disposableBox.BeginDelayedSequence(50));
        Assert.Throws<ObjectDisposedException>(() => disposableBox.BeginAbsoluteSequence(50));
        Assert.Throws<ArgumentOutOfRangeException>(() => box.BeginDelayedSequence(double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => box.BeginAbsoluteSequence(double.PositiveInfinity));
    }

    [Fact]
    public void ScopesAndStandardFluentChainsAllocateZeroHeapBytesInSteadyState()
    {
        var manualClock = new TestManualClock(initialTime: 0);
        var clock = new FramedClock(manualClock);
        using var box = new Box { Clock = clock };

        // Warm up JIT and grow the timeline list capacity once.
        using (box.BeginDelayedSequence(10))
        {
            box.FadeIn(100).Then().MoveTo(new Vector2(10f, 20f), 100);
        }

        box.FinishTransforms();

        var startAllocated = GC.GetAllocatedBytesForCurrentThread();

        for (var i = 0; i < 40; i++)
        {
            using (box.BeginDelayedSequence(10))
            {
                box.FadeIn(100).Then().MoveTo(new Vector2(10f, 20f), 100);
            }

            box.FinishTransforms();
        }

        var endAllocated = GC.GetAllocatedBytesForCurrentThread();
        Assert.Equal(0, endAllocated - startAllocated);
    }
}
