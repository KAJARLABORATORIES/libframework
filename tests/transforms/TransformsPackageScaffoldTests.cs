using System.Reflection;
using kajarlabs.Framework.Scene;
using kajarlabs.Framework.Scene.Transforms;

namespace kajarlabs.Framework.Scene.Transforms.Tests;

public sealed class TransformsPackageScaffoldTests
{
    [Fact]
    public void TransformsAssemblyIsAotCompatibleAndDoesNotDependOnSdlDirectly()
    {
        var assembly = typeof(TransformsModule).Assembly;
        var metadata = assembly.GetCustomAttributes<AssemblyMetadataAttribute>().ToArray();

        Assert.Equal("kajarlabs.Framework.Scene.Transforms", TransformsModule.PACKAGE_ID);
        Assert.Equal(typeof(IDrawableExtension), TransformsModule.SeamContractType);
        Assert.Contains(metadata, static attr => attr.Key == "IsAotCompatible" && attr.Value == "True");
        Assert.Contains(metadata, static attr => attr.Key == "IsTrimmable" && attr.Value == "True");

        var referencedAssemblies = assembly.GetReferencedAssemblies();
        Assert.Contains(referencedAssemblies, static name => name.Name == "kajarlabs.Framework");
        Assert.DoesNotContain(referencedAssemblies, static name => name.Name == "SDL3-CS");
    }

    [Fact]
    public void ExternalPackageCanDriveDrawableViaPublicExtensionAndLifetimeSeam()
    {
        using var container = new Container();
        var box = new Box();
        container.Add(box);

        var probe = box.GetOrAddExtension(static _ => new ExternalProbeExtension());
        container.UpdateSubTree();

        Assert.Equal(1, probe.Updates);
        Assert.False(probe.Disposed);

        box.Expire();
        container.UpdateSubTree();

        Assert.True(probe.Disposed);
        Assert.Empty(container.Children);
    }

    private sealed class ExternalProbeExtension : IDrawableExtension
    {
        public int Updates { get; private set; }

        public bool Disposed { get; private set; }

        public void OnUpdate(Drawable target) => Updates++;

        public void OnDispose(Drawable target) => Disposed = true;
    }
}
