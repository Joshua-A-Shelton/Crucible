using Crucible.Assets;
using Crucible.Render;
using Crucible.UI;

namespace Crucible.Editor;

/// <summary>
/// The Tile Palette (ADR 0108): the tileset of the map Edit Tiles is
/// painting, a tile or a rectangle of them picked by dragging as the brush,
/// and the toolbar: Paint (B), Erase (E), Fill (G), Pick (I), and Flip X and
/// Flip Y for the brush. Opens when Edit Tiles starts.
/// </summary>
public sealed class TilePalettePanel : EditorPanel
{
    private readonly Shell _shell;
    private readonly Label _hint = new() { Wrap = true };
    private readonly Dictionary<TileTool, Button> _tools = [];
    private readonly Toggle _flipX = new("Flip X"), _flipY = new("Flip Y");
    private AssetId _shownTexture;
    private TextureHandle _handle;
    private bool _refreshing;

    internal TilePalettePanel(Shell shell) : base("TilePalette", "Tile Palette")
    {
        _shell = shell;
        _hint.Classes.Add("hint");
        View = new TileGridView();
        View.SelectionChanged += view =>
        {
            if (view.Selection is { } s)
            {
                _shell.Tiles.SetBrush(s.X0, s.Y0, s.X1, s.Y1);
                if (_shell.Tiles.Tool is TileTool.Pick)
                {
                    SetTool(TileTool.Paint);
                }
            }
        };
        StackPanel bar = new() { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(8, 6, 8, 6) };
        foreach ((TileTool tool, string label) in new[]
                 {
                     (TileTool.Paint, "Paint (B)"), (TileTool.Erase, "Erase (E)"), (TileTool.Fill, "Fill (G)"),
                     (TileTool.Pick, "Pick (I)"),
                 })
        {
            Button button = new(label);
            button.Clicked += _ => SetTool(tool);
            _tools[tool] = button;
            bar.Add(button);
        }
        _flipX.Changed += toggle =>
        {
            if (!_refreshing)
            {
                _shell.Tiles.FlipX = toggle.IsOn;
            }
        };
        _flipY.Changed += toggle =>
        {
            if (!_refreshing)
            {
                _shell.Tiles.FlipY = toggle.IsOn;
            }
        };
        bar.Add(_flipX);
        bar.Add(_flipY);
        _shell.Tiles.BrushChanged += Refresh;
        DockPanel.SetDock(bar, Dock.Top);
        DockPanel.SetDock(_hint, Dock.Bottom);
        _hint.Margin = new Thickness(8, 4, 8, 6);
        DockPanel layout = new();
        layout.Add(bar);
        layout.Add(_hint);
        layout.Add(View);
        AddChild(layout);
        Refresh();
    }

    internal TileGridView View { get; }

    /// <summary>Chooses a tool, as its button or key does.</summary>
    public void SetTool(TileTool tool)
    {
        _shell.Tiles.Tool = tool;
        Refresh();
    }

    /// <summary>Each frame: the texture of the map being painted shown, the toolbar in step with the painter.</summary>
    internal void Tick()
    {
        TilePainter painter = _shell.Tiles;
        AssetId texture = painter.IsEditing && painter.Tileset is { } tileset ? tileset.Texture : AssetId.None;
        if (texture != _shownTexture || (_handle.Generation == 0 && !texture.IsNone))
        {
            ReleaseTexture();
            _shownTexture = texture;
            if (!texture.IsNone && _shell.Database?.ImportedFile(texture) is not null)
            {
                _handle = RenderAssets.AcquireTexture(texture);
            }
            View.Texture = _handle;
            View.Selection = null;
            View.FitNextDraw();
            Refresh();
        }
        View.Tileset = painter.IsEditing ? painter.Tileset : null;
    }

    private void Refresh()
    {
        TilePainter painter = _shell.Tiles;
        foreach ((TileTool tool, Button button) in _tools)
        {
            button.Classes.Set("primary", tool == painter.Tool);
        }
        _refreshing = true;
        _flipX.IsOn = painter.FlipX;
        _flipY.IsOn = painter.FlipY;
        _refreshing = false;
        _hint.Text = !painter.IsEditing ? "Select an entity with a Tilemap and turn on Edit Tiles (T)."
            : painter.Tileset is null ? "The map has no tileset that reads: set its Tileset."
            : "Drag over tiles to pick the brush. In the viewport: left button to use the tool.";
        View.InvalidateVisual();
    }

    protected override void OnDetached(UIRoot root) => ReleaseTexture();

    private void ReleaseTexture()
    {
        if (_handle.Generation != 0)
        {
            RenderAssets.ReleaseTexture(_handle);
            _handle = default;
            View.Texture = default;
        }
    }
}
