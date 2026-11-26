using System.Numerics;
using Crucible.Binary;
using Crucible.Platform;
using Crucible.Text;
using Crucible.UI;

namespace Crucible.Tests;

// Text that doesn't fit (ADR 0130), under JIT and NativeAOT: labels cut
// with an ellipsis, paths keeping their file name, the whole text as a
// tooltip, fields not being edited shown from the start, and a title bar's
// centered content.
[NotInParallel("text")]
public class TrimmingTests
{
    private sealed class Rig : IDisposable
    {
        public double Time = 10;
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

        public void Layout() => Root.UpdateLayout(new Vector2(500, 400));

        public void Send(params InputEvent[] events)
        {
            Root.ProcessInput(events);
            Layout();
        }

        public void Dispose()
        {
            Root.Content = null;
            Fonts.Unload(_font);
        }
    }

    [Test]
    public async Task LabelsEndInAnEllipsisWhereTheyAreCut()
    {
        using Rig rig = new();
        Label fits = rig.Add(new Label("Height") { Trimming = TextTrimming.End });
        Label cut = rig.Add(new Label("Orthographic Height") { Trimming = TextTrimming.End, Width = 70 });
        Label clipped = rig.Add(new Label("Orthographic Height") { Width = 70 });
        await Assert.That(fits.IsTrimmed).IsFalse();
        await Assert.That(fits.ShownText).IsEqualTo("Height");
        await Assert.That(cut.IsTrimmed).IsTrue();
        await Assert.That(cut.ShownText).StartsWith("Ortho");
        await Assert.That(cut.ShownText).EndsWith("…");
        await Assert.That(clipped.IsTrimmed).IsFalse(); // None: drawn whole, as before
        // Given room again, it's whole.
        cut.Width = float.NaN;
        rig.Layout();
        await Assert.That(cut.IsTrimmed).IsFalse();
        await Assert.That(cut.ShownText).IsEqualTo("Orthographic Height");
    }

    [Test]
    public async Task PathsKeepTheirFileName()
    {
        using Rig rig = new();
        const string path = "Tilemaps/Levels/Forest/Tilemap 4.crtilemap";
        Label label = rig.Add(new Label(path) { Trimming = TextTrimming.Path, Width = 200 });
        await Assert.That(label.IsTrimmed).IsTrue();
        await Assert.That(label.ShownText).EndsWith("…/Tilemap 4.crtilemap");
        await Assert.That(label.ShownText).StartsWith("T");
        // Too narrow even for the file name: cut at the end.
        label.Width = 60;
        rig.Layout();
        await Assert.That(label.ShownText).StartsWith("Tile");
        await Assert.That(label.ShownText).EndsWith("…");
    }

    [Test]
    public async Task ACutLabelShowsItsWholeTextAsATooltip()
    {
        using Rig rig = new();
        Label cut = rig.Add(new Label("Orthographic Height") { Trimming = TextTrimming.End, Width = 70 });
        Label own = rig.Add(new Label("Orthographic Height") { Trimming = TextTrimming.End, Width = 70, ToolTip = "Its own" });
        async Task Hover(Label label, string expected)
        {
            Vector2 at = new(label.Bounds.X + 10, label.Bounds.Y + label.Bounds.Height / 2);
            rig.Send(new InputEvent { Type = InputEventType.MouseMotion, X = at.X, Y = at.Y });
            rig.Time += 1;
            rig.Root.UpdateStyles();
            rig.Layout();
            await Assert.That(rig.Root.ShownToolTip).IsEqualTo(expected);
        }
        await Hover(cut, "Orthographic Height");
        await Hover(own, "Its own");
    }

    [Test]
    public async Task FieldsNotBeingEditedShowTheirStart()
    {
        using Rig rig = new();
        TextField field = rig.Add(new TextField { Width = 60, Text = "17.7836512345" });
        field.Focus();
        rig.Send(new InputEvent { Type = InputEventType.KeyDown, Code = 77 }); // End: scrolled to the end
        await Assert.That(field.ScrollX).IsGreaterThan(0f);
        rig.Root.ClearFocus();
        rig.Layout();
        await Assert.That(field.ScrollX).IsEqualTo(0f);
    }

    [Test]
    public async Task ATitleBarCentersItsCenterAndPutsTheTitleAfterTheMenus()
    {
        using Rig rig = new();
        MenuBar menus = new();
        menus.Add("File").Add("Quit", () => { });
        Label middle = new("Play");
        TitleBar bar = new() { Title = "Village.crscene* — Diorama — Crucible", Leading = menus, Center = middle, Width = 500 };
        rig.Root.Content = bar;
        rig.Layout();
        Label title = bar.Children.OfType<Label>().First(label => label != middle);
        float center = middle.Bounds.X + middle.Bounds.Width / 2;
        await Assert.That(center).IsEqualTo(bar.Bounds.X + bar.Bounds.Width / 2).Within(1f);
        await Assert.That(title.Bounds.X).IsGreaterThanOrEqualTo(menus.Bounds.Right);
        await Assert.That(title.Bounds.Right).IsLessThanOrEqualTo(middle.Bounds.X);
        // Narrow, the title is cut where it meets the center.
        bar.Width = 260;
        rig.Layout();
        await Assert.That(title.IsTrimmed).IsTrue();
        await Assert.That(title.Bounds.Right).IsLessThanOrEqualTo(middle.Bounds.X);
    }

    private static FontHandle LoadNotoSans()
    {
        byte[] font = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fonts", "NotoSans-Regular.ttf"));
        string path = Path.Combine(Path.GetTempPath(), "crucible-trimming-" + Guid.NewGuid().ToString("N") + ".crfont");
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
}
