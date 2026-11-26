using kajarlabs.Framework.Graphics;
using kajarlabs.Framework.Graphics.Decoding;
using kajarlabs.Framework.Graphics.Rendering;
using kajarlabs.Framework.IO;
using kajarlabs.Framework.IO.Resources;
using kajarlabs.Framework.Numerics;
using kajarlabs.Framework.Tests.TestUtilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace kajarlabs.Framework.Tests.Graphics;

public sealed class TextureCacheTests : IDisposable
{
    private const string image_fixtures_prefix = "kajarlabs.Framework.Tests.Graphics.Decoding.Fixtures";
    private readonly string tempDir = Path.Combine(Path.GetTempPath(), "kajarlabs-texcache-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(tempDir))
            Directory.Delete(tempDir, recursive: true);
    }

    [Fact]
    public async Task LoadsAllSupportedFormatsFromEmbeddedAssemblyAndCachesAliases()
    {
        using var renderer = new NullRenderer(640, 480);
        using var resources = new AssemblyResourceProvider(typeof(TextureCacheTests).Assembly, image_fixtures_prefix);
        var countingDecoder = new CountingDecoder(new StbImageDecoder());
        using var cache = new TextureCache(renderer, resources, countingDecoder);

        var png = cache.Get("sample-rgba");
        Assert.NotNull(png);
        Assert.Equal(2, png.Width);
        Assert.Equal(2, png.Height);
        Assert.Equal(1, countingDecoder.DecodeCount);

        // Extensionless, explicit-extension, and differently cased lookups alias the exact same cached texture.
        Assert.Same(png, cache.Get("sample-rgba.png"));
        Assert.Same(png, cache.Get("SAMPLE-RGBA.PNG"));
        Assert.Same(png, await cache.GetAsync("sample-rgba", TestContext.Current.CancellationToken));
        Assert.True(cache.TryGetCached("sample-rgba", out var cachedAlias));
        Assert.Same(png, cachedAlias);
        Assert.True(cache.TryGetCached("sample-rgba.png", out var cachedResolved));
        Assert.Same(png, cachedResolved);
        Assert.Equal(1, countingDecoder.DecodeCount);

        // JPEG, BMP, and TGA also resolve via default image extensions.
        var jpg = cache.Get("sample-rgb.jpg");
        var bmp = await cache.GetAsync("sample-rgb.bmp", TestContext.Current.CancellationToken);
        var tga = cache.Get("sample-rgba.tga");

        Assert.NotNull(jpg);
        Assert.Equal(4, jpg.Width);
        Assert.Equal(4, jpg.Height);
        Assert.NotNull(bmp);
        Assert.Equal(2, bmp.Width);
        Assert.Equal(2, bmp.Height);
        Assert.NotNull(tga);
        Assert.Equal(2, tga.Width);
        Assert.Equal(2, tga.Height);
        Assert.Equal(4, countingDecoder.DecodeCount);
    }

    [Fact]
    public void LoadsFromDiskStorageAndResolvesExtensionlessLookupAfterExplicitKeyCached()
    {
        var storage = new FileSystemStorage(tempDir);
        writeFixtureToDisk(storage, "hero.png", "sample-rgba.png");

        using var renderer = new NullRenderer(640, 480);
        using var diskResources = new StorageResourceProvider(storage);
        var countingDecoder = new CountingDecoder(new StbImageDecoder());
        using var cache = new TextureCache(renderer, diskResources, countingDecoder);

        var explicitTex = cache.Get("hero.png");
        Assert.NotNull(explicitTex);
        Assert.Equal(1, countingDecoder.DecodeCount);

        // Extensionless lookup aliases the already-cached "hero.png" entry without decoding again.
        var aliasTex = cache.Get("hero");
        Assert.Same(explicitTex, aliasTex);
        Assert.Equal(1, countingDecoder.DecodeCount);
    }

    [Fact]
    public void CachedTextureProtectsAgainstCallerDisposeAndMutationAndUnwrapsInBothRenderers()
    {
        var fakeWindow = new nint(0x4000);
        var gpuBackend = new TextureLifecycleGpuBackend();
        using var gpuRenderer = new SdlGpuRenderer(gpuBackend, NullLogger.Instance);
        gpuRenderer.Initialize(fakeWindow);

        using var resources = new AssemblyResourceProvider(typeof(TextureCacheTests).Assembly, image_fixtures_prefix);
        using var gpuCache = new TextureCache(gpuRenderer, resources);

        var gpuCached = gpuCache.Get("sample-rgba");
        Assert.NotNull(gpuCached);

        // Caller mutations and Dispose are blocked / safe no-ops.
        Assert.Throws<NotSupportedException>(() => gpuCached.SetData(new byte[2 * 2 * 4]));
        Assert.Throws<NotSupportedException>(() => gpuCached.SetData(new RectangleI(0, 0, 1, 1), new byte[4]));
        gpuCached.Dispose();
        Assert.Equal(2, gpuRenderer.TextureManager!.LiveTextureCount); // WhitePixel + cached texture still alive.

        // SdlGpuRenderer / GpuQuadBatcher unwraps CachedTexture cleanly during DrawQuad.
        gpuRenderer.BeginFrame();
        gpuRenderer.DrawQuad(gpuCached, new RectangleF(0f, 0f, 16f, 16f), Color4.WHITE);
        gpuRenderer.EndFrame();
        Assert.Equal(1, gpuRenderer.LastFrameQuadCount);

        // Removing either alias removes both keys and releases the underlying GPU texture once.
        Assert.True(gpuCache.Remove("sample-rgba.png"));
        Assert.False(gpuCache.TryGetCached("sample-rgba", out _));
        Assert.False(gpuCache.TryGetCached("sample-rgba.png", out _));
        Assert.False(gpuCache.Remove("sample-rgba"));
        Assert.Equal(1, gpuRenderer.TextureManager.LiveTextureCount); // Only WhitePixel remains.

        // NullRenderer also unwraps CachedTexture cleanly during DrawQuad.
        using var nullRenderer = new NullRenderer(640, 480);
        using var nullCache = new TextureCache(nullRenderer, resources);
        var nullCached = nullCache.Get("sample-rgba");
        Assert.NotNull(nullCached);

        nullCached.Dispose();
        nullRenderer.BeginFrame();
        nullRenderer.DrawQuad(nullCached, new RectangleF(0f, 0f, 16f, 16f), Color4.WHITE);
        nullRenderer.EndFrame();
        Assert.Equal(1, nullRenderer.LastFrameQuadCount);
    }

    [Fact]
    public async Task ConcurrentGetAsyncCoalescesIntoSingleDecodeAndUploadWithIsolatedCancellation()
    {
        var pngBytes = readEmbeddedFixtureBytes("sample-rgba.png");
        using var gatedResources = new GatedAsyncResourceProvider("hero.png", pngBytes);
        using var renderer = new NullRenderer(640, 480);
        var countingDecoder = new CountingDecoder(new StbImageDecoder());
        using var cache = new TextureCache(renderer, gatedResources, countingDecoder);

        using var cancelledCallerCts = new CancellationTokenSource();
        var ct = TestContext.Current.CancellationToken;

        // Launch 6 concurrent callers (one will cancel while the load is in flight).
        var caller1 = cache.GetAsync("hero", cancelledCallerCts.Token);
        await gatedResources.ReadStarted.Task.WaitAsync(ct);

        var caller2 = cache.GetAsync("hero", ct);
        var caller3 = cache.GetAsync("HERO", ct);
        var caller4 = cache.GetAsync("hero", ct);

        // Cancelling caller1 cancels only caller1's wait; the shared load keeps running for callers 2..4.
        cancelledCallerCts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await caller1);

        gatedResources.ReleaseRead.TrySetResult();

        var results = await Task.WhenAll(caller2, caller3, caller4);

        Assert.All(results, tex => Assert.NotNull(tex));
        Assert.Same(results[0], results[1]);
        Assert.Same(results[0], results[2]);
        Assert.Equal(1, countingDecoder.DecodeCount);
        Assert.Equal(1, gatedResources.HitReadCount);
    }

    [Fact]
    public async Task MissingAndCorruptResourcesLogErrorReturnNullAndAreNotCached()
    {
        using var renderer = new NullRenderer(640, 480);
        using var resources = new MemoryResourceProvider();
        resources.Add("corrupt.png", [0x00, 0x01, 0x02, 0x03, 0xFF, 0xFE]);

        using var loggerProvider = new CapturingLoggerProvider();
        var logger = loggerProvider.CreateLogger(nameof(TextureCacheTests));
        using var cache = new TextureCache(renderer, resources, logger);

        // Missing resources return null and are not cached.
        Assert.Null(cache.Get("does-not-exist"));
        Assert.Null(await cache.GetAsync("does-not-exist", TestContext.Current.CancellationToken));
        Assert.False(cache.TryGetCached("does-not-exist", out _));

        // Adding the missing resource later succeeds immediately.
        resources.Add("does-not-exist.png", readEmbeddedFixtureBytes("sample-rgba.png"));
        Assert.NotNull(cache.Get("does-not-exist"));

        // Corrupt image logs LogLevel.Error, returns null, and is not cached.
        Assert.Null(cache.Get("corrupt"));
        Assert.Null(await cache.GetAsync("corrupt.png", TestContext.Current.CancellationToken));
        Assert.False(cache.TryGetCached("corrupt", out _));
        Assert.False(cache.TryGetCached("corrupt.png", out _));
        Assert.Contains(
            loggerProvider.Entries,
            entry => entry.Level == LogLevel.Error && entry.Message.Contains("corrupt.png", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DisposeDuringInFlightGetAsyncDisposesNewlyCreatedTextureWithoutLeaking()
    {
        var fakeWindow = new nint(0x4100);
        var gpuBackend = new TextureLifecycleGpuBackend();
        using var gpuRenderer = new SdlGpuRenderer(gpuBackend, NullLogger.Instance);
        gpuRenderer.Initialize(fakeWindow);

        var pngBytes = readEmbeddedFixtureBytes("sample-rgba.png");
        using var gatedResources = new GatedAsyncResourceProvider("hero.png", pngBytes);
        var cache = new TextureCache(gpuRenderer, gatedResources);

        var ct = TestContext.Current.CancellationToken;
        var inFlightTask = cache.GetAsync("hero", ct);
        await gatedResources.ReadStarted.Task.WaitAsync(ct);

        cache.Dispose();
        gatedResources.ReleaseRead.TrySetResult();

        var result = await inFlightTask;
        Assert.Null(result);

        // Only WhitePixel remains live; no GPU texture leaked from the in-flight load.
        Assert.Equal(1, gpuRenderer.TextureManager!.LiveTextureCount);
    }

    [Fact]
    public void ClearDisposeAndValidationBehaveDeterministically()
    {
        var fakeWindow = new nint(0x4200);
        var gpuBackend = new TextureLifecycleGpuBackend();
        using var gpuRenderer = new SdlGpuRenderer(gpuBackend, NullLogger.Instance);
        gpuRenderer.Initialize(fakeWindow);

        using var resources = new AssemblyResourceProvider(typeof(TextureCacheTests).Assembly, image_fixtures_prefix);

        Assert.Throws<ArgumentNullException>(() => new TextureCache(null!, resources));
        Assert.Throws<ArgumentNullException>(() => new TextureCache(gpuRenderer, null!));

        var cache = new TextureCache(gpuRenderer, resources);
        Assert.NotNull(cache.Get("sample-rgba"));
        Assert.NotNull(cache.Get("sample-rgb.jpg"));
        Assert.Equal(3, gpuRenderer.TextureManager!.LiveTextureCount); // WhitePixel + 2 textures.

        cache.Clear();
        Assert.Equal(1, gpuRenderer.TextureManager.LiveTextureCount);
        Assert.False(cache.TryGetCached("sample-rgba", out _));

        // Cache remains usable after Clear.
        Assert.NotNull(cache.Get("sample-rgba"));
        Assert.Equal(2, gpuRenderer.TextureManager.LiveTextureCount);

        // Empty/whitespace names return null/false; escaping/null names throw.
        Assert.Null(cache.Get(""));
        Assert.Null(cache.Get("   "));
        Assert.False(cache.TryGetCached("   ", out _));
        Assert.False(cache.Remove(""));
        Assert.Throws<ArgumentNullException>(() => cache.Get(null!));
        Assert.Throws<ArgumentException>(() => cache.Get("../escape.png"));
        Assert.Throws<ArgumentException>(() => { _ = cache.GetAsync("../escape.png", TestContext.Current.CancellationToken); });

        // Dispose releases all cached textures, is idempotent, leaves resources open, and blocks further access.
        cache.Dispose();
        cache.Dispose();
        Assert.Equal(1, gpuRenderer.TextureManager.LiveTextureCount);
        Assert.True(resources.Exists("sample-rgba.png"));

        Assert.Throws<ObjectDisposedException>(() => cache.Get("sample-rgba"));
        Assert.Throws<ObjectDisposedException>(() => { _ = cache.GetAsync("sample-rgba", TestContext.Current.CancellationToken); });
        Assert.Throws<ObjectDisposedException>(() => cache.TryGetCached("sample-rgba", out _));
        Assert.Throws<ObjectDisposedException>(() => cache.Remove("sample-rgba"));
        Assert.Throws<ObjectDisposedException>(cache.Clear);
    }

    private static byte[] readEmbeddedFixtureBytes(string fixtureName)
    {
        using var stream = typeof(TextureCacheTests).Assembly.GetManifestResourceStream($"{image_fixtures_prefix}.{fixtureName}")
            ?? throw new InvalidOperationException($"Missing embedded fixture '{fixtureName}'.");
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    private static void writeFixtureToDisk(Storage storage, string relativePath, string embeddedFixtureName)
    {
        var bytes = readEmbeddedFixtureBytes(embeddedFixtureName);
        using var stream = storage.GetStream(relativePath, FileAccess.Write, FileMode.Create);
        stream.Write(bytes, 0, bytes.Length);
    }

    private sealed class CountingDecoder : IImageDecoder
    {
        private readonly IImageDecoder inner;
        private int decodeCount;

        public CountingDecoder(IImageDecoder inner)
        {
            this.inner = inner;
        }

        public int DecodeCount => Volatile.Read(ref decodeCount);

        public DecodedImage Decode(Stream stream)
        {
            Interlocked.Increment(ref decodeCount);
            return inner.Decode(stream);
        }

        public DecodedImage Decode(ReadOnlySpan<byte> encodedBytes)
        {
            Interlocked.Increment(ref decodeCount);
            return inner.Decode(encodedBytes);
        }
    }

    private sealed class GatedAsyncResourceProvider : IResourceProvider
    {
        private readonly string targetKey;
        private readonly byte[] payload;
        private int hitReadCount;

        public GatedAsyncResourceProvider(string targetKey, byte[] payload)
        {
            this.targetKey = targetKey;
            this.payload = payload;
        }

        public TaskCompletionSource ReadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseRead { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int HitReadCount => Volatile.Read(ref hitReadCount);

        public Stream? GetStream(string name)
            => string.Equals(name, targetKey, StringComparison.OrdinalIgnoreCase)
                ? new MemoryStream(payload, writable: false)
                : null;

        public byte[]? GetBytes(string name)
            => string.Equals(name, targetKey, StringComparison.OrdinalIgnoreCase)
                ? [.. payload]
                : null;

        public async Task<byte[]?> GetBytesAsync(string name, CancellationToken cancellationToken = default)
        {
            if (!string.Equals(name, targetKey, StringComparison.OrdinalIgnoreCase))
                return null;

            Interlocked.Increment(ref hitReadCount);
            ReadStarted.TrySetResult();
            await ReleaseRead.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            return [.. payload];
        }

        public bool Exists(string name) => string.Equals(name, targetKey, StringComparison.OrdinalIgnoreCase);

        public IEnumerable<string> GetAvailableResources() => [targetKey];

        public void Dispose() => ReleaseRead.TrySetResult();
    }

    private sealed class TextureLifecycleGpuBackend : StubGpuBackend
    {
        private readonly Lock backendLock = new();
        private readonly Dictionary<nint, byte[]> mappedBuffers = [];

        public override nint CreateTransferBuffer(nint device, GpuTransferBufferUsage usage, uint sizeInBytes)
        {
            var handle = NextHandle();

            lock (backendLock)
                mappedBuffers[handle] = new byte[sizeInBytes];

            return handle;
        }

        public override void ReleaseTransferBuffer(nint device, nint transferBuffer)
        {
            lock (backendLock)
                mappedBuffers.Remove(transferBuffer);
        }

        public override nint MapTransferBuffer(nint device, nint transferBuffer, bool cycle) => transferBuffer;

        public override void CopyToMappedBuffer(nint mappedDestination, int byteOffset, byte[] source, int sourceOffset, int byteCount)
        {
            lock (backendLock)
                Buffer.BlockCopy(source, sourceOffset, mappedBuffers[mappedDestination], byteOffset, byteCount);
        }
    }
}
