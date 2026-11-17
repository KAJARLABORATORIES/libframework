using System.Numerics;
using kajarlabs.Framework.DependencyInjection;
using kajarlabs.Framework.Graphics;
using kajarlabs.Framework.Graphics.Rendering;
using kajarlabs.Framework.Platform;
using kajarlabs.Framework.Scene;

namespace kajarlabs.Framework.Tests.Scene;

public sealed partial class DrawableLoadLifecycleTests
{
    [Fact]
    public void LoadTransitionsStatesAndExecutesLifecycleHooksInDeterministicOrder()
    {
        var log = new List<string>();
        using var parent = new LifecycleProbeContainer("parent", log);
        var child = new LifecycleProbeContainer("child", log);
        var grandchild = new LifecycleProbeDrawable("grandchild", log);

        child.Add(grandchild);
        parent.Add(child);

        Assert.Equal(LoadState.NotLoaded, parent.LoadState);
        Assert.Equal(LoadState.NotLoaded, child.LoadState);
        Assert.Equal(LoadState.NotLoaded, grandchild.LoadState);
        Assert.Same(DependencyCollection.EMPTY, parent.Dependencies);

        var rootDeps = new DependencyCollection();
        rootDeps.Cache<IServiceA>(new ServiceA("root-a"));

        Assert.True(parent.Load(rootDeps));
        Assert.False(parent.Load(rootDeps));

        Assert.Equal(LoadState.Ready, parent.LoadState);
        Assert.Equal(LoadState.Ready, child.LoadState);
        Assert.Equal(LoadState.Ready, grandchild.LoadState);
        Assert.Equal(LoadState.Loading, parent.LoadStateObservedInLoad);
        Assert.Equal(LoadState.Loading, child.LoadStateObservedInLoad);
        Assert.Equal(LoadState.Loading, grandchild.LoadStateObservedInLoad);
        Assert.Equal("root-a", grandchild.ResolvedNameInLoad);

        Assert.Equal(
            [
                "parent:Load",
                "child:Load",
                "grandchild:Load",
            ],
            log);

        log.Clear();
        Assert.True(parent.UpdateSubTree());

        Assert.Equal(LoadState.Loaded, parent.LoadState);
        Assert.Equal(LoadState.Loaded, child.LoadState);
        Assert.Equal(LoadState.Loaded, grandchild.LoadState);

        Assert.Equal(
            [
                "parent:LoadComplete",
                "parent:Update",
                "child:LoadComplete",
                "child:Update",
                "grandchild:LoadComplete",
                "grandchild:Update",
            ],
            log);

        log.Clear();
        Assert.True(parent.UpdateSubTree());

        Assert.Equal(
            [
                "parent:Update",
                "child:Update",
                "grandchild:Update",
            ],
            log);
    }

    [Fact]
    public void NestedContainersPropagateShadowAndIsolateDependencies()
    {
        var rootDeps = new DependencyCollection();
        rootDeps.Cache<IServiceA>(new ServiceA("root-A"));
        rootDeps.Cache<IServiceB>(new ServiceB(42));

        using var root = new Container();
        var leftContainer = new ShadowingLeftContainer();
        var rightContainer = new Container();
        var deepChild = new DeepConsumerDrawable();
        var rightChild = new SiblingConsumerDrawable();

        leftContainer.Add(deepChild);
        rightContainer.Add(rightChild);
        root.AddRange([leftContainer, rightContainer]);

        root.Load(rootDeps);

        Assert.Equal("left-A", deepChild.ResolvedA.Name);
        Assert.Equal(42, deepChild.ResolvedB.Code);
        Assert.Equal("left-token", deepChild.ResolvedToken.Value);
        Assert.Null(deepChild.MissingOptional);
        Assert.Same(deepChild.ResolvedA, deepChild.Dependencies.Get<IServiceA>());

        Assert.Equal("root-A", rightChild.ResolvedA.Name);
        Assert.Null(rightChild.OptionalLeftToken);
        Assert.False(rightChild.Dependencies.TryGet<LeftToken>(out _));
    }

    [Fact]
    public void CompositeDrawableLoadsChildrenAddedInsideLoadAndDynamicallyToLoadedParent()
    {
        using var parent = new DynamicAddingContainer();
        var rootDeps = new DependencyCollection();

        parent.Load(rootDeps);

        Assert.NotNull(parent.ChildAddedInLoad);
        Assert.Equal(LoadState.Ready, parent.ChildAddedInLoad.LoadState);
        Assert.Equal("dynamic-parent", parent.ChildAddedInLoad.ResolvedA.Name);

        Assert.True(parent.UpdateSubTree());
        Assert.Equal(LoadState.Loaded, parent.LoadState);
        Assert.Equal(LoadState.Loaded, parent.ChildAddedInLoad.LoadState);
        Assert.Equal(1, parent.ChildAddedInLoad.LoadCount);
        Assert.Equal(1, parent.ChildAddedInLoad.LoadCompleteCount);

        var addedWhileLoaded = new DynamicConsumerDrawable();
        parent.Add(addedWhileLoaded);

        Assert.Equal(LoadState.Ready, addedWhileLoaded.LoadState);
        Assert.Equal("dynamic-parent", addedWhileLoaded.ResolvedA.Name);
        Assert.Equal(1, addedWhileLoaded.LoadCount);
        Assert.Equal(0, addedWhileLoaded.LoadCompleteCount);

        Assert.True(parent.UpdateSubTree());
        Assert.Equal(LoadState.Loaded, addedWhileLoaded.LoadState);
        Assert.Equal(1, addedWhileLoaded.LoadCount);
        Assert.Equal(1, addedWhileLoaded.LoadCompleteCount);

        // Pre-loaded Ready child (for async load completion) attaches without re-running Load().
        var preReadyChild = new DynamicConsumerDrawable();
        preReadyChild.Load(parent.Dependencies);
        Assert.Equal(LoadState.Ready, preReadyChild.LoadState);
        Assert.Equal(1, preReadyChild.LoadCount);

        parent.Add(preReadyChild);
        Assert.Equal(LoadState.Ready, preReadyChild.LoadState);
        Assert.Equal(1, preReadyChild.LoadCount);

        Assert.True(parent.UpdateSubTree());
        Assert.Equal(LoadState.Loaded, preReadyChild.LoadState);
        Assert.Equal(1, preReadyChild.LoadCount);
        Assert.Equal(1, preReadyChild.LoadCompleteCount);
    }

    [Fact]
    public void AddingLoadingChildOrMissingDependencyThrowsWithoutCorruptingParent()
    {
        using var parent = new Container();
        parent.Load(DependencyCollection.EMPTY);

        var missingConsumer = new DeepConsumerDrawable();
        Assert.Throws<DependencyNotFoundException>(() => parent.Add(missingConsumer));

        Assert.Empty(parent.Children);
        Assert.Null(missingConsumer.Parent);
        Assert.Equal(LoadState.NotLoaded, missingConsumer.LoadState);

        using var reentrantHost = new ReentrantLoadingAttachContainer();
        Assert.Throws<InvalidOperationException>(() => reentrantHost.Load(DependencyCollection.EMPTY));
        Assert.Equal(LoadState.NotLoaded, reentrantHost.LoadState);
    }

    [Fact]
    public void LoadingStateGatesUpdateSubTreeAndGenerateDrawNodeSubtreeWhilePreservingNotLoadedSubtrees()
    {
        // Standalone NotLoaded subtree remains functional without loading.
        using var standaloneParent = new Container { Size = new Vector2(100f, 100f) };
        var standaloneBox = new Box { Size = new Vector2(50f, 50f) };
        standaloneParent.Add(standaloneBox);

        Assert.True(standaloneParent.UpdateSubTree());
        var standaloneNode = Assert.IsType<CompositeDrawNode>(standaloneParent.GenerateDrawNodeSubtree(0));
        Assert.Single(standaloneNode.Children);

        // While LoadState == Loading, UpdateSubTree returns false and GenerateDrawNodeSubtree returns null.
        using var parent = new Container { Size = new Vector2(100f, 100f) };
        var loadingGateProbe = new LoadingGateProbeDrawable(parent);
        parent.Add(loadingGateProbe);

        parent.Load(DependencyCollection.EMPTY);

        Assert.False(loadingGateProbe.UpdateResultWhileLoading);
        Assert.Null(loadingGateProbe.DrawNodeWhileLoading);
        Assert.Equal(0, loadingGateProbe.UpdateCountDuringLoad);
        Assert.False(loadingGateProbe.ParentUpdateResultWhileLoading);
        Assert.Null(loadingGateProbe.ParentDrawNodeWhileParentLoading);

        Assert.True(parent.UpdateSubTree());
        Assert.Equal(1, loadingGateProbe.UpdateCount);
        var loadedParentNode = Assert.IsType<CompositeDrawNode>(parent.GenerateDrawNodeSubtree(0));
        Assert.Single(loadedParentNode.Children);

        // When a non-loading parent contains a child that is currently Loading, the parent omits the Loading child.
        using var activeParent = new Container { Size = new Vector2(100f, 100f) };
        var childProbe = new LoadingGateProbeDrawable(activeParent);
        activeParent.Add(childProbe);

        childProbe.Load(DependencyCollection.EMPTY);

        Assert.True(childProbe.ParentUpdateResultWhileLoading);
        Assert.Equal(0, childProbe.UpdateCountDuringLoad);
        Assert.Equal(0, childProbe.ParentNodeChildrenCountWhileChildLoading);

        Assert.True(activeParent.UpdateSubTree());
        Assert.Equal(1, childProbe.UpdateCount);
        var activeNode = Assert.IsType<CompositeDrawNode>(activeParent.GenerateDrawNodeSubtree(0));
        Assert.Single(activeNode.Children);
    }

    [Fact]
    public void HeadlessGameHostExecutesLoadedSubtreeHooksOnUpdateThread()
    {
        using var host = new HeadlessGameHost(new HostOptions
        {
            EnableConsoleLogging = false,
            EnableFileLogging = false,
        });

        using var game = new LifecycleHostGame();
        host.RunFrames(game, 3);

        Assert.NotNull(game.SubtreeContainer);
        Assert.Equal(LoadState.Loaded, game.SubtreeContainer.LoadState);
        Assert.Equal(LoadState.Loaded, game.SubtreeContainer.ProbeChild.LoadState);
        Assert.Equal(1, game.SubtreeContainer.ProbeChild.LoadCompleteCount);
        Assert.Equal(1, game.SubtreeContainer.ProbeChild.ScheduledExecutionCount);
        Assert.NotEqual(0, game.SubtreeContainer.ProbeChild.LoadCompleteThreadId);
        Assert.Equal(game.SubtreeContainer.ProbeChild.LoadCompleteThreadId, game.SubtreeContainer.ProbeChild.ScheduledThreadId);
    }

    public interface IServiceA
    {
        string Name { get; }
    }

    public interface IServiceB
    {
        int Code { get; }
    }

    public interface IMissingService
    {
    }

    public sealed class ServiceA(string name) : IServiceA
    {
        public string Name { get; } = name;
    }

    public sealed class ServiceB(int code) : IServiceB
    {
        public int Code { get; } = code;
    }

    public sealed class LeftToken(string value)
    {
        public string Value { get; } = value;
    }

    private sealed partial class LifecycleProbeContainer(string name, List<string> log) : Container
    {
        public LoadState LoadStateObservedInLoad { get; private set; }

        protected override void Load()
        {
            LoadStateObservedInLoad = LoadState;
            log.Add($"{name}:Load");
        }

        protected override void LoadComplete() => log.Add($"{name}:LoadComplete");

        protected override void Update() => log.Add($"{name}:Update");
    }

    private sealed partial class LifecycleProbeDrawable(string name, List<string> log) : Drawable
    {
        [Resolved]
        private IServiceA resolvedA = null!;

        public LoadState LoadStateObservedInLoad { get; private set; }

        public string ResolvedNameInLoad { get; private set; } = string.Empty;

        protected override void Load()
        {
            LoadStateObservedInLoad = LoadState;
            ResolvedNameInLoad = resolvedA.Name;
            log.Add($"{name}:Load");
        }

        protected override void LoadComplete() => log.Add($"{name}:LoadComplete");

        protected override void Update() => log.Add($"{name}:Update");
    }

    private sealed partial class ShadowingLeftContainer : Container
    {
        [Cached(typeof(IServiceA))]
        private readonly ServiceA shadowedA = new("left-A");

        [Cached]
        private readonly LeftToken token = new("left-token");
    }

    private sealed partial class DeepConsumerDrawable : Drawable
    {
        [Resolved]
        public IServiceA ResolvedA { get; private set; } = null!;

        [Resolved]
        public IServiceB ResolvedB { get; private set; } = null!;

        [Resolved]
        public LeftToken ResolvedToken { get; private set; } = null!;

        [Resolved]
        public IMissingService? MissingOptional { get; private set; }
    }

    private sealed partial class SiblingConsumerDrawable : Drawable
    {
        [Resolved]
        public IServiceA ResolvedA { get; private set; } = null!;

        [Resolved]
        public LeftToken? OptionalLeftToken { get; private set; }
    }

    private sealed partial class DynamicAddingContainer : Container
    {
        [Cached(typeof(IServiceA))]
        private readonly ServiceA serviceA = new("dynamic-parent");

        public DynamicConsumerDrawable? ChildAddedInLoad { get; private set; }

        protected override void Load()
        {
            ChildAddedInLoad = new DynamicConsumerDrawable();
            Add(ChildAddedInLoad);
        }
    }

    private sealed partial class DynamicConsumerDrawable : Drawable
    {
        [Resolved]
        public IServiceA ResolvedA { get; private set; } = null!;

        public int LoadCount { get; private set; }

        public int LoadCompleteCount { get; private set; }

        protected override void Load() => LoadCount++;

        protected override void LoadComplete() => LoadCompleteCount++;
    }

    private sealed class ReentrantLoadingAttachContainer : Container
    {
        protected override void Load()
        {
            var probeTarget = new Container();
            Add(probeTarget);
            probeTarget.Add(this);
        }
    }

    private sealed class LoadingGateProbeDrawable(Container parentContainer) : Box
    {
        public bool UpdateResultWhileLoading { get; private set; } = true;

        public DrawNode? DrawNodeWhileLoading { get; private set; }

        public int UpdateCountDuringLoad { get; private set; }

        public bool ParentUpdateResultWhileLoading { get; private set; }

        public DrawNode? ParentDrawNodeWhileParentLoading { get; private set; }

        public int? ParentNodeChildrenCountWhileChildLoading { get; private set; }

        public int UpdateCount { get; private set; }

        protected override void Load()
        {
            Size = new Vector2(20f, 20f);
            UpdateResultWhileLoading = UpdateSubTree();
            DrawNodeWhileLoading = GenerateDrawNodeSubtree(0);
            ParentUpdateResultWhileLoading = parentContainer.UpdateSubTree();
            UpdateCountDuringLoad = UpdateCount;

            var parentNode = parentContainer.GenerateDrawNodeSubtree(0);
            ParentDrawNodeWhileParentLoading = parentNode;
            ParentNodeChildrenCountWhileChildLoading = (parentNode as CompositeDrawNode)?.Children.Count;
        }

        protected override void Update() => UpdateCount++;
    }

    private sealed partial class HostSubtreeContainer : Container
    {
        [Cached(typeof(IServiceA))]
        private readonly ServiceA serviceA = new("host-service");

        public HostSubtreeChild ProbeChild { get; } = new();

        protected override void Load() => Add(ProbeChild);
    }

    private sealed partial class HostSubtreeChild : Drawable
    {
        [Resolved]
        public IServiceA ResolvedService { get; private set; } = null!;

        public int LoadCompleteCount { get; private set; }

        public int LoadCompleteThreadId { get; private set; }

        public int ScheduledExecutionCount { get; private set; }

        public int ScheduledThreadId { get; private set; }

        protected override void LoadComplete()
        {
            LoadCompleteCount++;
            LoadCompleteThreadId = Environment.CurrentManagedThreadId;

            Schedule(() =>
            {
                ScheduledExecutionCount++;
                ScheduledThreadId = Environment.CurrentManagedThreadId;
            });
        }
    }

    private sealed class LifecycleHostGame : Game
    {
        public HostSubtreeContainer? SubtreeContainer { get; private set; }

        protected override void Load()
        {
            SubtreeContainer = new HostSubtreeContainer();
            SubtreeContainer.Load(DependencyCollection.EMPTY);
            Root.Add(SubtreeContainer);
        }
    }
}
