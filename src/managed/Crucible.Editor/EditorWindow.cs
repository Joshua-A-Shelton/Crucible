using System.Drawing;
using System.Numerics;
using Crucible.Platform;
using Crucible.Render;
using Crucible.UI;

namespace Crucible.Editor;

/// <summary>
/// An OS window of the editor (ADR 0066): its UI root, drawn by a render
/// schedule of its own (Clear, then the UI), and a <see cref="DockSpace"/>.
/// The main window is the launcher's and has the menu bar and status bar;
/// floating windows hold panels torn off it.
/// </summary>
public sealed class EditorWindow : IDisposable
{
    private readonly RenderSchedule _schedule;
    private readonly bool _ownsWindow;
    private bool _disposed;

    internal EditorWindow(WindowHandle window, SurfaceHandle surface, bool isMain, bool ownsWindow, Theme theme,
        Text.FontHandle font)
    {
        Window = window;
        Surface = surface;
        IsMain = isMain;
        _ownsWindow = ownsWindow;
        Root = new UIRoot { Theme = theme, DefaultFont = font };
        DockSpace = new DockSpace();
        TitleBar = isMain ? new TitleBar() : new FloatingTitleBar(this);
        DockPanel page = new();
        _page = page;
        page.Classes.Add("editor");
        DockPanel.SetDock(TitleBar, Dock.Top);
        page.Add(TitleBar);
        if (isMain)
        {
            TitleBar.Title = EngineInfo.Name;
            TitleBar.Leading = MenuBar;
            DockPanel.SetDock(StatusBar, Dock.Bottom);
            page.Add(StatusBar);
        }
        else
        {
            FloatingButtons.Add(new WindowButton(WindowButtonKind.Minimize));
            FloatingButtons.Add(new WindowButton(WindowButtonKind.Maximize));
            FloatingButtons.Add(new WindowButton(WindowButtonKind.Close));
        }
        page.Add(DockSpace);
        Root.Content = page;
        // Input from the window, at its display scale (none headless); its
        // own frame, resized from a border a few units wide (ADR 0066).
        Root.Window = window;
        Platform.Api.SetWindowBordered(window, false);
        Platform.Api.SetWindowResizeBorder(window, ResizeBorder * Root.Scale);
        _schedule = RenderSchedule.Create();
        _schedule.Add("UI", Root);
        if (surface.Value != 0) // none in tests
        {
            RenderSchedule.SetSurfaceSchedule(surface, _schedule);
        }
    }

    private const float ResizeBorder = 6; // units

    private readonly DockPanel _page;

    /// <summary>Whether the game is playing (ADR 0133): its content has the class <c>playing</c>, for the theme to show it.</summary>
    internal bool ShowsPlaying
    {
        get => _page.Classes.Contains("playing");
        set => _page.Classes.Set("playing", value);
    }

    public WindowHandle Window { get; }

    public SurfaceHandle Surface { get; }

    public bool IsMain { get; }

    public UIRoot Root { get; }

    public DockSpace DockSpace { get; }

    /// <summary>
    /// The window's own title bar: the main window's holds the menu bar; a
    /// floating window's shows only while it holds more than one group
    /// (with one, the group's tab strip is its title bar).
    /// </summary>
    public TitleBar TitleBar { get; }

    /// <summary>The main window's menu bar (empty and unused in floating ones).</summary>
    public MenuBar MenuBar { get; } = new();

    // A floating window's buttons at its only group's strip end.
    internal StackPanel FloatingButtons { get; } = new() { Orientation = Orientation.Horizontal };

    // A floating window's frame follows its tree: buttons on the strip of
    // a single group, else a title bar of its own above the groups.
    internal void UpdateFrame()
    {
        if (IsMain)
        {
            return;
        }
        DockGroup? only = DockSpace.Node as DockGroup;
        foreach (DockGroup group in DockSpace.Groups)
        {
            if (group != only)
            {
                group.StripEnd = null;
            }
        }
        if (only is not null)
        {
            only.StripEnd = FloatingButtons;
        }
        TitleBar.Visibility = only is null ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>The main window's status bar.</summary>
    public StatusBar StatusBar { get; } = new();

    /// <summary>
    /// A floating window of a size in window coordinates: a real window's
    /// surface, or, where the platform can't present (headless runs), an
    /// offscreen one of the same size.
    /// </summary>
    internal static EditorWindow CreateFloating(Vector2 size, Theme theme, Text.FontHandle font)
    {
        uint width = (uint)Math.Max(size.X, 160), height = (uint)Math.Max(size.Y, 120);
        WindowHandle window = Platform.Api.CreateWindow(width, height, EngineInfo.Name);
        SurfaceHandle surface;
        try
        {
            surface = Render.Api.CreateSurface(window);
        }
        catch (CrucibleException)
        {
            surface = Render.Api.CreateOffscreenSurface(width, height);
        }
        return new EditorWindow(window, surface, false, true, theme, font);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        Root.Window = default;
        Root.Content = null;
        _schedule.Dispose();
        if (_ownsWindow)
        {
            Render.Api.DestroySurface(Surface);
            Platform.Api.DestroyWindow(Window);
        }
    }
}

/// <summary>A strip along the main window's bottom showing the project and the latest message (ADR 0066).</summary>
public sealed class StatusBar : UIElement
{
    private readonly Label _text = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly Label _project = new() { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.End };

    public StatusBar()
    {
        _text.Classes.Add("status");
        _project.Classes.Add("status-project");
        AddChild(_text);
        AddChild(_project);
    }

    /// <summary>The project's name, at the right end (ADR 0133); empty for none.</summary>
    public string Project
    {
        get => _project.Text;
        set => _project.Text = value ?? "";
    }

    // The message from the left, the project at the right; the message gives way.
    protected override Vector2 MeasureCore(Vector2 available)
    {
        _project.Measure(available);
        _text.Measure(new Vector2(MathF.Max(available.X - _project.DesiredSize.X - 16, 0), available.Y));
        return new Vector2(_text.DesiredSize.X + _project.DesiredSize.X + 16, MathF.Max(_text.DesiredSize.Y, _project.DesiredSize.Y));
    }

    protected override void ArrangeCore(RectangleF content)
    {
        float right = _project.DesiredSize.X;
        _project.Arrange(new RectangleF(content.Right - right, content.Y, right, content.Height));
        _text.Arrange(new RectangleF(content.X, content.Y, MathF.Max(content.Width - right - 16, 0), content.Height));
    }

    public string Text
    {
        get => _text.Text;
        set => _text.Text = value;
    }
}

// A floating window's title bar, while it holds several groups: dragged,
// it drags the window so it can dock, as its tab strips' empty parts do.
internal sealed class FloatingTitleBar(EditorWindow window) : TitleBar
{
    protected override void OnMoveStarted(Vector2 pressedAt) => Shell.Of(this)?.BeginWindowDrag(window, pressedAt);
}
