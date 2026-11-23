using System.Numerics;
using kajarlabs.Framework.Graphics;
using kajarlabs.Framework.Graphics.Rendering;
using kajarlabs.Framework.Numerics;
using kajarlabs.Framework.Scene;

namespace kajarlabs.Framework.Tests.Scene;

public sealed class DrawNodeLifecycleTests
{
    [Fact]
    public void DrawNodeSlotsSyncByInvalidationIdAndSkipUnchangedFrames()
    {
        var leaf = new ProbeVisualDrawable
        {
            Position = new Vector2(10f, 20f),
            Size = new Vector2(40f, 30f),
            Color = Color4.RED,
            Blending = BlendingParameters.ADDITIVE,
        };

        // First generation into slot 0 applies state once.
        var node0 = leaf.GenerateDrawNodeSubtree(0) as ProbeDrawNode;
        Assert.NotNull(node0);
        Assert.Equal(1, node0.ApplyCount);
        Assert.Equal(Color4.RED, node0.DrawColorInfo.Color);
        Assert.Equal(BlendingParameters.ADDITIVE, node0.Blending);

        // Second generation into slot 0 without invalidation skips ApplyState completely.
        var node0Again = leaf.GenerateDrawNodeSubtree(0) as ProbeDrawNode;
        Assert.Same(node0, node0Again);
        Assert.Equal(1, node0.ApplyCount);

        // Slot 1 lazily allocates its own DrawNode and syncs independently.
        var node1 = leaf.GenerateDrawNodeSubtree(1) as ProbeDrawNode;
        Assert.NotNull(node1);
        Assert.NotSame(node0, node1);
        Assert.Equal(1, node1.ApplyCount);

        // Mutating drawable invalidates slot 0 and slot 1 on their next respective syncs.
        leaf.Color = Color4.GREEN;
        _ = leaf.GenerateDrawNodeSubtree(0);
        Assert.Equal(2, node0.ApplyCount);
        Assert.Equal(Color4.GREEN, node0.DrawColorInfo.Color);
        Assert.Equal(1, node1.ApplyCount);
        Assert.Equal(Color4.RED, node1.DrawColorInfo.Color);

        // Slot 1 catches up on its next sync.
        _ = leaf.GenerateDrawNodeSubtree(1);
        Assert.Equal(2, node1.ApplyCount);
        Assert.Equal(Color4.GREEN, node1.DrawColorInfo.Color);
    }

    [Fact]
    public void CompositeSubtreeSkipsUnchangedChildrenAndOmitsNonPresentDrawables()
    {
        var root = new Container { Size = new Vector2(500f, 500f) };
        var branchA = new Container { Size = new Vector2(200f, 200f) };
        var leafA = new ProbeVisualDrawable { Size = new Vector2(50f, 50f) };
        var branchB = new Container { Size = new Vector2(200f, 200f) };
        var leafB = new ProbeVisualDrawable { Size = new Vector2(60f, 60f) };

        branchA.Add(leafA);
        branchB.Add(leafB);
        root.AddRange([branchA, branchB]);

        var rootNode = root.GenerateDrawNodeSubtree(0) as CompositeDrawNode;
        Assert.NotNull(rootNode);
        Assert.Equal(2, rootNode.Children.Count);

        var branchANode = Assert.IsType<CompositeDrawNode>(rootNode.Children[0]);
        var branchBNode = Assert.IsType<CompositeDrawNode>(rootNode.Children[1]);
        var leafANode = Assert.IsType<ProbeDrawNode>(branchANode.Children[0]);
        var leafBNode = Assert.IsType<ProbeDrawNode>(branchBNode.Children[0]);

        var listBefore = rootNode.Children;

        // Mutating only leafA updates leafA/branchA/root without touching branchB/leafB.
        leafA.Position = new Vector2(25f, 15f);
        var rootNode2 = root.GenerateDrawNodeSubtree(0) as CompositeDrawNode;
        Assert.Same(rootNode, rootNode2);
        Assert.Same(listBefore, rootNode2!.Children);
        Assert.Equal(2, leafANode.ApplyCount);
        Assert.Equal(1, leafBNode.ApplyCount);

        // Hiding leafB removes it from CompositeDrawNode.Children on next sync; restoring brings it back.
        leafB.Alpha = 0f;
        _ = root.GenerateDrawNodeSubtree(0);
        Assert.Empty(branchBNode.Children);

        leafB.Alpha = 1f;
        _ = root.GenerateDrawNodeSubtree(0);
        Assert.Single(branchBNode.Children);
    }

    [Fact]
    public void CompositeDrawNodeAppliesAndRestoresScissorAndBlendingInOrder()
    {
        var root = new Container { Size = new Vector2(800f, 600f) };

        var outerMasked = new Container
        {
            Position = new Vector2(10f, 10f),
            Size = new Vector2(200f, 200f),
            Masking = true,
        };

        var innerMasked = new Container
        {
            Position = new Vector2(50f, 50f),
            Size = new Vector2(300f, 300f),
            Masking = true,
        };

        var innerLeaf = new ProbeVisualDrawable
        {
            Size = new Vector2(40f, 40f),
            Blending = BlendingParameters.ADDITIVE,
        };

        var afterInnerSibling = new ProbeVisualDrawable
        {
            Size = new Vector2(20f, 20f),
            Blending = BlendingParameters.NONE,
        };

        innerMasked.Add(innerLeaf);
        outerMasked.AddRange([innerMasked, afterInnerSibling]);
        root.Add(outerMasked);

        var rootNode = Assert.IsType<CompositeDrawNode>(root.GenerateDrawNodeSubtree(0));
        var recordingRenderer = new RecordingRenderer();

        rootNode.Draw(recordingRenderer);

        Assert.Equal(
            [
                "Scissor:(10, 10, 200, 200)",
                "Scissor:(60, 60, 150, 150)",
                "Blending:True",
                "DrawQuad:(60, 60, 100, 100)",
                "Scissor:(10, 10, 200, 200)",
                "Blending:False",
                "DrawQuad:(10, 10, 30, 30)",
                "Scissor:null",
            ],
            recordingRenderer.Operations);
    }

    private sealed class ProbeVisualDrawable : Drawable
    {
        protected override DrawNode CreateDrawNode() => new ProbeDrawNode(this);
    }

    private sealed class ProbeDrawNode : DrawNode
    {
        public ProbeDrawNode(Drawable source)
            : base(source)
        {
        }

        public int ApplyCount { get; private set; }

        public override void ApplyState()
        {
            base.ApplyState();
            ApplyCount++;
        }

        public override void Draw(IRenderer renderer)
        {
            ApplyBlending(renderer);
            renderer.DrawQuad(renderer.WhitePixel, ScreenSpaceDrawQuad, DrawColorInfo.Color);
        }
    }

    private sealed class RecordingRenderer : IRenderer
    {
        public List<string> Operations { get; } = [];

        public Viewport Viewport => new(800, 600);

        public ITexture WhitePixel { get; } = new StubTexture();

        public int LastFrameDrawCallCount => 0;

        public int LastFrameQuadCount => 0;

        public ITexture CreateTexture(int width, int height) => new StubTexture();

        public void Clear(Color4 color) => Operations.Add($"Clear:{color}");

        public void SetBlending(BlendingParameters blending) => Operations.Add($"Blending:{blending.IsEnabled}");

        public void SetSampler(TextureSamplerParameters sampler)
        {
        }

        public void SetScissor(RectangleI? scissor) => Operations.Add($"Scissor:{scissor?.ToString() ?? "null"}");

        public void SetProjection(Matrix4x4? projection)
        {
        }

        public void DrawQuad(ITexture texture, RectangleF destination, Color4 color)
            => DrawQuad(texture, Quad.FromRectangle(destination), color);

        public void DrawQuad(ITexture texture, RectangleF destination, RectangleF textureCoords, Color4 color)
            => DrawQuad(texture, Quad.FromRectangle(destination), color);

        public void DrawQuad(ITexture texture, Quad destination, Color4 color)
        {
            var aabb = destination.AABB;
            Operations.Add($"DrawQuad:({aabb.Left:0}, {aabb.Top:0}, {aabb.Right:0}, {aabb.Bottom:0})");
        }

        public void DrawQuad(ITexture texture, Quad destination, RectangleF textureCoords, Color4 color)
            => DrawQuad(texture, destination, color);

        private sealed class StubTexture : ITexture
        {
            public int Width => 1;
            public int Height => 1;

            public void SetData(ReadOnlySpan<byte> rgba8Pixels)
            {
            }

            public void SetData(RectangleI region, ReadOnlySpan<byte> rgba8Pixels)
            {
            }

            public void Dispose()
            {
            }
        }
    }
}
