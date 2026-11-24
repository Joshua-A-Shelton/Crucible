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

    // A map tool's button (ADR 0108, 0112): its glyph and label.
    internal static Button GlyphButton(TileGlyph glyph, string label)
    {
        StackPanel content = new() { Orientation = Orientation.Horizontal, Spacing = 5, HorizontalAlignment = HorizontalAlignment.Center };
        content.Add(new TileGlyphView(glyph));
        content.Add(new Label(label) { VerticalAlignment = VerticalAlignment.Center });
        Button button = new() { Content = content, VerticalAlignment = VerticalAlignment.Center };
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

internal enum TileGlyph
{
    Stamp,
    Rectangle,
    Fill,
    Eraser,
    Select,
}

/// <summary>A map tool's symbol (ADR 0108, 0112), drawn in its <c>Foreground</c>.</summary>
internal sealed class TileGlyphView : UIElement
{
    private readonly TileGlyph _glyph;

    public TileGlyphView(TileGlyph glyph)
    {
        _glyph = glyph;
        Width = 14;
        Height = 14;
        VerticalAlignment = VerticalAlignment.Center;
    }

    protected override void OnDraw(Canvas canvas)
    {
        Vector4 color = Foreground;
        switch (_glyph)
        {
            case TileGlyph.Stamp: // a stamp: handle, block, and its mark
                canvas.FillRect(new Vector2(5, 1), new Vector2(4, 5), color, 1.5f);
                canvas.FillRect(new Vector2(2, 6), new Vector2(10, 4), color, 1);
                canvas.DrawLine(new Vector2(2, 13), new Vector2(12, 13), 1.5f, color);
                break;
            case TileGlyph.Rectangle:
                canvas.StrokeRect(new Vector2(1.5f, 3.5f), new Vector2(11, 8), color, 1.5f);
                break;
            case TileGlyph.Eraser: // an eraser on its side
                canvas.DrawLine(new Vector2(3, 11), new Vector2(10, 4), 5, color);
                canvas.DrawLine(new Vector2(1, 13), new Vector2(13, 13), 1, color);
                break;
            case TileGlyph.Fill: // a bucket and a drop
                canvas.StrokeRect(new Vector2(2, 5), new Vector2(7, 7), color, 1.5f);
                canvas.FillRect(new Vector2(11, 8), new Vector2(3, 4), color, 1.5f);
                break;
            case TileGlyph.Select: // a dashed rectangle
                for (int i = 0; i < 4; ++i)
                {
                    canvas.DrawLine(new Vector2(1.5f + i * 3, 2.5f), new Vector2(3 + i * 3, 2.5f), 1.5f, color);
                    canvas.DrawLine(new Vector2(1.5f + i * 3, 11.5f), new Vector2(3 + i * 3, 11.5f), 1.5f, color);
                }
                canvas.DrawLine(new Vector2(1.5f, 2.5f), new Vector2(1.5f, 11.5f), 1.5f, color);
                canvas.DrawLine(new Vector2(12.5f, 2.5f), new Vector2(12.5f, 11.5f), 1.5f, color);
                break;
        }
    }
}
