using System.Numerics;
using kajarlabs.Framework.DependencyInjection;
using kajarlabs.Framework.Graphics;
using kajarlabs.Framework.Graphics.Rendering;
using kajarlabs.Framework.IO;
using kajarlabs.Framework.IO.Resources;
using kajarlabs.Framework.Platform;
using kajarlabs.Framework.Scene;
using kajarlabs.Framework.Tests.TestUtilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace kajarlabs.Framework.Tests.Platform;

[Collection("SDL")]
public sealed partial class GameResourcesIntegrationTests : IDisposable
{
    private const string image_fixtures_prefix = "kajarlabs.Framework.Tests.Graphics.Decoding.Fixtures";
    private readonly string tempDir = Path.Combine(Path.GetTempPath(), "kajarlabs-gameres-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(tempDir))
            Directory.Delete(tempDir, recursive: true);
    }

    [Fact]
    public void AccessingGameTexturesBeforeLoadThrowsInvalidOperationException()
    {
        using var game = new EmptyResourceGame();

        Assert.NotNull(game.Resources);
        Assert.Throws<InvalidOperationException>(() => _ = game.Textures);
    }

    [Fact]
    public void HeadlessHostResolvesResourcesAndTextureCacheAcrossSyncAndAsyncSprites()
    {
        var diskStorage = new FileSystemStorage(tempDir);
        writeFixtureToDisk(diskStorage, "Textures/disk-hero.jpg", "sample-rgb.jpg");
        writeFixtureToDisk(diskStorage, "Textures/sample-rgba.png", "sample-rgb.jpg"); // 4x4 override in Textures/

        using var host = new HeadlessGameHost();
        using var game = new ResourceWiredGame(diskStorage);

        host.RunFrames(game, 500);

        Assert.Same(game.Resources, game.ResolvedResourceProvider);
        Assert.Same(game.Resources, game.ResolvedCompositeProvider);
        Assert.Same(game.Textures, game.ResolvedTextureCache);

        // Textures/ namespace takes priority over root fallback (4x4 disk override vs 2x2 embedded root).
        Assert.NotNull(game.SyncSprite.Texture);
        Assert.Equal(new Vector2(4f, 4f), game.SyncSprite.DrawSize);

        // Embedded asset at root still resolves via fallback.
        Assert.NotNull(game.EmbeddedRootTexture);
        Assert.Equal(2, game.EmbeddedRootTexture.Width);
        Assert.Equal(2, game.EmbeddedRootTexture.Height);

        // LoadComponentAsync sprite resolved [Resolved] TextureCache & loaded disk texture cleanly.
        Assert.True(game.AsyncLoadTask is { IsCompletedSuccessfully: true });
        Assert.Equal(LoadState.Loaded, game.AsyncSprite.LoadState);
        Assert.Same(game.Textures, game.AsyncSprite.ResolvedTextures);
        Assert.NotNull(game.AsyncSprite.Texture);
        Assert.Equal(new Vector2(4f, 4f), game.AsyncSprite.DrawSize);

        // Sprites rendered cached textures through NullRenderer without unwrap errors.
        Assert.True(host.Renderer.LastFrameQuadCount >= 2);

        // Cached textures remain inspectable after RunFrames until Game.Dispose().
        Assert.True(game.Textures.TryGetCached("disk-hero", out _));
    }

    [Fact]
    public void DisposingGameBeforeOrAfterSdlGpuRendererShutdownReleasesAllTexturesWithoutError()
    {
        // Order A: Game.Dispose() runs after Host.Run() (Renderer.Shutdown() already ran).
        var gpuBackendAfter = new TrackingGpuBackend();
        using (var host = createStubGpuHost(gpuBackendAfter))
        using (var game = new EmbeddedTextureLoadGame())
        {
            host.Run(game);
            Assert.NotNull(game.LoadedTexture);
        }

        Assert.Equal(gpuBackendAfter.CreatedSamplerTextures.Order(), gpuBackendAfter.ReleasedTextures.Order());
        Assert.True(gpuBackendAfter.LastReleaseIndex < gpuBackendAfter.DestroyDeviceIndex);

        // Order B: TextureCache.Dispose() / Game.Dispose() runs explicitly BEFORE SdlGpuRenderer.Shutdown().
        var gpuBackendBefore = new TrackingGpuBackend();
        using var rendererBefore = new SdlGpuRenderer(gpuBackendBefore, NullLogger.Instance);
        rendererBefore.Initialize(new nint(0x5100));

        using (var resourcesBefore = new AssemblyResourceProvider(typeof(GameResourcesIntegrationTests).Assembly, image_fixtures_prefix))
        {
            var cacheBefore = new TextureCache(rendererBefore, resourcesBefore);
            var texBefore = cacheBefore.Get("sample-rgba");
            Assert.NotNull(texBefore);
            Assert.Equal(2, rendererBefore.TextureManager!.LiveTextureCount);

            cacheBefore.Dispose();
            Assert.Equal(1, rendererBefore.TextureManager.LiveTextureCount);
            Assert.Throws<ObjectDisposedException>(() => cacheBefore.Get("sample-rgba"));
        }

        rendererBefore.Shutdown();
        Assert.Equal(gpuBackendBefore.CreatedSamplerTextures.Order(), gpuBackendBefore.ReleasedTextures.Order());
        Assert.True(gpuBackendBefore.LastReleaseIndex < gpuBackendBefore.DestroyDeviceIndex);
    }

    private GameHost createStubGpuHost(TrackingGpuBackend gpuBackend)
        => new(
            new HostOptions
            {
                Organization = "kajarlabs",
                Application = "TextureIntegrationTest",
                UserDataPath = Path.Combine(tempDir, Guid.NewGuid().ToString("N")),
                EnableConsoleLogging = false,
                EnableFileLogging = false,
            },
            new FakeWindowPlatformBackend(),
            gpuBackend);

    private static void writeFixtureToDisk(Storage storage, string relativePath, string embeddedFixtureName)
    {
        using var source = typeof(GameResourcesIntegrationTests).Assembly
            .GetManifestResourceStream($"{image_fixtures_prefix}.{embeddedFixtureName}")
            ?? throw new InvalidOperationException($"Missing embedded fixture '{embeddedFixtureName}'.");
        using var destination = storage.GetStream(relativePath, FileAccess.Write, FileMode.Create);
        source.CopyTo(destination);
    }

    private sealed class EmptyResourceGame : Game
    {
    }

    private sealed partial class ResourceWiredGame : Game
    {
        private readonly Storage diskStorage;
        private int postAsyncUpdates;
        private int postAsyncDraws;

        public ResourceWiredGame(Storage diskStorage)
        {
            this.diskStorage = diskStorage;

            // Register embedded assembly resources first, then disk overrides in constructor before Load().
            Resources.AddProvider(new AssemblyResourceProvider(typeof(GameResourcesIntegrationTests).Assembly, image_fixtures_prefix));
            Resources.AddProvider(new StorageResourceProvider(this.diskStorage));
        }

        [Resolved]
        public IResourceProvider ResolvedResourceProvider { get; private set; } = null!;

        [Resolved]
        public CompositeResourceProvider ResolvedCompositeProvider { get; private set; } = null!;

        [Resolved]
        public TextureCache ResolvedTextureCache { get; private set; } = null!;

        public TextureLoadedSprite SyncSprite { get; } = new("sample-rgba");

        public AsyncTextureSprite AsyncSprite { get; } = new("disk-hero");

        public ITexture? EmbeddedRootTexture { get; private set; }

        public Task<AsyncTextureSprite>? AsyncLoadTask { get; private set; }

        protected override void Load()
        {
            EmbeddedRootTexture = Textures.Get("sample-rgba.tga");
            Root.Add(SyncSprite);
            AsyncLoadTask = Root.LoadComponentAsync(AsyncSprite, Root.Add);
        }

        protected override void Update()
        {
            if (AsyncLoadTask is { IsCompleted: true } && AsyncSprite.LoadState == LoadState.Loaded)
            {
                var published = Interlocked.Increment(ref postAsyncUpdates);

                if (published >= 2 && Volatile.Read(ref postAsyncDraws) < 2)
                {
                    SpinWait.SpinUntil(
                        () => Volatile.Read(ref postAsyncDraws) >= 2,
                        TimeSpan.FromSeconds(5));
                }

                if (Volatile.Read(ref postAsyncDraws) >= 2)
                    Exit();

                return;
            }

            Thread.Sleep(1);
        }

        protected override void Draw()
        {
            if (Volatile.Read(ref postAsyncUpdates) >= 2)
                Interlocked.Increment(ref postAsyncDraws);
        }
    }

    private sealed partial class TextureLoadedSprite : Sprite
    {
        private readonly string textureName;

        public TextureLoadedSprite(string textureName)
        {
            this.textureName = textureName;
        }

        [Resolved]
        private TextureCache textures { get; set; } = null!;

        protected override void Load()
        {
            Texture = textures.Get(textureName);
        }
    }

    private sealed partial class AsyncTextureSprite : Sprite
    {
        private readonly string textureName;

        public AsyncTextureSprite(string textureName)
        {
            this.textureName = textureName;
        }

        [Resolved]
        public TextureCache ResolvedTextures { get; private set; } = null!;

        protected override void Load()
        {
            Texture = ResolvedTextures.GetAsync(textureName).GetAwaiter().GetResult();
        }
    }

    private sealed class EmbeddedTextureLoadGame : Game
    {
        public EmbeddedTextureLoadGame()
        {
            Resources.AddProvider(new AssemblyResourceProvider(typeof(GameResourcesIntegrationTests).Assembly, image_fixtures_prefix));
        }

        public ITexture? LoadedTexture { get; private set; }

        protected override void Load()
        {
            LoadedTexture = Textures.Get("sample-rgba");
            Root.Add(new Sprite { Texture = LoadedTexture });
        }

        protected override void Update() => Exit();
    }

    private sealed class FakeWindowPlatformBackend : IPlatformBackend
    {
        public IDisposable InstallLogRouter(ILoggerFactory loggerFactory) => new NoOpDisposable();

        public IDisposable InitializeVideo(string? videoDriverHint = null) => new NoOpDisposable();

        public bool IsDummyVideoDriver() => false;

        public nint CreateWindow(string title, int width, int height, bool resizable) => new(0x5000);

        public void DestroyWindow(nint handle)
        {
        }

        public uint GetWindowId(nint handle) => 1;

        public void SetWindowTitle(nint handle, string title)
        {
        }

        public void GetWindowSize(nint handle, out int width, out int height)
        {
            width = 640;
            height = 480;
        }

        public bool GetWindowPixelSize(nint handle, out int width, out int height)
        {
            width = 640;
            height = 480;
            return true;
        }

        public void SetWindowSize(nint handle, int width, int height)
        {
        }

        public void StartTextInput(nint handle)
        {
        }

        public void StopTextInput(nint handle)
        {
        }

        public bool PollEvent(out PlatformEvent platformEvent)
        {
            platformEvent = default;
            return false;
        }

        public IDisposable AddEventWatch(PlatformEventWatchCallback callback) => new NoOpDisposable();

        private sealed class NoOpDisposable : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }

    private sealed class TrackingGpuBackend : StubGpuBackend
    {
        private readonly Lock backendLock = new();
        private readonly Dictionary<nint, byte[]> mappedBuffers = [];
        private int eventSeq;

        public List<nint> CreatedSamplerTextures { get; } = [];

        public List<nint> ReleasedTextures { get; } = [];

        public int LastReleaseIndex { get; private set; } = -1;

        public int DestroyDeviceIndex { get; private set; } = -1;

        public override nint CreateTexture(nint device, in GpuTextureDescription description)
        {
            var handle = NextHandle();

            lock (backendLock)
            {
                if ((description.Usage & GpuTextureUsage.ColorTarget) == 0)
                    CreatedSamplerTextures.Add(handle);
            }

            return handle;
        }

        public override void ReleaseTexture(nint device, nint texture)
        {
            lock (backendLock)
            {
                if (CreatedSamplerTextures.Contains(texture))
                {
                    ReleasedTextures.Add(texture);
                    LastReleaseIndex = ++eventSeq;
                }
            }
        }

        public override void DestroyDevice(nint device)
        {
            lock (backendLock)
                DestroyDeviceIndex = ++eventSeq;
        }

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
