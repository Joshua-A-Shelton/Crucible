using System.Numerics;
using Crucible.Binary;
using Crucible.Platform;
using Crucible.Text;
using Crucible.UI;

namespace Crucible.Tests;

// Menus, commands, and shortcuts (ADR 0066), under JIT and NativeAOT, driven
// by synthetic platform events with the default theme.
[NotInParallel("text")]
public class MenuTests
{
    private const int Return = 40, Escape = 41, Right = 79, Left = 80, Down = 81;

    private sealed class Rig : IDisposable
    {
        public double Time = 10;
        public readonly UIRoot Root = new();
        public readonly DockPanel Page = new();
        public readonly MenuBar Bar = new();
        private readonly FontHandle _font = LoadNotoSans();

        public Rig()
        {
            Root.DefaultFont = _font;
            DockPanel.SetDock(Bar, Dock.Top);
            Page.Add(Bar);
            Root.Content = Page;
            Root.TestClock = () => Time;
        }

        public void Layout() => Root.UpdateLayout(new Vector2(800, 600));

        public void Send(params InputEvent[] events)
        {
            Root.ProcessInput(events);
            Layout();
        }

        public void Click(Vector2 at)
        {
            Time += 1; // never a double-click
            Send(Motion(at), Button(at, true), Button(at, false));
        }

        public void Dispose()
        {
            Root.Content = null;
            Fonts.Unload(_font);
        }
    }

    private static FontHandle LoadNotoSans()
    {
        byte[] font = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fonts", "NotoSans-Regular.ttf"));
        string path = Path.Combine(Path.GetTempPath(), "crucible-menus-" + Guid.NewGuid().ToString("N") + ".crfont");
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

    private static InputEvent Motion(Vector2 at) => new() { Type = InputEventType.MouseMotion, X = at.X, Y = at.Y };

    private static InputEvent Button(Vector2 at, bool down) =>
        new() { Type = down ? InputEventType.MouseButtonDown : InputEventType.MouseButtonUp, X = at.X, Y = at.Y, Code = 1 };

    private static InputEvent Key(int scancode, uint keycode = 0, KeyModifiers modifiers = KeyModifiers.None) =>
        new() { Type = InputEventType.KeyDown, Code = scancode, Keycode = keycode, Modifiers = (uint)modifiers };

    private static Vector2 Center(UIElement element) =>
        new(element.Bounds.X + element.Bounds.Width / 2, element.Bounds.Y + element.Bounds.Height / 2);

    private static MenuItem BarItem(Rig rig, int index)
    {
        // The bar's items, in order (MenuBar keeps them in a row).
        List<MenuItem> items = [];
        Collect(rig.Bar, items);
        return items[index];
    }

    private static void Collect(UIElement element, List<MenuItem> into)
    {
        foreach (UIElement child in element.Children)
        {
            if (child is MenuItem item)
            {
                into.Add(item);
            }
            else
            {
                Collect(child, into);
            }
        }
    }

    [Test]
    public async Task ShortcutsRunTheirCommands()
    {
        int saves = 0;
        bool canSave = true;
        CommandRegistry commands = new();
        Command save = commands.Add(new Command("File.Save", "Save", () => ++saves, () => canSave)
        {
            Shortcut = new Shortcut(KeyModifiers.Ctrl, Keycode.S),
        });
        await Assert.That(save.Shortcut!.Value.ToString()).IsEqualTo("Ctrl+S");
        await Assert.That(new Shortcut(KeyModifiers.Ctrl | KeyModifiers.Shift, Keycode.F5).ToString()).IsEqualTo("Ctrl+Shift+F5");
        await Assert.That(() => commands.Add(new Command("File.Save", "Again", () => { }))).Throws<InvalidOperationException>();
        await Assert.That(commands.Find("File.Save")).IsSameReferenceAs(save);

        // Keys a focused element leaves alone bubble to whatever handles shortcuts.
        using Rig rig = new();
        rig.Page.KeyDown += (_, e) => commands.HandleShortcut(e);
        rig.Layout();
        rig.Send(Key(22, Keycode.S, KeyModifiers.Ctrl));
        await Assert.That(saves).IsEqualTo(1);
        await Assert.That(rig.Root.HandledKeyboard).IsTrue();
        rig.Send(Key(22, Keycode.S, KeyModifiers.Ctrl | KeyModifiers.Shift)); // not this shortcut
        await Assert.That(saves).IsEqualTo(1);
        canSave = false;
        rig.Send(Key(22, Keycode.S, KeyModifiers.Ctrl)); // taken, but it can't run now
        await Assert.That(saves).IsEqualTo(1);
        await Assert.That(rig.Root.HandledKeyboard).IsTrue();
    }

    [Test]
    public async Task MenuBarsOpenMenusAndRunTheirItems()
    {
        using Rig rig = new();
        int opened = 0, quits = 0;
        bool grid = true;
        Menu file = rig.Bar.Add("File");
        MenuItem open = file.Add(new Command("File.Open", "Open…", () => ++opened)
        {
            Shortcut = new Shortcut(KeyModifiers.Ctrl, Keycode.O),
        });
        file.AddSeparator();
        MenuItem disabled = file.Add(new Command("File.Save", "Save", () => { }, () => false));
        MenuItem toggle = file.Add(new Command("View.Grid", "Show grid", () => grid = !grid) { IsChecked = () => grid });
        file.Add("Quit", () => ++quits);
        Menu edit = rig.Bar.Add("Edit");
        edit.Add("Undo", () => { });
        rig.Layout();

        rig.Click(Center(BarItem(rig, 0)));
        await Assert.That(file.IsOpen).IsTrue();
        await Assert.That(file.Parent!.Bounds.Top).IsEqualTo(BarItem(rig, 0).Bounds.Bottom); // below its title
        await Assert.That(BarItem(rig, 0).Classes.Contains("open")).IsTrue();
        await Assert.That(open.IsFocused).IsTrue(); // the first item
        await Assert.That(disabled.IsEnabled).IsFalse();
        await Assert.That(toggle.IsChecked).IsTrue();
        await Assert.That(open.Shortcut!.Value.ToString()).IsEqualTo("Ctrl+O");

        rig.Click(Center(disabled)); // nothing happens, the menu stays
        await Assert.That(file.IsOpen).IsTrue();
        rig.Click(Center(open));
        await Assert.That(opened).IsEqualTo(1);
        await Assert.That(file.IsOpen).IsFalse();
        await Assert.That(BarItem(rig, 0).Classes.Contains("open")).IsFalse();

        // Hovering another title while a menu is open switches to it.
        rig.Click(Center(BarItem(rig, 0)));
        rig.Send(Motion(Center(BarItem(rig, 1))));
        await Assert.That(file.IsOpen).IsFalse();
        await Assert.That(edit.IsOpen).IsTrue();
        // A press outside every menu closes them, and is used up.
        rig.Click(new Vector2(400, 400));
        await Assert.That(edit.IsOpen).IsFalse();
        await Assert.That(rig.Root.HandledPointer).IsTrue();

        // An on-off command shows its state each time its menu opens.
        rig.Click(Center(BarItem(rig, 0)));
        rig.Click(Center(toggle));
        rig.Click(Center(BarItem(rig, 0)));
        await Assert.That(toggle.IsChecked).IsFalse();
    }

    [Test]
    public async Task KeysMoveThroughMenusAndSubmenus()
    {
        using Rig rig = new();
        string? chosen = null;
        Menu file = rig.Bar.Add("File");
        file.Add("New", () => chosen = "New");
        MenuItem recent = file.Add(new MenuItem("Recent"));
        Menu recentMenu = new();
        recentMenu.Add("One", () => chosen = "One");
        recentMenu.Add("Two", () => chosen = "Two");
        recent.Submenu = recentMenu;
        Menu edit = rig.Bar.Add("Edit");
        MenuItem undo = edit.Add("Undo", () => chosen = "Undo");
        rig.Layout();

        BarItem(rig, 0).Focus();
        rig.Send(Key(Return));
        await Assert.That(file.IsOpen).IsTrue();
        rig.Send(Key(Down));
        await Assert.That(recent.IsFocused).IsTrue();
        rig.Send(Key(Right));
        await Assert.That(recentMenu.IsOpen).IsTrue();
        await Assert.That(recentMenu.Parent!.Bounds.Left).IsGreaterThanOrEqualTo(file.Parent!.Bounds.Right - 0.01f); // beside
        rig.Send(Key(Left));
        await Assert.That(recentMenu.IsOpen).IsFalse();
        await Assert.That(recent.IsFocused).IsTrue();
        rig.Send(Key(Right), Key(Down), Key(Return));
        await Assert.That(chosen).IsEqualTo("Two");
        await Assert.That(file.IsOpen).IsFalse();

        // Left and right in a bar's menu step along the bar; Escape closes.
        BarItem(rig, 0).Focus();
        rig.Send(Key(Return), Key(Right));
        await Assert.That(file.IsOpen).IsFalse();
        await Assert.That(edit.IsOpen).IsTrue();
        await Assert.That(undo.IsFocused).IsTrue();
        rig.Send(Key(Escape));
        await Assert.That(edit.IsOpen).IsFalse();
    }

    [Test]
    public async Task SubmenusOpenOnHoverAndPressesInMenusBelowAct()
    {
        using Rig rig = new();
        string? chosen = null;
        Menu file = rig.Bar.Add("File");
        MenuItem recent = file.Add(new MenuItem("Recent"));
        Menu recentMenu = new();
        recentMenu.Add("One", () => chosen = "One");
        recent.Submenu = recentMenu;
        MenuItem quit = file.Add("Quit", () => chosen = "Quit");
        rig.Layout();

        rig.Click(Center(BarItem(rig, 0)));
        rig.Send(Motion(Center(recent)));
        await Assert.That(recentMenu.IsOpen).IsTrue();
        await Assert.That(recent.IsFocused).IsTrue(); // focus stays with the pointer
        rig.Send(Motion(Center(quit))); // a sibling closes it
        await Assert.That(recentMenu.IsOpen).IsFalse();
        rig.Send(Motion(Center(recent)));
        // A press in the menu below closes the submenu and acts there.
        rig.Click(Center(quit));
        await Assert.That(chosen).IsEqualTo("Quit");
        await Assert.That(file.IsOpen).IsFalse();
    }

    [Test]
    public async Task ContextMenusOpenAtThePointerOnTheRoot()
    {
        using Rig rig = new();
        Label target = new("Right-click me");
        rig.Page.Add(target);
        ContextMenu menu = new();
        int copies = 0;
        menu.Add("Copy", () => ++copies);
        menu.Add("Paste", () => { });
        rig.Layout();

        menu.Open(target, new Vector2(300, 200));
        rig.Layout();
        await Assert.That(menu.IsOpen).IsTrue();
        await Assert.That(menu.Parent!.Bounds.Location).IsEqualTo(new System.Drawing.PointF(300, 200));
        menu.Close();
        // Near the corner it's moved to stay on the root.
        menu.Open(target, new Vector2(790, 590));
        rig.Layout();
        System.Drawing.RectangleF bounds = menu.Parent!.Bounds;
        await Assert.That(bounds.Right).IsLessThanOrEqualTo(800f);
        await Assert.That(bounds.Bottom).IsLessThanOrEqualTo(600f);
        rig.Click(Center((UIElement)menu.Items[0]));
        await Assert.That(copies).IsEqualTo(1);
        await Assert.That(menu.IsOpen).IsFalse();
    }
}
