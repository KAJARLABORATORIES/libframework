using System.Text;
using kajarlabs.Framework.IO;
using kajarlabs.Framework.IO.Resources;

namespace kajarlabs.Framework.Tests.IO.Resources;

public sealed class StorageResourceProviderTests : IDisposable
{
    private readonly string tempRoot = Path.Combine(Path.GetTempPath(), "kajarlabs-res-" + Guid.NewGuid().ToString("N"));
    private readonly FileSystemStorage storage;

    public StorageResourceProviderTests()
    {
        storage = new FileSystemStorage(tempRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(tempRoot))
            Directory.Delete(tempRoot, recursive: true);
    }

    [Fact]
    public async Task ReadsDiskFilesRecursivelyAndMapsMissingFilesWithoutThrowing()
    {
        writeDiskFile("root.txt", "root-data");
        writeDiskFile("Textures/hero.png", "hero-bytes");
        writeDiskFile("Textures/UI/Deep/icon.png", "icon-bytes");
        writeDiskFile("empty.bin", string.Empty);

        using var provider = new StorageResourceProvider(storage);

        Assert.True(provider.Exists("root.txt"));
        Assert.True(provider.Exists("Textures\\UI/./Deep//icon.png"));
        Assert.False(provider.Exists("missing.txt"));
        Assert.False(provider.Exists("Textures")); // Directory is not a file resource.

        Assert.Equal("root-data", Encoding.UTF8.GetString(provider.GetBytes("root.txt")!));
        Assert.Equal(
            "icon-bytes",
            Encoding.UTF8.GetString((await provider.GetBytesAsync("Textures/UI/Deep/icon.png", TestContext.Current.CancellationToken))!));
        Assert.Empty(provider.GetBytes("empty.bin")!);

        using (var stream = provider.GetStream("Textures/hero.png"))
        {
            Assert.NotNull(stream);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            Assert.Equal("hero-bytes", await reader.ReadToEndAsync(TestContext.Current.CancellationToken));
        }

        Assert.Null(provider.GetStream("missing.txt"));
        Assert.Null(provider.GetBytes("missing/deep/file.txt"));
        Assert.Null(await provider.GetBytesAsync("missing/deep/file.txt", TestContext.Current.CancellationToken));

        var available = provider.GetAvailableResources().Order(StringComparer.Ordinal).ToList();
        Assert.Equal(
            [
                "Textures/UI/Deep/icon.png",
                "Textures/hero.png",
                "empty.bin",
                "root.txt",
            ],
            available);
    }

    [Fact]
    public void ListingMissingStorageRootReturnsEmpty()
    {
        using var provider = new StorageResourceProvider(storage);

        Assert.False(provider.Exists("anything.txt"));
        Assert.Empty(provider.GetAvailableResources());
    }

    [Fact]
    public async Task SupportsCustomStorageIncludingNonSeekableStreamsAndToctouMissingFiles()
    {
        var inner = new NonSeekableAndToctouStorage();
        inner.Add("data/stream.bin", [10, 20, 30, 40]);
        inner.GhostFilePath = "ghost/deleted.bin";

        using var provider = new StorageResourceProvider(inner);

        Assert.Equal([10, 20, 30, 40], provider.GetBytes("data/stream.bin"));
        Assert.Equal(
            [10, 20, 30, 40],
            await provider.GetBytesAsync("data/stream.bin", TestContext.Current.CancellationToken));

        // TOCTOU: Exists returns true, GetStream throws FileNotFoundException -> provider maps cleanly to null.
        Assert.True(provider.Exists("ghost/deleted.bin"));
        Assert.Null(provider.GetStream("ghost/deleted.bin"));
        Assert.Null(provider.GetBytes("ghost/deleted.bin"));
        Assert.Null(await provider.GetBytesAsync("ghost/deleted.bin", TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(".")]
    [InlineData("a/..")]
    public async Task EmptyOrRootCollapsingNamesReturnMissingWithoutThrowing(string name)
    {
        writeDiskFile("a.txt", "a");
        using var provider = new StorageResourceProvider(storage);

        Assert.False(provider.Exists(name));
        Assert.Null(provider.GetStream(name));
        Assert.Null(provider.GetBytes(name));
        Assert.Null(await provider.GetBytesAsync(name, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("..")]
    [InlineData("../outside.txt")]
    [InlineData("a/../../outside.txt")]
    [InlineData("/etc/passwd")]
    [InlineData("\\windows")]
    [InlineData("C:file.txt")]
    [InlineData("a/b:stream")]
    public void EscapingAndInvalidPathsThrowArgumentException(string path)
    {
        using var provider = new StorageResourceProvider(storage);

        Assert.Throws<ArgumentException>(() => provider.Exists(path));
        Assert.Throws<ArgumentException>(() => provider.GetStream(path));
        Assert.Throws<ArgumentException>(() => provider.GetBytes(path));
        Assert.Throws<ArgumentException>(() => { _ = provider.GetBytesAsync(path, TestContext.Current.CancellationToken); });
    }

    [Fact]
    public void NullArgumentsThrowArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new StorageResourceProvider(null!));

        using var provider = new StorageResourceProvider(storage);
        Assert.Throws<ArgumentNullException>(() => provider.Exists(null!));
        Assert.Throws<ArgumentNullException>(() => provider.GetStream(null!));
        Assert.Throws<ArgumentNullException>(() => provider.GetBytes(null!));
        Assert.Throws<ArgumentNullException>(() => { _ = provider.GetBytesAsync(null!, TestContext.Current.CancellationToken); });
    }

    [Fact]
    public async Task GetBytesAsyncHonorsPreCancelledToken()
    {
        writeDiskFile("item.txt", "payload");
        using var provider = new StorageResourceProvider(storage);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var task = provider.GetBytesAsync("item.txt", cts.Token);
        Assert.True(task.IsCanceled);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await task);
    }

    [Fact]
    public async Task ConcurrentDiskReadsAreThreadSafe()
    {
        for (var i = 0; i < 8; i++)
            writeDiskFile($"assets/item_{i}.txt", $"value-{i}");

        using var provider = new StorageResourceProvider(storage);
        var ct = TestContext.Current.CancellationToken;
        var tasks = new List<Task>();

        for (var worker = 0; worker < 6; worker++)
        {
            var workerId = worker;
            tasks.Add(Task.Run(async () =>
            {
                for (var iter = 0; iter < 40; iter++)
                {
                    var idx = (workerId + iter) % 8;
                    var path = $"assets/item_{idx}.txt";

                    Assert.True(provider.Exists(path));
                    var bytes = await provider.GetBytesAsync(path, ct);
                    Assert.Equal($"value-{idx}", Encoding.UTF8.GetString(bytes!));
                }
            }, ct));
        }

        await Task.WhenAll(tasks);
    }

    [Fact]
    public void DisposeIsIdempotentAndBlocksSubsequentAccess()
    {
        writeDiskFile("item.txt", "hello");
        var provider = new StorageResourceProvider(storage);

        provider.Dispose();
        provider.Dispose();

        Assert.Throws<ObjectDisposedException>(() => provider.Exists("item.txt"));
        Assert.Throws<ObjectDisposedException>(() => provider.GetStream("item.txt"));
        Assert.Throws<ObjectDisposedException>(() => provider.GetBytes("item.txt"));
        Assert.Throws<ObjectDisposedException>(() => { _ = provider.GetBytesAsync("item.txt", TestContext.Current.CancellationToken); });
        Assert.Throws<ObjectDisposedException>(() => provider.GetAvailableResources());
    }

    [Fact]
    public async Task DiskStorageProviderOverridesEmbeddedAssemblyProviderInComposite()
    {
        writeDiskFile("Textures/hero.override", "disk-override-hero");

        var embedded = new AssemblyResourceProvider(
            typeof(StorageResourceProviderTests).Assembly,
            "kajarlabs.Framework.Tests.IO.Resources.Fixtures");

        var disk = new StorageResourceProvider(storage);

        using var composite = new CompositeResourceProvider(embedded, disk);
        composite.AddExtension(".txt");
        composite.AddExtension(".override");

        using var textures = new NamespacedResourceProvider(composite, "Textures", leaveOpen: true);

        // Disk hero.override must override embedded hero.txt (provider-first, extension-second).
        Assert.Equal("disk-override-hero", Encoding.UTF8.GetString(textures.GetBytes("hero")!));
        Assert.Equal(
            "disk-override-hero",
            Encoding.UTF8.GetString((await textures.GetBytesAsync("hero", TestContext.Current.CancellationToken))!));

        // Embedded-only resource still falls back cleanly to AssemblyResourceProvider.
        Assert.Equal("embedded-button-hover", Encoding.UTF8.GetString(composite.GetBytes("UI/button.hover")!));

        // Removing disk override restores embedded asset immediately.
        Assert.True(composite.RemoveProvider(disk));
        Assert.Equal("embedded-hero", Encoding.UTF8.GetString(textures.GetBytes("hero")!));
        disk.Dispose();
    }

    private void writeDiskFile(string relativePath, string content)
    {
        using var stream = storage.GetStream(relativePath, FileAccess.Write, FileMode.Create);
        var bytes = Encoding.UTF8.GetBytes(content);
        stream.Write(bytes, 0, bytes.Length);
    }

    private sealed class NonSeekableAndToctouStorage : Storage
    {
        private readonly Dictionary<string, byte[]> entries = new(StringComparer.Ordinal);

        public string? GhostFilePath { get; set; }

        public void Add(string path, byte[] data) => entries[path] = data;

        protected override bool ExistsCore(string relativePath)
            => entries.ContainsKey(relativePath) || string.Equals(relativePath, GhostFilePath, StringComparison.Ordinal);

        protected override bool ExistsDirectoryCore(string relativePath) => false;

        protected override IEnumerable<string> GetFilesCore(string relativePath) => [];

        protected override IEnumerable<string> GetDirectoriesCore(string relativePath) => [];

        protected override Stream GetStreamCore(string relativePath, FileAccess access, FileMode mode)
        {
            if (!entries.TryGetValue(relativePath, out var bytes))
                throw new FileNotFoundException("Simulated TOCTOU missing file.", relativePath);

            return new NonSeekableReadStream(bytes);
        }

        protected override void DeleteCore(string relativePath)
        {
        }

        protected override void DeleteDirectoryCore(string relativePath)
        {
        }

        protected override Storage GetStorageForDirectoryCore(string relativePath) => this;
    }

    private sealed class NonSeekableReadStream : Stream
    {
        private readonly byte[] bytes;
        private int position;

        public NonSeekableReadStream(byte[] bytes)
        {
            this.bytes = bytes;
        }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var remaining = bytes.Length - position;

            if (remaining <= 0)
                return 0;

            var toCopy = Math.Min(remaining, Math.Min(count, 2));
            Buffer.BlockCopy(bytes, position, buffer, offset, toCopy);
            position += toCopy;
            return toCopy;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
