using System.Numerics;
using Crucible.Platform;
using Crucible.UI;

namespace Crucible.Tests;

// UI input (ADR 0062), under JIT and NativeAOT, from synthetic platform
// events: pointer routing, capture and hover, focus, Tab order, focus
// scopes, directional navigation, and actions from keys and gamepads.
public class UIInputTests
{
    private const int Tab = 43, Return = 40, Escape = 41, Space = 44, Right = 79, Left = 80, Down = 81, Up = 82;

    // Records what reaches it; optionally handles one kind.
    private sealed class Probe : UIElement
    {
        public readonly List<string> Log = [];
        public string? Handles;

        public Probe(float x, float y, float size = 40)
        {
            Width = size;
            Height = size;
            AnchorPanel.SetAnchors(this, Anchors.At(Vector2.Zero, new Vector2(x, y)));
        }

        private void Note(string what, UIEvent e)
        {
            Log.Add(what);
            if (Handles == what)
            {
                e.Handled = true;
            }
        }

        protected override void OnPointerEnter(PointerEvent e) => Note("enter", e);
        protected override void OnPointerLeave(PointerEvent e) => Note("leave", e);
        protected override void OnPointerDown(PointerEvent e) => Note("down", e);
        protected override void OnPointerUp(PointerEvent e) => Note("up", e);
        protected override void OnPointerWheel(PointerEvent e) => Note("wheel", e);
        protected override void OnKeyDown(KeyEvent e) => Note("key", e);
        protected override void OnNavigate(NavigationEvent e) => Note($"navigate {e.Direction}", e);
        protected override void OnActivate(UIEvent e) => Note("activate", e);
        protected override void OnCancel(UIEvent e) => Note("cancel", e);
        protected override void OnGotFocus(UIEvent e) => Note("got focus", e);
        protected override void OnLostFocus(UIEvent e) => Note("lost focus", e);
    }

    private sealed class Rig
    {
        public double Time;
        public readonly UIRoot Root = new();
        public readonly AnchorPanel Panel = new();

        public Rig()
        {
            Root.Content = Panel;
            Root.TestClock = () => Time;
        }

        public T Add<T>(T element, UIElement? into = null) where T : UIElement
        {
            ((Panel?)into ?? Panel).Add(element);
            Root.UpdateLayout(new Vector2(400, 300));
            return element;
        }

        public void Send(params InputEvent[] events) => Root.ProcessInput(events);
    }

    private static InputEvent Motion(float x, float y) => new() { Type = InputEventType.MouseMotion, X = x, Y = y };

    private static InputEvent Button(float x, float y, bool down, int button = 1) =>
        new() { Type = down ? InputEventType.MouseButtonDown : InputEventType.MouseButtonUp, X = x, Y = y, Code = button };

    private static InputEvent Key(int scancode, bool down = true, KeyModifiers modifiers = KeyModifiers.None) =>
        new() { Type = down ? InputEventType.KeyDown : InputEventType.KeyUp, Code = scancode, Modifiers = (uint)modifiers };

    private static InputEvent Pad(GamepadButton button, bool down = true) =>
        new() { Type = down ? InputEventType.GamepadButtonDown : InputEventType.GamepadButtonUp, Code = (int)button };

    private static InputEvent Stick(GamepadAxis axis, float value) =>
        new() { Type = InputEventType.GamepadAxis, Code = (int)axis, X = value };

    [Test]
    public async Task ClicksBubbleUntilHandledAndActivateWhatWasPressed()
    {
        Rig rig = new();
        Probe outer = rig.Add(new Probe(0, 0, 200));
        AnchorPanel inside = new() { Width = 200, Height = 200 };
        AnchorPanel.SetAnchors(inside, Anchors.At(Vector2.Zero));
        Probe inner = new(10, 10);
        inside.Add(inner);
        rig.Panel.Remove(outer);
        Probe container = new(0, 0, 200);
        rig.Add(container);
        rig.Add(inside);
        List<string> order = [];
        inner.PointerDown += (_, _) => order.Add("event");
        inner.Handles = null;

        rig.Send(Button(20, 20, true), Button(20, 20, false));
        await Assert.That(string.Join(", ", inner.Log)).IsEqualTo("enter, down, up, activate");
        // inside's parent is the panel, not container: bubbling follows the tree.
        await Assert.That(container.Log.Contains("down")).IsFalse();
        await Assert.That(order.Single()).IsEqualTo("event");
        await Assert.That(rig.Root.HandledPointer).IsFalse(); // nothing handled it

        inner.Handles = "down";
        inner.Log.Clear();
        rig.Send(Button(20, 20, true));
        await Assert.That(rig.Root.HandledPointer).IsTrue();
        await Assert.That((inner.State & UIState.Pressed) != 0).IsTrue();

        // Captured: released elsewhere, it still gets the release, but no click.
        rig.Send(Motion(300, 250), Button(300, 250, false));
        await Assert.That(string.Join(", ", inner.Log)).IsEqualTo("down, leave, up");
        await Assert.That((inner.State & UIState.Pressed) != 0).IsFalse();
    }

    [Test]
    public async Task HoverCoversTheElementAndItsAncestors()
    {
        Rig rig = new();
        Probe a = rig.Add(new Probe(0, 0)), b = rig.Add(new Probe(100, 0));
        rig.Send(Motion(10, 10));
        await Assert.That((a.State & UIState.Hover) != 0).IsTrue();
        await Assert.That((rig.Panel.State & UIState.Hover) != 0).IsTrue();
        rig.Send(Motion(110, 10));
        await Assert.That((a.State & UIState.Hover) != 0).IsFalse();
        await Assert.That((b.State & UIState.Hover) != 0).IsTrue();
        await Assert.That(string.Join(", ", a.Log)).IsEqualTo("enter, leave");

        // Disabled elements pass hits to their parent.
        b.State |= UIState.Disabled;
        rig.Send(Motion(111, 10));
        await Assert.That((b.State & UIState.Hover) != 0).IsFalse();
        rig.Send(new InputEvent { Type = InputEventType.MouseWheel, X = 111, Y = 10, Dy = 1 });
        await Assert.That(b.Log.Contains("wheel")).IsFalse();
    }

    [Test]
    public async Task ClicksFocusAndKeysGoToFocus()
    {
        Rig rig = new();
        Probe a = rig.Add(new Probe(0, 0) { IsFocusable = true });
        Probe b = rig.Add(new Probe(100, 0));
        rig.Send(Button(10, 10, true), Button(10, 10, false));
        await Assert.That(rig.Root.FocusedElement).IsSameReferenceAs(a);
        await Assert.That(a.IsFocused).IsTrue();

        rig.Send(Key(4)); // "a"
        await Assert.That(a.Log.Contains("key")).IsTrue();
        rig.Send(Button(110, 10, true)); // b isn't focusable: focus goes nowhere
        await Assert.That(rig.Root.FocusedElement).IsNull();
        await Assert.That(a.Log.Contains("lost focus")).IsTrue();
        await Assert.That(b.Focus()).IsFalse();
    }

    [Test]
    public async Task TabFollowsTabIndexThenTreeOrderAndWraps()
    {
        Rig rig = new();
        Probe a = rig.Add(new Probe(0, 0) { IsFocusable = true });
        Probe b = rig.Add(new Probe(50, 0) { IsFocusable = true, TabIndex = -1 });
        Probe c = rig.Add(new Probe(100, 0) { IsFocusable = true });
        rig.Add(new Probe(150, 0) { IsFocusable = true, Visibility = Visibility.Collapsed });
        rig.Send(Key(Tab));
        await Assert.That(rig.Root.FocusedElement).IsSameReferenceAs(b);
        rig.Send(Key(Tab), Key(Tab));
        await Assert.That(rig.Root.FocusedElement).IsSameReferenceAs(c);
        rig.Send(Key(Tab));
        await Assert.That(rig.Root.FocusedElement).IsSameReferenceAs(b); // wrapped
        rig.Send(Key(Tab, modifiers: KeyModifiers.Shift));
        await Assert.That(rig.Root.FocusedElement).IsSameReferenceAs(c);
        _ = a;
    }

    [Test]
    public async Task FocusScopesKeepFocusInside()
    {
        Rig rig = new();
        rig.Add(new Probe(0, 0) { IsFocusable = true });
        AnchorPanel dialog = new() { IsFocusScope = true, Width = 200, Height = 100 };
        AnchorPanel.SetAnchors(dialog, Anchors.At(new Vector2(0, 0), new Vector2(100, 100)));
        rig.Add(dialog);
        Probe ok = rig.Add(new Probe(0, 0) { IsFocusable = true }, dialog);
        Probe cancel = rig.Add(new Probe(100, 0) { IsFocusable = true }, dialog);
        ok.Focus();
        rig.Send(Key(Tab));
        await Assert.That(rig.Root.FocusedElement).IsSameReferenceAs(cancel);
        rig.Send(Key(Tab));
        await Assert.That(rig.Root.FocusedElement).IsSameReferenceAs(ok); // not the button outside
        rig.Send(Key(Left));
        await Assert.That(rig.Root.FocusedElement).IsSameReferenceAs(ok);
    }

    [Test]
    public async Task ArrowsNavigateSpatiallyUnlessHandled()
    {
        // A 3x3 grid, 50 apart.
        Rig rig = new();
        Probe[,] grid = new Probe[3, 3];
        for (int row = 0; row < 3; ++row)
        {
            for (int column = 0; column < 3; ++column)
            {
                grid[row, column] = rig.Add(new Probe(column * 50, row * 50) { IsFocusable = true });
            }
        }
        grid[1, 1].Focus();
        rig.Send(Key(Right));
        await Assert.That(rig.Root.FocusedElement).IsSameReferenceAs(grid[1, 2]);
        rig.Send(Key(Up));
        await Assert.That(rig.Root.FocusedElement).IsSameReferenceAs(grid[0, 2]);
        rig.Send(Key(Up)); // nothing above: stays
        await Assert.That(rig.Root.FocusedElement).IsSameReferenceAs(grid[0, 2]);
        rig.Send(Key(Left), Key(Left), Key(Down), Key(Down));
        await Assert.That(rig.Root.FocusedElement).IsSameReferenceAs(grid[2, 0]);

        // An explicit neighbor wins; a control handling arrows keeps focus.
        grid[2, 0].NavigateRight = grid[0, 1];
        rig.Send(Key(Right));
        await Assert.That(rig.Root.FocusedElement).IsSameReferenceAs(grid[0, 1]);
        grid[0, 1].Handles = "navigate Down";
        rig.Send(Key(Down));
        await Assert.That(rig.Root.FocusedElement).IsSameReferenceAs(grid[0, 1]);
        await Assert.That(grid[0, 1].Log.Contains("navigate Down")).IsTrue();
        await Assert.That(rig.Root.HandledKeyboard).IsTrue();
    }

    [Test]
    public async Task EnterSpaceAndEscapeActAndGamepadsToo()
    {
        Rig rig = new();
        Probe button = rig.Add(new Probe(0, 0) { IsFocusable = true });
        Probe other = rig.Add(new Probe(0, 100) { IsFocusable = true });
        button.Focus();
        rig.Send(Key(Return));
        await Assert.That((button.State & UIState.Pressed) != 0).IsTrue();
        rig.Send(Key(Return, down: false), Key(Space), Key(Space, down: false), Key(Escape));
        await Assert.That(button.Log.Count(l => l == "activate")).IsEqualTo(2);
        await Assert.That(button.Log.Contains("cancel")).IsTrue();
        await Assert.That((button.State & UIState.Pressed) != 0).IsFalse();

        button.Log.Clear();
        rig.Send(Pad(GamepadButton.South), Pad(GamepadButton.South, false), Pad(GamepadButton.East));
        await Assert.That(string.Join(", ", button.Log)).IsEqualTo("activate, cancel");
        rig.Send(Pad(GamepadButton.DpadDown), Pad(GamepadButton.DpadDown, false));
        await Assert.That(rig.Root.FocusedElement).IsSameReferenceAs(other);

        // Remapped: East confirms, South cancels.
        rig.Root.InputMap.Buttons[GamepadButton.East] = UIAction.Activate;
        rig.Root.InputMap.Buttons[GamepadButton.South] = UIAction.Cancel;
        rig.Send(Pad(GamepadButton.East), Pad(GamepadButton.East, false), Pad(GamepadButton.South));
        await Assert.That(string.Join(", ", other.Log.Where(l => l is "activate" or "cancel"))).IsEqualTo("activate, cancel");
    }

    [Test]
    public async Task HeldDirectionsAndTheStickRepeat()
    {
        Rig rig = new();
        Probe[] column = [.. Enumerable.Range(0, 8).Select(i => rig.Add(new Probe(0, i * 30, 20) { IsFocusable = true }))];
        column[0].Focus();
        rig.Send(Stick(GamepadAxis.LeftY, 0.9f)); // past the threshold: one step
        await Assert.That(rig.Root.FocusedElement).IsSameReferenceAs(column[1]);
        rig.Time = 0.3;
        rig.Send(Stick(GamepadAxis.LeftY, 0.95f)); // still held: no repeat before the delay
        await Assert.That(rig.Root.FocusedElement).IsSameReferenceAs(column[1]);
        rig.Time = 0.45;
        rig.Send();
        await Assert.That(rig.Root.FocusedElement).IsSameReferenceAs(column[2]);
        rig.Time = 0.56;
        rig.Send();
        await Assert.That(rig.Root.FocusedElement).IsSameReferenceAs(column[3]);
        rig.Send(Stick(GamepadAxis.LeftY, 0.45f)); // within the release margin: still held
        rig.Time = 0.67;
        rig.Send();
        await Assert.That(rig.Root.FocusedElement).IsSameReferenceAs(column[4]);
        rig.Send(Stick(GamepadAxis.LeftY, 0.1f)); // let go
        rig.Time = 2;
        rig.Send();
        await Assert.That(rig.Root.FocusedElement).IsSameReferenceAs(column[4]);

        rig.Send(Pad(GamepadButton.DpadUp));
        rig.Time = 2.41;
        rig.Send();
        await Assert.That(rig.Root.FocusedElement).IsSameReferenceAs(column[2]);
        rig.Send(Pad(GamepadButton.DpadUp, false));
        rig.Time = 3;
        rig.Send();
        await Assert.That(rig.Root.FocusedElement).IsSameReferenceAs(column[2]);
    }

    [Test]
    public async Task RemovingAnElementTakesItsFocusAndHover()
    {
        Rig rig = new();
        Probe a = rig.Add(new Probe(0, 0) { IsFocusable = true });
        rig.Send(Motion(10, 10), Button(10, 10, true));
        await Assert.That(a.IsFocused).IsTrue();
        rig.Panel.Remove(a);
        await Assert.That(rig.Root.FocusedElement).IsNull();
        await Assert.That(a.State).IsEqualTo(UIState.None);
    }

    [Test]
    public async Task PointerPositionsAreLogicalUnits()
    {
        Rig rig = new();
        rig.Root.Scale = 2;
        Probe a = rig.Add(new Probe(100, 100));
        rig.Root.UpdateLayout(new Vector2(800, 600));
        rig.Send(Motion(220, 220)); // physical: logical (110, 110)
        await Assert.That((a.State & UIState.Hover) != 0).IsTrue();
        rig.Send(new InputEvent { Type = InputEventType.DisplayScaleChanged, X = 1.5f });
        await Assert.That(rig.Root.Scale).IsEqualTo(1.5f);
    }
}
