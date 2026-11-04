using System.Numerics;
using System.Text.Json.Nodes;
using Crucible.Loop;
using Crucible.Platform;
using Crucible.UI;

namespace Crucible.Editor.Tests;

// Docking, panels, layouts, and settings (ADR 0066), driven by synthetic
// platform events. The platform isn't started here, so tearing panels off
// into windows is left to launcher.jit.editor and to trying it by hand.
[NotInParallel("text")]
public class DockingTests
{
    private sealed class TestPanel(string id) : EditorPanel(id, id);

    // A shell on a test frame context (no window, no surface), its settings in a temporary file.
    private sealed class Rig : IDisposable
    {
        public readonly string Folder = Path.Combine(Path.GetTempPath(), "crucible-editor-" + Guid.NewGuid().ToString("N"));
        public readonly Shell Shell;

        public Rig()
        {
            Directory.CreateDirectory(Folder);
            Shell = new Shell(new FrameContext(), SettingsPath);
            Layout();
        }

        public string SettingsPath => Path.Combine(Folder, "editor.json");

        public UIRoot Root => Shell.Main.Root;

        public DockSpace Space => Shell.Main.DockSpace;

        public void Layout() => Root.UpdateLayout(new Vector2(1280, 720));

        public void Send(params InputEvent[] events)
        {
            Root.ProcessInput(events);
            Layout();
        }

        // Presses at one point, moves through the others, releases at the last.
        public void Drag(params Vector2[] points)
        {
            List<InputEvent> events = [Motion(points[0]), Button(points[0], true)];
            foreach (Vector2 point in points.Skip(1))
            {
                events.Add(Motion(point));
            }
            events.Add(Button(points[^1], false));
            foreach (InputEvent input in events)
            {
                Send(input);
            }
        }

        public void Dispose()
        {
            Shell.Stop();
            Directory.Delete(Folder, true);
        }
    }

    private static InputEvent Motion(Vector2 at) => new() { Type = InputEventType.MouseMotion, X = at.X, Y = at.Y };

    private static InputEvent Button(Vector2 at, bool down, int button = 1) =>
        new() { Type = down ? InputEventType.MouseButtonDown : InputEventType.MouseButtonUp, X = at.X, Y = at.Y, Code = button };

    private static Vector2 Center(UIElement element) =>
        new(element.Bounds.X + element.Bounds.Width / 2, element.Bounds.Y + element.Bounds.Height / 2);

    private static DockTab TabOf(EditorPanel panel) =>
        panel.Group!.Tabs.OfType<DockTab>().Single(tab => tab.Panel == panel);

    private static EditorPanel Open(Rig rig, string id) => rig.Space.Panels.Single(panel => panel.Id == id);

    [Test]
    public async Task PanelsDockBesideAndAmongTabsAndEmptiedNodesGo()
    {
        using Rig rig = new();
        rig.Shell.RestoreLayout(null);
        DockSpace space = new();
        rig.Root.Content = space; // a space of its own, under the shell's root
        TestPanel a = new("A"), b = new("B"), c = new("C"), d = new("D"), e = new("E");
        space.Dock(a, null, DockTarget.Center);
        await Assert.That(space.Node).IsTypeOf<DockGroup>();
        DockGroup first = a.Group!;
        space.Dock(b, first, DockTarget.Center);
        await Assert.That(first.Panels.Count).IsEqualTo(2);
        await Assert.That(first.ActivePanel).IsSameReferenceAs(b);

        space.Dock(c, first, DockTarget.Right);
        var split = (DockSplit)space.Node!;
        await Assert.That(split.Orientation).IsEqualTo(Orientation.Horizontal);
        await Assert.That(split.Nodes[1]).IsSameReferenceAs(c.Group);
        // Beside a group in a split going the same way: a sibling sharing its room.
        space.Dock(d, c.Group, DockTarget.Right);
        await Assert.That(split.Nodes.Count).IsEqualTo(3);
        await Assert.That(split.Sizes[1]).IsEqualTo(0.25f).Within(0.001f);
        await Assert.That(split.Sizes[2]).IsEqualTo(0.25f).Within(0.001f);
        // Across: a split of its own in the group's place.
        space.Dock(e, first, DockTarget.Top);
        var nested = (DockSplit)split.Nodes[0];
        await Assert.That(nested.Orientation).IsEqualTo(Orientation.Vertical);
        await Assert.That(nested.Nodes[0]).IsSameReferenceAs(e.Group);

        // Taking panels out: an emptied group goes, and a split left with one node gives way to it.
        space.Take(e);
        await Assert.That(split.Nodes[0]).IsSameReferenceAs(first);
        space.Take(c);
        space.Take(d);
        await Assert.That(space.Node).IsSameReferenceAs(first);
        space.Take(a);
        space.Take(b);
        await Assert.That(space.Node).IsNull();
    }

    [Test]
    public async Task DropsGoAmongTabsInTheMiddleAndBesideNearTheEdges()
    {
        using Rig rig = new();
        DockGroup viewport = Open(rig, "Viewport").Group!;
        System.Drawing.RectangleF b = viewport.Bounds;
        await Assert.That(rig.Space.TargetAt(Center(viewport))).IsEqualTo((viewport, DockTarget.Center));
        await Assert.That(rig.Space.TargetAt(new Vector2(b.X + 4, b.Y + b.Height / 2))).IsEqualTo((viewport, DockTarget.Left));
        await Assert.That(rig.Space.TargetAt(new Vector2(b.Right - 4, b.Y + b.Height / 2))).IsEqualTo((viewport, DockTarget.Right));
        await Assert.That(rig.Space.TargetAt(new Vector2(b.X + b.Width / 2, b.Bottom - 4))).IsEqualTo((viewport, DockTarget.Bottom));
        await Assert.That(rig.Space.TargetAt(new Vector2(-10, -10))).IsNull();
    }

    [Test]
    public async Task DraggingATabDocksItWhereItsDropped()
    {
        using Rig rig = new();
        EditorPanel log = Open(rig, "Log"), hierarchy = Open(rig, "Hierarchy"), viewport = Open(rig, "Viewport");
        DockGroup hierarchyGroup = hierarchy.Group!;

        // Over the hierarchy's middle, the preview covers it; dropped there, the log joins its tabs.
        Vector2 from = Center(TabOf(log));
        rig.Send(Motion(from), Button(from, true), Motion(from + new Vector2(20, 0)), Motion(Center(hierarchyGroup)));
        await Assert.That(rig.Space.PreviewArea).IsEqualTo(hierarchyGroup.Bounds);
        rig.Send(Button(Center(hierarchyGroup), false));
        await Assert.That(log.Group).IsSameReferenceAs(hierarchyGroup);
        await Assert.That(hierarchyGroup.ActivePanel).IsSameReferenceAs(log);
        await Assert.That(rig.Space.PreviewArea).IsNull();

        // Dropped near the viewport's top, it splits the viewport's group.
        System.Drawing.RectangleF v = viewport.Group!.Bounds;
        rig.Drag(Center(TabOf(log)), Center(TabOf(log)) + new Vector2(10, 10), new Vector2(v.X + v.Width / 2, v.Y + 30));
        await Assert.That(log.Group!.Parent).IsTypeOf<DockSplit>();
        var split = (DockSplit)log.Group.Parent!;
        await Assert.That(split.Orientation).IsEqualTo(Orientation.Vertical);
        await Assert.That(split.Nodes[0]).IsSameReferenceAs(log.Group);

        // A short press only shows the tab's panel.
        rig.Drag(Center(TabOf(hierarchy)), Center(TabOf(hierarchy)) + new Vector2(2, 0));
        await Assert.That(hierarchyGroup.ActivePanel).IsSameReferenceAs(hierarchy);
    }

    [Test]
    public async Task SplittersMoveTheBoundaryAndKeepAMinimum()
    {
        using Rig rig = new();
        DockGroup hierarchy = Open(rig, "Hierarchy").Group!;
        var columns = (DockSplit)hierarchy.Parent!;
        Splitter splitter = columns.Children.OfType<Splitter>().First();
        float before = hierarchy.Bounds.Width;
        rig.Drag(Center(splitter), Center(splitter) + new Vector2(60, 0));
        await Assert.That(hierarchy.Bounds.Width).IsEqualTo(before + 60).Within(1f);
        rig.Drag(Center(splitter), Center(splitter) - new Vector2(2000, 0));
        await Assert.That(hierarchy.Bounds.Width).IsGreaterThanOrEqualTo(45f); // the minimum, less the group's margin
    }

    [Test]
    public async Task TabsCloseAndTheWindowMenuOpensPanelsAgain()
    {
        using Rig rig = new();
        EditorPanel assets = Open(rig, "Assets");
        Command toggle = rig.Shell.Commands.Find("Window.Assets")!;
        await Assert.That(toggle.IsChecked!()).IsTrue();
        DockTab tab = TabOf(assets);
        // The close button sits at the tab's right end.
        Vector2 close = new(tab.Bounds.Right - tab.Padding.Right - 7, tab.Bounds.Y + tab.Bounds.Height / 2);
        rig.Send(Motion(close), Button(close, true), Button(close, false));
        await Assert.That(rig.Space.Panels.Any(panel => panel.Id == "Assets")).IsFalse();
        await Assert.That(toggle.IsChecked!()).IsFalse();
        toggle.Execute();
        rig.Layout();
        await Assert.That(rig.Space.Panels.Any(panel => panel.Id == "Assets")).IsTrue();
        // A right-click offers Close.
        Vector2 at = Center(TabOf(Open(rig, "Assets")));
        rig.Send(Motion(at), Button(at, true, 3), Button(at, false, 3));
        await Assert.That(rig.Root.Popups.Count).IsEqualTo(1);
    }

    [Test]
    public async Task LayoutsRoundTripAndBadOnesFallBackToTheDefault()
    {
        using Rig rig = new();
        EditorPanel log = Open(rig, "Log");
        rig.Space.Dock(log, Open(rig, "Hierarchy").Group, DockTarget.Bottom);
        rig.Layout();
        JsonObject saved = rig.Shell.SaveLayout();
        string before = saved.ToJsonString();

        rig.Shell.RestoreLayout(null);
        rig.Layout();
        await Assert.That(rig.Shell.SaveLayout().ToJsonString()).IsNotEqualTo(before);
        rig.Shell.RestoreLayout(saved);
        rig.Layout();
        await Assert.That(Same(rig.Shell.SaveLayout()["Main"], saved["Main"])).IsTrue();

        // Unknown and repeated panels are skipped; nothing usable means the default.
        rig.Shell.RestoreLayout(JsonNode.Parse("""{ "Main": { "Panels": ["Nope", "Log", "Log"] } }""")!.AsObject());
        await Assert.That(string.Join(",", rig.Space.Panels.Select(panel => panel.Id))).IsEqualTo("Log");
        rig.Shell.RestoreLayout(JsonNode.Parse("""{ "Main": { "Children": [] } }""")!.AsObject());
        await Assert.That(rig.Space.Panels.Count()).IsEqualTo(5);
    }

    // The same tree: the same panels and splits, sizes within float rounding.
    private static bool Same(JsonNode? a, JsonNode? b) => (a, b) switch
    {
        (JsonObject x, JsonObject y) => x.Count == y.Count && x.All(pair => y.ContainsKey(pair.Key) && Same(pair.Value, y[pair.Key])),
        (JsonArray x, JsonArray y) => x.Count == y.Count && x.Zip(y).All(pair => Same(pair.First, pair.Second)),
        (JsonValue x, JsonValue y) when x.GetValueKind() == System.Text.Json.JsonValueKind.Number =>
            Math.Abs(double.Parse(x.ToJsonString(), System.Globalization.CultureInfo.InvariantCulture) -
                     double.Parse(y.ToJsonString(), System.Globalization.CultureInfo.InvariantCulture)) < 1e-4,
        (JsonValue x, JsonValue y) => x.ToJsonString() == y.ToJsonString(),
        (null, null) => true,
        _ => false,
    };

    [Test]
    public async Task SettingsKeepRecentProjectsAndTheLayout()
    {
        using Rig rig = new();
        EditorSettings settings = new();
        for (int i = 0; i < 12; ++i)
        {
            settings.AddRecentProject(Path.Combine(rig.Folder, $"P{i}.crproject"));
        }
        settings.AddRecentProject(Path.Combine(rig.Folder, "P5.crproject"));
        await Assert.That(settings.RecentProjects.Count).IsEqualTo(EditorSettings.MaxRecentProjects);
        await Assert.That(settings.RecentProjects[0]).EndsWith("P5.crproject");
        await Assert.That(settings.RecentProjects.Count(path => path.EndsWith("P5.crproject"))).IsEqualTo(1);
        settings.Layout = rig.Shell.SaveLayout();
        string path = Path.Combine(rig.Folder, "saved.json");
        settings.Save(path);
        EditorSettings loaded = EditorSettings.Load(path);
        await Assert.That(string.Join("|", loaded.RecentProjects)).IsEqualTo(string.Join("|", settings.RecentProjects));
        await Assert.That(Same(loaded.Layout, settings.Layout)).IsTrue();

        File.WriteAllText(path, "{ not json");
        EditorSettings broken = EditorSettings.Load(path);
        await Assert.That(broken.RecentProjects.Count).IsEqualTo(0);
        await Assert.That(broken.Layout).IsNull();

        // The shell saves its layout on stop, and starts from it.
        rig.Shell.TogglePanel("Inspector");
        rig.Shell.Stop();
        Shell again = new(new FrameContext(), rig.SettingsPath);
        await Assert.That(again.Main.DockSpace.Panels.Any(panel => panel.Id == "Inspector")).IsFalse();
        again.Stop();
    }

    [Test]
    public async Task MenuShortcutsRunFromAnyFocus()
    {
        using Rig rig = new();
        bool quit = false;
        rig.Shell.Commands.Add(new Command("Test.Quit", "Quit", () => quit = true)
        {
            Shortcut = new Shortcut(KeyModifiers.Ctrl | KeyModifiers.Shift, Keycode.Q),
        });
        TabOf(Open(rig, "Log")).Focus();
        rig.Send(new InputEvent
        {
            Type = InputEventType.KeyDown,
            Code = 20,
            Keycode = Keycode.Q,
            Modifiers = (uint)(KeyModifiers.Ctrl | KeyModifiers.Shift),
        });
        await Assert.That(quit).IsTrue();
        await Assert.That(string.Join(",", rig.Shell.Main.MenuBar.Menus.Select(menu => menu.Title))).IsEqualTo("File,Edit,Window,Help");
    }
}
