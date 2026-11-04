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
    private const int Return = 40, Right = 79, Left = 80, Down = 81, Up = 82;

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
}
