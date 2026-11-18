using System.Numerics;
using kajarlabs.Framework.Input;
using kajarlabs.Framework.Scene;
using kajarlabs.Framework.Tests.Timing;
using kajarlabs.Framework.Timing;

namespace kajarlabs.Framework.Tests.Input;

public sealed class InputManagerTests
{
    [Fact]
    public void HoverTracksDepthOrderDynamicSceneChangesRemovalAndMouseLeave()
    {
        using var manager = new ManualInputManager { Size = new Vector2(400f, 300f) };

        using var back = new TestInputDrawable("back")
        {
            Position = new Vector2(0f, 0f),
            Size = new Vector2(200f, 100f),
            Depth = 5f,
            HandlePositional = true,
            ConsumeHover = true,
        };
        using var front = new TestInputDrawable("front")
        {
            Position = new Vector2(0f, 0f),
            Size = new Vector2(100f, 100f),
            Depth = -5f,
            HandlePositional = true,
            ConsumeHover = true,
        };

        manager.AddRange([back, front]);

        // Over both -> only front gets hovered because front consumes hover.
        manager.MoveMouseTo(new Vector2(50f, 50f));
        Assert.True(front.IsHovered);
        Assert.False(back.IsHovered);
        Assert.Equal(1, front.HoverCount);
        Assert.Equal(0, back.HoverCount);
        Assert.Equal([front], manager.HoveredDrawables);

        // Moving inside front does not re-fire OnHover.
        manager.MoveMouseTo(new Vector2(60f, 50f));
        Assert.Equal(1, front.HoverCount);

        // Moving to (150, 50) leaves front and enters back.
        manager.MoveMouseTo(new Vector2(150f, 50f));
        Assert.False(front.IsHovered);
        Assert.True(back.IsHovered);
        Assert.Equal(1, front.HoverLostCount);
        Assert.Equal(1, back.HoverCount);

        // Moving back to (50, 50), then hiding front -> UpdateSubTree re-evaluates hover to back.
        manager.MoveMouseTo(new Vector2(50f, 50f));
        Assert.True(front.IsHovered);
        Assert.False(back.IsHovered);

        front.Alpha = 0f;
        manager.UpdateSubTree();
        Assert.False(front.IsHovered);
        Assert.True(back.IsHovered);
        Assert.Equal(2, front.HoverLostCount);

        // Removing hovered drawable triggers OnHoverLost during UpdateSubTree.
        manager.Remove(back, disposeImmediately: false);
        manager.UpdateSubTree();
        Assert.False(back.IsHovered);
        Assert.Empty(manager.HoveredDrawables);

        // Re-adding under cursor restores hover; MouseLeave clears it.
        manager.Add(back);
        manager.UpdateSubTree();
        Assert.True(back.IsHovered);

        manager.MouseLeave();
        Assert.False(manager.CurrentState.Mouse.IsPositionValid);
        Assert.False(back.IsHovered);
        Assert.Empty(manager.HoveredDrawables);
    }

    [Fact]
    public void MouseDownUpClickAndDoubleClickRespectCaptureBoundsAndClock()
    {
        var clock = new ManualClock(start: true, initialTime: 100.0);
        var framedClock = new FramedClock(clock);
        framedClock.ProcessFrame();

        using var manager = new ManualInputManager
        {
            Size = new Vector2(400f, 300f),
            Clock = framedClock,
            ProcessCustomClock = false,
            DoubleClickTimeThreshold = 250.0,
            DoubleClickDistanceThreshold = 10f,
        };

        using var button = new TestInputDrawable("button")
        {
            Position = new Vector2(20f, 20f),
            Size = new Vector2(100f, 50f),
            HandlePositional = true,
            ConsumeMouseDown = true,
            ConsumeClick = true,
            ConsumeDoubleClick = true,
        };
        manager.Add(button);

        // Press inside, release outside -> MouseUp fires, Click does not.
        manager.MoveMouseTo(new Vector2(50f, 40f));
        manager.PressButton(MouseButton.Left);
        Assert.Equal(1, button.MouseDownCount);

        manager.MoveMouseTo(new Vector2(250f, 250f));
        manager.ReleaseButton(MouseButton.Left);
        Assert.Equal(1, button.MouseUpCount);
        Assert.Equal(new Vector2(50f, 40f), button.LastMouseUpDownPosition);
        Assert.Equal(0, button.ClickCount);

        // First click inside.
        manager.Click(MouseButton.Left, new Vector2(50f, 40f));
        Assert.Equal(1, button.ClickCount);
        Assert.Equal(0, button.DoubleClickCount);

        // Second click within time (100 -> 200ms) and distance (2px) triggers DoubleClick and suppresses Click.
        clock.Seek(200.0);
        framedClock.ProcessFrame();
        manager.Click(MouseButton.Left, new Vector2(52f, 40f));
        Assert.Equal(1, button.DoubleClickCount);
        Assert.Equal(1, button.ClickCount);

        // Third click starts a fresh single click (not another double-click).
        clock.Seek(250.0);
        framedClock.ProcessFrame();
        manager.Click(MouseButton.Left, new Vector2(52f, 40f));
        Assert.Equal(1, button.DoubleClickCount);
        Assert.Equal(2, button.ClickCount);

        // Fourth click after DoubleClickTimeThreshold triggers single click.
        clock.Seek(600.0);
        framedClock.ProcessFrame();
        manager.Click(MouseButton.Left, new Vector2(52f, 40f));
        Assert.Equal(1, button.DoubleClickCount);
        Assert.Equal(3, button.ClickCount);
    }

    [Fact]
    public void DragLifecycleRespectsThresholdBoundsRemovalAndWindowFocusLoss()
    {
        using var manager = new ManualInputManager
        {
            Size = new Vector2(400f, 300f),
            DragDistanceThreshold = 10f,
        };

        using var draggable = new TestInputDrawable("drag")
        {
            Position = new Vector2(50f, 50f),
            Size = new Vector2(80f, 80f),
            HandlePositional = true,
            ConsumeDrag = true,
            ConsumeClick = true,
        };
        manager.Add(draggable);

        // Below threshold -> no drag, click fires.
        manager.MoveMouseTo(new Vector2(80f, 80f));
        manager.PressButton(MouseButton.Left);
        manager.MoveMouseTo(new Vector2(84f, 83f)); // distance 5 < 10
        Assert.False(draggable.IsDragged);
        Assert.Equal(0, draggable.DragStartCount);
        manager.ReleaseButton(MouseButton.Left);
        Assert.Equal(1, draggable.ClickCount);

        // Exceeds threshold -> DragStart, Drag outside bounds, DragEnd on release, no Click.
        manager.MoveMouseTo(new Vector2(80f, 80f));
        manager.PressButton(MouseButton.Left);
        manager.MoveMouseTo(new Vector2(95f, 80f)); // distance 15 >= 10
        Assert.True(draggable.IsDragged);
        Assert.Equal(1, draggable.DragStartCount);
        Assert.Equal(0, draggable.DragCount);

        manager.MoveMouseTo(new Vector2(350f, 250f));
        Assert.True(draggable.IsDragged);
        Assert.Equal(1, draggable.DragCount);
        Assert.Equal(new Vector2(255f, 170f), draggable.LastDragScreenDelta);

        manager.ReleaseButton(MouseButton.Left);
        Assert.False(draggable.IsDragged);
        Assert.Equal(1, draggable.DragEndCount);
        Assert.Equal(1, draggable.ClickCount); // unchanged

        // Mid-drag disposal ends drag immediately and prevents Click on release.
        manager.MoveMouseTo(new Vector2(80f, 80f));
        manager.PressButton(MouseButton.Left);
        manager.MoveMouseTo(new Vector2(95f, 80f));
        Assert.True(draggable.IsDragged);

        manager.Remove(draggable, disposeImmediately: true);
        manager.UpdateSubTree();
        Assert.False(draggable.IsDragged);

        manager.MoveMouseTo(new Vector2(80f, 80f));
        manager.ReleaseButton(MouseButton.Left);
        Assert.Equal(1, draggable.ClickCount);

        // WindowFocusLost ends active drag cleanly.
        using var draggable2 = new TestInputDrawable("drag2")
        {
            Size = new Vector2(200f, 200f),
            HandlePositional = true,
            ConsumeDrag = true,
        };
        manager.Add(draggable2);
        manager.SetWindowFocus(true);
        manager.MoveMouseTo(new Vector2(20f, 20f));
        manager.PressButton(MouseButton.Left);
        manager.MoveMouseTo(new Vector2(40f, 20f));
        Assert.True(draggable2.IsDragged);

        manager.SetWindowFocus(false);
        Assert.False(draggable2.IsDragged);
        Assert.Equal(1, draggable2.DragEndCount);
        Assert.Equal(1, draggable2.MouseUpCount);
    }

    [Fact]
    public void ScrollBubblesFrontToBackAndRespectsMasking()
    {
        using var manager = new ManualInputManager { Size = new Vector2(400f, 300f) };

        using var maskedParent = new ScrollableContainer
        {
            Position = new Vector2(20f, 20f),
            Size = new Vector2(100f, 100f),
            Masking = true,
            ConsumeScroll = true,
        };
        using var extendingChild = new TestInputDrawable("child")
        {
            Position = new Vector2(0f, 0f),
            Size = new Vector2(180f, 100f),
            HandlePositional = true,
            ConsumeScroll = false, // lets scroll bubble to maskedParent
        };
        maskedParent.Add(extendingChild);
        manager.Add(maskedParent);

        // Inside both -> child receives first (does not consume), then parent consumes.
        manager.MoveMouseTo(new Vector2(60f, 60f));
        manager.Scroll(new Vector2(0f, -3f), isPrecise: true);
        Assert.Equal(1, extendingChild.ScrollCount);
        Assert.Equal(1, maskedParent.ScrollCount);
        Assert.Equal(new Vector2(0f, -3f), maskedParent.LastScrollDelta);

        // Outside maskedParent bounds -> neither receives scroll.
        manager.MoveMouseTo(new Vector2(160f, 60f));
        manager.Scroll(new Vector2(0f, 1f));
        Assert.Equal(1, extendingChild.ScrollCount);
        Assert.Equal(1, maskedParent.ScrollCount);

        // When child consumes scroll, parent does not receive it.
        extendingChild.ConsumeScroll = true;
        manager.MoveMouseTo(new Vector2(60f, 60f));
        manager.Scroll(new Vector2(0f, 2f));
        Assert.Equal(2, extendingChild.ScrollCount);
        Assert.Equal(1, maskedParent.ScrollCount);
    }

    [Fact]
    public void FocusLifecycleContentionLossAndKeyboardRoutingFollowContracts()
    {
        using var manager = new ManualInputManager { Size = new Vector2(400f, 300f) };
        var transitionLog = new List<string>();

        using var focusA = new TestInputDrawable("A", transitionLog)
        {
            Position = new Vector2(10f, 10f),
            Size = new Vector2(80f, 80f),
            AcceptsFocusFlag = true,
            ConsumeKeyDown = true,
            ConsumeTextInput = true,
        };
        using var transparentOverA = new TestInputDrawable("transparent")
        {
            Position = new Vector2(10f, 10f),
            Size = new Vector2(40f, 40f),
            HandlePositional = true,
            ConsumeMouseDown = false,
        };
        using var focusB = new TestInputDrawable("B", transitionLog)
        {
            Position = new Vector2(120f, 10f),
            Size = new Vector2(80f, 80f),
            AcceptsFocusFlag = true,
            RequestsFocusFlag = true,
            ConsumeKeyDown = false, // falls through to non-positional handler
        };
        using var nonFocusableBlocker = new TestInputDrawable("blocker")
        {
            Position = new Vector2(220f, 10f),
            Size = new Vector2(80f, 80f),
            HandlePositional = true,
            ConsumeMouseDown = true,
        };
        using var globalShortcutTarget = new TestInputDrawable("global", transitionLog)
        {
            HandleNonPositional = true,
            ConsumeKeyDown = true,
            ConsumeTextInput = true,
        };

        manager.AddRange([focusA, transparentOverA, focusB, nonFocusableBlocker, globalShortcutTarget]);

        // Clicking through transparentOverA focuses focusA.
        manager.Click(MouseButton.Left, new Vector2(20f, 20f));
        Assert.Same(focusA, manager.FocusedDrawable);
        Assert.True(focusA.HasFocus);

        // Keyboard and text go to focusA and stop there because focusA consumes them.
        manager.PressKey(Key.K, KeyModifiers.Shift);
        manager.InputText("k");
        manager.ReleaseKey(Key.K);
        Assert.Equal(1, focusA.KeyDownCount);
        Assert.Equal(1, focusA.TextInputCount);
        Assert.Equal(0, globalShortcutTarget.KeyDownCount);
        Assert.Equal(0, globalShortcutTarget.TextInputCount);

        // Clicking focusB fires focusA.OnFocusLost BEFORE focusB.OnFocus.
        transitionLog.Clear();
        manager.Click(MouseButton.Left, new Vector2(150f, 40f));
        Assert.Same(focusB, manager.FocusedDrawable);
        Assert.False(focusA.HasFocus);
        Assert.True(focusB.HasFocus);
        Assert.Equal(["A:FocusLost", "B:Focus"], transitionLog);

        // focusB does not consume KeyDown -> falls back to globalShortcutTarget in non-positional queue.
        manager.PressKey(Key.Enter);
        manager.ReleaseKey(Key.Enter);
        Assert.Equal(1, focusB.KeyDownCount);
        Assert.Equal(1, globalShortcutTarget.KeyDownCount);

        // Clicking non-focusable blocker clears focus.
        manager.Click(MouseButton.Left, new Vector2(250f, 40f));
        Assert.Null(manager.FocusedDrawable);
        Assert.False(focusB.HasFocus);

        // TriggerFocusContention focuses highest-priority RequestsFocus (focusB).
        Assert.True(manager.TriggerFocusContention());
        Assert.Same(focusB, manager.FocusedDrawable);

        // Hiding focused drawable (!IsPresent) clears focus on UpdateSubTree.
        focusB.Alpha = 0f;
        manager.UpdateSubTree();
        Assert.Null(manager.FocusedDrawable);
        Assert.False(focusB.HasFocus);

        // WindowFocusLost releases held keys and clears focus.
        manager.SetWindowFocus(true);
        Assert.True(manager.ChangeFocus(focusA));
        manager.PressKey(Key.A);
        Assert.True(manager.CurrentState.Keyboard.IsKeyDown(Key.A));

        manager.SetWindowFocus(false);
        Assert.Null(manager.FocusedDrawable);
        Assert.False(focusA.HasFocus);
        Assert.False(manager.CurrentState.WindowHasFocus);
        Assert.False(manager.CurrentState.Keyboard.AnyKeyDown);
        Assert.Equal(2, focusA.KeyUpCount);
    }

    [Fact]
    public void PassThroughAndNestedManualInputManagersRespectUseParentInput()
    {
        using var rootManager = new ManualInputManager { Size = new Vector2(400f, 300f) };
        using var passThrough = new PassThroughInputManager
        {
            Position = new Vector2(10f, 10f),
            Size = new Vector2(150f, 150f),
        };
        using var passChild = new TestInputDrawable("passChild")
        {
            Size = new Vector2(150f, 150f),
            AcceptsFocusFlag = true,
            ConsumeHover = true,
            ConsumeClick = true,
            ConsumeKeyDown = true,
        };
        passThrough.Add(passChild);

        using var isolatedManual = new ManualInputManager
        {
            Position = new Vector2(200f, 10f),
            Size = new Vector2(150f, 150f),
        };
        using var isolatedChild = new TestInputDrawable("isolatedChild")
        {
            Size = new Vector2(150f, 150f),
            AcceptsFocusFlag = true,
            ConsumeClick = true,
        };
        isolatedManual.Add(isolatedChild);

        rootManager.AddRange([passThrough, isolatedManual]);

        // Root events reach passThrough child.
        rootManager.SetWindowFocus(true);
        rootManager.Click(MouseButton.Left, new Vector2(50f, 50f));
        Assert.True(passChild.IsHovered);
        Assert.True(passChild.HasFocus);
        Assert.Equal(1, passChild.ClickCount);
        Assert.Same(passChild, passThrough.FocusedDrawable);

        rootManager.PressKey(Key.Space);
        Assert.Equal(1, passChild.KeyDownCount);

        // Moving outside passThrough clears hover inside passThrough.
        rootManager.MoveMouseTo(new Vector2(250f, 50f));
        Assert.False(passChild.IsHovered);

        // Root events do NOT penetrate isolatedManual (UseParentInput == false).
        rootManager.Click(MouseButton.Left, new Vector2(250f, 50f));
        Assert.Equal(0, isolatedChild.ClickCount);

        // Driving isolatedManual directly works.
        isolatedManual.Click(MouseButton.Left, new Vector2(250f, 50f));
        Assert.Equal(1, isolatedChild.ClickCount);
    }

    private sealed class ScrollableContainer : Container
    {
        public bool ConsumeScroll { get; set; }

        public int ScrollCount { get; private set; }

        public Vector2 LastScrollDelta { get; private set; }

        public override bool HandlePositionalInput => true;

        protected override bool OnScroll(ScrollEvent e)
        {
            ScrollCount++;
            LastScrollDelta = e.ScrollDelta;
            return ConsumeScroll;
        }
    }

    private sealed class TestInputDrawable(string name, List<string>? log = null) : Drawable
    {
        public bool AcceptsFocusFlag { get; init; }

        public bool RequestsFocusFlag { get; init; }

        public bool HandlePositional { get; init; }

        public bool HandleNonPositional { get; init; }

        public bool ConsumeHover { get; set; }

        public bool ConsumeMouseDown { get; set; }

        public bool ConsumeClick { get; set; }

        public bool ConsumeDoubleClick { get; set; }

        public bool ConsumeDrag { get; set; }

        public bool ConsumeScroll { get; set; }

        public bool ConsumeKeyDown { get; set; }

        public bool ConsumeTextInput { get; set; }

        public int HoverCount { get; private set; }

        public int HoverLostCount { get; private set; }

        public int MouseDownCount { get; private set; }

        public int MouseUpCount { get; private set; }

        public int ClickCount { get; private set; }

        public int DoubleClickCount { get; private set; }

        public int DragStartCount { get; private set; }

        public int DragCount { get; private set; }

        public int DragEndCount { get; private set; }

        public int ScrollCount { get; private set; }

        public int KeyDownCount { get; private set; }

        public int KeyUpCount { get; private set; }

        public int TextInputCount { get; private set; }

        public Vector2? LastMouseUpDownPosition { get; private set; }

        public Vector2 LastDragScreenDelta { get; private set; }

        public override bool AcceptsFocus => AcceptsFocusFlag;

        public override bool RequestsFocus => RequestsFocusFlag;

        public override bool HandlePositionalInput => HandlePositional || base.HandlePositionalInput;

        public override bool HandleNonPositionalInput => HandleNonPositional;

        protected override bool OnHover(HoverEvent e)
        {
            HoverCount++;
            return ConsumeHover;
        }

        protected override void OnHoverLost(HoverLostEvent e) => HoverLostCount++;

        protected override bool OnMouseDown(MouseDownEvent e)
        {
            MouseDownCount++;
            return ConsumeMouseDown;
        }

        protected override bool OnMouseUp(MouseUpEvent e)
        {
            MouseUpCount++;
            LastMouseUpDownPosition = e.ScreenSpaceMouseDownPosition;
            return false;
        }

        protected override bool OnClick(ClickEvent e)
        {
            ClickCount++;
            return ConsumeClick;
        }

        protected override bool OnDoubleClick(DoubleClickEvent e)
        {
            DoubleClickCount++;
            return ConsumeDoubleClick;
        }

        protected override bool OnDragStart(DragStartEvent e)
        {
            DragStartCount++;
            return ConsumeDrag;
        }

        protected override bool OnDrag(DragEvent e)
        {
            DragCount++;
            LastDragScreenDelta = e.ScreenSpaceDelta;
            return ConsumeDrag;
        }

        protected override void OnDragEnd(DragEndEvent e) => DragEndCount++;

        protected override bool OnScroll(ScrollEvent e)
        {
            ScrollCount++;
            return ConsumeScroll;
        }

        protected override bool OnKeyDown(KeyDownEvent e)
        {
            KeyDownCount++;
            return ConsumeKeyDown;
        }

        protected override bool OnKeyUp(KeyUpEvent e)
        {
            KeyUpCount++;
            return false;
        }

        protected override bool OnTextInput(TextInputEvent e)
        {
            TextInputCount++;
            return ConsumeTextInput;
        }

        protected override void OnFocus(FocusEvent e) => log?.Add($"{name}:Focus");

        protected override void OnFocusLost(FocusLostEvent e) => log?.Add($"{name}:FocusLost");
    }
}
