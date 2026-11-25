using kajarlabs.Framework.IO.Resources;

namespace kajarlabs.Framework.Tests.IO.Resources;

public sealed class MemoryResourceProviderTests
{
    [Fact]
    public async Task AddLookupStreamBytesAndRemoveWorkWithNormalizedPaths()
    {
        using var provider = new MemoryResourceProvider();
        var payload = new byte[] { 1, 2, 3, 4 };

        provider.Add("Textures\\UI//hero.png", payload);

        Assert.True(provider.Exists("Textures/UI/hero.png"));
        Assert.True(provider.Exists("./Textures/UI/hero.png"));
        Assert.True(provider.Exists("textures/ui/HERO.PNG"));
        Assert.Equal(payload, provider.GetBytes("Textures/UI/hero.png"));
        Assert.Equal(payload, await provider.GetBytesAsync("Textures/UI/hero.png", TestContext.Current.CancellationToken));
        Assert.Equal(["Textures/UI/hero.png"], provider.GetAvailableResources());

        using (var stream = provider.GetStream("Textures/UI/hero.png"))
        {
            Assert.NotNull(stream);
            Assert.False(stream.CanWrite);

            var buffer = new byte[payload.Length];
            stream.ReadExactly(buffer);
            Assert.Equal(payload, buffer);
        }

        Assert.True(provider.Remove("textures\\ui\\hero.png"));
        Assert.False(provider.Remove("textures/ui/hero.png"));
        Assert.False(provider.Exists("Textures/UI/hero.png"));
        Assert.Null(provider.GetStream("Textures/UI/hero.png"));
        Assert.Null(provider.GetBytes("Textures/UI/hero.png"));
        Assert.Null(await provider.GetBytesAsync("Textures/UI/hero.png", TestContext.Current.CancellationToken));
        Assert.Empty(provider.GetAvailableResources());
    }

    [Fact]
    public void CustomCaseSensitiveComparerIsRespected()
    {
        using var provider = new MemoryResourceProvider(StringComparer.Ordinal);
        provider.Add("Textures/Hero.png", [42]);

        Assert.True(provider.Exists("Textures/Hero.png"));
        Assert.False(provider.Exists("Textures/hero.png"));
        Assert.Null(provider.GetBytes("Textures/hero.png"));
    }

    [Fact]
    public async Task StoredBytesAreIsolatedFromExternalMutations()
    {
        using var provider = new MemoryResourceProvider();
        var source = new byte[] { 10, 20, 30 };

        provider.Add("data.bin", source);
        source[0] = 99;

        var copy1 = provider.GetBytes("data.bin");
        Assert.NotNull(copy1);
        Assert.Equal([10, 20, 30], copy1);
        copy1[1] = 88;

        var copy2 = await provider.GetBytesAsync("data.bin", TestContext.Current.CancellationToken);
        Assert.NotNull(copy2);
        Assert.Equal([10, 20, 30], copy2);
        copy2[2] = 77;

        Assert.Equal([10, 20, 30], provider.GetBytes("data.bin"));
    }

    [Fact]
    public async Task EmptyResourcePayloadIsSupportedAndOverwritingReplacesData()
    {
        using var provider = new MemoryResourceProvider();
        provider.Add("empty.bin", ReadOnlySpan<byte>.Empty);

        Assert.True(provider.Exists("empty.bin"));
        Assert.Empty(provider.GetBytes("empty.bin")!);
        Assert.Empty((await provider.GetBytesAsync("empty.bin", TestContext.Current.CancellationToken))!);

        using (var stream = provider.GetStream("empty.bin"))
        {
            Assert.NotNull(stream);
            Assert.Equal(0, stream.Length);
        }

        provider.Add("empty.bin", [7, 8]);
        Assert.Equal([7, 8], provider.GetBytes("empty.bin"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(".")]
    [InlineData("a/..")]
    public async Task EmptyOrWhitespaceLookupNamesReturnMissingWithoutThrowing(string name)
    {
        using var provider = new MemoryResourceProvider();
        provider.Add("a.bin", [1]);

        Assert.False(provider.Exists(name));
        Assert.False(provider.Remove(name));
        Assert.Null(provider.GetStream(name));
        Assert.Null(provider.GetBytes(name));
        Assert.Null(await provider.GetBytesAsync(name, TestContext.Current.CancellationToken));
        Assert.Throws<ArgumentException>(() => provider.Add(name, [1]));
    }

    [Theory]
    [InlineData("..")]
    [InlineData("../secret.bin")]
    [InlineData("a/../../secret.bin")]
    [InlineData("/abs.bin")]
    [InlineData("\\abs.bin")]
    [InlineData("C:drive.bin")]
    [InlineData("a/b:stream")]
    public void InvalidAndEscapingPathsThrowArgumentExceptionForEveryMember(string path)
    {
        using var provider = new MemoryResourceProvider();

        Assert.Throws<ArgumentException>(() => provider.Add(path, [1]));
        Assert.Throws<ArgumentException>(() => provider.Remove(path));
        Assert.Throws<ArgumentException>(() => provider.Exists(path));
        Assert.Throws<ArgumentException>(() => provider.GetStream(path));
        Assert.Throws<ArgumentException>(() => provider.GetBytes(path));
        Assert.Throws<ArgumentException>(() => { _ = provider.GetBytesAsync(path, TestContext.Current.CancellationToken); });
    }

    [Fact]
    public void NullPathThrowsArgumentNullExceptionForEveryMember()
    {
        using var provider = new MemoryResourceProvider();

        Assert.Throws<ArgumentNullException>(() => provider.Add(null!, [1]));
        Assert.Throws<ArgumentNullException>(() => provider.Remove(null!));
        Assert.Throws<ArgumentNullException>(() => provider.Exists(null!));
        Assert.Throws<ArgumentNullException>(() => provider.GetStream(null!));
        Assert.Throws<ArgumentNullException>(() => provider.GetBytes(null!));
        Assert.Throws<ArgumentNullException>(() => { _ = provider.GetBytesAsync(null!, TestContext.Current.CancellationToken); });
    }

    [Fact]
    public async Task GetBytesAsyncHonorsPreCancelledToken()
    {
        using var provider = new MemoryResourceProvider();
        provider.Add("item.bin", [1, 2, 3]);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var task = provider.GetBytesAsync("item.bin", cts.Token);
        Assert.True(task.IsCanceled);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await task);
    }

    [Fact]
    public async Task ConcurrentReadsAndWritesAreThreadSafe()
    {
        using var provider = new MemoryResourceProvider();

        for (var i = 0; i < 16; i++)
            provider.Add($"static/item_{i}.bin", [(byte)i]);

        var ct = TestContext.Current.CancellationToken;
        var tasks = new List<Task>();

        for (var r = 0; r < 6; r++)
        {
            var readerId = r;
            tasks.Add(Task.Run(async () =>
            {
                for (var iter = 0; iter < 150; iter++)
                {
                    var key = $"static/item_{(iter + readerId) % 16}.bin";
                    Assert.True(provider.Exists(key));

                    var bytes = await provider.GetBytesAsync(key, ct);
                    Assert.NotNull(bytes);

                    using var stream = provider.GetStream(key);
                    Assert.NotNull(stream);

                    foreach (var _ in provider.GetAvailableResources())
                    {
                    }
                }
            }, ct));
        }

        for (var w = 0; w < 2; w++)
        {
            var writerId = w;
            tasks.Add(Task.Run(() =>
            {
                for (var iter = 0; iter < 150; iter++)
                {
                    var dynamicKey = $"dynamic/w{writerId}_{iter % 8}.bin";
                    provider.Add(dynamicKey, [(byte)writerId, (byte)iter]);
                    _ = provider.GetBytes(dynamicKey);
                    provider.Remove(dynamicKey);
                }
            }, ct));
        }

        await Task.WhenAll(tasks);
    }

    [Fact]
    public void DisposeIsIdempotentAndBlocksSubsequentAccess()
    {
        var provider = new MemoryResourceProvider();
        provider.Add("a.bin", [1]);

        provider.Dispose();
        provider.Dispose();

        Assert.Throws<ObjectDisposedException>(() => provider.Add("b.bin", [2]));
        Assert.Throws<ObjectDisposedException>(() => provider.Remove("a.bin"));
        Assert.Throws<ObjectDisposedException>(() => provider.Exists("a.bin"));
        Assert.Throws<ObjectDisposedException>(() => provider.GetStream("a.bin"));
        Assert.Throws<ObjectDisposedException>(() => provider.GetBytes("a.bin"));
        Assert.Throws<ObjectDisposedException>(() => { _ = provider.GetBytesAsync("a.bin", TestContext.Current.CancellationToken); });
        Assert.Throws<ObjectDisposedException>(() => provider.GetAvailableResources());
    }
}
