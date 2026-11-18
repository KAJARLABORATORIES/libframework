using System.Numerics;
using kajarlabs.Framework.DependencyInjection;
using kajarlabs.Framework.Input;
using kajarlabs.Framework.Scene;

namespace kajarlabs.Framework.Tests.Scene;

public sealed class DrawableInputTraversalTests
{
    [Fact]
    public void DefaultInputAndFocusFlagsFollowContract()
    {
        using var plain = new Drawable();
        Assert.False(plain.AcceptsFocus);
        Assert.False(plain.RequestsFocus);
        Assert.False(plain.RequestsTextInput);
        Assert.False(plain.HasFocus);
        Assert.False(plain.IsHovered);
        Assert.False(plain.IsDragged);
        Assert.False(plain.HandlePositionalInput);
        Assert.False(plain.HandleNonPositionalInput);

        using var focusable = new InputProbeDrawable { AcceptsFocusFlag = true };
        Assert.True(focusable.AcceptsFocus);
        Assert.True(focusable.HandlePositionalInput);
        Assert.False(focusable.HandleNonPositionalInput);

        focusable.HasFocus = true;
        focusable.IsHovered = true;
        focusable.IsDragged = true;
        focusable.Dispose();

        Assert.False(focusable.HasFocus);
        Assert.False(focusable.IsHovered);
        Assert.False(focusable.IsDragged);
    }

    [Fact]
    public void PositionalHitTestingRespectsTransformsAndCustomOverrides()
    {
        using var root = new Container { Size = new Vector2(400f, 300f) };
        using var transformed = new InputProbeDrawable
        {
            HandlePositional = true,
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            Position = new Vector2(20f, -10f),
            Size = new Vector2(60f, 30f),
            Scale = new Vector2(2f, 1.5f),
            Rotation = 45f,
            Shear = new Vector2(0.25f, 0f),
        };
        using var zeroScale = new InputProbeDrawable
        {
            HandlePositional = true,
            Size = new Vector2(50f, 50f),
            Scale = Vector2.Zero,
        };
        using var circular = new CircularDrawable
        {
            Position = new Vector2(300f, 200f),
            Size = new Vector2(40f, 40f),
        };

        root.AddRange([transformed, zeroScale, circular]);

        var insideTransformed = transformed.ToScreenSpace(new Vector2(30f, 15f));
        var cornerTransformed = transformed.ToScreenSpace(new Vector2(59f, 29f));
        var outsideTransformed = transformed.ToScreenSpace(new Vector2(61f, 15f));

        var queue = new List<Drawable>();
        root.BuildPositionalInputQueue(insideTransformed, queue);
        Assert.Equal([transformed], queue);

        queue.Clear();
        root.BuildPositionalInputQueue(cornerTransformed, queue);
        Assert.Equal([transformed], queue);

        queue.Clear();
        root.BuildPositionalInputQueue(outsideTransformed, queue);
        Assert.Empty(queue);

        Assert.False(zeroScale.ReceivePositionalInputAt(Vector2.Zero));

        // Circular override rejects corners inside the bounding box.
        var circleCenter = circular.ToScreenSpace(new Vector2(20f, 20f));
        var circleCorner = circular.ToScreenSpace(new Vector2(2f, 2f));

        queue.Clear();
        root.BuildPositionalInputQueue(circleCenter, queue);
        Assert.Equal([circular], queue);

        queue.Clear();
        root.BuildPositionalInputQueue(circleCorner, queue);
        Assert.Empty(queue);
    }

    [Fact]
    public void BuildsPositionalAndNonPositionalQueuesFrontToBackByDepthAndHierarchy()
    {
        using var root = new InteractiveContainer { Size = new Vector2(200f, 200f), HandlePositional = true, HandleNonPositional = true };

        using var backContainer = new InteractiveContainer
        {
            Size = new Vector2(200f, 200f),
            Depth = 10f,
            HandlePositional = true,
            HandleNonPositional = true,
        };
        using var backChild = new InputProbeDrawable
        {
            Size = new Vector2(200f, 200f),
            HandlePositional = true,
            HandleNonPositional = true,
        };
        backContainer.Add(backChild);

        using var midFirstAdded = new InputProbeDrawable
        {
            Size = new Vector2(200f, 200f),
            Depth = 0f,
            HandlePositional = true,
            HandleNonPositional = true,
        };
        using var midSecondAdded = new InputProbeDrawable
        {
            Size = new Vector2(200f, 200f),
            Depth = 0f,
            HandlePositional = true,
            HandleNonPositional = true,
        };

        using var frontContainer = new InteractiveContainer
        {
            Size = new Vector2(200f, 200f),
            Depth = -5f,
            HandlePositional = true,
            HandleNonPositional = true,
        };
        using var frontChildBack = new InputProbeDrawable
        {
            Size = new Vector2(200f, 200f),
            Depth = 2f,
            HandlePositional = true,
            HandleNonPositional = true,
        };
        using var frontChildFront = new InputProbeDrawable
        {
            Size = new Vector2(200f, 200f),
            Depth = -1f,
            HandlePositional = true,
            HandleNonPositional = true,
        };
        frontContainer.AddRange([frontChildBack, frontChildFront]);

        root.AddRange([backContainer, midFirstAdded, midSecondAdded, frontContainer]);

        Drawable[] expectedOrder =
        [
            frontChildFront,
            frontChildBack,
            frontContainer,
            midSecondAdded,
            midFirstAdded,
            backChild,
            backContainer,
            root,
        ];

        var positionalQueue = new List<Drawable>();
        root.BuildPositionalInputQueue(new Vector2(50f, 50f), positionalQueue);
        Assert.Equal(expectedOrder, positionalQueue);

        var nonPositionalQueue = new List<Drawable>();
        root.BuildNonPositionalInputQueue(nonPositionalQueue);
        Assert.Equal(expectedOrder, nonPositionalQueue);

        // Dynamic depth change moves backContainer in front of frontContainer.
        backContainer.Depth = -20f;
        positionalQueue.Clear();
        root.BuildPositionalInputQueue(new Vector2(50f, 50f), positionalQueue);
        Assert.Same(backChild, positionalQueue[0]);
        Assert.Same(backContainer, positionalQueue[1]);
    }

    [Fact]
    public async Task SkipsNotPresentDisposedAndLoadingSubtrees()
    {
        using var root = new Container { Size = new Vector2(200f, 200f) };

        using var hiddenContainer = new InteractiveContainer
        {
            Size = new Vector2(200f, 200f),
            Alpha = 0f,
            HandlePositional = true,
            HandleNonPositional = true,
        };
        using var hiddenChild = new InputProbeDrawable
        {
            Size = new Vector2(200f, 200f),
            HandlePositional = true,
            HandleNonPositional = true,
        };
        hiddenContainer.Add(hiddenChild);

        using var alwaysPresentHidden = new InputProbeDrawable
        {
            Size = new Vector2(200f, 200f),
            Alpha = 0f,
            AlwaysPresent = true,
            HandlePositional = true,
            HandleNonPositional = true,
        };

        using var disposedLeaf = new InputProbeDrawable
        {
            Size = new Vector2(200f, 200f),
            HandlePositional = true,
            HandleNonPositional = true,
        };

        using var loadingGate = new ManualResetEventSlim(false);
        using var loadingStarted = new ManualResetEventSlim(false);
        using var loadingLeaf = new BlockingLoadDrawable(loadingStarted, loadingGate)
        {
            Size = new Vector2(200f, 200f),
        };

        root.AddRange([hiddenContainer, alwaysPresentHidden, disposedLeaf, loadingLeaf]);

        var loadTask = Task.Run(() => loadingLeaf.Load(DependencyCollection.EMPTY), TestContext.Current.CancellationToken);
        Assert.True(loadingStarted.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Assert.Equal(LoadState.Loading, loadingLeaf.LoadState);

        disposedLeaf.Dispose();

        var positional = new List<Drawable>();
        root.BuildPositionalInputQueue(new Vector2(50f, 50f), positional);
        Assert.Equal([alwaysPresentHidden], positional);

        var nonPositional = new List<Drawable>();
        root.BuildNonPositionalInputQueue(nonPositional);
        Assert.Equal([alwaysPresentHidden], nonPositional);

        loadingGate.Set();
        await loadTask;

        // Once Ready/Loaded, loadingLeaf becomes eligible.
        positional.Clear();
        root.BuildPositionalInputQueue(new Vector2(50f, 50f), positional);
        Assert.Equal([loadingLeaf, alwaysPresentHidden], positional);
    }

    [Fact]
    public void PositionalTraversalEnforcesMaskingAndNestedRotatedMasking()
    {
        using var root = new Container { Size = new Vector2(400f, 400f) };

        using var outerMasked = new InteractiveContainer
        {
            Position = new Vector2(50f, 50f),
            Size = new Vector2(100f, 100f),
            Masking = true,
            HandlePositional = true,
        };
        using var innerMasked = new InteractiveContainer
        {
            Position = new Vector2(50f, 0f),
            Size = new Vector2(100f, 100f),
            Masking = true,
            HandlePositional = true,
        };
        using var extendingChild = new InputProbeDrawable
        {
            Size = new Vector2(100f, 100f),
            HandlePositional = true,
            HandleNonPositional = true,
        };
        innerMasked.Add(extendingChild);
        outerMasked.Add(innerMasked);

        using var rotatedMasked = new InteractiveContainer
        {
            Position = new Vector2(280f, 200f),
            Size = new Vector2(100f, 40f),
            Origin = Anchor.Centre,
            Rotation = 45f,
            Masking = true,
            HandlePositional = true,
        };
        using var rotatedOverflowChild = new InputProbeDrawable
        {
            Position = new Vector2(-40f, -40f),
            Size = new Vector2(200f, 120f),
            HandlePositional = true,
        };
        rotatedMasked.Add(rotatedOverflowChild);

        root.AddRange([outerMasked, rotatedMasked]);

        var queue = new List<Drawable>();

        // (125, 100) is inside outerMasked [50..150, 50..150] AND inside innerMasked [100..200, 50..150].
        root.BuildPositionalInputQueue(new Vector2(125f, 100f), queue);
        Assert.Equal([extendingChild, innerMasked, outerMasked], queue);

        // (175, 100) is inside innerMasked & extendingChild, but clipped by outerMasked!
        queue.Clear();
        root.BuildPositionalInputQueue(new Vector2(175f, 100f), queue);
        Assert.Empty(queue);

        // Disabling outer Masking allows innerMasked (still masked to [100..200]) to receive input at (175, 100).
        outerMasked.Masking = false;
        queue.Clear();
        root.BuildPositionalInputQueue(new Vector2(175f, 100f), queue);
        Assert.Equal([extendingChild, innerMasked], queue);

        // Rotated masked container allows hits inside rotated bounds and prunes overflow outside rotated bounds.
        var insideRotated = rotatedMasked.ToScreenSpace(new Vector2(50f, 20f));
        var outsideRotated = rotatedMasked.ToScreenSpace(new Vector2(50f, -10f));

        queue.Clear();
        root.BuildPositionalInputQueue(insideRotated, queue);
        Assert.Equal([rotatedOverflowChild, rotatedMasked], queue);

        queue.Clear();
        root.BuildPositionalInputQueue(outsideRotated, queue);
        Assert.Empty(queue);

        // Non-positional traversal ignores masking bounds completely.
        queue.Clear();
        root.BuildNonPositionalInputQueue(queue);
        Assert.Equal([extendingChild], queue);
    }

    [Fact]
    public void TriggerEventAssignsTargetTransformsLocalCoordinatesAndPropagatesConsumption()
    {
        using var target = new RecordingInputDrawable
        {
            Position = new Vector2(100f, 50f),
            Size = new Vector2(80f, 40f),
            Scale = new Vector2(2f, 2f),
            ConsumeEvents = true,
        };

        var screenPos = target.ToScreenSpace(new Vector2(10f, 5f));
        var downScreenPos = target.ToScreenSpace(new Vector2(4f, 2f));
        var state = InputState.EMPTY
            .WithMouse(MouseState.EMPTY.WithPosition(screenPos).WithButtonDown(MouseButton.Left).WithScrollDelta(new Vector2(0f, 1f)))
            .WithKeyboard(KeyboardState.EMPTY.WithKeyDown(Key.A))
            .WithWindowHasFocus(true);

        var mouseDown = new MouseDownEvent(state, MouseButton.Left);
        Assert.True(target.TriggerEvent(mouseDown));
        Assert.Same(target, mouseDown.Target);
        Assert.Equal(new Vector2(10f, 5f), mouseDown.MousePosition);

        var hover = new HoverEvent(state);
        Assert.True(target.TriggerEvent(hover));
        var hoverLost = new HoverLostEvent(state);
        Assert.False(target.TriggerEvent(hoverLost));

        Assert.True(target.TriggerEvent(new MouseMoveEvent(state)));
        Assert.True(target.TriggerEvent(new MouseUpEvent(state, MouseButton.Left, downScreenPos)));
        Assert.True(target.TriggerEvent(new ClickEvent(state, MouseButton.Left, downScreenPos)));
        Assert.True(target.TriggerEvent(new DoubleClickEvent(state, MouseButton.Left, downScreenPos)));
        Assert.True(target.TriggerEvent(new DragStartEvent(state, MouseButton.Left, downScreenPos)));
        Assert.True(target.TriggerEvent(new DragEvent(state, MouseButton.Left, downScreenPos)));
        Assert.False(target.TriggerEvent(new DragEndEvent(state, MouseButton.Left, downScreenPos)));
        Assert.True(target.TriggerEvent(new ScrollEvent(state)));
        Assert.True(target.TriggerEvent(new KeyDownEvent(state, Key.A)));
        Assert.True(target.TriggerEvent(new KeyUpEvent(state, Key.A)));
        Assert.True(target.TriggerEvent(new TextInputEvent(state, "a")));
        Assert.False(target.TriggerEvent(new FocusEvent(state)));
        Assert.False(target.TriggerEvent(new FocusLostEvent(state)));

        Assert.Equal(
            [
                "OnMouseDown",
                "OnHover",
                "OnHoverLost",
                "OnMouseMove",
                "OnMouseUp",
                "OnClick",
                "OnDoubleClick",
                "OnDragStart",
                "OnDrag",
                "OnDragEnd",
                "OnScroll",
                "OnKeyDown",
                "OnKeyUp",
                "OnTextInput",
                "OnFocus",
                "OnFocusLost",
            ],
            target.InvokedHandlers);

        target.ConsumeEvents = false;
        Assert.False(target.TriggerEvent(new MouseDownEvent(state, MouseButton.Left)));
    }

    private sealed class InputProbeDrawable : Drawable
    {
        public bool AcceptsFocusFlag { get; init; }

        public bool HandlePositional { get; init; }

        public bool HandleNonPositional { get; init; }

        public override bool AcceptsFocus => AcceptsFocusFlag;

        public override bool HandlePositionalInput => HandlePositional || base.HandlePositionalInput;

        public override bool HandleNonPositionalInput => HandleNonPositional;
    }

    private sealed class InteractiveContainer : Container
    {
        public bool HandlePositional { get; init; }

        public bool HandleNonPositional { get; init; }

        public override bool HandlePositionalInput => HandlePositional;

        public override bool HandleNonPositionalInput => HandleNonPositional;
    }

    private sealed class CircularDrawable : Drawable
    {
        public override bool HandlePositionalInput => true;

        public override bool ReceivePositionalInputAt(Vector2 screenSpacePosition)
        {
            if (!TryToLocalSpace(screenSpacePosition, out var local))
                return false;

            var center = DrawSize * 0.5f;
            var radius = MathF.Min(DrawSize.X, DrawSize.Y) * 0.5f;
            return Vector2.DistanceSquared(local, center) <= radius * radius;
        }
    }

    private sealed class BlockingLoadDrawable(ManualResetEventSlim started, ManualResetEventSlim release) : Drawable
    {
        public override bool HandlePositionalInput => true;

        public override bool HandleNonPositionalInput => true;

        protected override void Load()
        {
            started.Set();
            release.Wait();
        }
    }

    private sealed class RecordingInputDrawable : Drawable
    {
        public bool ConsumeEvents { get; set; }

        public List<string> InvokedHandlers { get; } = [];

        public override bool AcceptsFocus => true;

        public override bool HandleNonPositionalInput => true;

        protected override bool OnHover(HoverEvent e)
        {
            InvokedHandlers.Add(nameof(OnHover));
            return ConsumeEvents;
        }

        protected override void OnHoverLost(HoverLostEvent e)
            => InvokedHandlers.Add(nameof(OnHoverLost));

        protected override bool OnMouseMove(MouseMoveEvent e)
        {
            InvokedHandlers.Add(nameof(OnMouseMove));
            return ConsumeEvents;
        }

        protected override bool OnMouseDown(MouseDownEvent e)
        {
            InvokedHandlers.Add(nameof(OnMouseDown));
            return ConsumeEvents;
        }

        protected override bool OnMouseUp(MouseUpEvent e)
        {
            InvokedHandlers.Add(nameof(OnMouseUp));
            return ConsumeEvents;
        }

        protected override bool OnClick(ClickEvent e)
        {
            InvokedHandlers.Add(nameof(OnClick));
            return ConsumeEvents;
        }

        protected override bool OnDoubleClick(DoubleClickEvent e)
        {
            InvokedHandlers.Add(nameof(OnDoubleClick));
            return ConsumeEvents;
        }

        protected override bool OnDragStart(DragStartEvent e)
        {
            InvokedHandlers.Add(nameof(OnDragStart));
            return ConsumeEvents;
        }

        protected override bool OnDrag(DragEvent e)
        {
            InvokedHandlers.Add(nameof(OnDrag));
            return ConsumeEvents;
        }

        protected override void OnDragEnd(DragEndEvent e)
            => InvokedHandlers.Add(nameof(OnDragEnd));

        protected override bool OnScroll(ScrollEvent e)
        {
            InvokedHandlers.Add(nameof(OnScroll));
            return ConsumeEvents;
        }

        protected override bool OnKeyDown(KeyDownEvent e)
        {
            InvokedHandlers.Add(nameof(OnKeyDown));
            return ConsumeEvents;
        }

        protected override bool OnKeyUp(KeyUpEvent e)
        {
            InvokedHandlers.Add(nameof(OnKeyUp));
            return ConsumeEvents;
        }

        protected override bool OnTextInput(TextInputEvent e)
        {
            InvokedHandlers.Add(nameof(OnTextInput));
            return ConsumeEvents;
        }

        protected override void OnFocus(FocusEvent e)
            => InvokedHandlers.Add(nameof(OnFocus));

        protected override void OnFocusLost(FocusLostEvent e)
            => InvokedHandlers.Add(nameof(OnFocusLost));
    }
}
