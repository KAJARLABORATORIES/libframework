using System.Numerics;
using kajarlabs.Framework.Graphics;
using kajarlabs.Framework.Numerics;

namespace kajarlabs.Framework.Tests.Graphics;

public sealed class GraphicsMathTests
{
    [Fact]
    public void OrthographicProjectionMapsTopLeftScreenSpaceToSdlGpuClipSpace()
    {
        const float width = 1280f;
        const float height = 720f;

        var projection = GraphicsMath.CreateOrthographicProjection(width, height);

        var topLeft = Vector4.Transform(new Vector4(0f, 0f, 0f, 1f), projection);
        Assert.Equal(-1f, topLeft.X, precision: 5);
        Assert.Equal(1f, topLeft.Y, precision: 5);
        Assert.Equal(0f, topLeft.Z, precision: 5);
        Assert.Equal(1f, topLeft.W, precision: 5);

        var bottomRight = Vector4.Transform(new Vector4(width, height, 1f, 1f), projection);
        Assert.Equal(1f, bottomRight.X, precision: 5);
        Assert.Equal(-1f, bottomRight.Y, precision: 5);
        Assert.Equal(1f, bottomRight.Z, precision: 5);
        Assert.Equal(1f, bottomRight.W, precision: 5);

        var center = Vector4.Transform(new Vector4(width * 0.5f, height * 0.5f, 0.5f, 1f), projection);
        Assert.Equal(0f, center.X, precision: 5);
        Assert.Equal(0f, center.Y, precision: 5);
        Assert.Equal(0.5f, center.Z, precision: 5);
        Assert.Equal(1f, center.W, precision: 5);
    }

    [Fact]
    public void OrthographicProjectionFromViewportMatchesDimensionOverload()
    {
        var viewport = new Viewport(800f, 600f);

        Assert.Equal(
            GraphicsMath.CreateOrthographicProjection(800f, 600f),
            GraphicsMath.CreateOrthographicProjection(viewport));
    }

    [Fact]
    public void OrthographicProjectionRejectsInvalidDimensionsAndDegeneratePlanes()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => GraphicsMath.CreateOrthographicProjection(0f, 600f));
        Assert.Throws<ArgumentOutOfRangeException>(() => GraphicsMath.CreateOrthographicProjection(800f, -1f));
        Assert.Throws<ArgumentException>(() => GraphicsMath.CreateOrthographicProjectionOffCenter(10f, 10f, 100f, 0f));
        Assert.Throws<ArgumentException>(() => GraphicsMath.CreateOrthographicProjectionOffCenter(0f, 100f, 50f, 50f));
        Assert.Throws<ArgumentException>(() => GraphicsMath.CreateOrthographicProjectionOffCenter(0f, 100f, 100f, 0f, 1f, 1f));
    }

    [Fact]
    public void ViewportValidatesDimensionsAndPreservesEquality()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Viewport(0f, 0f, -1f, 100f));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Viewport(0f, 0f, 100f, -1f));

        var bounds = new RectangleI(10, 20, 1280, 720);
        var vp = new Viewport(bounds);

        Assert.Equal(10f, vp.X);
        Assert.Equal(20f, vp.Y);
        Assert.Equal(1280f, vp.Width);
        Assert.Equal(720f, vp.Height);
        Assert.Equal(0f, vp.MinDepth);
        Assert.Equal(1f, vp.MaxDepth);
        Assert.Equal(bounds, vp.Bounds);
        Assert.Equal(1280f / 720f, vp.AspectRatio, precision: 5);

        var zeroViewport = new Viewport(0f, 0f);
        Assert.Equal(0f, zeroViewport.AspectRatio);
        Assert.True(vp == new Viewport(10f, 20f, 1280f, 720f));
        Assert.Equal(vp.GetHashCode(), new Viewport(10f, 20f, 1280f, 720f).GetHashCode());
    }
}
