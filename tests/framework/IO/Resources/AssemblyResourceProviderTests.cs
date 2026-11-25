using System.Text;
using kajarlabs.Framework.IO.Resources;

namespace kajarlabs.Framework.Tests.IO.Resources;

public sealed class AssemblyResourceProviderTests
{
    private const string fixtures_prefix = "kajarlabs.Framework.Tests.IO.Resources.Fixtures";

    [Fact]
    public async Task ResolvesSlashSeparatedAndMultiDotEmbeddedResourcesWithDefaultAndExplicitPrefix()
    {
        var testAssembly = typeof(AssemblyResourceProviderTests).Assembly;

        using var defaultPrefixed = new AssemblyResourceProvider(testAssembly);
        Assert.Equal(testAssembly.GetName().Name, defaultPrefixed.Prefix);
        Assert.True(defaultPrefixed.Exists("IO/Resources/Fixtures/UI/button.hover.txt"));
        Assert.Equal(
            "embedded-button-hover",
            Encoding.UTF8.GetString(defaultPrefixed.GetBytes("IO/Resources/Fixtures/UI/button.hover.txt")!));

        using var scopedPrefixed = new AssemblyResourceProvider(testAssembly, "kajarlabs.Framework.Tests/IO/Resources/Fixtures/");
        Assert.Equal("kajarlabs.Framework.Tests/IO/Resources/Fixtures", scopedPrefixed.Prefix);
        Assert.True(scopedPrefixed.Exists("UI/button.hover.txt"));
        Assert.True(scopedPrefixed.Exists("UI\\button.hover.txt"));
        Assert.True(scopedPrefixed.Exists("UI/sub/../button.hover.txt"));
        Assert.Equal(
            "embedded-button-hover",
            Encoding.UTF8.GetString((await scopedPrefixed.GetBytesAsync("UI/button.hover.txt", TestContext.Current.CancellationToken))!));

        using var stream = scopedPrefixed.GetStream("UI/button.hover.txt");
        Assert.NotNull(stream);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        Assert.Equal("embedded-button-hover", await reader.ReadToEndAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public void SupportsCustomSlashLogicalNamesAndEmptyPrefix()
    {
        var testAssembly = typeof(AssemblyResourceProviderTests).Assembly;

        using var unprefixed = new AssemblyResourceProvider(testAssembly, prefix: string.Empty);
        Assert.Equal(string.Empty, unprefixed.Prefix);
        Assert.True(unprefixed.Exists("CustomLogical/nested/slash-asset.txt"));
        Assert.Equal("slash-logical-asset", Encoding.UTF8.GetString(unprefixed.GetBytes("CustomLogical/nested/slash-asset.txt")!));

        using var logicalScoped = new AssemblyResourceProvider(testAssembly, prefix: "CustomLogical");
        Assert.True(logicalScoped.Exists("nested/slash-asset.txt"));
        Assert.Equal("slash-logical-asset", Encoding.UTF8.GetString(logicalScoped.GetBytes("nested/slash-asset.txt")!));
        Assert.Equal(["nested/slash-asset.txt"], logicalScoped.GetAvailableResources());
    }

    [Fact]
    public void ReadsBuiltInFrameworkEmbeddedShaders()
    {
        using var frameworkResources = new AssemblyResourceProvider(typeof(Game).Assembly, "kajarlabs.Framework.Resources");

        Assert.True(frameworkResources.Exists("Shaders/Compiled/Sprite2D.vert.spv"));
        Assert.True(frameworkResources.Exists("Shaders/Compiled/Sprite2D.frag.spv"));

        var vertBytes = frameworkResources.GetBytes("Shaders/Compiled/Sprite2D.vert.spv");
        Assert.NotNull(vertBytes);
        Assert.NotEmpty(vertBytes);
    }

    [Fact]
    public void GetAvailableResourcesSatisfiesRoundTripInvariant()
    {
        using var provider = new AssemblyResourceProvider(typeof(AssemblyResourceProviderTests).Assembly, fixtures_prefix);
        var available = provider.GetAvailableResources().ToList();

        Assert.NotEmpty(available);

        foreach (var resourceName in available)
        {
            Assert.True(provider.Exists(resourceName), $"Expected listed resource '{resourceName}' to exist.");
            using var stream = provider.GetStream(resourceName);
            Assert.NotNull(stream);
        }
    }

    [Fact]
    public void DefaultLookupIsCaseInsensitiveAndPrefersExactCaseOnCollision()
    {
        using var insensitive = new AssemblyResourceProvider(typeof(AssemblyResourceProviderTests).Assembly, fixtures_prefix);

        // Case-insensitive lookup for non-colliding entry.
        Assert.True(insensitive.Exists("ui/BUTTON.HOVER.TXT"));
        Assert.Equal("embedded-button-hover", Encoding.UTF8.GetString(insensitive.GetBytes("ui/BUTTON.HOVER.TXT")!));

        // Exact-case matches win when both Collision.Hero.txt and Collision.hero.txt exist in the manifest.
        Assert.Equal("UPPER-HERO", Encoding.UTF8.GetString(insensitive.GetBytes("Collision/Hero.txt")!));
        Assert.Equal("lower-hero", Encoding.UTF8.GetString(insensitive.GetBytes("Collision/hero.txt")!));

        // Different casing still resolves deterministically via fallback comparer.
        Assert.True(insensitive.Exists("collision/HERO.TXT"));

        // Explicit Ordinal comparer disables case-insensitive fallback.
        using var ordinal = new AssemblyResourceProvider(
            typeof(AssemblyResourceProviderTests).Assembly,
            fixtures_prefix,
            StringComparer.Ordinal);

        Assert.True(ordinal.Exists("UI/button.hover.txt"));
        Assert.False(ordinal.Exists("ui/BUTTON.HOVER.TXT"));
        Assert.Equal("UPPER-HERO", Encoding.UTF8.GetString(ordinal.GetBytes("Collision/Hero.txt")!));
        Assert.Equal("lower-hero", Encoding.UTF8.GetString(ordinal.GetBytes("Collision/hero.txt")!));
        Assert.False(ordinal.Exists("collision/HERO.TXT"));
    }

    [Fact]
    public void PrefixBoundaryDoesNotMatchPartialSegmentNames()
    {
        using var provider = new AssemblyResourceProvider(typeof(AssemblyResourceProviderTests).Assembly, "CustomLog");

        Assert.Empty(provider.GetAvailableResources());
        Assert.False(provider.Exists("ical/nested/slash-asset.txt"));
    }

    [Fact]
    public void ValidatesConstructorArgumentsAndRejectsInvalidPaths()
    {
        var assembly = typeof(AssemblyResourceProviderTests).Assembly;

        Assert.Throws<ArgumentNullException>(() => new AssemblyResourceProvider(null!));
        Assert.Throws<ArgumentException>(() => new AssemblyResourceProvider(assembly, "   "));
        Assert.Throws<ArgumentException>(() => new AssemblyResourceProvider(assembly, "/rooted"));
        Assert.Throws<ArgumentException>(() => new AssemblyResourceProvider(assembly, "\\rooted"));
        Assert.Throws<ArgumentException>(() => new AssemblyResourceProvider(assembly, "C:prefix"));
        Assert.Throws<ArgumentException>(() => new AssemblyResourceProvider(assembly, "..."));

        using var provider = new AssemblyResourceProvider(assembly, fixtures_prefix);

        Assert.Throws<ArgumentNullException>(() => provider.Exists(null!));
        Assert.Throws<ArgumentNullException>(() => provider.GetStream(null!));
        Assert.Throws<ArgumentNullException>(() => provider.GetBytes(null!));
        Assert.Throws<ArgumentNullException>(() => { _ = provider.GetBytesAsync(null!, TestContext.Current.CancellationToken); });

        foreach (var invalid in new[] { "..", "../x", "UI/../../x", "/abs", "\\abs", "C:x", "a/b:stream" })
        {
            Assert.Throws<ArgumentException>(() => provider.Exists(invalid));
            Assert.Throws<ArgumentException>(() => provider.GetStream(invalid));
            Assert.Throws<ArgumentException>(() => provider.GetBytes(invalid));
            Assert.Throws<ArgumentException>(() => { _ = provider.GetBytesAsync(invalid, TestContext.Current.CancellationToken); });
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(".")]
    [InlineData("a/..")]
    public async Task EmptyOrRootCollapsingNamesReturnMissingWithoutThrowing(string name)
    {
        using var provider = new AssemblyResourceProvider(typeof(AssemblyResourceProviderTests).Assembly, fixtures_prefix);

        Assert.False(provider.Exists(name));
        Assert.Null(provider.GetStream(name));
        Assert.Null(provider.GetBytes(name));
        Assert.Null(await provider.GetBytesAsync(name, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetBytesAsyncHonorsPreCancelledTokenAndSupportsConcurrentReads()
    {
        using var provider = new AssemblyResourceProvider(typeof(AssemblyResourceProviderTests).Assembly, fixtures_prefix);

        using (var cts = new CancellationTokenSource())
        {
            cts.Cancel();
            var canceledTask = provider.GetBytesAsync("UI/button.hover.txt", cts.Token);
            Assert.True(canceledTask.IsCanceled);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await canceledTask);
        }

        var ct = TestContext.Current.CancellationToken;
        var tasks = new List<Task>();

        for (var worker = 0; worker < 6; worker++)
        {
            tasks.Add(Task.Run(async () =>
            {
                for (var i = 0; i < 60; i++)
                {
                    var bytes = await provider.GetBytesAsync("UI/button.hover.txt", ct);
                    Assert.Equal("embedded-button-hover", Encoding.UTF8.GetString(bytes!));
                    Assert.True(provider.Exists("Collision/Hero.txt"));
                }
            }, ct));
        }

        await Task.WhenAll(tasks);
    }

    [Fact]
    public void DisposeIsIdempotentAndBlocksSubsequentAccess()
    {
        var provider = new AssemblyResourceProvider(typeof(AssemblyResourceProviderTests).Assembly, fixtures_prefix);

        provider.Dispose();
        provider.Dispose();

        Assert.Throws<ObjectDisposedException>(() => provider.Exists("UI/button.hover.txt"));
        Assert.Throws<ObjectDisposedException>(() => provider.GetStream("UI/button.hover.txt"));
        Assert.Throws<ObjectDisposedException>(() => provider.GetBytes("UI/button.hover.txt"));
        Assert.Throws<ObjectDisposedException>(() => { _ = provider.GetBytesAsync("UI/button.hover.txt", TestContext.Current.CancellationToken); });
        Assert.Throws<ObjectDisposedException>(() => provider.GetAvailableResources());
    }
}
