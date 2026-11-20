using System.Numerics;
using Crucible.Assets;
using Crucible.Assets.Importers;
using Crucible.Render;
using Crucible.UI;

namespace Crucible.Editor;

/// <summary>
/// The Tiles panel (ADR 0108): everything about the selected tilemap in one
/// place, shown when one is selected. At the top, the entity's map file
/// (Use Another Map points it at another), its tileset (pick one, or drop
/// a texture to make one beside it, cut like its sprite sheet grid), and the
/// tileset's grid. Under them, two tabs over the tileset's texture: Paint,
/// whose toolbar's tools (Paint B, Rectangle R, Erase E, Fill G, Pick I)
/// and flips apply in the viewports, a tile or a block of them dragged out
/// being the brush and starting painting (Escape stops); and Collision,
/// whose presets, flips, Custom, and Clear set the chosen tiles' shapes.
/// Every change saves at once and is an undo step. Double-clicking a
/// tileset shows it here alone, on its Collision tab.
/// </summary>
public sealed class TilesPanel : EditorPanel
{
    private const int ScancodeEscape = 41;

    private static readonly Vector4 _shape = new(0.35f, 0.85f, 0.45f, 0.95f), _drawing = new(1f, 0.85f, 0.2f, 1);

    private readonly Shell _shell;
    private readonly Label _map = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _useMap = new("Use Another Map");
    private readonly ComboBox _tilesetBox = new() { MinWidth = 0 };
    private readonly NumberField _tileWidth = Whole(1), _tileHeight = Whole(1), _margin = Whole(0), _spacing = Whole(0);
    private readonly Button _paintTab = new("Paint"), _collisionTab = new("Collision");
    private readonly StackPanel _paintBar = Bar(), _collisionBar = Bar();
    private readonly Dictionary<TileTool, Button> _tools = [];
    private readonly Toggle _flipX = new("Flip X"), _flipY = new("Flip Y");
    private readonly Button _custom = new("Custom");
    private readonly Label _hint = new() { Wrap = true };
    private readonly UIElement _mapRow, _gridRow;
    private readonly List<AssetId> _tilesets = [];
    private readonly List<Vector2> _pending = [];
    private (int X0, int Y0, int X1, int Y1)? _paintSelection, _collisionSelection;
    private AssetId _alone; // a tileset shown without a map
    private TilesetData? _data;
    private AssetId _shownTileset, _shownTexture;
    private TextureHandle _handle;
    private bool _refreshing;

    internal TilesPanel(Shell shell) : base("Tiles", "Tiles")
    {
        _shell = shell;
        _hint.Classes.Add("hint");
        View = new TileGridView { Overlay = DrawShapes };
        View.SelectionChanged += _ => Chosen();
        View.TexelClicked += AddPoint;

        _useMap.Clicked += button => ShowMaps(button);
        _tilesetBox.Opening += _ => ListTilesets();
        _tilesetBox.SelectionChanged += box =>
        {
            if (!_refreshing && box.SelectedIndex >= 0 && box.SelectedIndex < _tilesets.Count)
            {
                UseTileset(_tilesets[box.SelectedIndex]);
            }
        };
        Bind(_tileWidth, (t, v) => t.TileWidth = v);
        Bind(_tileHeight, (t, v) => t.TileHeight = v);
        Bind(_margin, (t, v) => t.Margin = v);
        Bind(_spacing, (t, v) => t.Spacing = v);
        _paintTab.Clicked += _ => SetTab(collision: false);
        _collisionTab.Clicked += _ => SetTab(collision: true);

        foreach ((TileTool tool, TileGlyph glyph, string label) in new[]
                 {
                     (TileTool.Paint, TileGlyph.Paint, "Paint (B)"), (TileTool.Rectangle, TileGlyph.Rectangle, "Rectangle (R)"),
                     (TileTool.Erase, TileGlyph.Erase, "Erase (E)"), (TileTool.Fill, TileGlyph.Fill, "Fill (G)"),
                     (TileTool.Pick, TileGlyph.Pick, "Pick (I)"),
                 })
        {
            Button button = GlyphButton(glyph, label);
            button.Clicked += _ => SetTool(tool);
            _tools[tool] = button;
            _paintBar.Add(button);
        }
        _flipX.Changed += toggle => SetFlip(toggle.IsOn, _shell.Tiles.FlipY);
        _flipY.Changed += toggle => SetFlip(_shell.Tiles.FlipX, toggle.IsOn);
        _paintBar.Add(_flipX);
        _paintBar.Add(_flipY);

        foreach ((TilePreset preset, string label) in new[]
                 {
                     (TilePreset.Full, "Full"), (TilePreset.Half, "Half"), (TilePreset.Slope, "Slope"),
                     (TilePreset.ShallowLow, "Shallow Low"), (TilePreset.ShallowHigh, "Shallow High"),
                 })
        {
            Button button = new(label);
            button.Clicked += _ => SetShape(preset);
            _collisionBar.Add(button);
        }
        _custom.Clicked += _ => SetDrawing(!View.Drawing);
        _collisionBar.Add(_custom);
        Button clear = new("Clear");
        clear.Clicked += _ => ClearShapes();
        _collisionBar.Add(clear);
        Button mirrorX = new("Flip X"), mirrorY = new("Flip Y");
        mirrorX.Clicked += _ => MirrorShapes(x: true);
        mirrorY.Clicked += _ => MirrorShapes(x: false);
        _collisionBar.Add(mirrorX);
        _collisionBar.Add(mirrorY);

        _shell.Tiles.BrushChanged += FromPainter;
        _shell.AssetsChanged += () => ReadTileset(force: true);

        StackPanel head = new() { Spacing = 4, Margin = new Thickness(8, 6, 8, 4) };
        StackPanel mapFields = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
        mapFields.Add(_map);
        mapFields.Add(_useMap);
        _mapRow = Row("Map", mapFields);
        head.Add(_mapRow);
        head.Add(Row("Tileset", _tilesetBox));
        StackPanel grid = new() { Orientation = Orientation.Horizontal, Spacing = 6 };
        foreach ((string label, NumberField field) in new[]
                 {
                     ("Width", _tileWidth), ("Height", _tileHeight), ("Margin", _margin), ("Spacing", _spacing),
                 })
        {
            grid.Add(new Label(label) { VerticalAlignment = VerticalAlignment.Center });
            grid.Add(field);
        }
        _gridRow = Row("Tiles", grid);
        head.Add(_gridRow);
        StackPanel tabs = new() { Orientation = Orientation.Horizontal, Spacing = 2, Margin = new Thickness(0, 4, 0, 0) };
        tabs.Add(_paintTab);
        tabs.Add(_collisionTab);
        head.Add(tabs);
        head.Add(_paintBar);
        head.Add(_collisionBar);
        DockPanel.SetDock(head, Dock.Top);
        DockPanel.SetDock(_hint, Dock.Bottom);
        _hint.Margin = new Thickness(8, 4, 8, 6);
        DockPanel layout = new();
        layout.Add(head);
        layout.Add(_hint);
        layout.Add(View);
        AddChild(layout);
        SetTab(collision: false);
    }

    internal TileGridView View { get; }

    /// <summary>The Collision tab is showing.</summary>
    public bool OnCollision { get; private set; }

    /// <summary>The tileset shown: the selected map's, or one shown alone; none for neither.</summary>
    public AssetId Tileset => _shell.Tiles.IsEditing ? _shell.Tiles.Data.Tileset : _alone;

    /// <summary>The tileset as its file reads now; null if there's none.</summary>
    public TilesetData? TilesetData => _data;

    /// <summary>The points of the Custom shape being drawn, in the chosen tile's pixels.</summary>
    public IReadOnlyList<Vector2> Pending => _pending;

    /// <summary>The tiles chosen on the Collision tab, by number.</summary>
    public IEnumerable<int> ChosenTiles
    {
        get
        {
            (int columns, _) = View.Grid;
            if (!OnCollision || View.Selection is not { } s || columns == 0)
            {
                yield break;
            }
            for (int y = s.Y0; y <= s.Y1; ++y)
            {
                for (int x = s.X0; x <= s.X1; ++x)
                {
                    yield return y * columns + x;
                }
            }
        }
    }

    /// <summary>A tileset shown alone, on the Collision tab (the selection is let go).</summary>
    public void ShowTileset(AssetId tileset)
    {
        _shell.Selection.Selected = Crucible.Ecs.Entity.Null;
        _alone = tileset;
        SetTab(collision: true);
        ReadTileset(force: true);
    }

    /// <summary>Shows the Paint or the Collision tab; each keeps its own chosen tiles.</summary>
    public void SetTab(bool collision)
    {
        if (OnCollision)
        {
            _collisionSelection = View.Selection;
        }
        else
        {
            _paintSelection = View.Selection;
        }
        OnCollision = collision;
        View.Selection = collision ? _collisionSelection : _paintSelection;
        SetDrawing(false);
        if (collision)
        {
            _shell.Tiles.IsPainting = false;
        }
        Refresh();
    }

    /// <summary>A painting tool, as its button or key chooses it: painting starts.</summary>
    public void SetTool(TileTool tool)
    {
        if (OnCollision)
        {
            SetTab(collision: false);
        }
        _shell.SetTileTool(tool);
        Refresh();
    }

    private void SetFlip(bool x, bool y)
    {
        if (_refreshing)
        {
            return;
        }
        _shell.Tiles.FlipX = x;
        _shell.Tiles.FlipY = y;
        _shell.Tiles.IsPainting = _shell.Tiles.IsEditing;
    }

    // Tiles dragged out in the view: on Paint the brush, painting started;
    // on Collision, the tiles the buttons change.
    private void Chosen()
    {
        if (!OnCollision && View.Selection is { } s && _shell.Tiles.IsEditing)
        {
            _shell.Tiles.SetBrush(s.X0, s.Y0, s.X1, s.Y1);
            if (_shell.Tiles.Tool is TileTool.Pick or TileTool.Erase)
            {
                _shell.Tiles.Tool = TileTool.Paint;
            }
            _shell.Tiles.IsPainting = true;
        }
        _pending.Clear();
        Refresh();
    }

    // The painter changed the brush or the tool (Pick, a key, Escape): the toolbar and the brush shown follow.
    private void FromPainter()
    {
        TilePainter painter = _shell.Tiles;
        (int columns, _) = View.Grid;
        if (!OnCollision && painter.Brush.Count == 1 && columns > 0)
        {
            int tile = painter.Brush[0].Tile;
            View.Selection = (tile % columns, tile / columns, tile % columns, tile / columns);
        }
        Refresh();
    }

    /// <summary>Each frame: the tileset and its texture kept current, the painter given the texture.</summary>
    internal void Tick()
    {
        if (_shell.Tiles.IsEditing)
        {
            _alone = AssetId.None;
        }
        ReadTileset(force: false);
        AssetId texture = _data?.Texture ?? AssetId.None;
        if (texture != _shownTexture || (_handle.Generation == 0 && !texture.IsNone && _shell.Database?.ImportedFile(texture) is not null))
        {
            ReleaseTexture();
            _shownTexture = texture;
            if (!texture.IsNone && _shell.Database?.ImportedFile(texture) is not null)
            {
                _handle = RenderAssets.AcquireTexture(texture);
            }
            View.Texture = _handle;
            View.FitNextDraw();
            Refresh();
        }
        _shell.Tiles.Texture = _handle;
        View.Tileset = _data;
    }

    // The tileset's file read when it's another one (or, forced, again after a change).
    private void ReadTileset(bool force)
    {
        AssetId tileset = Tileset;
        if (!force && tileset == _shownTileset)
        {
            return;
        }
        if (tileset != _shownTileset)
        {
            _paintSelection = _collisionSelection = null;
            View.Selection = null;
            _pending.Clear();
        }
        _shownTileset = tileset;
        _data = null;
        if (_shell.Project is { } project && !tileset.IsNone && _shell.Database?.Find(tileset) is { } entry)
        {
            try
            {
                _data = TilesetImporter.Read(File.ReadAllBytes(Path.Combine(project.AssetsFolder, entry.Path)));
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or System.Text.Json.JsonException)
            {
                _shell.Log($"Can't read {entry.Path}: {exception.Message}");
            }
        }
        _shell.Tiles.Reread();
        View.Tileset = _data;
        Refresh();
    }

    private void UseTileset(AssetId tileset)
    {
        if (_shell.Tiles.IsEditing)
        {
            _shell.SetMapTileset(_shell.Tiles.Map, tileset);
            _shell.Tiles.Reread();
        }
        else
        {
            _alone = tileset;
        }
        ReadTileset(force: true);
    }

    // Use Another Map: the project's maps in a menu, the entity's checked.
    private void ShowMaps(ButtonBase button)
    {
        TilePainter painter = _shell.Tiles;
        if (!painter.IsEditing || _shell.Database is not { } database)
        {
            return;
        }
        ContextMenu menu = new();
        foreach (AssetEntry entry in database.Assets.Where(entry => entry.Importer == "Tilemap").OrderBy(entry => entry.Path))
        {
            AssetId id = entry.Id;
            MenuItem item = menu.Add(entry.Path, () => _shell.UseMap(painter.Entity, id));
            item.IsChecked = id == painter.Map;
        }
        menu.Open(button, new Vector2(button.Bounds.X, button.Bounds.Y + button.Bounds.Height));
    }

    // A texture dropped makes a tileset of it; a tileset dropped is used.
    protected override void OnDragOver(DragEvent e) =>
        e.Accepted = e.Payload is AssetDrag drag && _shell.Database?.Find(drag.Id) is { Importer: "Texture" or "Tileset" };

    protected override void OnDrop(DragEvent e)
    {
        if (e.Payload is AssetDrag drag && _shell.Database?.Find(drag.Id) is { } entry)
        {
            DropAsset(entry);
        }
    }

    /// <summary>An asset dropped on the panel: a texture becomes a tileset, used with a tileset dropped as it is.</summary>
    public void DropAsset(AssetEntry entry)
    {
        AssetId tileset = entry.Importer switch
        {
            "Tileset" => entry.Id,
            "Texture" => _shell.CreateTilesetFor(entry.Id),
            _ => AssetId.None,
        };
        if (!tileset.IsNone)
        {
            UseTileset(tileset);
        }
    }

    /// <summary>The chosen tiles given a preset shape (only it).</summary>
    public void SetShape(TilePreset preset) =>
        EditChosen("Tile Shape", (t, tile) => t.Shapes[tile] = [Shell.Preset(preset, t.TileWidth, t.TileHeight)]);

    /// <summary>The chosen tiles' shapes taken away.</summary>
    public void ClearShapes() => EditChosen("Clear Tile Shape", (t, tile) => t.Shapes.Remove(tile));

    /// <summary>The chosen tiles' shapes mirrored across the tile, left to right (or top to bottom).</summary>
    public void MirrorShapes(bool x) =>
        EditChosen("Flip Tile Shape", (t, tile) =>
        {
            if (t.Shapes.TryGetValue(tile, out List<List<Vector2>>? shapes))
            {
                t.Shapes[tile] = [.. shapes.Select(shape => shape.Select(p =>
                    x ? new Vector2(t.TileWidth - p.X, p.Y) : new Vector2(p.X, t.TileHeight - p.Y)).ToList())];
            }
        });

    private void EditChosen(string title, Action<TilesetData, int> change)
    {
        int[] tiles = [.. ChosenTiles];
        if (tiles.Length == 0)
        {
            _hint.Text = "Choose tiles first: click one, or drag over several.";
            return;
        }
        SetDrawing(false);
        _shell.EditTileset(Tileset, title, data =>
        {
            foreach (int tile in tiles)
            {
                change(data, tile);
            }
        });
        ReadTileset(force: true);
    }

    /// <summary>Custom on or off: clicks in the view place a shape's points on the chosen tile.</summary>
    public void SetDrawing(bool drawing)
    {
        View.Drawing = drawing && OnCollision;
        _pending.Clear();
        Refresh();
    }

    /// <summary>A Custom point, in the chosen tile's pixels; the first point again closes the shape, which is added.</summary>
    public void AddPoint(Vector2 texel)
    {
        (int columns, _) = View.Grid;
        if (_data is not { } data || columns == 0 || View.Selection is not { } s)
        {
            _hint.Text = "Choose a tile first.";
            return;
        }
        int tile = s.Y0 * columns + s.X0;
        (int x, int y) = data.PixelOf(tile, columns);
        Vector2 point = new(Math.Clamp(MathF.Round(texel.X - x), 0, data.TileWidth), Math.Clamp(MathF.Round(texel.Y - y), 0, data.TileHeight));
        if (_pending.Count >= 3 && point == _pending[0])
        {
            List<Vector2> shape = [.. _pending];
            _pending.Clear();
            _shell.EditTileset(Tileset, "Custom Tile Shape",
                t => (t.Shapes.TryGetValue(tile, out var shapes) ? shapes : t.Shapes[tile] = []).Add(shape));
            ReadTileset(force: true);
            return;
        }
        if (_pending.Count == 0 || _pending[^1] != point)
        {
            _pending.Add(point);
        }
        Refresh();
    }

    protected override void OnKeyDown(KeyEvent e)
    {
        if (e.Scancode != ScancodeEscape)
        {
            return;
        }
        if (_pending.Count > 0 || View.Drawing)
        {
            SetDrawing(false);
            e.Handled = true;
        }
        else if (_shell.Tiles.IsPainting)
        {
            _shell.Tiles.IsPainting = false;
            e.Handled = true;
        }
    }

    protected override void OnDetached(UIRoot root)
    {
        ReleaseTexture();
        _shell.Tiles.Texture = default;
    }

    private void Refresh()
    {
        TilePainter painter = _shell.Tiles;
        _refreshing = true;
        string? mapPath = painter.IsEditing ? _shell.Database?.Find(painter.Map)?.Path : null;
        _map.Text = mapPath ?? "";
        _mapRow.Visibility = painter.IsEditing ? Visibility.Visible : Visibility.Collapsed;
        ListTilesets();
        _tilesetBox.SelectedIndex = _tilesets.IndexOf(Tileset);
        _gridRow.Visibility = _data is null ? Visibility.Collapsed : Visibility.Visible;
        if (_data is { } data)
        {
            _tileWidth.Value = data.TileWidth;
            _tileHeight.Value = data.TileHeight;
            _margin.Value = data.Margin;
            _spacing.Value = data.Spacing;
        }
        _flipX.IsOn = painter.FlipX;
        _flipY.IsOn = painter.FlipY;
        _refreshing = false;
        _paintTab.Classes.Set("primary", !OnCollision);
        _collisionTab.Classes.Set("primary", OnCollision);
        _paintTab.State = (_paintTab.State & ~UIState.Disabled) | (painter.IsEditing ? 0 : UIState.Disabled);
        _paintBar.Visibility = OnCollision ? Visibility.Collapsed : Visibility.Visible;
        _collisionBar.Visibility = OnCollision ? Visibility.Visible : Visibility.Collapsed;
        foreach ((TileTool tool, Button button) in _tools)
        {
            button.Classes.Set("primary", painter.IsPainting && tool == painter.Tool);
        }
        _custom.Classes.Set("primary", View.Drawing);
        int shapes = ChosenTiles.Sum(tile => _data?.Shapes.TryGetValue(tile, out var list) == true ? list.Count : 0);
        int chosen = ChosenTiles.Count();
        _hint.Text = !painter.IsEditing && _alone.IsNone ? "Select a tilemap, or make one with Create > 2D > Tilemap in the Hierarchy."
            : _data is null ? "Choose a tileset, or drop a texture here to make one."
            : View.Grid.Columns == 0 ? "Waiting for the texture to load…"
            : OnCollision && View.Drawing ? $"Click points on the tile; click the first again to close the shape ({_pending.Count} so far). Escape cancels."
            : OnCollision && chosen == 0 ? "Choose tiles (click one, or drag over several), then a shape."
            : OnCollision ? $"{chosen} {(chosen == 1 ? "tile" : "tiles")} chosen, {shapes} {(shapes == 1 ? "shape" : "shapes")}."
            : painter.IsPainting ? "Painting: left button in the viewport. Escape stops."
            : "Choose a tile (or drag over several) to start painting.";
        View.InvalidateVisual();
    }

    private void ListTilesets()
    {
        _tilesets.Clear();
        List<string> labels = [];
        foreach (AssetEntry entry in (_shell.Database?.Assets ?? []).Where(entry => entry.Importer == "Tileset").OrderBy(entry => entry.Path))
        {
            _tilesets.Add(entry.Id);
            labels.Add(entry.Path);
        }
        _tilesetBox.Options = labels;
    }

    // Every tile's shapes (the chosen ones' brighter), and the Custom shape being drawn.
    private void DrawShapes(Canvas canvas, Func<Vector2, Vector2> place)
    {
        (int columns, _) = View.Grid;
        if (_data is not { } data || columns == 0 || !OnCollision)
        {
            return;
        }
        foreach ((int tile, List<List<Vector2>> shapes) in data.Shapes)
        {
            (int x, int y) = data.PixelOf(tile, columns);
            foreach (List<Vector2> shape in shapes)
            {
                Vector2[] points = [.. shape.Append(shape[0]).Select(p => place(p + new Vector2(x, y)))];
                canvas.DrawPolyline(points, 2, _shape);
            }
        }
        if (_pending.Count > 0 && View.Selection is { } s)
        {
            (int x, int y) = data.PixelOf(s.Y0 * columns + s.X0, columns);
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

    private void ReleaseTexture()
    {
        if (_handle.Generation != 0)
        {
            RenderAssets.ReleaseTexture(_handle);
            _handle = default;
            View.Texture = default;
        }
    }

    private void Bind(NumberField field, Action<TilesetData, int> set) =>
        field.ValueChanged += f =>
        {
            if (!_refreshing && !Tileset.IsNone)
            {
                _shell.EditTileset(Tileset, "Tile Grid", t => set(t, (int)f.Value));
                ReadTileset(force: true);
            }
        };

    private static NumberField Whole(int least) => new() { MinWidth = 0, Width = 56, Minimum = least, Step = 1 };

    private static StackPanel Bar() => new() { Orientation = Orientation.Horizontal, Spacing = 4 };

    private static Button GlyphButton(TileGlyph glyph, string label)
    {
        StackPanel content = new() { Orientation = Orientation.Horizontal, Spacing = 5, HorizontalAlignment = HorizontalAlignment.Center };
        content.Add(new TileGlyphView(glyph));
        content.Add(new Label(label) { VerticalAlignment = VerticalAlignment.Center });
        return new Button { Content = content };
    }

    private static UIElement Row(string label, UIElement field)
    {
        Label name = new(label) { Width = 64, VerticalAlignment = VerticalAlignment.Center };
        DockPanel row = new();
        DockPanel.SetDock(name, Dock.Left);
        row.Add(name);
        row.Add(field);
        return row;
    }
}

internal enum TileGlyph
{
    Paint,
    Rectangle,
    Erase,
    Fill,
    Pick,
}

/// <summary>A painting tool's symbol, drawn in its <c>Foreground</c>.</summary>
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
            case TileGlyph.Paint: // a brush: handle and tip
                canvas.DrawLine(new Vector2(12, 2), new Vector2(6, 8), 2, color);
                canvas.FillRect(new Vector2(2, 8), new Vector2(5, 5), color, 1.5f);
                break;
            case TileGlyph.Rectangle:
                canvas.StrokeRect(new Vector2(1.5f, 3.5f), new Vector2(11, 8), color, 1.5f);
                break;
            case TileGlyph.Erase: // an eraser on its side
                canvas.DrawLine(new Vector2(3, 11), new Vector2(10, 4), 5, color);
                canvas.DrawLine(new Vector2(1, 13), new Vector2(13, 13), 1, color);
                break;
            case TileGlyph.Fill: // a bucket and a drop
                canvas.StrokeRect(new Vector2(2, 5), new Vector2(7, 7), color, 1.5f);
                canvas.FillRect(new Vector2(11, 8), new Vector2(3, 4), color, 1.5f);
                break;
            case TileGlyph.Pick: // a dropper
                canvas.DrawLine(new Vector2(2, 12), new Vector2(9, 5), 1.5f, color);
                canvas.FillRect(new Vector2(8.5f, 1.5f), new Vector2(4.5f, 4.5f), color, 2);
                break;
        }
    }
}
