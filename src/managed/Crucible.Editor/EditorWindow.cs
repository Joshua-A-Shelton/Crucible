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
        if (isMain)
        {
            DockPanel page = new();
            page.Classes.Add("editor");
            DockPanel.SetDock(MenuBar, Dock.Top);
            DockPanel.SetDock(StatusBar, Dock.Bottom);
            page.Add(MenuBar);
            page.Add(StatusBar);
            page.Add(DockSpace);
            Root.Content = page;
        }
        else
        {
            DockSpace.Classes.Add("editor");
            Root.Content = DockSpace;
        }
        // Input from the window, at its display scale (none headless).
        Root.Window = window;
        _schedule = RenderSchedule.Create();
        _schedule.Add("UI", Root);
        if (surface.Value != 0) // none in tests
        {
            RenderSchedule.SetSurfaceSchedule(surface, _schedule);
        }
    }

    public WindowHandle Window { get; }

    public SurfaceHandle Surface { get; }

    public bool IsMain { get; }

    public UIRoot Root { get; }

    public DockSpace DockSpace { get; }

    /// <summary>The main window's menu bar (empty and hidden in floating ones).</summary>
    public MenuBar MenuBar { get; } = new();

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

    public StatusBar()
    {
        _text.Classes.Add("status");
        AddChild(_text);
    }

    public string Text
    {
        get => _text.Text;
        set => _text.Text = value;
    }
}
