using System.Numerics;
using kajarlabs.Framework.DependencyInjection;
using kajarlabs.Framework.Input;
using kajarlabs.Framework.Platform;
using kajarlabs.Framework.Scene;
using kajarlabs.Framework.Tests.Timing;
using kajarlabs.Framework.Timing;

namespace kajarlabs.Framework.Tests.Scene;

[Collection("SDL")]
public sealed class DrawableLifetimeTests
{
    [Fact]
    public void DefaultsAndNaNValidationBehaveAsExpected()
    {
        using var drawable = new Drawable();

        Assert.Equal(double.MinValue, drawable.LifetimeStart);
        Assert.Equal(double.MaxValue, drawable.LifetimeEnd);
        Assert.True(drawable.IsAlive);
        Assert.True(drawable.RemoveWhenNotAlive);
        Assert.True(drawable.IsPresent);

        Assert.Throws<ArgumentOutOfRangeException>(() => drawable.LifetimeStart = double.NaN);
        Assert.Throws<ArgumentOutOfRangeException>(() => drawable.LifetimeEnd = double.NaN);
    }

    [Fact]
    public void FutureChildWaitsUntilLifetimeStartThenUpdatesAndAutoDisposesOnLifetimeEnd()
    {
        var manualClock = new ManualClock(start: true, initialTime: 0);
        var clock = new FramedClock(manualClock);

        using var container = new Container
        {
            Clock = clock,
            AutoSizeAxes = Axes.Both,
        };

        var extensionUpdates = 0;
        var extensionDisposed = false;
        var child = new ProbeBox
        {
            Size = new Vector2(120f, 80f),
            LifetimeStart = 100,
            LifetimeEnd = 300,
            AlwaysPresent = true,
        };

        child.GetOrAddExtension(_ => new LifecycleProbeExtension(
            onUpdate: _ => extensionUpdates++,
            onDispose: _ => extensionDisposed = true));

        container.Add(child);
        Assert.True(container.Load(new DependencyCollection()));

        // At t = 0: not yet alive, not present, not auto-sized, not removed.
        Assert.False(child.IsAlive);
        Assert.False(child.IsPresent);
        Assert.Single(container.Children);
        Assert.Equal(Vector2.Zero, container.DrawSize);
        Assert.Null(child.GenerateDrawNodeSubtree(0));

        container.UpdateSubTree();
        Assert.Equal(0, child.UpdateCount);
        Assert.Equal(0, extensionUpdates);
        Assert.Single(container.Children);
        Assert.False(child.IsDisposed);

        // Advance to t = 150 (alive): wakes up, runs extension + Update, expands AutoSize, produces DrawNode.
        manualClock.Seek(150);
        container.UpdateSubTree();

        Assert.True(child.IsAlive);
        Assert.True(child.IsPresent);
        Assert.Equal(1, child.UpdateCount);
        Assert.Equal(1, extensionUpdates);
        Assert.Equal(new Vector2(120f, 80f), container.DrawSize);
        Assert.NotNull(container.GenerateDrawNodeSubtree(0));

        // Advance to t = 300 (exact LifetimeEnd): extension gets final tick, Update is skipped,
        // and CompositeDrawable removes & disposes child before AutoSize recalculates.
        manualClock.Seek(300);
        container.UpdateSubTree();

        Assert.False(child.IsAlive);
        Assert.True(child.IsDisposed);
        Assert.True(extensionDisposed);
        Assert.Equal(2, extensionUpdates);
        Assert.Equal(1, child.UpdateCount);
        Assert.Empty(container.Children);
        Assert.Equal(Vector2.Zero, container.DrawSize);
    }

    [Fact]
    public void ExpireSetsLifetimeEndToCurrentTimeAndRemovesOnNextUpdate()
    {
        var manualClock = new ManualClock(start: true, initialTime: 250);
        var clock = new FramedClock(manualClock);

        using var container = new Container { Clock = clock };
        var child = new ProbeBox { Size = new Vector2(40f, 40f) };
        container.Add(child);

        child.Expire();

        Assert.Equal(250, child.LifetimeEnd);
        Assert.False(child.IsAlive);
        Assert.False(child.IsPresent);

        container.UpdateSubTree();

        Assert.Empty(container.Children);
        Assert.True(child.IsDisposed);
    }

    [Fact]
    public void RemoveWhenNotAliveFalseRetainsChildAndSupportsClockRewind()
    {
        var manualClock = new ManualClock(start: true, initialTime: 0);
        var clock = new FramedClock(manualClock);

        using var container = new Container
        {
            Clock = clock,
            AutoSizeAxes = Axes.Both,
        };

        var child = new ProbeBox
        {
            Size = new Vector2(60f, 30f),
            LifetimeStart = 100,
            LifetimeEnd = 200,
            RemoveWhenNotAlive = false,
            AlwaysPresent = true,
        };

        container.Add(child);

        // Before start
        container.UpdateSubTree();
        Assert.False(child.IsAlive);
        Assert.False(child.IsPresent);
        Assert.Single(container.Children);
        Assert.Equal(Vector2.Zero, container.DrawSize);

        // Inside window
        manualClock.Seek(150);
        container.UpdateSubTree();
        Assert.True(child.IsAlive);
        Assert.True(child.IsPresent);
        Assert.Equal(1, child.UpdateCount);
        Assert.Equal(new Vector2(60f, 30f), container.DrawSize);

        // Past end: stays attached and undisposed, stops being present
        manualClock.Seek(250);
        container.UpdateSubTree();
        Assert.False(child.IsAlive);
        Assert.False(child.IsPresent);
        Assert.False(child.IsDisposed);
        Assert.Single(container.Children);
        Assert.Equal(Vector2.Zero, container.DrawSize);

        // Rewind back into window: becomes alive and present again
        manualClock.Seek(125);
        container.UpdateSubTree();
        Assert.True(child.IsAlive);
        Assert.True(child.IsPresent);
        Assert.Equal(2, child.UpdateCount);
        Assert.Equal(new Vector2(60f, 30f), container.DrawSize);
    }

    [Fact]
    public void InputManagerIgnoresDeadDrawablesAndDropsHoverOnExpiry()
    {
        var manualClock = new ManualClock(start: true, initialTime: 0);
        var clock = new FramedClock(manualClock);

        using var manager = new ManualInputManager
        {
            Clock = clock,
            Size = new Vector2(300f, 300f),
        };

        var target = new InteractiveLifetimeBox
        {
            Size = new Vector2(100f, 100f),
            LifetimeStart = 50,
            LifetimeEnd = 150,
            RemoveWhenNotAlive = false,
        };

        manager.Add(target);
        manager.UpdateSubTree();

        // Dead at t = 0 -> pointer inside bounds does not hover or click.
        manager.MoveMouseTo(new Vector2(50f, 50f));
        manager.Click(MouseButton.Left);
        Assert.False(target.IsHovered);
        Assert.Equal(0, target.ClickCount);

        // Enter lifetime [50, 150) -> hover and click work.
        manualClock.Seek(100);
        manager.UpdateSubTree();
        Assert.True(target.IsHovered);

        manager.Click(MouseButton.Left);
        Assert.Equal(1, target.ClickCount);

        // Cross LifetimeEnd -> hover lost cleanly and click no longer hits.
        manualClock.Seek(200);
        manager.UpdateSubTree();
        Assert.False(target.IsHovered);

        manager.Click(MouseButton.Left);
        Assert.Equal(1, target.ClickCount);
    }

    [Fact]
    public void HeadlessGameHostCleansUpExpiredChildrenAcrossFrames()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"kajarlabs-lifetime-host-{Guid.NewGuid():N}");

        try
        {
            using var host = new HeadlessGameHost(new HostOptions
            {
                UserDataPath = tempPath,
                WindowWidth = 640,
                WindowHeight = 480,
                EnableConsoleLogging = false,
                EnableFileLogging = false,
            });

            using var game = new ExpiryProbeGame();
            host.RunFrames(game, 6);

            Assert.True(game.TransientDisposed);
            Assert.Empty(game.Root.Children);
        }
        finally
        {
            if (Directory.Exists(tempPath))
                Directory.Delete(tempPath, recursive: true);
        }
    }

    private sealed class ProbeBox : Box
    {
        public int UpdateCount { get; private set; }

        protected override void Update() => UpdateCount++;
    }

    private sealed class InteractiveLifetimeBox : Box
    {
        public int ClickCount { get; private set; }

        public override bool HandlePositionalInput => true;

        protected override bool OnHover(HoverEvent e) => true;

        protected override bool OnClick(ClickEvent e)
        {
            ClickCount++;
            return true;
        }
    }

    private sealed class LifecycleProbeExtension : IDrawableExtension
    {
        private readonly Action<Drawable> onUpdate;
        private readonly Action<Drawable> onDispose;

        public LifecycleProbeExtension(Action<Drawable> onUpdate, Action<Drawable> onDispose)
        {
            this.onUpdate = onUpdate;
            this.onDispose = onDispose;
        }

        public void OnUpdate(Drawable target) => onUpdate(target);

        public void OnDispose(Drawable target) => onDispose(target);
    }

    private sealed class ExpiryProbeGame : Game
    {
        private Box? transient;
        private int frames;

        public bool TransientDisposed => transient?.IsDisposed ?? false;

        protected override void Load()
        {
            transient = new Box
            {
                Size = new Vector2(100f, 100f),
            };
            Root.Add(transient);
        }

        protected override void Update()
        {
            frames++;

            if (frames == 2 && transient is not null && !transient.IsDisposed)
                transient.Expire();
        }
    }
}
