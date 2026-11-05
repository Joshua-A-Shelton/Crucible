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

    /// <summary>Its tab's text.</summary>
    public string Title { get; }

    /// <summary>The group showing it, if it's docked.</summary>
    public DockGroup? Group => Parent as DockGroup;
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

/// <summary>The selection's properties (M7 step 4).</summary>
public sealed class InspectorPanel() : PlaceholderPanel("Inspector", "Inspector", "M7 step 4");

/// <summary>
/// The scene, rendered (ADR 0067): a toolbar (the handles' mode and space,
/// the grid, the last frame's time) over a <see cref="SceneView"/>.
/// </summary>
public sealed class ViewportPanel : EditorPanel
{
    private readonly Button _move = Tool("Move"), _rotate = Tool("Rotate"), _scale = Tool("Scale");
    private readonly Button _space = Tool("World");
    private readonly Toggle _grid = new("Grid") { IsOn = true, VerticalAlignment = VerticalAlignment.Center };
    private readonly Label _time = new() { VerticalAlignment = VerticalAlignment.Center };

    internal ViewportPanel(Shell shell) : base("Viewport", "Viewport")
    {
        View = new SceneView(shell);
        _move.Clicked += _ => View.Handles.Mode = HandleMode.Move;
        _rotate.Clicked += _ => View.Handles.Mode = HandleMode.Rotate;
        _scale.Clicked += _ => View.Handles.Mode = HandleMode.Scale;
        _space.Clicked += _ =>
            View.Handles.Space = View.Handles.Space == HandleSpace.World ? HandleSpace.Local : HandleSpace.World;
        _grid.Changed += toggle => View.ShowsGrid = toggle.IsOn;
        _time.Classes.Add("frame-time");
        StackPanel tools = new() { Orientation = Orientation.Horizontal, Spacing = 4 };
        tools.Classes.Add("viewport-tools");
        foreach (UIElement tool in (UIElement[])[_move, _rotate, _scale, _space, _grid, _time])
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

    // The toolbar shows the view's state, which keys change too; each frame.
    internal void Refresh()
    {
        Mark(_move, View.Handles.Mode == HandleMode.Move);
        Mark(_rotate, View.Handles.Mode == HandleMode.Rotate);
        Mark(_scale, View.Handles.Mode == HandleMode.Scale);
        _space.Text = View.Handles.Space == HandleSpace.World ? "World" : "Local";
        _time.Text = View.Surface.Value != 0 ? $"{View.LastFrameMilliseconds:0.0} ms" : "";
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

/// <summary>The project's assets (M7 step 5).</summary>
public sealed class AssetsPanel() : PlaceholderPanel("Assets", "Assets", "M7 step 5");

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

    public void Add(string message)
    {
        if (_lines.Children.Count == MaxLines)
        {
            _lines.Remove(_lines.Children[0]);
        }
        _lines.Add(new Label(message) { Wrap = true });
        _scroll.ScrollTo(Orientation.Vertical, float.PositiveInfinity);
    }
}
