using System.Numerics;
using Crucible.UI;

namespace Crucible.Editor;

/// <summary>
/// Dockable content (ADR 0066): shown as a tab in a <see cref="DockGroup"/>,
/// in the main window or a floating one. One panel per <see cref="Id"/> is
/// open at a time; the <see cref="PanelRegistry"/> makes them.
/// </summary>
public abstract class EditorPanel : UIElement
{
    protected EditorPanel(string id, string title)
    {
        Id = id;
        Title = title;
    }

    /// <summary>What the layout saves it as: "Hierarchy".</summary>
    public string Id { get; }

    /// <summary>Its tab's text; a panel may change it (a workspace naming what it works on, ADR 0119).</summary>
    public string Title
    {
        get;
        protected set
        {
            field = value ?? "";
            TitleChanged?.Invoke();
        }
    }

    internal event Action? TitleChanged;

    /// <summary>The group showing it, if it's docked.</summary>
    public DockGroup? Group => Parent as DockGroup;

    /// <summary>
    /// Writes what it wants kept (ADR 0117): called before a code reload
    /// remakes it, and when the layout is saved. Plain values only, so they
    /// outlive the code that wrote them.
    /// </summary>
    public virtual void SaveState(PanelState state)
    {
    }

    /// <summary>Reads what <see cref="SaveState"/> kept, just after it's made; not called when there's nothing.</summary>
    public virtual void LoadState(PanelState state)
    {
    }

    /// <summary>
    /// Each frame while it's open, shown or not (ADR 0123): for what it
    /// follows (a file, the scene) without an event to tell it. One that
    /// throws is logged and goes on.
    /// </summary>
    public virtual void Tick()
    {
    }
}

/// <summary>The panels the editor knows (ADR 0066), by ID: what the Window menu lists and layouts name.</summary>
public sealed class PanelRegistry
{
    private readonly List<(string Id, string Title, Func<EditorPanel> Create)> _panels = [];

    /// <summary>In the order registered, which the Window menu keeps.</summary>
    public IEnumerable<(string Id, string Title)> Panels => _panels.Select(panel => (panel.Id, panel.Title));

    public void Register(string id, string title, Func<EditorPanel> create)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentNullException.ThrowIfNull(create);
        if (_panels.Exists(panel => panel.Id == id))
        {
            throw new InvalidOperationException($"A panel \"{id}\" is already registered");
        }
        _panels.Add((id, title, create));
    }

    public bool Contains(string id) => _panels.Exists(panel => panel.Id == id);

    /// <summary>Forgets a panel (an extension's, as its code reloads); open ones stay open.</summary>
    internal bool Unregister(string id) => _panels.RemoveAll(panel => panel.Id == id) > 0;

    /// <summary>A registered panel's title; null for one not registered.</summary>
    internal string? TitleOf(string id) => _panels.Find(panel => panel.Id == id).Title;

    /// <summary>A new panel of that ID; null for one not registered.</summary>
    public EditorPanel? Create(string id)
    {
        int at = _panels.FindIndex(panel => panel.Id == id);
        return at < 0 ? null : _panels[at].Create();
    }
}

// Until its step builds it: the panel's name and when it comes.
public abstract class PlaceholderPanel : EditorPanel
{
    protected PlaceholderPanel(string id, string title, string arrives) : base(id, title)
    {
        Label note = new($"{title} arrives in {arrives}.")
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        note.Classes.Add("placeholder");
        AddChild(note);
    }
}

/// <summary>
/// The scene, rendered (ADR 0067): a toolbar (the handles' mode and space,
/// the grid, the last frame's time) over a <see cref="SceneView"/>. Map
/// mode's tools are its own tab's (ADR 0119).
/// </summary>
public sealed class ViewportPanel : EditorPanel
{
    private readonly Shell _shell;
    // The scene's tools (ADR 0121), a button each, made again when they change.
    private readonly StackPanel _toolButtons = new() { Orientation = Orientation.Horizontal, Spacing = 4 };
    private readonly List<(Button Button, ViewportTool Tool)> _buttons = [];
    private readonly Button _space = ToolIcon("globe", "World space: the handles follow the world's axes (click for Local)");
    private readonly Toggle _grid = EditorIcons.Toggle("grid-3x3", "Grid: the floor's lines");
    private readonly Label _time = new() { VerticalAlignment = VerticalAlignment.Center };

    internal ViewportPanel(Shell shell) : base("Viewport", "Viewport")
    {
        _shell = shell;
        View = new SceneView(shell) { Tool = shell.DefaultTool };
        MakeToolButtons();
        _space.Clicked += _ =>
            View.Handles.Space = View.Handles.Space == HandleSpace.World ? HandleSpace.Local : HandleSpace.World;
        _grid.IsOn = true;
        _grid.Changed += toggle => View.ShowsGrid = toggle.IsOn;
        _time.Classes.Add("frame-time");
        StackPanel tools = new() { Orientation = Orientation.Horizontal, Spacing = 4 };
        tools.Classes.Add("viewport-tools");
        foreach (UIElement tool in (UIElement[])[_toolButtons, _space, _grid, _time])
        {
            tools.Add(tool);
        }
        DockPanel page = new();
        DockPanel.SetDock(tools, Dock.Top);
        page.Add(tools);
        page.Add(View);
        AddChild(page);
        Refresh();
    }

    public SceneView View { get; }

    /// <summary>The tools' buttons, in order (the checks).</summary>
    internal IReadOnlyList<(Button Button, ViewportTool Tool)> ToolButtons => _buttons;

    protected override void OnAttached(UIRoot root)
    {
        _shell.ToolsChanged += MakeToolButtons;
        MakeToolButtons();
    }

    protected override void OnDetached(UIRoot root) => _shell.ToolsChanged -= MakeToolButtons;

    private void MakeToolButtons()
    {
        _toolButtons.Clear();
        _buttons.Clear();
        foreach ((ViewportTool tool, Shortcut? shortcut) in _shell.Tools)
        {
            string title = shortcut is { } key ? $"{tool.Title} ({key})" : tool.Title;
            Button button = tool.Icon is { } icon && EditorIcons.Has(icon) ? ToolIcon(icon, title) : Tool(title);
            button.Clicked += _ => View.Tool = tool;
            _toolButtons.Add(button);
            _buttons.Add((button, tool));
        }
    }

    // The toolbar shows the view's state, which keys change too; each frame.
    internal void Refresh()
    {
        foreach ((Button button, ViewportTool tool) in _buttons)
        {
            Mark(button, View.Tool == tool);
        }
        bool world = View.Handles.Space == HandleSpace.World;
        if (_space.Content is Icon space)
        {
            space.Glyph = EditorIcons.Glyph(world ? "globe" : "box");
        }
        _space.ToolTip = world
            ? "World space: the handles follow the world's axes (click for Local)"
            : "Local space: the handles follow the selection's axes (click for World)";
        _time.Text = View.Surface.Value != 0 ? $"{View.LastFrameMilliseconds:0.0} ms" : "";
    }

    // An icon in the toolbar's style, its tooltip saying what it is (ADR 0124).
    private static Button ToolIcon(string icon, string toolTip)
    {
        Button button = EditorIcons.Button(icon, toolTip);
        button.Classes.Add("tool");
        return button;
    }

    private static Button Tool(string text)
    {
        Button button = new(text) { VerticalAlignment = VerticalAlignment.Center };
        button.Classes.Add("tool");
        return button;
    }

    private static void Mark(UIElement element, bool on)
    {
        if (on)
        {
            element.Classes.Add("on");
        }
        else
        {
            element.Classes.Remove("on");
        }
    }
}


/// <summary>The editor's messages, newest last.</summary>
public sealed class LogPanel : EditorPanel
{
    private const int MaxLines = 500;

    private readonly StackPanel _lines = new() { Spacing = 2 };
    private readonly ScrollView _scroll;

    public LogPanel() : base("Log", "Log")
    {
        _scroll = new ScrollView { Content = _lines, Padding = new Thickness(8, 4, 8, 4) };
        AddChild(_scroll);
    }

    public int LineCount => _lines.Children.Count;

    /// <summary>A line, with a style class (log-warning, log-error) if given.</summary>
    public void Add(string message, string? style = null)
    {
        if (_lines.Children.Count == MaxLines)
        {
            _lines.Remove(_lines.Children[0]);
        }
        Label line = new(message) { Wrap = true };
        if (style is not null)
        {
            line.Classes.Add(style);
        }
        _lines.Add(line);
        _scroll.ScrollTo(Orientation.Vertical, float.PositiveInfinity);
    }
}
