using System.Runtime.CompilerServices;
using kajarlabs.Framework.Bindables;

namespace kajarlabs.Framework.Tests.Bindables;

public sealed class BindableTests
{
    [Fact]
    public void ValueChangedEventSupportsEqualityAndFormatting()
    {
        var a = new ValueChangedEvent<int>(1, 2);
        var b = new ValueChangedEvent<int>(1, 2);
        var c = new ValueChangedEvent<int>(2, 3);

        Assert.Equal(a, b);
        Assert.True(a == b);
        Assert.True(a != c);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.Equal("1 -> 2", a.ToString());
    }

    [Fact]
    public void ValueChangesSuppressEqualValuesAndRespectDefaultAndDisabled()
    {
        var bindable = new Bindable<string>("initial");
        var events = new List<ValueChangedEvent<string>>();
        var disabledEvents = new List<bool>();

        bindable.BindValueChanged(events.Add, runOnceImmediately: true);
        bindable.BindDisabledChanged(disabledEvents.Add, runOnceImmediately: true);

        Assert.Single(events);
        Assert.Equal(new ValueChangedEvent<string>("initial", "initial"), events[0]);
        Assert.Equal([false], disabledEvents);
        Assert.True(bindable.IsDefault);

        // Equal assignment must not emit another event.
        bindable.Value = "initial";
        Assert.Single(events);

        bindable.Value = "updated";
        Assert.Equal(2, events.Count);
        Assert.Equal(new ValueChangedEvent<string>("initial", "updated"), events[1]);
        Assert.False(bindable.IsDefault);

        bindable.SetDefault();
        Assert.Equal("initial", bindable.Value);
        Assert.True(bindable.IsDefault);
        Assert.Equal(3, events.Count);

        bindable.Disabled = true;
        Assert.Equal([false, true], disabledEvents);
        Assert.Throws<InvalidOperationException>(() => bindable.Value = "blocked");
        Assert.Throws<InvalidOperationException>(() => bindable.SetDefault());
        Assert.Equal("initial", bindable.Value);
    }

    [Fact]
    public void BindToSynchronizesInitialStateAndPropagatesBidirectionallyWithoutCycles()
    {
        var a = new Bindable<int>(10) { Default = 5 };
        var b = new Bindable<int>(99) { Disabled = true };
        var c = new Bindable<int>(77);

        var aEvents = new List<ValueChangedEvent<int>>();
        var bEvents = new List<ValueChangedEvent<int>>();
        var cEvents = new List<ValueChangedEvent<int>>();

        a.BindValueChanged(aEvents.Add);
        b.BindValueChanged(bEvents.Add);
        c.BindValueChanged(cEvents.Add);

        // Initial sync copies Default, Value, and Disabled from source (a).
        b.BindTo(a);
        Assert.Equal(10, b.Value);
        Assert.Equal(5, b.Default);
        Assert.False(b.Disabled);
        Assert.Single(bEvents);

        // Form a triangle cycle A <-> B <-> C <-> A.
        c.BindTo(b);
        a.BindTo(c);

        aEvents.Clear();
        bEvents.Clear();
        cEvents.Clear();

        // Changing A propagates across the full triangle, firing each listener exactly once.
        a.Value = 42;
        Assert.Equal(42, a.Value);
        Assert.Equal(42, b.Value);
        Assert.Equal(42, c.Value);
        Assert.Single(aEvents);
        Assert.Single(bEvents);
        Assert.Single(cEvents);

        // Changing B propagates back to A and C once each.
        b.Value = 7;
        Assert.Equal(7, a.Value);
        Assert.Equal(7, c.Value);
        Assert.Equal(2, aEvents.Count);
        Assert.Equal(2, bEvents.Count);
        Assert.Equal(2, cEvents.Count);

        // Disabled state propagates bidirectionally as well.
        c.Disabled = true;
        Assert.True(a.Disabled);
        Assert.True(b.Disabled);
        Assert.Throws<InvalidOperationException>(() => a.Value = 1);
    }

    [Fact]
    public void BindToRejectsSelfAndDuplicateBindingsAndSupportsIBindable()
    {
        var source = new Bindable<int>(3);
        IBindable<int> readOnlySource = source;

        Assert.Throws<ArgumentException>(() => source.BindTo(source));

        var copy = readOnlySource.GetBoundCopy();
        Assert.Equal(3, copy.Value);

        var second = new Bindable<int>();
        second.BindTo(readOnlySource);
        Assert.Equal(3, second.Value);

        Assert.Throws<ArgumentException>(() => second.BindTo(source));

        source.Value = 11;
        Assert.Equal(11, copy.Value);
        Assert.Equal(11, second.Value);
    }

    [Fact]
    public void UnbindEventsBindingsAndAllIsolateListenersAndPeersSafely()
    {
        var source = new Bindable<int>(1);
        var peerA = source.GetBoundCopy();
        var peerB = source.GetBoundCopy();

        var sourceCalls = 0;
        var peerACalls = 0;
        var peerBCalls = 0;

        source.ValueChanged += _ => sourceCalls++;
        peerA.ValueChanged += _ =>
        {
            peerACalls++;
            peerA.UnbindBindings();
        };
        peerB.ValueChanged += _ => peerBCalls++;

        // Re-entrant UnbindBindings inside peerA's callback must not break propagation to peerB.
        source.Value = 2;
        Assert.Equal(1, sourceCalls);
        Assert.Equal(1, peerACalls);
        Assert.Equal(1, peerBCalls);

        // peerA is now disconnected; peerB remains connected.
        source.Value = 3;
        Assert.Equal(2, sourceCalls);
        Assert.Equal(1, peerACalls);
        Assert.Equal(2, peerBCalls);
        Assert.Equal(2, peerA.Value);
        Assert.Equal(3, peerB.Value);

        // UnbindEvents keeps bindings intact while clearing handlers.
        peerB.UnbindEvents();
        source.Value = 4;
        Assert.Equal(4, peerB.Value);
        Assert.Equal(2, peerBCalls);

        // UnbindAll clears both handlers and peer links.
        source.UnbindAll();
        source.Value = 5;
        Assert.Equal(3, sourceCalls);
        Assert.Equal(4, peerB.Value);
    }

    [Fact]
    public void UnreferencedBoundCopiesCanBeGarbageCollected()
    {
        var source = new Bindable<int>(10);
        var weakCopy = createEphemeralCopy(source);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.False(weakCopy.TryGetTarget(out _));

        // Ensure source remains functional after dead weak references are pruned.
        source.Value = 20;
        Assert.Equal(20, source.Value);

        [MethodImpl(MethodImplOptions.NoInlining)]
        static WeakReference<Bindable<int>> createEphemeralCopy(Bindable<int> target)
        {
            var copy = target.GetBoundCopy();
            return new WeakReference<Bindable<int>>(copy);
        }
    }

    [Fact]
    public void BindableBoolTogglesAndPreservesConcreteTypeInBoundCopy()
    {
        var flag = new BindableBool(false);
        var copy = Assert.IsType<BindableBool>(flag.GetBoundCopy());

        flag.Toggle();
        Assert.True(flag.Value);
        Assert.True(copy.Value);

        copy.Toggle();
        Assert.False(flag.Value);

        flag.Disabled = true;
        Assert.Throws<InvalidOperationException>(() => copy.Toggle());
    }

    [Fact]
    public void BindableNumberClampsSnapsPrecisionAndPropagatesRange()
    {
        var floatNum = new BindableNumber<float>(0.5f)
        {
            MinValue = 0f,
            MaxValue = 1f,
            Precision = 0.25f,
        };

        Assert.Equal(0.5f, floatNum.Value);
        floatNum.Value = 0.63f;
        Assert.Equal(0.75f, floatNum.Value);

        floatNum.Value = 5f;
        Assert.Equal(1f, floatNum.Value);

        floatNum.Value = -2f;
        Assert.Equal(0f, floatNum.Value);

        var copy = Assert.IsType<BindableNumber<float>>(floatNum.GetBoundCopy());
        Assert.Equal(0f, copy.MinValue);
        Assert.Equal(1f, copy.MaxValue);
        Assert.Equal(0.25f, copy.Precision);

        // Changing MaxValue re-clamps existing Value and propagates range to bound copies.
        floatNum.Value = 1f;
        floatNum.MaxValue = 0.5f;
        Assert.Equal(0.5f, floatNum.Value);
        Assert.Equal(0.5f, copy.MaxValue);
        Assert.Equal(0.5f, copy.Value);

        Assert.Throws<ArgumentOutOfRangeException>(() => floatNum.MinValue = 2f);
        Assert.Throws<ArgumentOutOfRangeException>(() => floatNum.Precision = -0.1f);
        Assert.Throws<ArgumentOutOfRangeException>(() => floatNum.Value = float.NaN);

        var intNum = new BindableNumber<int>(10)
        {
            MinValue = 0,
            MaxValue = 100,
            Precision = 5,
        };
        Assert.Equal(10, intNum.Value);
        intNum.Value = 13;
        Assert.Equal(15, intNum.Value);

        var decNum = new BindableNumber<decimal>(1.00m)
        {
            MinValue = 0m,
            MaxValue = 2m,
            Precision = 0.2m,
        };
        Assert.Equal(1.00m, decNum.Value);
        decNum.Value = 1.33m;
        Assert.Equal(1.4m, decNum.Value);

        // Clamping on a bound BindableNumber converges back to an unclamped peer without cycles.
        var unclamped = new Bindable<int>(0);
        var clamped = new BindableNumber<int>(0) { MinValue = 0, MaxValue = 5 };
        unclamped.BindTo(clamped);

        unclamped.Value = 8;
        Assert.Equal(5, unclamped.Value);
        Assert.Equal(5, clamped.Value);
    }
}
