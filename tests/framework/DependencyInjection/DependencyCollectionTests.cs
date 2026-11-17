using kajarlabs.Framework.DependencyInjection;
using kajarlabs.Framework.Scene;

namespace kajarlabs.Framework.Tests.DependencyInjection;

public sealed class DependencyCollectionTests
{
    [Fact]
    public void CacheAndLookupSupportTypedInterfaceAndStructKeysWithoutReflection()
    {
        var collection = new DependencyCollection();
        var service = new ProbeService("primary");
        var metrics = new ProbeMetrics(42, 0.75f);

        collection.Cache<IProbeService>(service);
        collection.Cache(metrics);

        Assert.Equal(2, collection.Count);

        // Caching under IProbeService must not implicitly register ProbeService concrete type.
        Assert.False(collection.TryGet<ProbeService>(out var concrete));
        Assert.Null(concrete);

        Assert.True(collection.TryGet<IProbeService>(out var resolvedService));
        Assert.Same(service, resolvedService);
        Assert.Same(service, collection.Get<IProbeService>());
        Assert.Same(service, collection.Get(typeof(IProbeService)));

        Assert.True(collection.TryGet<ProbeMetrics>(out var resolvedMetrics));
        Assert.Equal(metrics, resolvedMetrics);
        Assert.Equal(metrics, collection.Get<ProbeMetrics>());
    }

    [Fact]
    public void CacheAsValidatesNullAndAssignabilityAndRejectsLocalDuplicates()
    {
        var collection = new DependencyCollection();
        var service = new ProbeService("one");

        Assert.Throws<ArgumentNullException>(() => collection.Cache<IProbeService>(null!));
        Assert.Throws<ArgumentNullException>(() => collection.CacheAs(null!, service));
        Assert.Throws<ArgumentNullException>(() => collection.CacheAs(typeof(IProbeService), null!));

        Assert.Throws<ArgumentException>(() => collection.CacheAs(typeof(IProbeService), "not-a-service"));

        collection.CacheAs(typeof(IProbeService), service);
        Assert.Same(service, collection.Get<IProbeService>());

        // Duplicate registration on the same collection throws instead of silently overwriting.
        Assert.Throws<InvalidOperationException>(() => collection.Cache<IProbeService>(new ProbeService("two")));
        Assert.Throws<InvalidOperationException>(() => collection.CacheAs(typeof(IProbeService), new ProbeService("three")));
    }

    [Fact]
    public void HierarchicalFallbackAndShadowingPreserveParentState()
    {
        var root = new DependencyCollection();
        var rootService = new ProbeService("root");
        var sharedToken = new SharedConfig(1);
        root.Cache<IProbeService>(rootService);
        root.Cache(sharedToken);

        var middle = new DependencyCollection(root);
        var middleService = new ProbeService("middle");
        middle.Cache<IProbeService>(middleService);

        var leaf = new DependencyCollection(middle);

        // Leaf resolves shadowed IProbeService from middle and unshadowed SharedConfig from root.
        Assert.Same(middleService, leaf.Get<IProbeService>());
        Assert.Same(sharedToken, leaf.Get<SharedConfig>());
        Assert.Equal(0, leaf.Count);

        // Parent collections remain untouched by child shadowing.
        Assert.Same(rootService, root.Get<IProbeService>());
        Assert.Same(middleService, middle.Get<IProbeService>());
    }

    [Fact]
    public void MissingDependencyThrowsDependencyNotFoundExceptionAndEmptyAlwaysMisses()
    {
        var collection = new DependencyCollection();

        Assert.False(collection.TryGet<IProbeService>(out var missed));
        Assert.Null(missed);
        Assert.False(collection.TryGet(typeof(IProbeService), out var missedObj));
        Assert.Null(missedObj);

        var genericEx = Assert.Throws<DependencyNotFoundException>(() => collection.Get<IProbeService>());
        Assert.Equal(typeof(IProbeService), genericEx.DependencyType);
        Assert.IsAssignableFrom<InvalidOperationException>(genericEx);

        var untypedEx = Assert.Throws<DependencyNotFoundException>(() => collection.Get(typeof(SharedConfig)));
        Assert.Equal(typeof(SharedConfig), untypedEx.DependencyType);

        Assert.False(DependencyCollection.EMPTY.TryGet<IProbeService>(out _));
        Assert.False(DependencyCollection.EMPTY.TryGet(typeof(IProbeService), out _));
        Assert.Throws<DependencyNotFoundException>(() => DependencyCollection.EMPTY.Get<IProbeService>());
        Assert.Throws<DependencyNotFoundException>(() => DependencyCollection.EMPTY.Get(typeof(IProbeService)));
        Assert.Throws<ArgumentNullException>(() => DependencyCollection.EMPTY.TryGet(null!, out _));
        Assert.Throws<ArgumentNullException>(() => DependencyCollection.EMPTY.Get(null!));
    }

    [Fact]
    public async Task ConcurrentReadsAcrossHierarchyAreThreadSafe()
    {
        var root = new DependencyCollection();
        var rootService = new ProbeService("root");
        root.Cache<IProbeService>(rootService);

        var child = new DependencyCollection(root);
        var config = new SharedConfig(99);
        child.Cache(config);

        var tasks = new Task[8];

        for (var i = 0; i < tasks.Length; i++)
        {
            tasks[i] = Task.Run(() =>
            {
                for (var iteration = 0; iteration < 500; iteration++)
                {
                    Assert.Same(rootService, child.Get<IProbeService>());
                    Assert.Same(config, child.Get<SharedConfig>());
                    Assert.False(child.TryGet<ProbeService>(out _));
                }
            }, TestContext.Current.CancellationToken);
        }

        await Task.WhenAll(tasks);
    }

    [Fact]
    public void DrawableAndGameHooksChainCacheAndResolveThroughHierarchy()
    {
        using var game = new ProbeGame();
        var rootDependencies = new DependencyCollection();

        game.InvokeCacheDependencies(rootDependencies);
        game.InvokeResolveDependencies(rootDependencies);

        Assert.NotNull(game.ResolvedOwnConfig);
        Assert.Equal(7, game.ResolvedOwnConfig!.Version);

        using var child = new DerivedProbeDrawable();
        var childDependencies = new DependencyCollection(rootDependencies);

        child.InvokeCacheDependencies(childDependencies);
        child.InvokeResolveDependencies(childDependencies);

        Assert.Same(game.OwnService, child.ResolvedService);
        Assert.NotNull(child.ResolvedSelfMetric);
        Assert.Equal(10, child.ResolvedSelfMetric!.Value.Count);
    }

    private interface IProbeService
    {
        string Name { get; }
    }

    private sealed class ProbeService : IProbeService
    {
        public ProbeService(string name)
        {
            Name = name;
        }

        public string Name { get; }
    }

    private sealed class SharedConfig
    {
        public SharedConfig(int version)
        {
            Version = version;
        }

        public int Version { get; }
    }

    private readonly record struct ProbeMetrics(int Count, float Ratio);

    private sealed class ProbeGame : Game
    {
        public IProbeService OwnService { get; } = new ProbeService("game-service");

        public SharedConfig? ResolvedOwnConfig { get; private set; }

        protected override void CacheDependencies(DependencyCollection dependencies)
        {
            base.CacheDependencies(dependencies);
            dependencies.Cache(OwnService);
            dependencies.Cache(new SharedConfig(7));
        }

        protected override void ResolveDependencies(IReadOnlyDependencyCollection dependencies)
        {
            base.ResolveDependencies(dependencies);
            ResolvedOwnConfig = dependencies.Get<SharedConfig>();
        }
    }

    private class BaseProbeDrawable : Drawable
    {
        public IProbeService? ResolvedService { get; private set; }

        protected override void CacheDependencies(DependencyCollection dependencies)
        {
            base.CacheDependencies(dependencies);
            dependencies.Cache(new ProbeMetrics(10, 0.5f));
        }

        protected override void ResolveDependencies(IReadOnlyDependencyCollection dependencies)
        {
            base.ResolveDependencies(dependencies);
            ResolvedService = dependencies.Get<IProbeService>();
        }
    }

    private sealed class DerivedProbeDrawable : BaseProbeDrawable
    {
        public ProbeMetrics? ResolvedSelfMetric { get; private set; }

        protected override void ResolveDependencies(IReadOnlyDependencyCollection dependencies)
        {
            base.ResolveDependencies(dependencies);
            ResolvedSelfMetric = dependencies.Get<ProbeMetrics>();
        }
    }
}
