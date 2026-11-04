using System.Drawing;
using System.Numerics;
using Crucible.Binary;
using Crucible.Platform;
using Crucible.Text;
using Crucible.UI;

namespace Crucible.Tests;

// The controls (ADR 0063), under JIT and NativeAOT, driven by synthetic
// input with the default theme; launcher.jit.controls draws them.
[NotInParallel("text")]
public class ControlsTests
{
    private const int Return = 40, Escape = 41, Right = 79, Left = 80, Down = 81, Up = 82;

    private static FontHandle LoadNotoSans()
    {
        byte[] font = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fonts", "NotoSans-Regular.ttf"));
        string path = Path.Combine(Path.GetTempPath(), "crucible-controls-" + Guid.NewGuid().ToString("N") + ".crfont");
        try
        {
            using ImportedFileWriter writer = new(Tag.Of("FONT"), 1);
            writer.Add(Tag.Of("DATA"), 1, font).Write(path, Compression.None);
            return Fonts.Load(File.ReadAllBytes(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private sealed class Rig : IDisposable
    {
        public double Time;
        public readonly UIRoot Root = new();
        public readonly StackPanel Panel = new() { Spacing = 10, HorizontalAlignment = HorizontalAlignment.Start };
        private readonly FontHandle _font = LoadNotoSans();

        public Rig()
        {
            Root.DefaultFont = _font;
            Root.Content = Panel;
            Root.TestClock = () => Time;
        }

        public T Add<T>(T element) where T : UIElement
        {
            Panel.Add(element);
            Layout();
            return element;
        }

        public void Layout() => Root.UpdateLayout(new Vector2(400, 300));

        public void Send(params InputEvent[] events)
        {
            Root.ProcessInput(events);
            Layout();
        }

        public void Click(UIElement element)
        {
            Vector2 at = Center(element);
            Send(Motion(at), Button(at, true), Button(at, false));
        }

        public void Dispose()
        {
            Root.Content = null; // labels let go of their layouts
            Fonts.Unload(_font);
        }
    }

    private static Vector2 Center(UIElement element) =>
        new(element.Bounds.X + element.Bounds.Width / 2, element.Bounds.Y + element.Bounds.Height / 2);

    private static InputEvent Motion(Vector2 at) => new() { Type = InputEventType.MouseMotion, X = at.X, Y = at.Y };

    private static InputEvent Button(Vector2 at, bool down) =>
        new() { Type = down ? InputEventType.MouseButtonDown : InputEventType.MouseButtonUp, X = at.X, Y = at.Y, Code = 1 };

    private static InputEvent Key(int scancode) => new() { Type = InputEventType.KeyDown, Code = scancode };

    private static InputEvent Wheel(Vector2 at, float notches) =>
        new() { Type = InputEventType.MouseWheel, X = at.X, Y = at.Y, Dy = notches };

    [Test]
    public async Task LabelsMeasureTheirTextAndWrap()
    {
        using Rig rig = new();
        Label label = rig.Add(new Label("Crucible"));
        Vector2 size = label.DesiredSize;
        await Assert.That(size.X).IsGreaterThan(40f);
        await Assert.That(size.Y).IsGreaterThan(10f);
        await Assert.That(label.Foreground).IsNotEqualTo(new Vector4(0, 0, 0, 1)); // the default theme's

        label.Text = "Crucible Crucible";
        rig.Layout();
        await Assert.That(label.DesiredSize.X).IsGreaterThan(size.X * 1.5f);
        label.Wrap = true;
        label.Width = 80;
        rig.Layout();
        await Assert.That(label.Layout!.LineCount).IsEqualTo(2);
        label.FontSize = 32;
        rig.Layout();
        await Assert.That(label.Layout!.Style.Size).IsEqualTo(32f);
    }

    [Test]
    public async Task ButtonsClickFromPointersAndKeys()
    {
        using Rig rig = new();
        Button button = rig.Add(new Button("Play"));
        int clicks = 0;
        button.Clicked += _ => ++clicks;
        await Assert.That(button.Content).IsTypeOf<Label>();
        await Assert.That(button.Text).IsEqualTo("Play");
        rig.Click(button);
        await Assert.That(clicks).IsEqualTo(1);
        await Assert.That(button.IsFocused).IsTrue();
        rig.Send(Key(Return));
        await Assert.That(clicks).IsEqualTo(2);
        await Assert.That(rig.Root.HandledKeyboard).IsTrue();
        button.Text = "Resume";
        await Assert.That(((Label)button.Content!).Text).IsEqualTo("Resume");
        await Assert.That(button.Background.W).IsGreaterThan(0f); // styled
    }

    [Test]
    public async Task TogglesFlipAndMarkTheirParts()
    {
        using Rig rig = new();
        Toggle toggle = rig.Add(new Toggle("Subtitles"));
        int changes = 0;
        toggle.Changed += _ => ++changes;
        rig.Click(toggle);
        await Assert.That(toggle.IsOn).IsTrue();
        await Assert.That(toggle.Box.Classes.Contains("on")).IsTrue();
        await Assert.That(toggle.Mark.Classes.Contains("on")).IsTrue();
        rig.Time = 1; // past the mark's fade in
        rig.Layout();
        await Assert.That(toggle.Mark.Opacity).IsEqualTo(1f);
        rig.Click(toggle);
        await Assert.That(toggle.IsOn).IsFalse();
        await Assert.That(changes).IsEqualTo(2);
    }

    [Test]
    public async Task SlidersSnapDragAndTakeLeftAndRight()
    {
        using Rig rig = new();
        Slider slider = rig.Add(new Slider { Minimum = 0, Maximum = 100, Step = 5, Value = 12 });
        await Assert.That(slider.Value).IsEqualTo(10f);
        slider.Value = 1000;
        await Assert.That(slider.Value).IsEqualTo(100f);

        // Pressing at the track's middle, then dragging to its start.
        RectangleF b = slider.Bounds;
        Vector2 middle = new(b.X + b.Width / 2, b.Y + b.Height / 2);
        rig.Send(Motion(middle), Button(middle, true));
        await Assert.That(slider.Value).IsEqualTo(50f);
        rig.Send(Motion(new Vector2(b.X - 50, middle.Y)), Button(new Vector2(b.X - 50, middle.Y), false));
        await Assert.That(slider.Value).IsEqualTo(0f);
        await Assert.That(slider.IsFocused).IsTrue();

        Button below = rig.Add(new Button("Below"));
        rig.Send(Key(Right), Key(Right));
        await Assert.That(slider.Value).IsEqualTo(10f);
        await Assert.That(rig.Root.FocusedElement).IsSameReferenceAs(slider);
        rig.Send(Key(Left));
        await Assert.That(slider.Value).IsEqualTo(5f);
        rig.Send(Key(Down)); // not a slider's: navigates
        await Assert.That(rig.Root.FocusedElement).IsSameReferenceAs(below);
    }

    [Test]
    public async Task ScrollViewsScrollByWheelBarAndFocus()
    {
        using Rig rig = new();
        StackPanel items = new();
        Button[] buttons = [.. Enumerable.Range(0, 20).Select(i => new Button($"Item {i}") { Height = 30 })];
        foreach (Button item in buttons)
        {
            items.Add(item);
        }
        ScrollView view = rig.Add(new ScrollView { Content = items, Width = 200, Height = 100 });
        await Assert.That(view.Extent.Y).IsEqualTo(600f);
        await Assert.That(view.VerticalBar.Visibility).IsEqualTo(Visibility.Visible);

        Vector2 inside = Center(view);
        rig.Send(Wheel(inside, -2)); // two notches down
        await Assert.That(view.Offset.Y).IsEqualTo(96f);
        rig.Send(Wheel(inside, 10));
        await Assert.That(view.Offset.Y).IsEqualTo(0f); // clamped
        rig.Send(Wheel(inside, -100));
        await Assert.That(view.Offset.Y).IsEqualTo(500f);
        view.Offset = Vector2.Zero;
        rig.Layout();

        // Focus moving below the fold scrolls the item into view.
        buttons[0].Focus();
        for (int i = 0; i < 5; ++i)
        {
            rig.Send(Key(Down));
        }
        await Assert.That(rig.Root.FocusedElement).IsSameReferenceAs(buttons[5]);
        RectangleF shown = buttons[5].Bounds;
        await Assert.That(shown.Bottom).IsLessThanOrEqualTo(view.Bounds.Bottom + 0.01f);
        await Assert.That(view.Offset.Y).IsGreaterThan(0f);

        // Dragging the thumb to the bar's end scrolls to the end.
        rig.Send(Key(Up), Key(Up), Key(Up), Key(Up), Key(Up));
        RectangleF thumb = view.VerticalBar.Thumb.Bounds;
        Vector2 grab = new(thumb.X + thumb.Width / 2, thumb.Y + thumb.Height / 2);
        rig.Send(Motion(grab), Button(grab, true), Motion(grab + new Vector2(0, 500)), Button(grab + new Vector2(0, 500), false));
        await Assert.That(view.Offset.Y).IsEqualTo(500f);
        await Assert.That(view.HorizontalBar.Visibility).IsEqualTo(Visibility.Collapsed);
    }

    [Test]
    public async Task ImagesFitTheirSpace()
    {
        Vector2 natural = new(200, 100);
        await Assert.That(Image.Fit(Stretch.None, natural, new Vector2(100, 100)).Size).IsEqualTo(natural);
        await Assert.That(Image.Fit(Stretch.Fill, natural, new Vector2(100, 100)).Size).IsEqualTo(new Vector2(100, 100));
        (Vector2 size, Vector2 offset) = Image.Fit(Stretch.Uniform, natural, new Vector2(100, 100));
        await Assert.That(size).IsEqualTo(new Vector2(100, 50));
        await Assert.That(offset).IsEqualTo(new Vector2(0, 25));
        await Assert.That(Image.Fit(Stretch.UniformToFill, natural, new Vector2(100, 100)).Size).IsEqualTo(new Vector2(200, 100));
        await Assert.That(Image.Fit(Stretch.Uniform, natural, new Vector2(float.PositiveInfinity, 50)).Size)
            .IsEqualTo(new Vector2(100, 50));
        await Assert.That(new Image().NaturalSize).IsEqualTo(Vector2.Zero); // no texture
    }

    // A list of numbered labels, the tallest every tenth.
    private static ListBox Numbers(int count) => new()
    {
        ItemCount = count,
        Height = 200,
        CreateItem = () => new Label(),
        BindItem = (element, index) =>
        {
            var label = (Label)element;
            label.Text = $"Item {index}";
            label.FontSize = index % 10 == 0 ? 40 : 16;
        },
    };

    [Test]
    public async Task ListBoxesMakeRowsOnlyForWhatsInView()
    {
        using Rig rig = new();
        ListBox list = rig.Add(Numbers(1000));
        await Assert.That(list.RealizedCount).IsLessThan(20);
        await Assert.That(list.RealizedCount).IsGreaterThan(3);
        float extent = list.ScrollView.Extent.Y;
        await Assert.That(extent).IsGreaterThan(1000 * 20f);

        // Down to the end: rows are reused, not added.
        list.ScrollView.Offset = new Vector2(0, float.MaxValue);
        rig.Layout();
        rig.Layout();
        await Assert.That(list.RealizedCount).IsLessThan(20);
        list.FocusItem(999);
        await Assert.That(rig.Root.FocusedElement is ListItem { Index: 999 }).IsTrue();
        // More rows measured: the estimate (and so the extent) settled.
        await Assert.That(list.ScrollView.Extent.Y).IsNotEqualTo(extent);
    }

    [Test]
    public async Task ListBoxesNavigateRowByRowAndSelectOnActivate()
    {
        using Rig rig = new();
        Button above = rig.Add(new Button("Above"));
        ListBox list = rig.Add(Numbers(50));
        int activated = -1;
        list.ItemActivated += (_, index) => activated = index;
        list.FocusItem(0);
        for (int i = 0; i < 25; ++i)
        {
            rig.Send(Key(Down));
        }
        await Assert.That(rig.Root.FocusedElement is ListItem { Index: 25 }).IsTrue();
        RectangleF row = rig.Root.FocusedElement!.Bounds;
        await Assert.That(row.Top).IsGreaterThanOrEqualTo(list.ScrollView.Bounds.Top - 0.01f);
        await Assert.That(row.Bottom).IsLessThanOrEqualTo(list.ScrollView.Bounds.Bottom + 0.01f);

        rig.Send(Key(Return));
        await Assert.That(list.SelectedIndex).IsEqualTo(25);
        await Assert.That(activated).IsEqualTo(25);
        await Assert.That(rig.Root.FocusedElement!.Classes.Contains("selected")).IsTrue();

        list.FocusItem(0);
        rig.Send(Key(Up)); // past the top: focus leaves the list
        await Assert.That(rig.Root.FocusedElement).IsSameReferenceAs(above);
    }

    [Test]
    public async Task PopupsOpenBesideTheirAnchorAndCloseOnAPressOutside()
    {
        using Rig rig = new();
        Button anchor = rig.Add(new Button("Menu"));
        anchor.Focus();
        Button inside = new("Inside");
        Popup popup = new() { Anchor = anchor, Content = inside };
        int closed = 0;
        popup.Closed += _ => ++closed;
        popup.Open();
        await Assert.That(popup.IsOpen).IsTrue();
        await Assert.That(popup.Bounds.Top).IsEqualTo(anchor.Bounds.Bottom);
        await Assert.That(popup.Bounds.Width).IsGreaterThanOrEqualTo(anchor.Bounds.Width);
        await Assert.That(rig.Root.FocusedElement).IsSameReferenceAs(inside);
        // Hits go to the popup first.
        await Assert.That(rig.Root.HitTest(Center(inside))).IsSameReferenceAs(inside.Content);

        rig.Send(Key(Escape));
        await Assert.That(popup.IsOpen).IsFalse();
        await Assert.That(rig.Root.FocusedElement).IsSameReferenceAs(anchor); // focus came back

        popup.Open();
        Vector2 outside = new(390, 290);
        rig.Send(Motion(outside), Button(outside, true), Button(outside, false));
        await Assert.That(popup.IsOpen).IsFalse();
        await Assert.That(rig.Root.HandledPointer).IsTrue(); // used up closing it
        await Assert.That(closed).IsEqualTo(2);
    }

    [Test]
    public async Task PopupsFlipAboveWhenThereIsNoRoomBelow()
    {
        using Rig rig = new();
        AnchorPanel panel = new();
        rig.Root.Content = panel;
        Button anchor = new("Bottom") { Height = 30 };
        AnchorPanel.SetAnchors(anchor, Anchors.At(new Vector2(0, 1), new Vector2(10, -10)));
        panel.Add(anchor);
        rig.Layout();
        Popup popup = new() { Anchor = anchor, Content = new UIElement { Width = 100, Height = 120 } };
        popup.Open();
        await Assert.That(popup.Bounds.Bottom).IsEqualTo(anchor.Bounds.Top);
    }

    [Test]
    public async Task SpinnersStepAndWrap()
    {
        using Rig rig = new();
        Spinner spinner = rig.Add(new Spinner { Options = ["Easy", "Normal", "Hard"] });
        await Assert.That(spinner.SelectedOption).IsEqualTo("Easy");
        spinner.Focus();
        rig.Send(Key(Left));
        await Assert.That(spinner.SelectedOption).IsEqualTo("Hard"); // wrapped
        rig.Send(Key(Right), Key(Right));
        await Assert.That(spinner.SelectedOption).IsEqualTo("Normal");
        await Assert.That(rig.Root.FocusedElement).IsSameReferenceAs(spinner);
        spinner.Wraps = false;
        spinner.Step(5);
        await Assert.That(spinner.SelectedOption).IsEqualTo("Hard");
        // The arrows click.
        rig.Click(spinner.Children[0].Children[0]);
        await Assert.That(spinner.SelectedOption).IsEqualTo("Normal");
        await Assert.That(spinner.ValueLabel.Text).IsEqualTo("Normal");
    }

    [Test]
    public async Task ComboBoxesPickFromAListInAPopup()
    {
        using Rig rig = new();
        ComboBox combo = rig.Add(new ComboBox { Options = ["Low", "Medium", "High", "Ultra"], SelectedIndex = 1 });
        int changes = 0;
        combo.SelectionChanged += _ => ++changes;
        rig.Click(combo);
        await Assert.That(combo.IsOpen).IsTrue();
        await Assert.That(rig.Root.FocusedElement is ListItem { Index: 1 }).IsTrue(); // on the selection
        rig.Send(Key(Down), Key(Return));
        await Assert.That(combo.IsOpen).IsFalse();
        await Assert.That(combo.SelectedOption).IsEqualTo("High");
        await Assert.That(rig.Root.FocusedElement).IsSameReferenceAs(combo);
        await Assert.That(changes).IsEqualTo(1);

        rig.Send(Key(Return)); // opens again from the keyboard
        await Assert.That(combo.IsOpen).IsTrue();
        rig.Send(Key(Down), Key(Escape)); // closed unchanged
        await Assert.That(combo.IsOpen).IsFalse();
        await Assert.That(combo.SelectedOption).IsEqualTo("High");
    }
}
