using kajarlabs.Framework.IO;

namespace kajarlabs.Framework.Tests.IO;

public sealed class FileSystemStorageTests : IDisposable
{
    private readonly string temp = Path.Combine(Path.GetTempPath(), "kajarlabs-" + Guid.NewGuid().ToString("N"));
    private readonly string root;
    private readonly FileSystemStorage storage;

    public FileSystemStorageTests()
    {
        root = Path.Combine(temp, "root");
        storage = new FileSystemStorage(root);
    }

    public void Dispose()
    {
        if (Directory.Exists(temp))
        {
            Directory.Delete(temp, true);
        }
    }

    [Fact]
    public void WritingCreatesParentDirectoriesAndReadingReturnsTheContent()
    {
        using (var stream = storage.GetStream("a/b/file.txt", FileAccess.Write, FileMode.Create))
        using (var writer = new StreamWriter(stream))
        {
            writer.Write("hello");
        }

        Assert.Equal("hello", File.ReadAllText(Path.Combine(root, "a", "b", "file.txt")));

        using var read = new StreamReader(storage.GetStream("a/b/file.txt"));
        Assert.Equal("hello", read.ReadToEnd());
    }

    [Fact]
    public void ReadingAMissingFileThrowsAndDoesNotCreateIt()
    {
        // The root does not exist yet here, so this is a DirectoryNotFoundException; both derive from IOException.
        Assert.ThrowsAny<IOException>(() => storage.GetStream("missing.txt"));
        Assert.False(storage.Exists("missing.txt"));
        Assert.False(storage.ExistsDirectory(""));
    }

    [Fact]
    public void ExistsDistinguishesFilesFromDirectories()
    {
        Directory.CreateDirectory(Path.Combine(root, "dir"));
        File.WriteAllText(Path.Combine(root, "dir", "file.txt"), "x");

        Assert.True(storage.Exists("dir/file.txt"));
        Assert.False(storage.Exists("dir"));
        Assert.True(storage.ExistsDirectory("dir"));
        Assert.False(storage.ExistsDirectory("dir/file.txt"));
        Assert.False(storage.Exists("nope.txt"));
    }

    [Fact]
    public void ListingReturnsPathsRelativeToTheRootWithForwardSlashes()
    {
        Directory.CreateDirectory(Path.Combine(root, "dir", "sub"));
        File.WriteAllText(Path.Combine(root, "dir", "one.txt"), "1");
        File.WriteAllText(Path.Combine(root, "dir", "sub", "two.txt"), "2");

        Assert.Equal(["dir/one.txt"], storage.GetFiles("dir"));
        Assert.Equal(["dir/sub"], storage.GetDirectories("dir"));
        Assert.Equal(["dir"], storage.GetDirectories(""));
    }

    [Fact]
    public void ListingAMissingDirectoryReturnsNothing()
    {
        Assert.Empty(storage.GetFiles("missing"));
        Assert.Empty(storage.GetDirectories("missing"));
    }

    [Fact]
    public void DeleteRemovesFilesAndIgnoresMissingOnes()
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "file.txt"), "x");

        storage.Delete("file.txt");
        storage.Delete("file.txt");
        storage.Delete("missing/file.txt");

        Assert.False(storage.Exists("file.txt"));
    }

    [Fact]
    public void DeleteDirectoryIsRecursiveAndRefusesTheRoot()
    {
        Directory.CreateDirectory(Path.Combine(root, "dir", "sub"));
        File.WriteAllText(Path.Combine(root, "dir", "sub", "file.txt"), "x");

        storage.DeleteDirectory("dir");
        storage.DeleteDirectory("dir");

        Assert.False(storage.ExistsDirectory("dir"));
        Assert.Throws<ArgumentException>(() => storage.DeleteDirectory(""));
        Assert.True(Directory.Exists(root));
    }

    [Fact]
    public void GetFullPathIsUnderTheRoot()
    {
        Assert.Equal(Path.Combine(Path.GetFullPath(root), "a", "b.txt"), storage.GetFullPath("a/b.txt"));
        Assert.Equal(Path.GetFullPath(root), storage.GetFullPath(""));
    }

    [Theory]
    [InlineData("../outside.txt")]
    [InlineData("a/../../outside.txt")]
    [InlineData("..\\outside.txt")]
    [InlineData("/outside.txt")]
    public void EscapingPathsThrowAndNeverTouchFilesOutsideTheRoot(string path)
    {
        var outside = Path.Combine(temp, "outside.txt");
        Directory.CreateDirectory(temp);
        File.WriteAllText(outside, "untouched");

        Assert.Throws<ArgumentException>(() => storage.Exists(path));
        Assert.Throws<ArgumentException>(() => storage.GetStream(path, FileAccess.Write, FileMode.Create));
        Assert.Throws<ArgumentException>(() => storage.Delete(path));

        Assert.Equal("untouched", File.ReadAllText(outside));
    }

    [Fact]
    public void AbsolutePathPointingInsideTheRootIsStillRejected()
    {
        var absolute = Path.Combine(Path.GetFullPath(root), "file.txt");

        Assert.Throws<ArgumentException>(() => storage.Exists(absolute));
    }

    [Fact]
    public void SubStorageIsRootedAtTheDirectoryAndEnforcesItsOwnContainment()
    {
        Directory.CreateDirectory(Path.Combine(root, "a"));
        File.WriteAllText(Path.Combine(root, "a", "inside.txt"), "x");
        File.WriteAllText(Path.Combine(root, "sibling.txt"), "x");

        var sub = storage.GetStorageForDirectory("a");

        Assert.True(sub.Exists("inside.txt"));
        Assert.False(sub.Exists("sibling.txt"));
        Assert.Throws<ArgumentException>(() => sub.Exists("../sibling.txt"));
        Assert.Equal(Path.Combine(Path.GetFullPath(root), "a", "inside.txt"), sub.GetFullPath("inside.txt"));
    }

    [Fact]
    public void ConstructorRejectsAnEmptyRoot()
    {
        Assert.Throws<ArgumentException>(() => new FileSystemStorage(string.Empty));
        Assert.Throws<ArgumentNullException>(() => new FileSystemStorage(null!));
    }
}
