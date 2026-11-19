using System.Numerics;
using System.Text;
using Crucible.Assets;
using Crucible.Assets.Importers;
using Crucible.Render;
using Crucible.UI;

namespace Crucible.Editor;

/// <summary>
/// The Tileset Editor (ADR 0108): a tileset's texture and grid, and each
/// tile's collision shapes, drawn over the texture. Its toolbar's Select
/// picks a tile; Draw Shape adds points to the selected tile's new shape,
/// snapped to its pixels, closed by clicking its first point again (Escape
/// drops it); Full gives the tile a shape covering it, Clear takes its
/// shapes away. Apply saves the file, which imports it and the maps using
/// it again; Revert drops the edits.
/// </summary>
public sealed class TilesetEditorPanel : EditorPanel
{
    private const int ScancodeEscape = 41;

    private static readonly Vector4 _shape = new(0.35f, 0.85f, 0.45f, 0.95f), _drawing = new(1f, 0.85f, 0.2f, 1);

    private readonly Shell _shell;
    private readonly Label _title = new() { Wrap = true };
    private readonly Label _status = new() { Wrap = true };
    private readonly ComboBox _texture = new() { MinWidth = 0 };
    private readonly NumberField _tileWidth = Whole(1), _tileHeight = Whole(1), _margin = Whole(0), _spacing = Whole(0);
    private readonly Button _select = new("Select"), _draw = new("Draw Shape"), _full = new("Full"), _clear = new("Clear");
    private readonly Button _apply = new("Apply"), _revert = new("Revert");
    private readonly List<AssetId> _textures = [];
    private string _applied = "";
    private TilesetData _edited = new();
    private TextureHandle _handle;
    private bool _refreshing;

    internal TilesetEditorPanel(Shell shell) : base("TilesetEditor", "Tileset Editor")
    {
        _shell = shell;
        _status.Classes.Add("hint");
        View = new TileGridView { Overlay = DrawShapes };
        View.SelectionChanged += _ => Refresh();
        View.TexelClicked += AddPoint;
        _texture.Opening += _ => ListTextures();
        _texture.SelectionChanged += box =>
        {
            if (!_refreshing && box.SelectedIndex >= 0 && box.SelectedIndex < _textures.Count)
            {
                Edit(t => t.Texture = _textures[box.SelectedIndex]);
                AcquireTexture();
            }
        };
        Bind(_tileWidth, (t, v) => t.TileWidth = v);
        Bind(_tileHeight, (t, v) => t.TileHeight = v);
        Bind(_margin, (t, v) => t.Margin = v);
        Bind(_spacing, (t, v) => t.Spacing = v);
        _select.Clicked += _ => SetDrawing(false);
        _draw.Clicked += _ => SetDrawing(true);
        _full.Clicked += _ => Full();
        _clear.Clicked += _ => Clear();
        _apply.Classes.Add("primary");
        _apply.Clicked += _ => Apply();
        _revert.Clicked += _ => Revert();

        StackPanel tools = new() { Orientation = Orientation.Horizontal, Spacing = 4 };
        foreach (Button button in new[] { _select, _draw, _full, _clear })
        {
            tools.Add(button);
        }
        StackPanel buttons = new() { Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.End };
        buttons.Add(_revert);
        buttons.Add(_apply);
        StackPanel side = new() { Spacing = 6, Width = 280 };
        side.Add(_title);
        side.Add(Row("Texture", _texture));
        side.Add(Row("Tile Width", _tileWidth));
        side.Add(Row("Tile Height", _tileHeight));
        side.Add(Row("Margin", _margin));
        side.Add(Row("Spacing", _spacing));
        side.Add(buttons);
        side.Add(_status);
        ScrollView scroll = new() { Content = side, Padding = new Thickness(8) };
        DockPanel.SetDock(scroll, Dock.Right);
        DockPanel.SetDock(tools, Dock.Top);
        tools.Margin = new Thickness(8, 6, 8, 6);
        DockPanel layout = new();
        layout.Add(scroll);
        layout.Add(tools);
        layout.Add(View);
        AddChild(layout);
        Refresh();
    }

    /// <summary>The tileset shown, or none.</summary>
    public AssetId Asset { get; private set; }

    internal TileGridView View { get; }

    /// <summary>The tileset as edited, not yet applied.</summary>
    public TilesetData Edited => _edited;

    public bool HasChanges => _applied.Length > 0 && TilesetImporter.Format(_edited) != _applied;

    /// <summary>The points of the shape being drawn, in the selected tile's pixels.</summary>
    public IReadOnlyList<Vector2> Pending => _pending;

    private readonly List<Vector2> _pending = [];

    /// <summary>The selected tile's number; -1 for none.</summary>
    public int SelectedTile
    {
        get
        {
            (int columns, _) = View.Grid;
            return View.Selection is { } s && columns > 0 ? s.Y0 * columns + s.X0 : -1;
        }
    }

    public Button ApplyButton => _apply;

    /// <summary>Shows a tileset; edits to another not yet applied are dropped.</summary>
    public void Show(AssetId tileset)
    {
        if (tileset == Asset && _applied.Length > 0)
        {
            return;
        }
        Asset = tileset;
        _applied = "";
        _edited = new TilesetData();
        _pending.Clear();
        View.Selection = null;
        if (FileOf(tileset) is { } file)
        {
            try
            {
                _edited = TilesetImporter.Read(File.ReadAllBytes(file));
                _applied = TilesetImporter.Format(_edited);
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or System.Text.Json.JsonException)
            {
                _shell.Log($"Can't edit {Path.GetFileName(file)}: {exception.Message}");
            }
        }
        AcquireTexture();
        View.FitNextDraw();
        Refresh();
    }

    public void Apply()
    {
        if (!HasChanges || FileOf(Asset) is not { } file)
        {
            return;
        }
        try
        {
            string text = TilesetImporter.Format(_edited);
            File.WriteAllText(file, text);
            _applied = text;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _shell.Log($"Can't save {Path.GetFileName(file)}: {exception.Message}");
            return;
        }
        _shell.Log($"Saved {_shell.Database?.Find(Asset)?.Path}");
        _shell.RefreshAssets(); // the tileset, and the maps using it
        Refresh();
    }

    public void Revert()
    {
        if (_applied.Length > 0)
        {
            _edited = TilesetImporter.Read(Encoding.UTF8.GetBytes(_applied));
            _pending.Clear();
            AcquireTexture();
            Refresh();
        }
    }

    /// <summary>Draw Shape on or off; switching drops a shape half drawn.</summary>
    public void SetDrawing(bool drawing)
    {
        View.Drawing = drawing;
        _pending.Clear();
        Refresh();
    }

    /// <summary>The selected tile covered by one square shape.</summary>
    public void Full()
    {
        int tile = SelectedTile;
        if (tile >= 0)
        {
            Edit(t => t.Shapes[tile] =
                [[new Vector2(0, 0), new Vector2(t.TileWidth, 0), new Vector2(t.TileWidth, t.TileHeight), new Vector2(0, t.TileHeight)]]);
        }
    }

    /// <summary>The selected tile's shapes taken away.</summary>
    public void Clear()
    {
        int tile = SelectedTile;
        if (tile >= 0)
        {
            Edit(t => t.Shapes.Remove(tile));
        }
    }

    /// <summary>A point for the shape being drawn, in the selected tile's pixels; the first point again closes it.</summary>
    public void AddPoint(Vector2 texel)
    {
        int tile = SelectedTile;
        (int columns, _) = View.Grid;
        if (tile < 0 || columns == 0)
        {
            _status.Text = "Select a tile first.";
            return;
        }
        (int x, int y) = _edited.PixelOf(tile, columns);
        Vector2 point = new(Math.Clamp(MathF.Round(texel.X - x), 0, _edited.TileWidth),
            Math.Clamp(MathF.Round(texel.Y - y), 0, _edited.TileHeight));
        if (_pending.Count >= 3 && point == _pending[0])
        {
            List<Vector2> shape = [.. _pending];
            _pending.Clear();
            Edit(t => (t.Shapes.TryGetValue(tile, out var shapes) ? shapes : t.Shapes[tile] = []).Add(shape));
            return;
        }
        if (_pending.Count == 0 || _pending[^1] != point)
        {
            _pending.Add(point);
        }
        Refresh();
    }

    internal void Tick()
    {
        if (_handle.Generation == 0 && !_edited.Texture.IsNone && _shell.Database?.ImportedFile(_edited.Texture) is not null)
        {
            AcquireTexture();
        }
        UIState disabled = HasChanges ? 0 : UIState.Disabled;
        _apply.State = (_apply.State & ~UIState.Disabled) | disabled;
        _revert.State = (_revert.State & ~UIState.Disabled) | disabled;
    }

    protected override void OnKeyDown(KeyEvent e)
    {
        if (e.Scancode == ScancodeEscape && _pending.Count > 0)
        {
            _pending.Clear();
            Refresh();
            e.Handled = true;
        }
    }

    protected override void OnDetached(UIRoot root) => ReleaseTexture();

    private void Edit(Action<TilesetData> change)
    {
        if (_applied.Length == 0)
        {
            return;
        }
        change(_edited);
        Refresh();
    }

    private void Refresh()
    {
        _refreshing = true;
        string? path = Asset.IsNone ? null : _shell.Database?.Find(Asset)?.Path;
        _title.Text = path ?? "No tileset: double-click one in the Assets panel, or make one with Create > Tileset.";
        ListTextures();
        _texture.SelectedIndex = _textures.IndexOf(_edited.Texture);
        _tileWidth.Value = _edited.TileWidth;
        _tileHeight.Value = _edited.TileHeight;
        _margin.Value = _edited.Margin;
        _spacing.Value = _edited.Spacing;
        _refreshing = false;
        _select.Classes.Set("primary", !View.Drawing);
        _draw.Classes.Set("primary", View.Drawing);
        View.Tileset = _edited;
        int tile = SelectedTile;
        int shapes = tile >= 0 && _edited.Shapes.TryGetValue(tile, out var list) ? list.Count : 0;
        _status.Text = Asset.IsNone ? ""
            : View.Grid.Columns == 0 ? "Pick a texture, or wait for it to load."
            : tile < 0 ? "Select a tile."
            : View.Drawing ? $"Tile {tile}: click points; click the first again to close the shape ({_pending.Count} so far)."
            : $"Tile {tile}: {shapes} {(shapes == 1 ? "shape" : "shapes")}.";
        View.InvalidateVisual();
    }

    private void ListTextures()
    {
        _textures.Clear();
        List<string> labels = [];
        foreach (AssetEntry entry in _shell.Database?.Assets.Where(entry => entry.Importer == "Texture") ?? [])
        {
            _textures.Add(entry.Id);
            labels.Add(entry.Path);
        }
        _texture.Options = labels;
    }

    // Every tile's shapes, and the one being drawn.
    private void DrawShapes(Canvas canvas, Func<Vector2, Vector2> place)
    {
        (int columns, _) = View.Grid;
        if (columns == 0)
        {
            return;
        }
        foreach ((int tile, List<List<Vector2>> shapes) in _edited.Shapes)
        {
            (int x, int y) = _edited.PixelOf(tile, columns);
            foreach (List<Vector2> shape in shapes)
            {
                Vector2[] points = [.. shape.Append(shape[0]).Select(p => place(p + new Vector2(x, y)))];
                canvas.DrawPolyline(points, 2, _shape);
            }
        }
        if (_pending.Count > 0 && SelectedTile is var selected and >= 0)
        {
            (int x, int y) = _edited.PixelOf(selected, columns);
            Vector2[] points = [.. _pending.Select(p => place(p + new Vector2(x, y)))];
            if (points.Length > 1)
            {
                canvas.DrawPolyline(points, 2, _drawing);
            }
            foreach (Vector2 point in points)
            {
                canvas.FillRect(point - new Vector2(3), new Vector2(6), _drawing, 3);
            }
        }
    }

    private void AcquireTexture()
    {
        ReleaseTexture();
        if (!_edited.Texture.IsNone && _shell.Database?.ImportedFile(_edited.Texture) is not null)
        {
            _handle = RenderAssets.AcquireTexture(_edited.Texture);
        }
        View.Texture = _handle;
        View.FitNextDraw();
    }

    private void ReleaseTexture()
    {
        if (_handle.Generation != 0)
        {
            RenderAssets.ReleaseTexture(_handle);
            _handle = default;
            View.Texture = default;
        }
    }

    private string? FileOf(AssetId id) =>
        _shell.Project is { } project && _shell.Database?.Find(id) is { } entry ? Path.Combine(project.AssetsFolder, entry.Path) : null;

    private void Bind(NumberField field, Action<TilesetData, int> set) =>
        field.ValueChanged += f =>
        {
            if (!_refreshing)
            {
                Edit(t => set(t, (int)f.Value));
            }
        };

    private static NumberField Whole(int least) => new() { MinWidth = 0, Width = 64, Minimum = least, Step = 1 };

    private static UIElement Row(string label, UIElement field)
    {
        Label name = new(label) { Width = 90, VerticalAlignment = VerticalAlignment.Center };
        DockPanel row = new();
        DockPanel.SetDock(name, Dock.Left);
        row.Add(name);
        row.Add(field);
        return row;
    }
}
