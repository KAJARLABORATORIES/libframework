using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using kajarlabs.Framework.Graphics.Rendering.Vertices;
using kajarlabs.Framework.Numerics;

namespace kajarlabs.Framework.Tests.Graphics.Rendering.Vertices;

public sealed class TexturedVertex2DTests
{
    [Fact]
    public void MemoryLayoutIsSequentialThirtyTwoBytesWithoutReferences()
    {
        Assert.Equal(32, Unsafe.SizeOf<TexturedVertex2D>());
        Assert.Equal(0, Marshal.OffsetOf<TexturedVertex2D>(nameof(TexturedVertex2D.Position)));
        Assert.Equal(8, Marshal.OffsetOf<TexturedVertex2D>(nameof(TexturedVertex2D.TexCoord)));
        Assert.Equal(16, Marshal.OffsetOf<TexturedVertex2D>(nameof(TexturedVertex2D.Color)));
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<TexturedVertex2D>());
    }

    [Fact]
    public void ConstructorsAndEqualityBehaveCorrectly()
    {
        var a = new TexturedVertex2D(new Vector2(10f, 20f), new Vector2(0.5f, 0.25f), Color4.RED);
        var b = new TexturedVertex2D(10f, 20f, 0.5f, 0.25f, new Color4(1f, 0f, 0f, 1f));
        var c = new TexturedVertex2D(10f, 20f, 0.5f, 0.25f, Color4.BLUE);

        Assert.True(a == b);
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.True(a != c);
    }
}
