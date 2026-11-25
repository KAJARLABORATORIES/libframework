using kajarlabs.Framework.IO.Resources;

namespace kajarlabs.Framework.Tests.IO.Resources;

public sealed class CompositeResourceProviderTests
{
    [Fact]
    public async Task EmptyCompositeReturnsMissingForAllLookups()
    {
        using var composite = new CompositeResourceProvider();

        Assert.False(composite.Exists("missing.png"));
        Assert.Null(composite.GetStream("missing.png"));
        Assert.Null(composite.GetBytes("missing.png"));
        Assert.Null(await composite.GetBytesAsync("missing.png", TestContext.Current.CancellationToken));
        Assert.Empty(composite.GetAvailableResources());
    }

    [Fact]
    public async Task LastAddedProviderWinsPrecedence()
    {
        var baseProvider = new MemoryResourceProvider();
        baseProvider.Add("Textures/hero.png", [1]);
        baseProvider.Add("Textures/base-only.png", [2]);

        var overrideProvider = new MemoryResourceProvider();
        overrideProvider.Add("Textures/hero.png", [9]);

        using var composite = new CompositeResourceProvider(baseProvider, overrideProvider);

        Assert.Equal([9], composite.GetBytes("Textures/hero.png"));
        Assert.Equal([9], await composite.GetBytesAsync("Textures/hero.png", TestContext.Current.CancellationToken));
        Assert.Equal([2], composite.GetBytes("Textures/base-only.png"));

        using (var stream = composite.GetStream("Textures/hero.png"))
        {
            Assert.NotNull(stream);
            Assert.Equal(9, stream.ReadByte());
        }

        // Removing override falls back to base without disposing the removed provider.
        Assert.True(composite.RemoveProvider(overrideProvider));
        Assert.False(composite.RemoveProvider(overrideProvider));
        Assert.Equal([1], composite.GetBytes("Textures/hero.png"));
        Assert.True(overrideProvider.Exists("Textures/hero.png"));

        overrideProvider.Dispose();
    }

    [Fact]
    public void ReAddingExistingProviderPromotesItToHighestPrecedenceAndUpdatesLeaveOpen()
    {
        var first = new MemoryResourceProvider();
        first.Add("shared.bin", [1]);

        var second = new MemoryResourceProvider();
        second.Add("shared.bin", [2]);

        using (var composite = new CompositeResourceProvider())
        {
            composite.AddProvider(first, leaveOpen: false);
            composite.AddProvider(second, leaveOpen: false);
            Assert.Equal([2], composite.GetBytes("shared.bin"));

            composite.AddProvider(first, leaveOpen: true);
            Assert.Equal([1], composite.GetBytes("shared.bin"));
        }

        // First was updated to leaveOpen: true; second remained owned and was disposed.
        Assert.True(first.Exists("shared.bin"));
        Assert.Throws<ObjectDisposedException>(() => second.Exists("shared.bin"));
        first.Dispose();
    }

    [Fact]
    public void AddingSelfThrowsArgumentException()
    {
        using var composite = new CompositeResourceProvider();

        Assert.Throws<ArgumentException>(() => composite.AddProvider(composite));
        Assert.Throws<ArgumentNullException>(() => composite.AddProvider(null!));
        Assert.Throws<ArgumentNullException>(() => composite.RemoveProvider(null!));
    }

    [Fact]
    public async Task ExtensionFallbackIsProviderFirstThenExtensionOrder()
    {
        var embedded = new MemoryResourceProvider();
        embedded.Add("Textures/hero.png", [1, 1]);
        embedded.Add("UI/button.hover.png", [2, 2]);

        var diskOverride = new MemoryResourceProvider();
        diskOverride.Add("Textures/hero.jpg", [9, 9]);

        using var composite = new CompositeResourceProvider(embedded, diskOverride);
        composite.AddExtension("png");
        composite.AddExtension(".PNG"); // Duplicate case-insensitively.
        composite.AddExtension(".jpg");

        // Higher-precedence diskOverride's hero.jpg must override embedded's hero.png.
        Assert.True(composite.Exists("Textures/hero"));
        Assert.Equal([9, 9], composite.GetBytes("Textures/hero"));
        Assert.Equal([9, 9], await composite.GetBytesAsync("Textures/hero", TestContext.Current.CancellationToken));

        using (var stream = composite.GetStream("Textures/hero"))
        {
            Assert.NotNull(stream);
            var buf = new byte[2];
            stream.ReadExactly(buf);
            Assert.Equal([9, 9], buf);
        }

        // Multi-dot resource names still resolve via extension fallback.
        Assert.Equal([2, 2], composite.GetBytes("UI/button.hover"));
    }

    [Fact]
    public void ExtensionProbingOrderAndDuplicateSuffixSkipAreExact()
    {
        var baseSpy = new RecordingResourceProvider();
        var topSpy = new RecordingResourceProvider();

        using var composite = new CompositeResourceProvider(baseSpy, topSpy);
        composite.AddExtension("png");
        composite.AddExtension(".jpg");

        Assert.Null(composite.GetBytes("hero"));
        Assert.Equal(["hero", "hero.png", "hero.jpg"], topSpy.ProbedNames);
        Assert.Equal(["hero", "hero.png", "hero.jpg"], baseSpy.ProbedNames);

        topSpy.ProbedNames.Clear();
        baseSpy.ProbedNames.Clear();

        Assert.Null(composite.GetBytes("missing.png"));
        Assert.Equal(["missing.png", "missing.png.jpg"], topSpy.ProbedNames);
        Assert.Equal(["missing.png", "missing.png.jpg"], baseSpy.ProbedNames);
    }

    [Fact]
    public void GetAvailableResourcesUnionsAndDeduplicatesAcrossProviders()
    {
        var p1 = new MemoryResourceProvider();
        p1.Add("a.png", [1]);
        p1.Add("shared.png", [2]);

        var p2 = new MemoryResourceProvider();
        p2.Add("shared.png", [3]);
        p2.Add("b.png", [4]);

        using var composite = new CompositeResourceProvider(p1, p2);
        var available = composite.GetAvailableResources().ToList();

        Assert.Equal(3, available.Count);
        Assert.Contains("a.png", available);
        Assert.Contains("shared.png", available);
        Assert.Contains("b.png", available);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(".")]
    [InlineData("a/..")]
    public async Task EmptyOrWhitespaceNamesReturnMissingWithoutProbingChildren(string name)
    {
        var spy = new RecordingResourceProvider();
        using var composite = new CompositeResourceProvider(spy);
        composite.AddExtension(".png");

        Assert.False(composite.Exists(name));
        Assert.Null(composite.GetStream(name));
        Assert.Null(composite.GetBytes(name));
        Assert.Null(await composite.GetBytesAsync(name, TestContext.Current.CancellationToken));
        Assert.Empty(spy.ProbedNames);
    }

    [Theory]
    [InlineData("..")]
    [InlineData("../x")]
    [InlineData("a/../../x")]
    [InlineData("/abs")]
    [InlineData("\\abs")]
    [InlineData("C:x")]
    [InlineData("a/b:stream")]
    public void InvalidAndEscapingPathsThrowBeforeProbingChildren(string path)
    {
        var spy = new RecordingResourceProvider();
        using var composite = new CompositeResourceProvider(spy);

        Assert.Throws<ArgumentException>(() => composite.Exists(path));
        Assert.Throws<ArgumentException>(() => composite.GetStream(path));
        Assert.Throws<ArgumentException>(() => composite.GetBytes(path));
        Assert.Throws<ArgumentException>(() => { _ = composite.GetBytesAsync(path, TestContext.Current.CancellationToken); });
        Assert.Empty(spy.ProbedNames);
    }

    [Fact]
    public void NullPathThrowsArgumentNullExceptionBeforeProbingChildren()
    {
        var spy = new RecordingResourceProvider();
        using var composite = new CompositeResourceProvider(spy);

        Assert.Throws<ArgumentNullException>(() => composite.Exists(null!));
        Assert.Throws<ArgumentNullException>(() => composite.GetStream(null!));
        Assert.Throws<ArgumentNullException>(() => composite.GetBytes(null!));
        Assert.Throws<ArgumentNullException>(() => { _ = composite.GetBytesAsync(null!, TestContext.Current.CancellationToken); });
        Assert.Empty(spy.ProbedNames);
    }

    [Fact]
    public async Task GetBytesAsyncHonorsPreCancelledAndMidLookupCancellation()
    {
        var spy = new RecordingResourceProvider();
        using var composite = new CompositeResourceProvider(spy);
        composite.AddExtension(".png");

        using (var preCancelled = new CancellationTokenSource())
        {
            preCancelled.Cancel();
            var task = composite.GetBytesAsync("hero", preCancelled.Token);
            Assert.True(task.IsCanceled);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await task);
            Assert.Empty(spy.ProbedNames);
        }

        using var midCts = new CancellationTokenSource();
        var cancellingProvider = new CancellingAsyncProvider(midCts);
        var lowerSpy = new RecordingResourceProvider();

        using var cancellingComposite = new CompositeResourceProvider(lowerSpy, cancellingProvider);
        cancellingComposite.AddExtension(".png");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await cancellingComposite.GetBytesAsync("hero", midCts.Token));

        Assert.Empty(lowerSpy.ProbedNames);
    }

    [Fact]
    public async Task ConcurrentLookupsAndMutationsAreThreadSafe()
    {
        var stable = new MemoryResourceProvider();
        stable.Add("base/item.png", [7, 7]);

        using var composite = new CompositeResourceProvider();
        composite.AddProvider(stable, leaveOpen: false);
        composite.AddExtension(".png");

        var ct = TestContext.Current.CancellationToken;
        var tasks = new List<Task>();
        var transientProviders = new MemoryResourceProvider[100];

        for (var iter = 0; iter < transientProviders.Length; iter++)
        {
            var transient = new MemoryResourceProvider();
            transient.Add("base/item.jpg", [(byte)iter]);
            transientProviders[iter] = transient;
        }

        try
        {
            for (var r = 0; r < 6; r++)
            {
                tasks.Add(Task.Run(async () =>
                {
                    for (var iter = 0; iter < 150; iter++)
                    {
                        Assert.True(composite.Exists("base/item"));
                        var bytes = await composite.GetBytesAsync("base/item", ct);
                        Assert.NotNull(bytes);

                        using var stream = composite.GetStream("base/item");
                        Assert.NotNull(stream);

                        foreach (var _ in composite.GetAvailableResources())
                        {
                        }
                    }
                }, ct));
            }

            tasks.Add(Task.Run(() =>
            {
                for (var iter = 0; iter < transientProviders.Length; iter++)
                {
                    var transient = transientProviders[iter];
                    composite.AddProvider(transient, leaveOpen: true);
                    composite.AddExtension(iter % 2 == 0 ? ".jpg" : ".webp");
                    composite.RemoveProvider(transient);
                }
            }, ct));

            await Task.WhenAll(tasks);
        }
        finally
        {
            for (var i = 0; i < transientProviders.Length; i++)
                transientProviders[i].Dispose();
        }
    }

    [Fact]
    public void DisposeRespectsLeaveOpenDisposesAllOwnedOnFailureAndBlocksSubsequentAccess()
    {
        var disposalOrder = new List<string>();
        var ownedFirst = new TrackingDisposableProvider("first", disposalOrder);
        var keptOpen = new TrackingDisposableProvider("kept", disposalOrder);
        var throwingOwned = new TrackingDisposableProvider("throwing", disposalOrder, throwOnDispose: true);

        var composite = new CompositeResourceProvider();
        composite.AddProvider(ownedFirst, leaveOpen: false);
        composite.AddProvider(keptOpen, leaveOpen: true);
        composite.AddProvider(throwingOwned, leaveOpen: false);

        Assert.Throws<InvalidOperationException>(composite.Dispose);

        // Both owned providers were disposed in LIFO order even though the top one threw; leaveOpen was untouched.
        Assert.Equal(["throwing", "first"], disposalOrder);
        Assert.False(keptOpen.IsDisposed);

        // Second Dispose is a no-op.
        composite.Dispose();
        Assert.Equal(["throwing", "first"], disposalOrder);

        Assert.Throws<ObjectDisposedException>(() => composite.AddProvider(keptOpen, leaveOpen: true));
        Assert.Throws<ObjectDisposedException>(() => composite.RemoveProvider(keptOpen));
        Assert.Throws<ObjectDisposedException>(() => composite.AddExtension(".png"));
        Assert.Throws<ObjectDisposedException>(() => composite.Exists("a"));
        Assert.Throws<ObjectDisposedException>(() => composite.GetStream("a"));
        Assert.Throws<ObjectDisposedException>(() => composite.GetBytes("a"));
        Assert.Throws<ObjectDisposedException>(() => { _ = composite.GetBytesAsync("a", TestContext.Current.CancellationToken); });
        Assert.Throws<ObjectDisposedException>(() => composite.GetAvailableResources());

        keptOpen.Dispose();
    }

    private sealed class RecordingResourceProvider : IResourceProvider
    {
        public List<string> ProbedNames { get; } = [];

        public Stream? GetStream(string name)
        {
            ProbedNames.Add(name);
            return null;
        }

        public byte[]? GetBytes(string name)
        {
            ProbedNames.Add(name);
            return null;
        }

        public Task<byte[]?> GetBytesAsync(string name, CancellationToken cancellationToken = default)
        {
            ProbedNames.Add(name);
            return Task.FromResult<byte[]?>(null);
        }

        public bool Exists(string name)
        {
            ProbedNames.Add(name);
            return false;
        }

        public IEnumerable<string> GetAvailableResources() => [];

        public void Dispose()
        {
        }
    }

    private sealed class CancellingAsyncProvider : IResourceProvider
    {
        private readonly CancellationTokenSource cts;

        public CancellingAsyncProvider(CancellationTokenSource cts)
        {
            this.cts = cts;
        }

        public Stream? GetStream(string name) => null;

        public byte[]? GetBytes(string name) => null;

        public async Task<byte[]?> GetBytesAsync(string name, CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            cts.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
            return null;
        }

        public bool Exists(string name) => false;

        public IEnumerable<string> GetAvailableResources() => [];

        public void Dispose()
        {
        }
    }

    private sealed class TrackingDisposableProvider : IResourceProvider
    {
        private readonly string label;
        private readonly List<string> log;
        private readonly bool throwOnDispose;

        public TrackingDisposableProvider(string label, List<string> log, bool throwOnDispose = false)
        {
            this.label = label;
            this.log = log;
            this.throwOnDispose = throwOnDispose;
        }

        public bool IsDisposed { get; private set; }

        public Stream? GetStream(string name) => null;

        public byte[]? GetBytes(string name) => null;

        public Task<byte[]?> GetBytesAsync(string name, CancellationToken cancellationToken = default)
            => Task.FromResult<byte[]?>(null);

        public bool Exists(string name) => false;

        public IEnumerable<string> GetAvailableResources() => [];

        public void Dispose()
        {
            IsDisposed = true;
            log.Add(label);

            if (throwOnDispose)
                throw new InvalidOperationException("Simulated disposal failure.");
        }
    }
}
