using System.Numerics;
using Crucible.Binary;
using Crucible.Platform;
using Crucible.Text;
using Crucible.UI;

namespace Crucible.Tests;

// Title bars of windows drawing their own frame (ADR 0066), under JIT and
// NativeAOT, with the window calls recorded instead of made.
[NotInParallel("text")]
public class TitleBarTests
{
    private sealed class Recorder : IWindowControls
    {
        public readonly List<string> Calls = [];
        public bool Maximized;

        public bool StartMove(WindowHandle window)
        {
            Calls.Add("move");
            return true;
        }

        public void Minimize(WindowHandle window) => Calls.Add("minimize");

        public void ToggleMaximized(WindowHandle window)
        {
            Maximized = !Maximized;
            Calls.Add(Maximized ? "maximize" : "restore");
        }

        public bool IsMaximized(WindowHandle window) => Maximized;

        public void ShowMenu(WindowHandle window, Vector2 pixels) => Calls.Add($"menu {pixels.X:0} {pixels.Y:0}");
    }

    private sealed class Rig : IDisposable
    {
        public double Time = 10;
        public readonly UIRoot Root = new() { Scale = 1.25f };
        public readonly TitleBar Bar = new() { Title = "Crucible" };
        public readonly MenuBar Menus = new();
        public readonly Recorder Controls = new();
        private readonly IWindowControls _before = TitleBar.Controls;
        private readonly FontHandle _font = LoadNotoSans();

        public Rig()
        {
            TitleBar.Controls = Controls;
            Menus.Add("File").Add("Quit", () => { });
            Bar.Leading = Menus;
            DockPanel page = new();
            DockPanel.SetDock(Bar, Dock.Top);
            page.Add(Bar);
            Root.DefaultFont = _font;
            Root.Content = page;
            Root.TestClock = () => Time;
            Layout();
        }

        public void Layout() => Root.UpdateLayout(new Vector2(1000, 600) * Root.Scale);

        // Events in the root's units, sent as the platform's pixels.
        public void Send(params InputEvent[] events)
        {
            Root.ProcessInput(events);
            Layout();
        }

        public InputEvent Motion(Vector2 at) => new() { Type = InputEventType.MouseMotion, X = at.X * Root.Scale, Y = at.Y * Root.Scale };

        public InputEvent Button(Vector2 at, bool down, int button = 1) => new()
        {
            Type = down ? InputEventType.MouseButtonDown : InputEventType.MouseButtonUp,
            X = at.X * Root.Scale,
            Y = at.Y * Root.Scale,
            Code = button,
        };

        public void Click(Vector2 at, int button = 1) => Send(Motion(at), Button(at, true, button), Button(at, false, button));

        public void Dispose()
        {
            TitleBar.Controls = _before;
            Root.Content = null;
            Fonts.Unload(_font);
        }
    }

    private static FontHandle LoadNotoSans()
    {
        byte[] font = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fonts", "NotoSans-Regular.ttf"));
        string path = Path.Combine(Path.GetTempPath(), "crucible-titlebar-" + Guid.NewGuid().ToString("N") + ".crfont");
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

    private static Vector2 Center(UIElement element) =>
        new(element.Bounds.X + element.Bounds.Width / 2, element.Bounds.Y + element.Bounds.Height / 2);

    [Test]
    public async Task MenusSitLeftButtonsRightAndTheTitleCentered()
    {
        using Rig rig = new();
        System.Drawing.RectangleF bar = rig.Bar.Bounds;
        await Assert.That(rig.Menus.Bounds.Left).IsEqualTo(bar.Left + rig.Bar.Padding.Left).Within(0.5f);
        await Assert.That(rig.Bar.CloseButton.Bounds.Right).IsEqualTo(bar.Right).Within(0.5f);
        await Assert.That(rig.Bar.MinimizeButton.Bounds.Right).IsLessThanOrEqualTo(rig.Bar.MaximizeButton.Bounds.Left + 0.5f);
        Label title = rig.Bar.Children.OfType<Label>().Single();
        await Assert.That(Center(title).X).IsEqualTo(bar.Left + bar.Width / 2).Within(1f);
        await Assert.That(bar.Height).IsGreaterThanOrEqualTo(32f);
    }

    [Test]
    public async Task DraggingMovesDoubleClickingMaximizesAndRightClickingShowsTheMenu()
    {
        using Rig rig = new();
        Vector2 empty = new(rig.Bar.Bounds.Left + rig.Bar.Bounds.Width * 0.7f, Center(rig.Bar).Y);
        // A press that doesn't move is just a click; one that moves, a move by the system.
        rig.Click(empty);
        rig.Time += 1;
        rig.Send(rig.Motion(empty), rig.Button(empty, true), rig.Motion(empty + new Vector2(10, 0)), rig.Button(empty, false));
        await Assert.That(string.Join(",", rig.Controls.Calls)).IsEqualTo("move");

        rig.Time += 1;
        rig.Click(empty);
        rig.Time += 0.1;
        rig.Click(empty);
        await Assert.That(rig.Controls.Calls[^1]).IsEqualTo("maximize");

        rig.Time += 1;
        rig.Click(empty, 3);
        await Assert.That(rig.Controls.Calls[^1]).IsEqualTo($"menu {empty.X * 1.25f:0} {empty.Y * 1.25f:0}"); // pixels

        // The title counts as empty; the menu bar doesn't.
        int calls = rig.Controls.Calls.Count;
        Vector2 file = Center(rig.Menus.Children.Single().Children.Single());
        rig.Time += 1;
        rig.Send(rig.Motion(file), rig.Button(file, true), rig.Motion(file + new Vector2(10, 0)), rig.Button(file, false));
        await Assert.That(rig.Controls.Calls.Count).IsEqualTo(calls);
    }

    [Test]
    public async Task ButtonsMinimizeMaximizeAndLeaveClosingToTheirOwner()
    {
        using Rig rig = new();
        int closes = 0;
        rig.Bar.CloseButton.Activated += (_, _) => ++closes;
        rig.Click(Center(rig.Bar.MinimizeButton));
        rig.Time += 1;
        rig.Click(Center(rig.Bar.MaximizeButton));
        rig.Time += 1;
        rig.Click(Center(rig.Bar.MaximizeButton));
        rig.Time += 1;
        rig.Click(Center(rig.Bar.CloseButton));
        await Assert.That(string.Join(",", rig.Controls.Calls)).IsEqualTo("minimize,maximize,restore");
        await Assert.That(closes).IsEqualTo(1);
        await Assert.That(rig.Bar.CloseButton.Classes.Contains("close")).IsTrue();
    }
}
