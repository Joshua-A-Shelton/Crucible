using System.Numerics;
using System.Text.Json.Nodes;
using Crucible.Assets;
using Crucible.Binary;
using Crucible.Loop;
using Crucible.Platform;
using Crucible.Render;
using Crucible.Text;
using Crucible.UI;

namespace Crucible.Editor;

/// <summary>
/// The editor's shell (ADR 0066): its windows, menus and commands, panels and
/// their docking, the saved layout, and opening projects. Main thread only.
/// </summary>
internal sealed class Shell
{
    // Each root's shell, for elements finding theirs.
    private static readonly Dictionary<UIRoot, Shell> _shells = [];

    private readonly FrameContext _context;
    private readonly string? _settingsPath;
    private readonly List<EditorWindow> _windows = [];
    private readonly Dictionary<string, EditorPanel> _open = new(StringComparer.Ordinal);
    private readonly Theme _theme;
    private readonly FontHandle _font;
    private readonly Menu _recentMenu = new();
    private readonly MenuItem _recentItem = new("Recent Projects");
    private TabDrag? _drag;
    private bool _stopped;
    // CRUCIBLE_DEBUG_DRAGS=1: the platform's drag events, as the editor sees them.
    private readonly bool _debugDrags = Environment.GetEnvironmentVariable("CRUCIBLE_DEBUG_DRAGS") == "1";
    private uint _debugOver = uint.MaxValue;

    public Shell(FrameContext context, string? settingsPath)
    {
        Current = this;
        _context = context;
        _settingsPath = settingsPath;
        Settings = EditorSettings.Load(settingsPath);
        _font = LoadFont();
        _theme = CreateTheme();
        Main = new EditorWindow(context.Window, context.Surface, true, false, _theme, _font);
        Add(Main);
        RegisterPanels();
        RegisterCommands();
        BuildMenus();
        RestoreLayout(Settings.Layout);
        Status("No project open");
        Log($"{EngineInfo.Name} editor started (window drags: {Platform.Api.WindowDragSupport()})");
    }

    /// <summary>The running editor's shell (one per process), for its checks and tests.</summary>
    public static Shell? Current { get; private set; }

    public EditorSettings Settings { get; }

    public CommandRegistry Commands { get; } = new();

    public PanelRegistry Panels { get; } = new();

    public EditorWindow Main { get; }

    public IReadOnlyList<EditorWindow> Windows => _windows;

    public Project? Project { get; private set; }

    public LogPanel? LogPanel => _open.GetValueOrDefault("Log") as LogPanel;

    /// <summary>The editor holding an element, through its root.</summary>
    public static Shell? Of(UIElement element) =>
        element.Root is { } root && _shells.TryGetValue(root, out Shell? shell) ? shell : null;

    public EditorWindow? WindowOf(UIElement element) => _windows.Find(window => window.Root == element.Root);

    // -----------------------------------------------------------------------
    // Frames
    // -----------------------------------------------------------------------

    /// <summary>The frame's platform events the UI doesn't route: drags, closing floating windows, dialogs.</summary>
    public void ProcessEvents(ReadOnlySpan<InputEvent> events, ReadOnlySpan<byte> text)
    {
        foreach (ref readonly InputEvent input in events)
        {
            switch (input.Type)
            {
                case InputEventType.DragMoved:
                    PlatformDragMoved(input.Window, new Vector2(input.X, input.Y));
                    break;
                case InputEventType.DragDropped:
                    PlatformDragDropped(input.Window, new Vector2(input.X, input.Y));
                    break;
                case InputEventType.DragCanceled:
                    PlatformDragEnded();
                    break;
                case InputEventType.WindowCloseRequested:
                    uint closed = input.Window.Value;
                    if (_windows.Find(window => !window.IsMain && window.Window.Value == closed) is { } floating)
                    {
                        DockBack(floating);
                    }
                    break;
                case InputEventType.FileDialogResult:
                    if (input.Value > 0 && input.Code >= 0 && input.Code + input.Value <= text.Length)
                    {
                        OpenProject(System.Text.Encoding.UTF8.GetString(text.Slice(input.Code, input.Value)));
                    }
                    break;
            }
        }
    }

    /// <summary>Renders the floating windows; the launcher renders the main one.</summary>
    public void RenderFloating()
    {
        foreach (EditorWindow window in _windows)
        {
            if (!window.IsMain)
            {
                window.UpdateFrame(); // its title bar follows its tree
                Status status = Render.Api.RenderFrame(window.Surface, default, 0, 0.02f, 0.02f, 0.025f, 1);
                if (status < 0)
                {
                    Log($"A floating window failed to render ({status})");
                }
            }
        }
    }

    public void Stop()
    {
        if (_stopped)
        {
            return;
        }
        _stopped = true;
        Settings.Layout = SaveLayout();
        SaveSettings();
        foreach (EditorWindow window in _windows.ToArray())
        {
            Remove(window);
        }
        Fonts.Unload(_font);
        if (Current == this)
        {
            Current = null;
        }
    }

    // -----------------------------------------------------------------------
    // Projects
    // -----------------------------------------------------------------------

    public void OpenProject(string projectFile)
    {
        try
        {
            Project = Project.Open(projectFile);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException
                                              or System.Text.Json.JsonException or ArgumentException)
        {
            Log($"Can't open {projectFile}: {exception.Message}");
            return;
        }
        Settings.AddRecentProject(Project.ProjectFilePath);
        SaveSettings();
        RebuildRecent();
        Status($"{Project.Name} — {Project.ProjectFilePath}");
        Main.TitleBar.Title = $"{Project.Name} — {EngineInfo.Name}";
        Log($"Opened {Project.Name}");
    }

    private void SaveSettings()
    {
        try
        {
            Settings.Save(_settingsPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Crucible.Editor: can't save {_settingsPath}: {exception.Message}");
        }
    }

    // -----------------------------------------------------------------------
    // Commands and menus
    // -----------------------------------------------------------------------

    private void RegisterCommands()
    {
        Commands.Add(new Command("File.OpenProject", "Open Project…",
            () => Platform.Api.ShowOpenFileDialog(Main.Window, "Crucible projects", "crproject"))
        {
            Shortcut = new Shortcut(KeyModifiers.Ctrl, Keycode.O),
        });
        Commands.Add(new Command("File.Quit", "Quit", () => _context.QuitRequested = true)
        {
            Shortcut = new Shortcut(KeyModifiers.Ctrl, Keycode.Q),
        });
        // The editor's undo comes with the hierarchy and inspector (M7 step 4).
        Commands.Add(new Command("Edit.Undo", "Undo", () => { }, () => false)
        {
            Shortcut = new Shortcut(KeyModifiers.Ctrl, Keycode.Z),
        });
        Commands.Add(new Command("Edit.Redo", "Redo", () => { }, () => false)
        {
            Shortcut = new Shortcut(KeyModifiers.Ctrl, Keycode.Y),
        });
        foreach ((string id, string title) in Panels.Panels)
        {
            Commands.Add(new Command($"Window.{id}", title, () => TogglePanel(id)) { IsChecked = () => _open.ContainsKey(id) });
        }
        Commands.Add(new Command("Window.ResetLayout", "Reset Layout", () => RestoreLayout(null)));
        Commands.Add(new Command("Help.About", $"About {EngineInfo.Name}",
            () => Log($"{EngineInfo.Name} editor, M7 (ADR 0016, 0066)")));
    }

    private void BuildMenus()
    {
        Menu file = Main.MenuBar.Add("File");
        file.Add(Commands.Find("File.OpenProject")!);
        _recentItem.Submenu = _recentMenu;
        file.Add(_recentItem);
        RebuildRecent();
        file.AddSeparator();
        file.Add(Commands.Find("File.Quit")!);

        Menu edit = Main.MenuBar.Add("Edit");
        edit.Add(Commands.Find("Edit.Undo")!);
        edit.Add(Commands.Find("Edit.Redo")!);

        Menu window = Main.MenuBar.Add("Window");
        foreach ((string id, _) in Panels.Panels)
        {
            window.Add(Commands.Find($"Window.{id}")!);
        }
        window.AddSeparator();
        window.Add(Commands.Find("Window.ResetLayout")!);

        Menu help = Main.MenuBar.Add("Help");
        help.Add(Commands.Find("Help.About")!);
    }

    private void RebuildRecent()
    {
        Menu recent = new();
        foreach (string project in Settings.RecentProjects)
        {
            recent.Add(project, () => OpenProject(project));
        }
        if (Settings.RecentProjects.Count == 0)
        {
            MenuItem none = recent.Add(new MenuItem("No recent projects"));
            none.State |= UIState.Disabled;
        }
        _recentItem.Submenu = recent;
    }

    // Keys no focused element used: the commands' shortcuts.
    private void HandleKeys(UIElement element, KeyEvent e) => Commands.HandleShortcut(e);

    // -----------------------------------------------------------------------
    // Panels
    // -----------------------------------------------------------------------

    private void RegisterPanels()
    {
        Panels.Register("Hierarchy", "Hierarchy", () => new HierarchyPanel());
        Panels.Register("Viewport", "Viewport", () => new ViewportPanel());
        Panels.Register("Inspector", "Inspector", () => new InspectorPanel());
        Panels.Register("Assets", "Assets", () => new AssetsPanel());
        Panels.Register("Log", "Log", () => new LogPanel());
    }

    // The open panel of an ID, made if it isn't open.
    private EditorPanel? Panel(string id)
    {
        if (_open.TryGetValue(id, out EditorPanel? panel))
        {
            return panel;
        }
        panel = Panels.Create(id);
        if (panel is not null)
        {
            _open[id] = panel;
        }
        return panel;
    }

    public void TogglePanel(string id)
    {
        if (_open.TryGetValue(id, out EditorPanel? panel))
        {
            ClosePanel(panel);
        }
        else if (Panel(id) is { } made)
        {
            Main.DockSpace.Dock(made, null, DockTarget.Center);
        }
    }

    public void ClosePanel(EditorPanel panel)
    {
        EditorWindow? window = WindowOf(panel);
        window?.DockSpace.Take(panel);
        _open.Remove(panel.Id);
        if (window is { IsMain: false } && !window.DockSpace.Panels.Any())
        {
            Remove(window);
        }
    }

    public void ShowTabMenu(DockTab tab, Vector2 at)
    {
        EditorPanel panel = tab.Panel;
        ContextMenu menu = new();
        menu.Add("Close", () => ClosePanel(panel));
        if (WindowOf(tab) is { IsMain: false } floating)
        {
            menu.Add("Dock to Main Window", () => DockBack(floating));
        }
        menu.Open(tab, at);
    }

    // A floating window's panels go back into the main window, and it closes.
    public void DockBack(EditorWindow floating)
    {
        foreach (EditorPanel panel in floating.DockSpace.Panels.ToArray())
        {
            Main.DockSpace.Dock(panel, null, DockTarget.Center);
        }
        Remove(floating);
    }

    // -----------------------------------------------------------------------
    // Dragging tabs (ADR 0066): within a window by the UI's own pointer
    // capture; out of it, torn off into a floating window, and then by the
    // platform's window drag across windows.
    // -----------------------------------------------------------------------

    private sealed class TabDrag(EditorPanel panel, EditorWindow source)
    {
        public EditorPanel Panel { get; } = panel;
        public EditorWindow Source { get; } = source;
        // Once the platform carries it: the window dragged (a torn-off one).
        public EditorWindow? Dragged { get; set; }
    }

    public bool BeginTabDrag(DockTab tab, Vector2 at)
    {
        if (_drag is not null || WindowOf(tab) is not { } window)
        {
            return false;
        }
        _drag = new TabDrag(tab.Panel, window);
        TabDragMoved(tab, at);
        return true;
    }

    public void TabDragMoved(DockTab tab, Vector2 at)
    {
        if (_drag is not { Dragged: null } drag)
        {
            return;
        }
        Vector2 size = drag.Source.Root.Size;
        if (at.X < 0 || at.Y < 0 || at.X >= size.X || at.Y >= size.Y)
        {
            drag.Source.DockSpace.ShowPreview(null);
            TearOff(drag, at);
            return;
        }
        drag.Source.DockSpace.ShowPreview(DropTarget(drag.Source, at, drag.Panel));
    }

    public void TabDragReleased(DockTab tab, Vector2 at)
    {
        if (_drag is not { Dragged: null } drag)
        {
            return;
        }
        _drag = null;
        drag.Source.DockSpace.ShowPreview(null);
        if (DropTarget(drag.Source, at, drag.Panel) is ({ } group, var where))
        {
            drag.Source.DockSpace.Dock(drag.Panel, group, where);
        }
    }

    // Where a panel would dock at a point of a window; null for nowhere (or
    // nowhere new: a group's only panel onto itself).
    private static (DockGroup Group, DockTarget Where)? DropTarget(EditorWindow window, Vector2 at, EditorPanel panel)
    {
        if (window.DockSpace.TargetAt(at) is not { } target)
        {
            return null;
        }
        return target.Group == panel.Group && panel.Group.Panels.Count == 1 ? null : target;
    }

    // The pointer left the window holding the tab: its panel goes into a
    // window of its own, which the platform drags (Full support: the window
    // follows the pointer). A floating window's only panel drags its window.
    private void TearOff(TabDrag drag, Vector2 at)
    {
        EditorWindow source = drag.Source;
        float scale = source.Root.Scale;
        Vector2 offset = new(32, 12); // the pointer over the new window's tab, in units
        EditorWindow dragged;
        if (!source.IsMain && source.DockSpace.Panels.Count() == 1)
        {
            dragged = source;
            offset = at;
        }
        else
        {
            Vector2 size = drag.Panel.Group?.ActualSize ?? new Vector2(480, 360);
            source.DockSpace.Take(drag.Panel);
            dragged = EditorWindow.CreateFloating(size, _theme, _font);
            Add(dragged);
            dragged.DockSpace.Dock(drag.Panel, null, DockTarget.Center);
            Vector2 screen = Platform.Api.PointerScreenPosition();
            Platform.Api.SetWindowPosition(dragged.Window, screen - offset);
        }
        bool started = source.Window.Value != 0 &&
                       Platform.Api.StartWindowDrag(source.Window, dragged.Window, offset * scale);
        if (started)
        {
            drag.Dragged = dragged;
            Log($"Dragging {drag.Panel.Title}");
        }
        else
        {
            _drag = null;
            Log($"{drag.Panel.Title} is floating");
        }
    }

    /// <summary>
    /// A floating window's title bar (or tab strip) dragged: the platform
    /// drags the window so it can dock where it's dropped. Where windows
    /// can't follow a drag, the system moves it instead (no docking).
    /// </summary>
    public void BeginWindowDrag(EditorWindow window, Vector2 at)
    {
        if (_drag is not null || window.IsMain || window.DockSpace.Panels.FirstOrDefault() is not { } panel)
        {
            return;
        }
        if (Platform.Api.WindowDragSupport() == WindowDragSupport.Full &&
            Platform.Api.StartWindowDrag(window.Window, window.Window, at * window.Root.Scale))
        {
            _drag = new TabDrag(panel, window) { Dragged = window };
            return;
        }
        Platform.Api.StartWindowMove(window.Window);
    }

    public static void ToggleMaximized(EditorWindow window)
    {
        if (Platform.Api.WindowMaximized(window.Window))
        {
            Platform.Api.RestoreWindow(window.Window);
        }
        else
        {
            Platform.Api.MaximizeWindow(window.Window);
        }
    }

    private void PlatformDragMoved(WindowHandle over, Vector2 pixels)
    {
        if (_debugDrags && over.Value != _debugOver)
        {
            _debugOver = over.Value;
            Console.WriteLine($"Crucible.Editor: drag over window {over.Value} at {pixels} " +
                              $"(main {Main.Window.Value}, dragged {_drag?.Dragged?.Window.Value}, editor windows " +
                              $"{string.Join(", ", _windows.Select(window => window.Window.Value))})");
        }
        if (_drag is not { Dragged: { } dragged } drag)
        {
            return;
        }
        foreach (EditorWindow window in _windows)
        {
            bool target = window.Window.Value == over.Value && window != dragged;
            window.DockSpace.ShowPreview(target ? DropTarget(window, pixels / window.Root.Scale, drag.Panel) : null);
        }
    }

    // Dropped on one of our windows: the dragged window's panels dock there
    // (the first where it was dropped, the rest beside it), and it closes.
    private void PlatformDragDropped(WindowHandle over, Vector2 pixels)
    {
        if (_debugDrags)
        {
            Console.WriteLine($"Crucible.Editor: drag dropped on window {over.Value} at {pixels} (dragging: {_drag?.Dragged is not null})");
        }
        if (_drag is not { Dragged: { } dragged })
        {
            return;
        }
        PlatformDragEnded();
        if (_windows.Find(window => window.Window.Value == over.Value && window != dragged) is not { } target)
        {
            return;
        }
        Vector2 at = pixels / target.Root.Scale;
        (DockGroup Group, DockTarget Where)? where = target.DockSpace.TargetAt(at);
        EditorPanel[] panels = [.. dragged.DockSpace.Panels];
        DockGroup? group = where?.Group;
        DockTarget side = where?.Where ?? DockTarget.Center;
        foreach (EditorPanel panel in panels)
        {
            target.DockSpace.Dock(panel, group, side);
            group = panel.Group;
            side = DockTarget.Center;
        }
        Remove(dragged);
        Log($"Docked {string.Join(", ", panels.Select(panel => panel.Title))}");
    }

    private void PlatformDragEnded()
    {
        _drag = null;
        foreach (EditorWindow window in _windows)
        {
            window.DockSpace.ShowPreview(null);
        }
    }

    // -----------------------------------------------------------------------
    // Windows
    // -----------------------------------------------------------------------

    private void Add(EditorWindow window)
    {
        _windows.Add(window);
        _shells[window.Root] = this;
        if (window.Root.Content is { } content)
        {
            content.KeyDown += HandleKeys;
        }
        // The window's close buttons: the main one quits, a floating one docks back.
        Action<UIElement, UIEvent> close = window.IsMain
            ? (_, _) => _context.QuitRequested = true
            : (_, _) => DockBack(window);
        window.TitleBar.CloseButton.Activated += close;
        foreach (UIElement button in window.FloatingButtons.Children)
        {
            if (button is WindowButton { Kind: WindowButtonKind.Close })
            {
                button.Activated += close;
            }
        }
        window.UpdateFrame();
    }

    private void Remove(EditorWindow window)
    {
        if (_drag?.Dragged == window || _drag?.Source == window)
        {
            _drag = null;
        }
        foreach (EditorPanel panel in window.DockSpace.Panels.ToArray())
        {
            window.DockSpace.Take(panel);
        }
        _windows.Remove(window);
        _shells.Remove(window.Root);
        window.Dispose();
    }

    // -----------------------------------------------------------------------
    // The layout (ADR 0066): the main window's tree and each floating
    // window's size, place (where the platform tells it), and tree, saved in
    // the settings. A group is { "Panels": [ids], "Active": i }; a split is
    // { "Split": "Horizontal" | "Vertical", "Sizes": [...], "Children": [...] }.
    // -----------------------------------------------------------------------

    public JsonObject SaveLayout()
    {
        JsonArray floating = [];
        foreach (EditorWindow window in _windows)
        {
            if (window.IsMain || window.DockSpace.Node is not { } node)
            {
                continue;
            }
            Vector2 position = Platform.Api.WindowPosition(window.Window);
            Vector2 size = window.Root.Size;
            floating.Add((JsonNode)new JsonObject
            {
                ["Width"] = size.X,
                ["Height"] = size.Y,
                ["X"] = position.X,
                ["Y"] = position.Y,
                ["Node"] = Save(node),
            });
        }
        return new JsonObject
        {
            ["Main"] = Main.DockSpace.Node is { } main ? Save(main) : null,
            ["Floating"] = floating,
        };
    }

    private static JsonObject Save(UIElement node)
    {
        if (node is DockGroup group)
        {
            return new JsonObject
            {
                ["Panels"] = new JsonArray([.. group.Panels.Select(panel => (JsonNode)JsonValue.Create(panel.Id))]),
                ["Active"] = group.ActivePanel is { } active ? group.Panels.ToList().IndexOf(active) : 0,
            };
        }
        var split = (DockSplit)node;
        return new JsonObject
        {
            ["Split"] = split.Orientation.ToString(),
            ["Sizes"] = new JsonArray([.. split.Sizes.Select(size => (JsonNode)JsonValue.Create(size))]),
            ["Children"] = new JsonArray([.. split.Nodes.Select(child => (JsonNode)Save(child))]),
        };
    }

    /// <summary>Lays the windows out from saved JSON, or the default layout for null (or JSON that doesn't make one).</summary>
    public void RestoreLayout(JsonObject? layout)
    {
        _drag = null;
        foreach (EditorWindow window in _windows.ToArray())
        {
            if (!window.IsMain)
            {
                Remove(window);
            }
        }
        foreach (EditorPanel panel in Main.DockSpace.Panels.ToArray())
        {
            Main.DockSpace.Take(panel);
        }
        _open.Clear();
        UIElement? main = layout?["Main"] is JsonObject saved ? Load(saved) : null;
        if (main is null)
        {
            Main.DockSpace.Node = DefaultLayout();
            return;
        }
        Main.DockSpace.Node = main;
        if (layout?["Floating"] is JsonArray floating)
        {
            foreach (JsonNode? entry in floating)
            {
                if (entry is JsonObject window && window["Node"] is JsonObject saved2 && Load(saved2) is { } node)
                {
                    EditorWindow made = EditorWindow.CreateFloating(
                        new Vector2(Number(window["Width"], 480), Number(window["Height"], 360)), _theme, _font);
                    Add(made);
                    made.DockSpace.Node = node;
                    Platform.Api.SetWindowPosition(made.Window, new Vector2(Number(window["X"], 0), Number(window["Y"], 0)));
                }
            }
        }
    }

    // The default: hierarchy left, inspector right, the viewport over the
    // assets and log in the middle.
    private UIElement DefaultLayout()
    {
        DockSplit middle = new(Orientation.Vertical);
        middle.Add(Group("Viewport"), 0.68f);
        middle.Add(Group("Assets", "Log"), 0.32f);
        DockSplit columns = new(Orientation.Horizontal);
        columns.Add(Group("Hierarchy"), 0.2f);
        columns.Add(middle, 0.58f);
        columns.Add(Group("Inspector"), 0.22f);
        return columns;
    }

    private DockGroup Group(params string[] ids)
    {
        DockGroup group = new();
        foreach (string id in ids)
        {
            if (Panel(id) is { } panel)
            {
                group.Add(panel);
            }
        }
        if (group.Panels.Count > 0)
        {
            group.Activate(group.Panels[0]);
        }
        return group;
    }

    // A saved node; null if it names no panel the editor has (unknown IDs
    // and repeats are skipped).
    private UIElement? Load(JsonObject saved)
    {
        if (saved["Panels"] is JsonArray ids)
        {
            DockGroup group = new();
            foreach (JsonNode? id in ids)
            {
                if (id?.GetValueKind() == System.Text.Json.JsonValueKind.String && !_open.ContainsKey(id.GetValue<string>()) &&
                    Panel(id.GetValue<string>()) is { } panel)
                {
                    group.Add(panel);
                }
            }
            if (group.Panels.Count == 0)
            {
                return null;
            }
            int active = (int)Number(saved["Active"], 0);
            group.Activate(group.Panels[Math.Clamp(active, 0, group.Panels.Count - 1)]);
            return group;
        }
        if (saved["Children"] is not JsonArray children)
        {
            return null;
        }
        DockSplit split = new(saved["Split"]?.ToString() == "Vertical" ? Orientation.Vertical : Orientation.Horizontal);
        JsonArray? sizes = saved["Sizes"] as JsonArray;
        for (int i = 0; i < children.Count; ++i)
        {
            if (children[i] is JsonObject child && Load(child) is { } node)
            {
                split.Add(node, sizes is not null && i < sizes.Count ? Number(sizes[i], 1) : 1);
            }
        }
        return split.Nodes.Count switch
        {
            0 => null,
            1 => Unwrap(split),
            _ => split,
        };
    }

    private static UIElement Unwrap(DockSplit split)
    {
        UIElement only = split.Nodes[0];
        split.Remove(only);
        return only;
    }

    // A number, whether parsed (a JSON element) or made in memory (an int or a float).
    private static float Number(JsonNode? node, float fallback) =>
        node?.GetValueKind() == System.Text.Json.JsonValueKind.Number
            ? float.Parse(node.ToJsonString(), System.Globalization.CultureInfo.InvariantCulture)
            : fallback;

    // -----------------------------------------------------------------------
    // Look and messages
    // -----------------------------------------------------------------------

    public void Status(string text) => Main.StatusBar.Text = text;

    public void Log(string message)
    {
        Console.WriteLine($"Crucible.Editor: {message}");
        LogPanel?.Add(message);
        Main.StatusBar.Text = Project is null ? message : $"{Project.Name} — {message}";
    }

    // Noto Sans, beside the editor (Fonts/, OFL), imported in memory as the
    // asset pipeline would.
    private static FontHandle LoadFont()
    {
        // Beside the --game path the launcher loaded the editor from; in tests, beside them.
        string game = Crucible.Api.GamePath();
        string folder = game.Length > 0 ? Path.GetDirectoryName(Path.GetFullPath(game))! : AppContext.BaseDirectory;
        byte[] font = File.ReadAllBytes(Path.Combine(folder, "Fonts", "NotoSans-Regular.ttf"));
        string path = Path.Combine(Path.GetTempPath(), $"crucible-editor-{Guid.NewGuid():N}.crfont");
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

    // The default theme, plus the editor's own parts.
    private static Theme CreateTheme()
    {
        Vector4 text = Color(0xe8e8ec), muted = Color(0x9aa0aa), accent = Color(0x4c8bf5);
        Vector4 page = Color(0x1c1e22), panel = Color(0x26292e), strip = Color(0x202327), hover = Color(0x343941);
        Theme theme = Theme.CreateDefault();
        theme.Add(new Style("DockPanel.editor").Set(UIElement.BackgroundProperty, page));
        theme.Add(new Style("DockSpace.editor").Set(UIElement.BackgroundProperty, page));
        theme.Add(new Style("DockGroup")
            .Set(UIElement.BackgroundProperty, panel)
            .Set(UIElement.CornerRadiiProperty, new Vector4(4))
            .Set(UIElement.MarginProperty, new Thickness(1)));
        theme.Add(new Style("StackPanel.dock-tabs").Set(UIElement.BackgroundProperty, strip));
        theme.Add(new Style("DockTab")
            .Set(UIElement.PaddingProperty, new Thickness(10, 5, 6, 5))
            .Set(UIElement.ForegroundProperty, muted)
            .Set(UIElement.CornerRadiiProperty, new Vector4(4, 4, 0, 0)));
        theme.Add(new Style("DockTab:hover").Set(UIElement.BackgroundProperty, hover));
        theme.Add(new Style("DockTab.active")
            .Set(UIElement.BackgroundProperty, panel)
            .Set(UIElement.ForegroundProperty, text)
            .Set(UIElement.BorderColorProperty, accent));
        theme.Add(new Style("Label.placeholder").Set(UIElement.ForegroundProperty, muted));
        theme.Add(new Style("Splitter").Set(UIElement.BackgroundProperty, page));
        theme.Add(new Style("Splitter:hover").Set(UIElement.BackgroundProperty, accent with { W = 0.6f }));
        theme.Add(new Style("Splitter:pressed").Set(UIElement.BackgroundProperty, accent));
        theme.Add(new Style("StatusBar")
            .Set(UIElement.BackgroundProperty, strip)
            .Set(UIElement.PaddingProperty, new Thickness(10, 3, 10, 3)));
        theme.Add(new Style("Label.status").Set(UIElement.ForegroundProperty, muted));
        theme.Add(new Style("DropPreview").Set(UIElement.ForegroundProperty, accent));
        return theme;
    }

    private static Vector4 Color(uint rgb) => new((rgb >> 16) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, 1);
}
