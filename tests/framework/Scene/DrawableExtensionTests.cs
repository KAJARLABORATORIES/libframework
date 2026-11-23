using kajarlabs.Framework.DependencyInjection;
using kajarlabs.Framework.Scene;

namespace kajarlabs.Framework.Tests.Scene;

public sealed class DrawableExtensionTests
{
    [Fact]
    public void TryGetAndRemoveReturnFalseWithoutAttachingWhenNoExtensionExists()
    {
        using var drawable = new Drawable();

        Assert.False(drawable.TryGetExtension<RecordingExtension>(out var missing));
        Assert.Null(missing);
        Assert.False(drawable.RemoveExtension<RecordingExtension>());
    }

    [Fact]
    public void GetOrAddExtensionReusesExistingInstanceAndSupportsMultipleExtensions()
    {
        var log = new List<string>();
        using var drawable = new Drawable();

        var firstFactoryCalls = 0;
        var first = drawable.GetOrAddExtension(_ =>
        {
            firstFactoryCalls++;
            return new RecordingExtension("first", log);
        });

        var secondLookup = drawable.GetOrAddExtension(_ =>
        {
            firstFactoryCalls++;
            return new RecordingExtension("duplicate", log);
        });

        Assert.Same(first, secondLookup);
        Assert.Equal(1, firstFactoryCalls);
        Assert.True(drawable.TryGetExtension<RecordingExtension>(out var foundFirst));
        Assert.Same(first, foundFirst);

        var secondary = drawable.GetOrAddExtension(_ => new SecondaryExtension(log));
        Assert.True(drawable.TryGetExtension<RecordingExtension>(out foundFirst));
        Assert.Same(first, foundFirst);
        Assert.True(drawable.TryGetExtension<SecondaryExtension>(out var foundSecondary));
        Assert.Same(secondary, foundSecondary);

        Assert.True(drawable.UpdateSubTree());
        Assert.Equal(["first:OnUpdate", "secondary:OnUpdate"], log);

        log.Clear();
        Assert.True(drawable.RemoveExtension<RecordingExtension>());
        Assert.False(drawable.TryGetExtension<RecordingExtension>(out _));
        Assert.True(drawable.TryGetExtension<SecondaryExtension>(out _));
        Assert.Equal(["first:OnDispose"], log);

        log.Clear();
        Assert.True(drawable.UpdateSubTree());
        Assert.Equal(["secondary:OnUpdate"], log);
    }

    [Fact]
    public void UpdateSubTreeInvokesExtensionsAfterLoadCompleteAndSchedulerBeforeUpdate()
    {
        var log = new List<string>();
        using var container = new LifecycleContainer(log);
        var child = new LifecycleDrawable(log);
        container.Add(child);

        container.GetOrAddExtension(_ => new RecordingExtension("container-ext", log));
        child.GetOrAddExtension(_ => new RecordingExtension("child-ext", log));

        Assert.True(container.Load(new DependencyCollection()));
        container.Schedule(() => log.Add("container:Scheduled"));
        child.Schedule(() => log.Add("child:Scheduled"));

        log.Clear();
        Assert.True(container.UpdateSubTree());

        Assert.Equal(
            [
                "container:LoadComplete",
                "container:Scheduled",
                "container-ext:OnUpdate",
                "container:Update",
                "child:LoadComplete",
                "child:Scheduled",
                "child-ext:OnUpdate",
                "child:Update",
                "container:UpdateAfterChildren",
            ],
            log);
    }

    [Fact]
    public void DisposeNotifiesExtensionsOnceAndThrowsOnSubsequentGetOrAdd()
    {
        var log = new List<string>();
        var drawable = new Drawable();

        drawable.GetOrAddExtension(_ => new RecordingExtension("a", log));
        drawable.GetOrAddExtension(_ => new SecondaryExtension(log));

        drawable.Dispose();
        drawable.Dispose();

        Assert.Equal(["secondary:OnDispose", "a:OnDispose"], log);
        Assert.False(drawable.TryGetExtension<RecordingExtension>(out _));
        Assert.False(drawable.TryGetExtension<SecondaryExtension>(out _));
        Assert.Throws<ObjectDisposedException>(() => drawable.GetOrAddExtension(_ => new RecordingExtension("late", log)));
    }

    [Fact]
    public void UpdateSubTreeHandlesSelfDisposeAndSelfRemovalInsideOnUpdate()
    {
        var log = new List<string>();

        using (var selfDisposing = new LifecycleDrawable(log))
        {
            selfDisposing.GetOrAddExtension(_ => new CallbackExtension(
                onUpdate: static target => target.Dispose(),
                onDispose: _ => log.Add("disposed")));

            Assert.False(selfDisposing.UpdateSubTree());
            Assert.True(selfDisposing.IsDisposed);
            Assert.Equal(["disposed"], log);
        }

        log.Clear();

        using var multi = new Drawable();
        multi.GetOrAddExtension(_ => new CallbackExtension(
            onUpdate: static target => target.RemoveExtension<CallbackExtension>(),
            onDispose: _ => log.Add("removed-first")));
        multi.GetOrAddExtension(_ => new SecondaryExtension(log));
        multi.GetOrAddExtension(_ => new ThirdExtension(log));

        Assert.True(multi.UpdateSubTree());
        Assert.Equal(["removed-first", "secondary:OnUpdate", "third:OnUpdate"], log);
    }

    private sealed class RecordingExtension : IDrawableExtension
    {
        private readonly string name;
        private readonly List<string> log;

        public RecordingExtension(string name, List<string> log)
        {
            this.name = name;
            this.log = log;
        }

        public void OnUpdate(Drawable target) => log.Add($"{name}:OnUpdate");

        public void OnDispose(Drawable target) => log.Add($"{name}:OnDispose");
    }

    private sealed class SecondaryExtension : IDrawableExtension
    {
        private readonly List<string> log;

        public SecondaryExtension(List<string> log)
        {
            this.log = log;
        }

        public void OnUpdate(Drawable target) => log.Add("secondary:OnUpdate");

        public void OnDispose(Drawable target) => log.Add("secondary:OnDispose");
    }

    private sealed class ThirdExtension : IDrawableExtension
    {
        private readonly List<string> log;

        public ThirdExtension(List<string> log)
        {
            this.log = log;
        }

        public void OnUpdate(Drawable target) => log.Add("third:OnUpdate");

        public void OnDispose(Drawable target) => log.Add("third:OnDispose");
    }

    private sealed class CallbackExtension : IDrawableExtension
    {
        private readonly Action<Drawable> onUpdate;
        private readonly Action<Drawable> onDispose;

        public CallbackExtension(Action<Drawable> onUpdate, Action<Drawable> onDispose)
        {
            this.onUpdate = onUpdate;
            this.onDispose = onDispose;
        }

        public void OnUpdate(Drawable target) => onUpdate(target);

        public void OnDispose(Drawable target) => onDispose(target);
    }

    private sealed class LifecycleContainer : Container
    {
        private readonly List<string> log;

        public LifecycleContainer(List<string> log)
        {
            this.log = log;
        }

        protected override void LoadComplete() => log.Add("container:LoadComplete");

        protected override void Update() => log.Add("container:Update");

        protected override void UpdateAfterChildren()
        {
            base.UpdateAfterChildren();
            log.Add("container:UpdateAfterChildren");
        }
    }

    private sealed class LifecycleDrawable : Drawable
    {
        private readonly List<string> log;

        public LifecycleDrawable(List<string> log)
        {
            this.log = log;
        }

        protected override void LoadComplete() => log.Add("child:LoadComplete");

        protected override void Update() => log.Add("child:Update");
    }
}
