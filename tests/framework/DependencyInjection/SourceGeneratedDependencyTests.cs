using kajarlabs.Framework.DependencyInjection;
using kajarlabs.Framework.Scene;

namespace kajarlabs.Framework.Tests.DependencyInjection;

public sealed partial class SourceGeneratedDependencyTests
{
    [Fact]
    public void GeneratedHooksChainAcrossGameAndDrawableInheritanceHierarchy()
    {
        using var game = new GeneratedProbeGame();
        var rootDependencies = new DependencyCollection();

        game.InvokeCacheDependencies(rootDependencies);
        game.InvokeResolveDependencies(rootDependencies);

        Assert.Same(game.PrimaryService, game.ResolvedOwnService);

        using var child = new DerivedGeneratedDrawable();
        var childDependencies = new DependencyCollection(rootDependencies);

        child.InvokeCacheDependencies(childDependencies);
        child.InvokeResolveDependencies(childDependencies);

        Assert.Same(game.PrimaryService, child.ReadResolvedPrimary());
        Assert.Same(game.CombinedService, child.ResolvedSecondary);
        Assert.Same(child, childDependencies.Get<DerivedGeneratedDrawable>());
        Assert.Same(child.BaseToken, child.ReadResolvedBaseToken());
        Assert.Equal(42, child.ResolvedOptionalCount);
        Assert.Null(child.ResolvedMissingService);
    }

    [Fact]
    public void GeneratedResolveThrowsDependencyNotFoundExceptionWhenRequiredOrCanBeNullFalseIsMissing()
    {
        using var drawable = new DerivedGeneratedDrawable();
        var emptyParent = new DependencyCollection();

        drawable.InvokeCacheDependencies(emptyParent);

        var ex = Assert.Throws<DependencyNotFoundException>(() => drawable.InvokeResolveDependencies(emptyParent));
        Assert.Equal(typeof(IPrimaryTestService), ex.DependencyType);
    }

    [Fact]
    public void GeneratedNullableCachedMemberThrowsArgumentNullExceptionWhenNull()
    {
        using var drawable = new NullableCachedDrawable();
        var dependencies = new DependencyCollection();

        Assert.Throws<ArgumentNullException>(() => drawable.InvokeCacheDependencies(dependencies));
    }

    [Fact]
    public void NestedGenericPartialDrawableCachesAndResolvesWithoutReflection()
    {
        var root = new DependencyCollection();
        var primary = new PrimaryTestService("nested");
        root.Cache<IPrimaryTestService>(primary);

        using var genericDrawable = new OuterGenericContainer<string>.InnerGenericDrawable<int>(99);
        var childScope = new DependencyCollection(root);

        genericDrawable.InvokeCacheDependencies(childScope);
        genericDrawable.InvokeResolveDependencies(childScope);

        Assert.Equal(99, childScope.Get<int>());
        Assert.Same(primary, genericDrawable.ReadResolvedPrimary());
        Assert.Null(genericDrawable.ResolvedOptionalOuter);
    }

    public interface IPrimaryTestService
    {
        string Name { get; }
    }

    public interface ISecondaryTestService
    {
        int Code { get; }
    }

    public interface IMissingOptionalService
    {
    }

    public sealed class PrimaryTestService : IPrimaryTestService
    {
        public PrimaryTestService(string name)
        {
            Name = name;
        }

        public string Name { get; }
    }

    public sealed class CombinedService : IPrimaryTestService, ISecondaryTestService
    {
        public string Name => "combined";

        public int Code => 77;
    }

    public sealed class LocalToken
    {
    }

    private sealed partial class GeneratedProbeGame : Game
    {
        [Cached(typeof(IPrimaryTestService))]
        public PrimaryTestService PrimaryService { get; } = new("game-primary");

        [Cached(typeof(ISecondaryTestService))]
        public CombinedService CombinedService { get; } = new();

        [Resolved]
        public IPrimaryTestService ResolvedOwnService { get; private set; } = null!;
    }

    private partial class BaseGeneratedDrawable : Container
    {
        [Cached]
        public readonly LocalToken BaseToken = new();

        [Cached]
        private int? cachedOptionalCount = 42;

        [Resolved]
        private IPrimaryTestService resolvedPrimary = null!;

        [Resolved]
        public IMissingOptionalService? ResolvedMissingService { get; private set; }

        public IPrimaryTestService ReadResolvedPrimary() => resolvedPrimary;
    }

    [Cached]
    private sealed partial class DerivedGeneratedDrawable : BaseGeneratedDrawable
    {
        [Resolved]
        private LocalToken resolvedBaseToken = null!;

        [Resolved]
        public int? ResolvedOptionalCount { get; private set; }

        [Resolved(CanBeNull = false)]
        public ISecondaryTestService? ResolvedSecondary { get; private set; }

        public LocalToken ReadResolvedBaseToken() => resolvedBaseToken;
    }

    private sealed partial class NullableCachedDrawable : Drawable
    {
        [Cached]
        private LocalToken? uninitializedToken = null;
    }

    public sealed partial class OuterGenericContainer<TOuter>
    {
        public sealed partial class InnerGenericDrawable<TInner> : Drawable
        {
            [Cached]
            private TInner cachedValue;

            [Resolved]
            private IPrimaryTestService resolvedPrimary = null!;

            [Resolved]
            public TOuter? ResolvedOptionalOuter { get; private set; }

            public InnerGenericDrawable(TInner cachedValue)
            {
                this.cachedValue = cachedValue;
            }

            public IPrimaryTestService ReadResolvedPrimary() => resolvedPrimary;
        }
    }
}
