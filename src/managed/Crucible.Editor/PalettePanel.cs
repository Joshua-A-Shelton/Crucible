using System.Numerics;
using Crucible.Assets;
using Crucible.Assets.Importers;
using Crucible.Render;
using Crucible.UI;

namespace Crucible.Editor;

/// <summary>
/// The Palette (ADR 0112): map mode's tilesets, a tab each, and a + tab to
/// add one (New Tileset…, or one of the project's of the map's tile size).
/// A tile or a block of them dragged out is the brush. With Collision on,
/// each tile's shapes show over it instead: clicking a tile makes it solid
/// (Full) or clear; Full, Half, Slope, Shallow Low, Shallow High, Custom
/// (points clicked on the tile), Clear, and Flip X and Y apply to the tiles
/// chosen, each saved at once as an undo step. A texture dropped on it opens
/// New Tileset; a tileset dropped is added. A tab's menu edits its grid or
/// takes it off the map. Outside map mode it shows one tileset alone, to
/// edit its collision (double-clicking a tileset).
/// </summary>
public sealed class PalettePanel : EditorPanel
{
    private const int ScancodeEscape = 41;

    private static readonly Vector4 _shape = new(0.35f, 0.85f, 0.45f, 0.95f), _drawing = new(1f, 0.85f, 0.2f, 1);

    private readonly Shell _shell;
    private readonly StackPanel _tabs = new() { Orientation = Orientation.Horizontal, Spacing = 2 };
    private readonly Toggle _collision = new("Collision");
    private readonly StackPanel _collisionBar = new() { Orientation = Orientation.Horizontal, Spacing = 4 };
    private readonly Button _custom = new("Custom");
    private readonly Label _hint = new() { Wrap = true };
    private readonly DockPanel _modeBar = new() { Margin = new Thickness(0, 0, 0, 4) };
    private readonly List<Vector2> _pending = [];
    private AssetId _alone; // a tileset shown outside map mode
    private TilesetData? _aloneData;
    private TextureHandle _aloneTexture;
    private (int X0, int Y0, int X1, int Y1)? _paintSelection, _collisionSelection;
    private int _shownSet = -1;
    private string _tabsKey = ""; // the tilesets the tabs were made for

    internal PalettePanel(Shell shell) : base("Palette", "Palette")
    {
        _shell = shell;
        _hint.Classes.Add("hint");
        View = new TileGridView { Overlay = DrawShapes };
        View.SelectionChanged += _ => Chosen();
        View.TileClicked += Clicked;
        View.TexelClicked += AddPoint;
        _collision.Changed += toggle => SetCollision(toggle.IsOn);

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

        _shell.MapToolChanged += FromMap;
        _shell.AssetsChanged += () =>
        {
            ReadAlone();
            _tabsKey = "";
        };

        // Map mode named, and the way out of it, where it's always in view.
        Button done = new("Done");
        done.Classes.Add("primary");
        done.Clicked += _ => _shell.ExitMapMode();
        DockPanel.SetDock(done, Dock.Right);
        _modeBar.Add(done);
        _modeBar.Add(new Label("Map Mode — Done (or Escape) to leave") { VerticalAlignment = VerticalAlignment.Center, Wrap = true });
        StackPanel head = new() { Spacing = 4, Margin = new Thickness(8, 6, 8, 4) };
        head.Add(_modeBar);
        head.Add(_tabs);
        head.Add(_collision);
        head.Add(_collisionBar);
        DockPanel.SetDock(head, Dock.Top);
        DockPanel.SetDock(_hint, Dock.Bottom);
        _hint.Margin = new Thickness(8, 4, 8, 6);
        DockPanel layout = new();
        layout.Add(head);
        layout.Add(_hint);
        layout.Add(View);
        AddChild(layout);
        Refresh();
    }

    internal TileGridView View { get; }

    /// <summary>The tab shown: the map's tileset by its place; -1 for none (or one shown alone).</summary>
    public int Set { get; private set; } = -1;

    /// <summary>Collision is on: shapes shown and edited instead of the brush chosen.</summary>
    public bool OnCollision => _collision.IsOn;

    /// <summary>The tileset shown: the map's of the tab, or one alone.</summary>
    public AssetId Tileset => !_alone.IsNone ? _alone : _shell.Map.IsOpen && Set >= 0 && Set < _shell.Map.Tilesets.Count ? _shell.Map.Tilesets[Set].Id : AssetId.None;

    /// <summary>The shown tileset as its file reads now; null if there's none.</summary>
    public TilesetData? TilesetData =>
        !_alone.IsNone ? _aloneData : _shell.Map.IsOpen && Set >= 0 && Set < _shell.Map.Tilesets.Count ? _shell.Map.Tilesets[Set].Data : null;

    /// <summary>The points of the Custom shape being drawn, in the chosen tile's pixels.</summary>
    public IReadOnlyList<Vector2> Pending => _pending;

    /// <summary>The tiles chosen with Collision on, by number.</summary>
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

    /// <summary>A tab chosen: one of the map's tilesets.</summary>
    public void ShowSet(int set)
    {
        if (_shell.Map.IsOpen && set >= 0 && set < _shell.Map.Tilesets.Count)
        {
            _alone = AssetId.None;
            Set = set;
            _paintSelection = _collisionSelection = null;
            View.Selection = null;
            _pending.Clear();
            Refresh();
        }
    }

    /// <summary>A tileset shown alone (outside map mode), Collision on.</summary>
    public void ShowAlone(AssetId tileset)
    {
        _alone = tileset;
        ReadAlone();
        _collision.IsOn = true;
        SetCollision(true);
        _tabsKey = "";
        View.FitNextDraw();
        Refresh();
    }

    private void ReadAlone()
    {
        _aloneData = _alone.IsNone ? null : _shell.ReadTileset(_alone);
        AssetId texture = _aloneData?.Texture ?? AssetId.None;
        if (_aloneTexture.Generation != 0)
        {
            RenderAssets.ReleaseTexture(_aloneTexture);
            _aloneTexture = default;
        }
        if (!texture.IsNone && _shell.Database?.ImportedFile(texture) is not null)
        {
            _aloneTexture = RenderAssets.AcquireTexture(texture);
        }
    }

    /// <summary>Collision on or off: the tiles chosen for each kept apart.</summary>
    public void SetCollision(bool on)
    {
        if (_collision.IsOn != on)
        {
            _collision.IsOn = on;
            return; // Changed comes back here
        }
        if (on)
        {
            _paintSelection = View.Selection;
        }
        else
        {
            _collisionSelection = View.Selection;
        }
        View.Selection = on ? _collisionSelection : _paintSelection;
        SetDrawing(false);
        Refresh();
    }

    // Tiles dragged out: the brush (Stamp the tool), unless Collision is on.
    private void Chosen()
    {
        if (!OnCollision && View.Selection is { } s && _shell.Map.IsOpen && _alone.IsNone)
        {
            _shell.Map.SetBrush(Set, s.X0, s.Y0, s.X1, s.Y1);
            _shell.RaiseMapToolChanged();
        }
        _pending.Clear();
        Refresh();
    }

    // With Collision on, a tile clicked becomes solid (Full), or clear if it was.
    private void Clicked((int X, int Y) tile)
    {
        (int columns, _) = View.Grid;
        if (!OnCollision || View.Drawing || columns == 0)
        {
            return;
        }
        int number = tile.Y * columns + tile.X;
        bool solid = TilesetData?.Shapes.ContainsKey(number) == true;
        Edit(solid ? "Clear Tile Shape" : "Tile Shape", [number], (t, n) =>
        {
            if (solid)
            {
                t.Shapes.Remove(n);
            }
            else
            {
                t.Shapes[n] = [Shell.Preset(TilePreset.Full, t.TileWidth, t.TileHeight)];
            }
        });
    }

    // The map's brush changed (a pick, a copy, a key): a one-tile brush shown on its tab.
    private void FromMap()
    {
        MapEditor map = _shell.Map;
        if (!OnCollision && _alone.IsNone && map.Brush.Count == 1 && map.Brush[0].Set is int set && set < map.Tilesets.Count &&
            map.Tilesets[set].Columns is int columns and > 0)
        {
            Set = set;
            int tile = map.Brush[0].Tile;
            View.Selection = (tile % columns, tile / columns, tile % columns, tile / columns);
        }
        Refresh();
    }

    /// <summary>Each frame: the tabs kept to the map's tilesets, the tab's texture shown.</summary>
    internal void Tick()
    {
        MapEditor map = _shell.Map;
        if (!_shell.InMapMode && _alone.IsNone)
        {
            Set = -1;
        }
        else if (map.IsOpen && _alone.IsNone && (Set < 0 || Set >= map.Tilesets.Count))
        {
            Set = map.Tilesets.Count - 1;
        }
        string key = string.Join(",", map.IsOpen ? map.Tilesets.Select(t => $"{t.Id}{t.Path}") : []) + $"|{_alone}|{Set}";
        if (key != _tabsKey)
        {
            _tabsKey = key;
            RebuildTabs();
        }
        TextureHandle texture = !_alone.IsNone ? _aloneTexture
            : map.IsOpen && Set >= 0 && Set < map.Tilesets.Count ? map.Tilesets[Set].Texture : default;
        if (texture.Generation != View.Texture.Generation || texture.Index != View.Texture.Index || Set != _shownSet)
        {
            View.Texture = texture;
            _shownSet = Set;
            View.FitNextDraw();
            Refresh();
        }
        View.Tileset = TilesetData;
    }

    // A tab a tileset, the shown one marked, and + to add one (in map mode).
    private void RebuildTabs()
    {
        _tabs.Clear();
        MapEditor map = _shell.Map;
        if (!_alone.IsNone)
        {
            Button only = new(Path.GetFileNameWithoutExtension(_shell.Database?.Find(_alone)?.Path ?? "Tileset"));
            only.Classes.Add("primary");
            _tabs.Add(only);
            return;
        }
        if (!map.IsOpen)
        {
            return;
        }
        for (int i = 0; i < map.Tilesets.Count; ++i)
        {
            int set = i;
            TabButton tab = new(Path.GetFileNameWithoutExtension(map.Tilesets[i].Path));
            tab.Classes.Set("primary", i == Set);
            tab.Clicked += _ => ShowSet(set);
            tab.ContextRequested += at => TabMenu(tab, set, at);
            _tabs.Add(tab);
        }
        Button add = new("+");
        add.Clicked += button => AddMenu(button);
        _tabs.Add(add);
    }

    // A tab's menu: its grid, or off the map.
    private void TabMenu(Button tab, int set, Vector2 at)
    {
        ContextMenu menu = new();
        AssetId id = _shell.Map.Tilesets[set].Id;
        menu.Add("Edit Grid…", () => _shell.OpenTilesetDialog(AssetId.None, id));
        menu.Add("Remove from Map", () => _shell.RemoveTilesetFromMap(set));
        menu.Open(tab, at);
    }

    // +: New Tileset…, and the project's tilesets the map could use.
    private void AddMenu(ButtonBase button)
    {
        ContextMenu menu = new();
        menu.Add("New Tileset…", () => _shell.OpenTilesetDialog(AssetId.None));
        (int, int)? size = _shell.Map.TileSize;
        foreach (AssetEntry entry in (_shell.Database?.Assets ?? []).Where(entry => entry.Importer == "Tileset").OrderBy(entry => entry.Path))
        {
            AssetId id = entry.Id;
            if (_shell.Map.Data.Tilesets.Contains(id) || _shell.ReadTileset(id) is not { } data ||
                (size is var (width, height) && (data.TileWidth != width || data.TileHeight != height)))
            {
                continue;
            }
            menu.Add(entry.Path, () =>
            {
                if (_shell.AddTilesetToMap(id))
                {
                    ShowSet(_shell.Map.Tilesets.Count - 1);
                }
            });
        }
        menu.Open(button, new Vector2(button.Bounds.X, button.Bounds.Y + button.Bounds.Height));
    }

    // A texture dropped opens New Tileset with it; a tileset dropped is added.
    protected override void OnDragOver(DragEvent e) =>
        e.Accepted = _shell.InMapMode && e.Payload is AssetDrag drag && _shell.Database?.Find(drag.Id) is { Importer: "Texture" or "Tileset" };

    protected override void OnDrop(DragEvent e)
    {
        if (e.Payload is AssetDrag drag && _shell.Database?.Find(drag.Id) is { } entry)
        {
            DropAsset(entry);
        }
    }

    /// <summary>An asset dropped: a texture opens New Tileset with it, a tileset is added to the map.</summary>
    public void DropAsset(AssetEntry entry)
    {
        if (entry.Importer == "Texture")
        {
            _shell.OpenTilesetDialog(entry.Id);
        }
        else if (entry.Importer == "Tileset" && _shell.AddTilesetToMap(entry.Id))
        {
            ShowSet(_shell.Map.Tilesets.Count - 1);
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
            _hint.Text = OnCollision ? "Choose tiles first: click one, or drag over several." : "Turn Collision on, then choose tiles.";
            return;
        }
        Edit(title, tiles, change);
    }

    private void Edit(string title, int[] tiles, Action<TilesetData, int> change)
    {
        SetDrawing(false);
        _shell.EditTileset(Tileset, title, data =>
        {
            foreach (int tile in tiles)
            {
                change(data, tile);
            }
        });
        if (!_alone.IsNone)
        {
            ReadAlone();
        }
        Refresh();
    }

    /// <summary>Custom on or off: clicks on the texture place a shape's points on the chosen tile.</summary>
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
        if (TilesetData is not { } data || columns == 0 || View.Selection is not { } s)
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
            Edit("Custom Tile Shape", [tile], (t, n) => (t.Shapes.TryGetValue(n, out var shapes) ? shapes : t.Shapes[n] = []).Add(shape));
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
        if (e.Scancode == ScancodeEscape && (_pending.Count > 0 || View.Drawing))
        {
            SetDrawing(false);
            e.Handled = true;
        }
    }

    protected override void OnDetached(UIRoot root)
    {
        if (_aloneTexture.Generation != 0)
        {
            RenderAssets.ReleaseTexture(_aloneTexture);
            _aloneTexture = default;
        }
        _alone = AssetId.None;
    }

    internal void Refresh()
    {
        MapEditor map = _shell.Map;
        _modeBar.Visibility = _shell.InMapMode ? Visibility.Visible : Visibility.Collapsed;
        _collisionBar.Visibility = OnCollision ? Visibility.Visible : Visibility.Collapsed;
        _custom.Classes.Set("primary", View.Drawing);
        _collision.Visibility = _alone.IsNone && !map.IsOpen ? Visibility.Collapsed : Visibility.Visible;
        int chosen = ChosenTiles.Count();
        int shapes = ChosenTiles.Sum(tile => TilesetData?.Shapes.TryGetValue(tile, out var list) == true ? list.Count : 0);
        _hint.Text = _alone.IsNone && !map.IsOpen ? "Tilesets show here in map mode: Edit Map on a tilemap, or Create > 2D > Tilemap."
            : TilesetData is null ? "Add a tileset: the + tab, or drop an image here."
            : View.Grid.Columns == 0 ? "Waiting for the image to load…"
            : OnCollision && View.Drawing ? $"Click points on the tile; click the first again to close the shape ({_pending.Count} so far). Escape cancels."
            : OnCollision && chosen == 0 ? "Click a tile to make it solid (again to clear it), or drag over several and choose a shape."
            : OnCollision ? $"{chosen} {(chosen == 1 ? "tile" : "tiles")} chosen, {shapes} {(shapes == 1 ? "shape" : "shapes")}."
            : map.Brush.Count > 0 ? "Paint in the viewport with the left button; right-drag over painted tiles picks them up."
            : "Choose a tile, or drag over several, to paint with.";
        View.InvalidateVisual();
    }

    // With Collision on, every tile's shapes (the chosen ones' brighter), and the Custom shape being drawn.
    private void DrawShapes(Canvas canvas, Func<Vector2, Vector2> place)
    {
        (int columns, _) = View.Grid;
        if (TilesetData is not { } data || columns == 0 || !OnCollision)
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
}

/// <summary>A button with a menu on the right button: the Palette's tabs.</summary>
internal sealed class TabButton(string text) : Button(text)
{
    public event Action<Vector2>? ContextRequested;

    protected override void OnPointerDown(PointerEvent e)
    {
        if (e.Button == PointerButton.Right)
        {
            e.Handled = true;
            ContextRequested?.Invoke(e.Position);
            return;
        }
        base.OnPointerDown(e);
    }
}
