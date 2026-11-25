using kajarlabs.Framework.IO.Resources;

namespace kajarlabs.Framework.Tests.IO.Resources;

public sealed class NamespacedResourceProviderTests
{
    [Fact]
    public void ConstructorValidatesArgumentsAndNormalizesPrefix()
    {
        using var inner = new MemoryResourceProvider();

        Assert.Throws<ArgumentNullException>(() => new NamespacedResourceProvider(null!, "Textures"));
        Assert.Throws<ArgumentNullException>(() => new NamespacedResourceProvider(inner, null!));
        Assert.Throws<ArgumentException>(() => new NamespacedResourceProvider(inner, ""));
        Assert.Throws<ArgumentException>(() => new NamespacedResourceProvider(inner, "   "));
        Assert.Throws<ArgumentException>(() => new NamespacedResourceProvider(inner, "."));
        Assert.Throws<ArgumentException>(() => new NamespacedResourceProvider(inner, "a/.."));
        Assert.Throws<ArgumentException>(() => new NamespacedResourceProvider(inner, "/Textures"));
        Assert.Throws<ArgumentException>(() => new NamespacedResourceProvider(inner, "../Textures"));
        Assert.Throws<ArgumentException>(() => new NamespacedResourceProvider(inner, "C:Textures"));

        using var scoped = new NamespacedResourceProvider(inner, "Textures\\UI//sub/../", leaveOpen: true);
        Assert.Equal("Textures/UI", scoped.Prefix);
    }

    [Fact]
    public async Task ScopesLookupsAndAvailableResourcesUnderPrefix()
    {
        using var inner = new MemoryResourceProvider();
        inner.Add("Textures/hero.png", [1, 2]);
        inner.Add("Textures/UI/button.png", [3, 4]);
        inner.Add("TexturesExtra/decoy.png", [5]);
        inner.Add("Audio/bgm.ogg", [6]);

        using var scoped = new NamespacedResourceProvider(inner, "Textures", leaveOpen: true);

        Assert.True(scoped.Exists("hero.png"));
        Assert.True(scoped.Exists("UI\\button.png"));
        Assert.True(scoped.Exists("UI/../hero.png"));
        Assert.False(scoped.Exists("decoy.png"));
        Assert.False(scoped.Exists("bgm.ogg"));

        Assert.Equal([1, 2], scoped.GetBytes("hero.png"));
        Assert.Equal([3, 4], await scoped.GetBytesAsync("UI/button.png", TestContext.Current.CancellationToken));

        using (var stream = scoped.GetStream("hero.png"))
        {
            Assert.NotNull(stream);
            var buf = new byte[2];
            stream.ReadExactly(buf);
            Assert.Equal([1, 2], buf);
        }

        var available = scoped.GetAvailableResources().Order(StringComparer.Ordinal).ToList();
        Assert.Equal(["UI/button.png", "hero.png"], available);
    }

    [Theory]
    [InlineData("..")]
    [InlineData("../Audio/bgm.ogg")]
    [InlineData("UI/../../Audio/bgm.ogg")]
    [InlineData("..\\Audio\\bgm.ogg")]
    [InlineData("/Textures/hero.png")]
    [InlineData("\\Textures\\hero.png")]
    [InlineData("C:hero.png")]
    [InlineData("hero:stream")]
    public void EscapingAndInvalidPathsThrowEvenWhenTargetExistsInInner(string path)
    {
        using var inner = new MemoryResourceProvider();
        inner.Add("Audio/bgm.ogg", [1]);
        inner.Add("Textures/hero.png", [2]);

        using var scoped = new NamespacedResourceProvider(inner, "Textures", leaveOpen: true);

        Assert.Throws<ArgumentException>(() => scoped.Exists(path));
        Assert.Throws<ArgumentException>(() => scoped.GetStream(path));
        Assert.Throws<ArgumentException>(() => scoped.GetBytes(path));
        Assert.Throws<ArgumentException>(() => { _ = scoped.GetBytesAsync(path, TestContext.Current.CancellationToken); });
    }

    [Fact]
    public void NullPathThrowsArgumentNullExceptionForEveryLookupMember()
    {
        using var inner = new MemoryResourceProvider();
        using var scoped = new NamespacedResourceProvider(inner, "Textures", leaveOpen: true);

        Assert.Throws<ArgumentNullException>(() => scoped.Exists(null!));
        Assert.Throws<ArgumentNullException>(() => scoped.GetStream(null!));
        Assert.Throws<ArgumentNullException>(() => scoped.GetBytes(null!));
        Assert.Throws<ArgumentNullException>(() => { _ = scoped.GetBytesAsync(null!, TestContext.Current.CancellationToken); });
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(".")]
    [InlineData("a/..")]
    public async Task EmptyOrRootCollapsingLookupsReturnMissing(string name)
    {
        using var inner = new MemoryResourceProvider();
        inner.Add("Textures/hero.png", [1]);

        using var scoped = new NamespacedResourceProvider(inner, "Textures", leaveOpen: true);

        Assert.False(scoped.Exists(name));
        Assert.Null(scoped.GetStream(name));
        Assert.Null(scoped.GetBytes(name));
        Assert.Null(await scoped.GetBytesAsync(name, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void NestedNamespacesAndCompositeWithExtensionsComposeCleanly()
    {
        using var root = new MemoryResourceProvider();
        root.Add("Assets/Textures/UI/icon.png", [42]);
        root.Add("Assets/Audio/click.wav", [7]);

        using var assets = new NamespacedResourceProvider(root, "Assets", leaveOpen: true);
        using var uiTextures = new NamespacedResourceProvider(assets, "Textures/UI", leaveOpen: true);

        Assert.Equal([42], uiTextures.GetBytes("icon.png"));
        Assert.Equal(["icon.png"], uiTextures.GetAvailableResources());
        Assert.Throws<ArgumentException>(() => uiTextures.Exists("../../Audio/click.wav"));

        using var composite = new CompositeResourceProvider();
        composite.AddProvider(uiTextures, leaveOpen: true);
        composite.AddExtension(".png");

        Assert.Equal([42], composite.GetBytes("icon"));
    }

    [Fact]
    public void GetAvailableResourcesNormalizesBackslashesAndIgnoresExactPrefixMatch()
    {
        using var inner = new BackslashListingProvider(["Textures", "Textures\\a.png", "Textures\\sub\\b.png", "TexturesOther\\c.png", ""]);
        using var scoped = new NamespacedResourceProvider(inner, "Textures", leaveOpen: true);

        Assert.Equal(["a.png", "sub/b.png"], scoped.GetAvailableResources());
    }

    [Fact]
    public async Task GetBytesAsyncHonorsCancellationToken()
    {
        using var inner = new MemoryResourceProvider();
        inner.Add("Textures/hero.png", [1, 2]);

        using var scoped = new NamespacedResourceProvider(inner, "Textures", leaveOpen: true);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var task = scoped.GetBytesAsync("hero.png", cts.Token);
        Assert.True(task.IsCanceled);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await task);
    }

    [Fact]
    public void DisposeRespectsLeaveOpenIsIdempotentAndBlocksSubsequentAccess()
    {
        var sharedInner = new MemoryResourceProvider();
        sharedInner.Add("Textures/hero.png", [1]);

        var leaveOpenScoped = new NamespacedResourceProvider(sharedInner, "Textures", leaveOpen: true);
        leaveOpenScoped.Dispose();
        leaveOpenScoped.Dispose();

        Assert.True(sharedInner.Exists("Textures/hero.png"));
        Assert.Throws<ObjectDisposedException>(() => leaveOpenScoped.Exists("hero.png"));
        Assert.Throws<ObjectDisposedException>(() => leaveOpenScoped.GetStream("hero.png"));
        Assert.Throws<ObjectDisposedException>(() => leaveOpenScoped.GetBytes("hero.png"));
        Assert.Throws<ObjectDisposedException>(() => { _ = leaveOpenScoped.GetBytesAsync("hero.png", TestContext.Current.CancellationToken); });
        Assert.Throws<ObjectDisposedException>(() => leaveOpenScoped.GetAvailableResources());

        var owningScoped = new NamespacedResourceProvider(sharedInner, "Textures", leaveOpen: false);
        owningScoped.Dispose();

        Assert.Throws<ObjectDisposedException>(() => sharedInner.Exists("Textures/hero.png"));
    }

    private sealed class BackslashListingProvider : IResourceProvider
    {
        private readonly string[] resources;

        public BackslashListingProvider(string[] resources)
        {
            this.resources = resources;
        }

        public Stream? GetStream(string name) => null;

        public byte[]? GetBytes(string name) => null;

        public Task<byte[]?> GetBytesAsync(string name, CancellationToken cancellationToken = default)
            => Task.FromResult<byte[]?>(null);

        public bool Exists(string name) => false;

        public IEnumerable<string> GetAvailableResources() => resources;

        public void Dispose()
        {
        }
    }
}
