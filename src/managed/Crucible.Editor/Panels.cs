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

    /// <summary>
    /// Whether what it shows has changes not yet saved (ADR 0133): its tab
    /// shows a dot after its title. The Hierarchy's follows the scene; an
    /// extension's editor sets its own.
    /// </summary>
    public bool HasUnsavedChanges
    {
        get;
        protected set
        {
            if (field != value)
            {
                field = value;
                TitleChanged?.Invoke();
            }
        }
    }

    /// <summary>
    /// A second part of its tab's text, after its title and quieter (ADR
    /// 0133): what it's showing, as the Hierarchy shows its scene's file
    /// name. Empty for none.
    /// </summary>
    public string TitleDetail
    {
        get;
        protected set
        {
            field = value ?? "";
            TitleChanged?.Invoke();
        }
    } = "";

    /// <summary>Its tab's tooltip (the Hierarchy's: the scene's path); empty for none.</summary>
    public string TitleToolTip
    {
        get;
        protected set
        {
            field = value ?? "";
            TitleChanged?.Invoke();
        }
    } = "";

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
        // The tools on the left, grouped; what the view shows at the right end.
        StackPanel view = new() { Orientation = Orientation.Horizontal, Spacing = 4 };
        view.Add(_grid);
        view.Add(_time);
        DockPanel tools = new();
        tools.Classes.Add("viewport-tools");
        DockPanel.SetDock(view, Dock.Right);
        tools.Add(view);
        tools.Add(_toolButtons);
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

    // In groups: Move, Rotate, Scale, and World/Local, which is theirs; the
    // editor's other tools (Edit Points); then extensions' tools.
    private void MakeToolButtons()
    {
        _toolButtons.Clear();
        _buttons.Clear();
        int group = -1;
        foreach ((ViewportTool tool, Shortcut? shortcut) in _shell.Tools)
        {
            int next = tool is TransformTool ? 0 : Shell.IsEditorTool(tool) ? 1 : 2;
            if (next != group)
            {
                if (group == 0)
                {
                    _toolButtons.Add(_space);
                }
                if (group >= 0)
                {
                    _toolButtons.Add(Separator());
                }
                group = next;
            }
            string title = shortcut is { } key ? $"{tool.Title} ({key})" : tool.Title;
            Button button = tool.Icon is { } icon && EditorIcons.Has(icon) ? ToolIcon(icon, title) : Tool(title);
            button.Clicked += _ => View.Tool = tool;
            _toolButtons.Add(button);
            _buttons.Add((button, tool));
        }
        if (group == 0)
        {
            _toolButtons.Add(_space);
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

    // A thin line between groups of tools.
    private static UIElement Separator()
    {
        UIElement line = new() { Width = 1, Height = 18, Margin = new Thickness(4, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center };
        line.Classes.Add("toolbar-separator");
        return line;
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


/// <summary>
/// The editor's messages, newest last (ADR 0133): each a row with its
/// severity's icon, warnings and errors tinted, its text selectable and
/// copyable (Ctrl+C), and Copy Line and Copy All on its right-click menu.
/// </summary>
public sealed class LogPanel : EditorPanel
{
    private const int MaxLines = 500;

    private readonly StackPanel _lines = new() { Spacing = 1 };
    private readonly ScrollView _scroll;

    public LogPanel() : base("Log", "Log")
    {
        _scroll = new ScrollView { Content = _lines, Padding = new Thickness(4, 4, 4, 4) };
        AddChild(_scroll);
    }

    public int LineCount => _lines.Children.Count;

    /// <summary>Every line's text, oldest first.</summary>
    public IEnumerable<string> Lines => _lines.Children.OfType<LogRow>().Select(row => row.Text);

    /// <summary>A line, with a style class (log-warning, log-error) if given.</summary>
    public void Add(string message, string? style = null)
    {
        if (_lines.Children.Count == MaxLines)
        {
            _lines.Remove(_lines.Children[0]);
        }
        LogRow row = new(this, message, style);
        _lines.Add(row);
        _scroll.ScrollTo(Orientation.Vertical, float.PositiveInfinity);
    }

    internal void ShowMenu(LogRow row, Vector2 at)
    {
        ContextMenu menu = new();
        menu.Add("Copy Line", () => Clipboard.Text = row.Text);
        menu.Add("Copy All", () => Clipboard.Text = string.Join(Environment.NewLine, Lines));
        menu.Open(row, at);
    }

    // One message: its icon, then its text, read-only but selectable.
    internal sealed class LogRow : UIElement
    {
        private readonly TextField _text;

        public LogRow(LogPanel log, string message, string? style)
        {
            Classes.Add("log-row");
            Icon icon = EditorIcons.Icon(style switch
            {
                "log-error" => "circle-x",
                "log-warning" => "triangle-alert",
                _ => "info",
            });
            icon.Classes.Add("log-icon");
            icon.VerticalAlignment = VerticalAlignment.Start;
            _text = new TextField { Text = message, IsReadOnly = true, Multiline = true };
            _text.Classes.Add("log-text");
            if (style is not null)
            {
                Classes.Add(style);
                _text.Classes.Add(style);
            }
            DockPanel row = new();
            DockPanel.SetDock(icon, Dock.Left);
            row.Add(icon);
            row.Add(_text);
            AddChild(row);
            PointerDown += (_, e) =>
            {
                if (e.Button == PointerButton.Right)
                {
                    e.Handled = true;
                    log.ShowMenu(this, e.Position);
                }
            };
        }

        public string Text => _text.Text;
    }
}
