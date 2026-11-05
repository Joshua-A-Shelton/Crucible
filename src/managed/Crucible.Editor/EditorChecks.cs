using System.Numerics;
using Crucible.Loop;
using Crucible.Platform;
using Crucible.Render;
using Crucible.UI;
using Png = Crucible.Samples.Diorama.Png;

namespace Crucible.Editor;

/// <summary>
/// The editor checking itself headless (ADR 0066), for CTest's
/// launcher.jit.editor: run with <c>--game-arg check=&lt;file.png&gt;</c> (and
/// a settings file of its own). With synthetic input it opens a menu, drags
/// a tab with its drop preview showing (captured), docks it, tears a panel
/// off into a floating window and closes that window, docking it back,
/// checking the pixels drawn along the way. Prints its checks and quits.
/// </summary>
[System(Phases.RenderPrepare)]
internal sealed class EditorChecks : GameSystem
{
    private const int Width = 1280, Height = 720; // the launcher's headless surface (Host.cs)

    private IEnumerator<int>? _script;
    private int _wait;
    private int _checks;
    private int _failures;
    private string _capture = "";
    private FrameContext? _context;

    public override void Start(FrameContext context)
    {
        if (!context.GameArguments.TryGetValue("check", out string? capture))
        {
            Enabled = false;
            return;
        }
        _capture = capture;
        _script = Script().GetEnumerator();
        _wait = 3; // the first frames lay the editor out
    }

    public override void Run(FrameContext context)
    {
        _context = context;
        if (_script is null || _wait-- > 0)
        {
            return;
        }
        try
        {
            if (_script.MoveNext())
            {
                _wait = _script.Current;
                return;
            }
        }
        catch (Exception exception)
        {
            Check(false, "the script ran", exception.ToString());
        }
        _script = null;
        Console.WriteLine(_failures == 0
            ? $"EditorChecks: all {_checks} checks passed"
            : $"EditorChecks: {_failures} of {_checks} checks failed");
        context.QuitRequested = true;
    }

    // Each step yields the frames to wait before the next: two, for input
    // to be drawn and read back.
    private IEnumerable<int> Script()
    {
        Shell shell = Shell.Current ?? throw new InvalidOperationException("No editor shell");
        EditorWindow main = shell.Main;
        UIRoot root = main.Root;
        shell.RestoreLayout(null); // whatever the last run saved
        yield return 2;
        Check(main.DockSpace.Groups.Count() == 4, "the default layout's four groups", main.DockSpace.Groups.Count().ToString());

        // The menu bar and a placeholder panel are drawn.
        byte[] pixels = Read(main.Surface);
        Check(CountLight(pixels, main.MenuBar.Bounds, 0xc0) > 40, "the menu bar's titles are drawn");
        EditorPanel viewport = Panel(shell, "Viewport");
        Check(CountLight(pixels, viewport.Bounds, 0x80) > 40, "the viewport's placeholder is drawn");
        Check(CountLight(pixels, main.StatusBar.Bounds, 0x80) > 20, "the status bar is drawn");
        Check(main.TitleBar.Leading == main.MenuBar && main.TitleBar.Bounds.Top == 0, "the menus are in the title bar, on top");
        Check(CountLight(pixels, main.TitleBar.CloseButton.Bounds, 0xc0) > 5, "the title bar's close button is drawn");
        Check(CountLight(pixels, main.TitleBar.MaximizeButton.Bounds, 0xc0) > 5, "and its maximize button");

        // A click on File opens its menu over the panels.
        MenuItem file = BarItem(main.MenuBar, 0);
        Click(root, Center(file));
        yield return 2;
        Menu menu = main.MenuBar.Menus[0];
        Check(menu.IsOpen, "a click opened the File menu");
        pixels = Read(main.Surface);
        Check(CountLight(pixels, menu.Parent!.Bounds, 0xc0) > 60, "its items are drawn");
        Send(root, new InputEvent { Type = InputEventType.KeyDown, Code = 41 }); // Escape
        yield return 1;
        Check(!menu.IsOpen, "Escape closed it");

        // The log's tab dragged over the hierarchy: the preview covers it.
        EditorPanel log = Panel(shell, "Log"), hierarchy = Panel(shell, "Hierarchy");
        DockGroup target = hierarchy.Group!;
        Vector2 from = Center(Tab(log)), over = Center(target);
        Send(root, Motion(from), Button(from, true), Motion(from + new Vector2(20, 0)), Motion(over));
        yield return 2;
        pixels = Read(main.Surface);
        (int r, int g, int b) = Pixel(pixels, over + new Vector2(0, 40));
        Check(b > r + 30 && b > g + 10, "the drop preview tints the hierarchy", $"({r}, {g}, {b})");
        Save(pixels, _capture);
        Send(root, Button(over, false));
        yield return 1;
        Check(log.Group == target && target.ActivePanel == log, "dropped, the log joined the hierarchy's tabs");

        // The inspector's tab dragged off the window: torn off into a window
        // of its own (headless, no platform drag: it just floats).
        EditorPanel inspector = Panel(shell, "Inspector");
        from = Center(Tab(inspector));
        Send(root, Motion(from), Button(from, true), Motion(from + new Vector2(-20, 0)), Motion(new Vector2(Width + 40, from.Y)));
        Send(root, Button(new Vector2(Width + 40, from.Y), false));
        yield return 3;
        EditorWindow? floating = shell.Windows.FirstOrDefault(window => !window.IsMain);
        Check(floating is not null && floating.DockSpace.Panels.Contains(inspector), "torn off, the inspector floats in its own window");
        Check(!main.DockSpace.Panels.Contains(inspector), "and left the main window");
        if (floating is not null)
        {
            byte[] floatingPixels = Read(floating.Surface, floating.Root.Size);
            Check(CountLight(floatingPixels, Tab(inspector).Bounds, 0xc0, (int)floating.Root.Size.X) > 15,
                "the floating window draws the inspector's tab");
            Check(inspector.Group!.StripEnd == floating.FloatingButtons && floating.TitleBar.Visibility == Visibility.Collapsed,
                "its tab strip is its title bar");
            Check(CountLight(floatingPixels, floating.FloatingButtons.Bounds, 0xc0, (int)floating.Root.Size.X) > 15,
                "with the window's buttons drawn at its end");
            // Closing it docks its panels back.
            shell.ProcessEvents([new InputEvent { Type = InputEventType.WindowCloseRequested, Window = floating.Window }], []);
            yield return 1;
            Check(shell.Windows.Count == 1 && main.DockSpace.Panels.Contains(inspector),
                "closing the floating window docked the inspector back");
        }

        // The Window menu's commands close and reopen panels.
        Command assets = shell.Commands.Find("Window.Assets")!;
        assets.Execute();
        Check(!main.DockSpace.Panels.Any(panel => panel.Id == "Assets"), "Window > Assets closed it");
        assets.Execute();
        Check(main.DockSpace.Panels.Any(panel => panel.Id == "Assets"), "and opened it again");
    }

    private static EditorPanel Panel(Shell shell, string id) =>
        shell.Windows.SelectMany(window => window.DockSpace.Panels).Single(panel => panel.Id == id);

    private static DockTab Tab(EditorPanel panel) => panel.Group!.Tabs.OfType<DockTab>().Single(tab => tab.Panel == panel);

    private static MenuItem BarItem(MenuBar bar, int index)
    {
        List<MenuItem> items = [];
        Collect(bar, items);
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

    // Synthetic input in physical pixels (the headless root's scale is 1).
    private static void Send(UIRoot root, params InputEvent[] events) => root.ProcessInput(events);

    private static void Click(UIRoot root, Vector2 at) => Send(root, Motion(at), Button(at, true), Button(at, false));

    private static InputEvent Motion(Vector2 at) => new() { Type = InputEventType.MouseMotion, X = at.X, Y = at.Y };

    private static InputEvent Button(Vector2 at, bool down) =>
        new() { Type = down ? InputEventType.MouseButtonDown : InputEventType.MouseButtonUp, X = at.X, Y = at.Y, Code = 1 };

    private static Vector2 Center(UIElement element) =>
        new(element.Bounds.X + element.Bounds.Width / 2, element.Bounds.Y + element.Bounds.Height / 2);

    private static byte[] Read(SurfaceHandle surface, Vector2 size = default)
    {
        int width = size == default ? Width : (int)size.X, height = size == default ? Height : (int)size.Y;
        byte[] bgra = new byte[width * height * 4];
        Render.Api.ReadSurfacePixels(surface, bgra);
        return bgra;
    }

    private static (int R, int G, int B) Pixel(byte[] bgra, Vector2 at, int width = Width)
    {
        int i = ((int)at.Y * width + (int)at.X) * 4;
        return (bgra[i + 2], bgra[i + 1], bgra[i]);
    }

    // Light pixels (text) within a rectangle.
    private static int CountLight(byte[] bgra, System.Drawing.RectangleF rect, int light, int width = Width)
    {
        int height = bgra.Length / 4 / width, count = 0;
        for (int y = Math.Max((int)rect.Top, 0); y < Math.Min((int)rect.Bottom, height); ++y)
        {
            for (int x = Math.Max((int)rect.Left, 0); x < Math.Min((int)rect.Right, width); ++x)
            {
                count += bgra[(y * width + x) * 4 + 2] >= light ? 1 : 0;
            }
        }
        return count;
    }

    private static void Save(byte[] bgra, string path)
    {
        byte[] rgb = new byte[Width * Height * 3];
        for (int i = 0; i < Width * Height; ++i)
        {
            rgb[i * 3] = bgra[i * 4 + 2];
            rgb[i * 3 + 1] = bgra[i * 4 + 1];
            rgb[i * 3 + 2] = bgra[i * 4];
        }
        Png.Write(path, Width, Height, rgb);
    }

    private void Check(bool passed, string what, string detail = "")
    {
        ++_checks;
        if (!passed)
        {
            ++_failures;
            Console.WriteLine($"EditorChecks: FAILED: {what} {detail}");
        }
    }
}
