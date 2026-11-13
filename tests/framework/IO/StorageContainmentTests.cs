using kajarlabs.Framework.IO;

namespace kajarlabs.Framework.Tests.IO;

public class StorageContainmentTests
{
    private static readonly (string Name, Action<Storage, string> Call)[] operations =
    [
        ("GetFullPath", (s, p) => s.GetFullPath(p)),
        ("Exists", (s, p) => s.Exists(p)),
        ("ExistsDirectory", (s, p) => s.ExistsDirectory(p)),
        ("GetFiles", (s, p) => s.GetFiles(p)),
        ("GetDirectories", (s, p) => s.GetDirectories(p)),
        ("GetStream", (s, p) => s.GetStream(p).Dispose()),
        ("Delete", (s, p) => s.Delete(p)),
        ("DeleteDirectory", (s, p) => s.DeleteDirectory(p)),
        ("GetStorageForDirectory", (s, p) => s.GetStorageForDirectory(p)),
    ];

    [Theory]
    [InlineData("..")]
    [InlineData("../x")]
    [InlineData("a/../../x")]
    [InlineData("a\\..\\..\\x")]
    [InlineData("a/b/../../..")]
    [InlineData("/etc/passwd")]
    [InlineData("\\windows")]
    [InlineData("C:\\Windows")]
    [InlineData("C:x")]
    [InlineData("a/b:stream")]
    public void PathOutsideRootThrowsForEveryMember(string path)
    {
        var storage = new InMemoryStorage();

        foreach (var (name, call) in operations)
        {
            var exception = Record.Exception(() => call(storage, path));

            Assert.True(
                exception?.GetType() == typeof(ArgumentException),
                $"{name}(\"{path}\") threw {exception?.GetType().Name ?? "nothing"} instead of ArgumentException.");
        }
    }

    [Fact]
    public void NullPathThrowsForEveryMember()
    {
        var storage = new InMemoryStorage();

        foreach (var (name, call) in operations)
        {
            var exception = Record.Exception(() => call(storage, null!));

            Assert.True(exception is ArgumentNullException, $"{name}(null) threw {exception?.GetType().Name ?? "nothing"}.");
        }
    }

    [Theory]
    [InlineData("a/../b", "b")]
    [InlineData("a//b", "a/b")]
    [InlineData("a\\b", "a/b")]
    [InlineData("./a/./b", "a/b")]
    [InlineData("a/b/..", "a")]
    [InlineData(".", "")]
    [InlineData("", "")]
    public void PathInsideRootIsNormalizedBeforeReachingTheSubclass(string input, string expected)
    {
        var storage = new InMemoryStorage();

        storage.Exists(input);

        Assert.Equal(expected, storage.LastRelativePath);
    }

    [Fact]
    public void SubStorageEnforcesContainmentRelativeToItsOwnRoot()
    {
        var root = new InMemoryStorage();
        var sub = root.GetStorageForDirectory("a/b");

        // Still inside the parent's root, but outside the sub-storage's root.
        Assert.Throws<ArgumentException>(() => sub.Exists("../x"));
        Assert.Throws<ArgumentException>(() => sub.GetStream("../../a/b/file.txt"));
        Assert.Throws<ArgumentException>(() => sub.GetStorageForDirectory(".."));

        var nested = sub.GetStorageForDirectory("c");
        Assert.Throws<ArgumentException>(() => nested.Exists("../x"));
    }

    [Fact]
    public void SubStorageOnlySeesItsOwnSubtree()
    {
        var root = new InMemoryStorage();
        write(root, "a/inside.txt", "inside");
        write(root, "outside.txt", "outside");

        var sub = root.GetStorageForDirectory("a");

        Assert.True(sub.Exists("inside.txt"));
        Assert.False(sub.Exists("outside.txt"));
        Assert.Equal(["inside.txt"], sub.GetFiles(""));
    }

    [Fact]
    public void DeleteDirectoryRefusesToDeleteTheRoot()
    {
        var storage = new InMemoryStorage();
        write(storage, "a.txt", "a");

        Assert.Throws<ArgumentException>(() => storage.DeleteDirectory(""));
        Assert.Throws<ArgumentException>(() => storage.DeleteDirectory("a/.."));
        Assert.True(storage.Exists("a.txt"));
    }

    [Fact]
    public void CustomSubclassCanReadWriteListAndDelete()
    {
        var storage = new InMemoryStorage();
        write(storage, "dir/one.txt", "1");
        write(storage, "dir/nested/two.txt", "2");

        Assert.Equal("1", read(storage, "dir/one.txt"));
        Assert.True(storage.ExistsDirectory("dir/nested"));
        Assert.Equal(["dir/one.txt"], storage.GetFiles("dir"));
        Assert.Equal(["dir/nested"], storage.GetDirectories("dir"));

        storage.DeleteDirectory("dir");

        Assert.False(storage.Exists("dir/one.txt"));
        Assert.False(storage.ExistsDirectory("dir"));
    }

    [Fact]
    public void GetFullPathIsNotSupportedByDefault()
    {
        var storage = new InMemoryStorage();

        Assert.Throws<NotSupportedException>(() => storage.GetFullPath("a.txt"));
    }

    private static void write(Storage storage, string path, string content)
    {
        using var stream = storage.GetStream(path, FileAccess.Write, FileMode.Create);
        using var writer = new StreamWriter(stream);
        writer.Write(content);
    }

    private static string read(Storage storage, string path)
    {
        using var stream = storage.GetStream(path);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
