using Crucible.Ecs;
using Crucible.Editor;
using Crucible.Platform;
using Crucible.Scenes;
using Crucible.UI;

namespace Crucible.Tilemaps.Editor;

/// <summary>
/// Map mode (ADR 0112) as the editor's own workspace (ADR 0119): a tab
/// "Map: &lt;name&gt;" beside the Viewport for a map entity, its Palette, its
/// own view of the scene framed straight at the map, and its Layers; the
/// map tools above them, Stamp B, Rectangle R, Fill F, Eraser E, Select S,
/// Flip X and Flip Y (X, Y), Show Collision, and Done. One tab: opening
/// another map moves it there.
/// </summary>
internal sealed class MapWorkspace : Workspace
{
    private readonly MapMode _maps;
    private readonly Dictionary<MapTool, Button> _toolButtons = [];
    private readonly Toggle _flipX = new("Flip X") { VerticalAlignment = VerticalAlignment.Center };
    private readonly Toggle _flipY = new("Flip Y") { VerticalAlignment = VerticalAlignment.Center };
    private readonly Toggle _showCollision = new("Show Collision") { VerticalAlignment = VerticalAlignment.Center };
    private bool _refreshing;

    public MapWorkspace(MapMode maps) : base("Map")
    {
        _maps = maps;
        maps.MapTab = this;
        Title = TitleFor(maps, Entity);
        // Opened again after a restart, or moved here from another map.
        if (Entity is { IsNull: false } root && (!maps.Map.IsOpen || maps.Map.Root.Value != root.Value) &&
            maps.MapOfRoot(root) is { IsNone: false } map)
        {
            maps.Map.Open(root, map);
        }
        Palette = new PalettePanel(maps);
        Layers = new LayersPanel(maps);
        // Its painting as its view's tool (ADR 0121).
        View = new SceneView { ShowsOutlines = false, TakesToolKeys = false, ShowsGrid = false, Tool = new MapPaintTool(maps) };
        Arrange(Palette, View, Layers);
        foreach ((MapTool tool, TileGlyph glyph, string label, uint key) in new[]
                 {
                     (MapTool.Stamp, TileGlyph.Stamp, "Stamp (B)", Keycode.B), (MapTool.Rectangle, TileGlyph.Rectangle, "Rectangle (R)", Keycode.R),
                     (MapTool.Fill, TileGlyph.Fill, "Fill (F)", Keycode.F), (MapTool.Eraser, TileGlyph.Eraser, "Eraser (E)", Keycode.E),
                     (MapTool.Select, TileGlyph.Select, "Select (S)", Keycode.S),
                 })
        {
            Button button = TileGlyphs.GlyphButton(glyph, label);
            button.Clicked += _ => maps.SetMapTool(tool);
            _toolButtons[tool] = button;
            Toolbar.Add(button);
            Commands.Add(new Command($"Map.{tool}", label, () => maps.SetMapTool(tool)) { Shortcut = new Shortcut(KeyModifiers.None, key) });
        }
        Commands.Add(new Command("Map.FlipX", "Flip X", () => Flip(x: true)) { Shortcut = new Shortcut(KeyModifiers.None, Keycode.X) });
        Commands.Add(new Command("Map.FlipY", "Flip Y", () => Flip(x: false)) { Shortcut = new Shortcut(KeyModifiers.None, Keycode.Y) });
        _flipX.Changed += toggle => Set(() => maps.Map.FlipX = toggle.IsOn);
        _flipY.Changed += toggle => Set(() => maps.Map.FlipY = toggle.IsOn);
        _showCollision.Changed += toggle => Set(() => maps.Map.ShowCollision = toggle.IsOn);
        foreach (UIElement option in (UIElement[])[_flipX, _flipY, _showCollision])
        {
            Toolbar.Add(option);
        }
    }

    public PalettePanel Palette { get; }

    public LayersPanel Layers { get; }

    /// <summary>Its view of the scene, framed on the map; the map's tools apply in it.</summary>
    public SceneView View { get; }

    /// <summary>The tools' buttons, by tool (the checks).</summary>
    internal IReadOnlyDictionary<MapTool, Button> ToolButtons => _toolButtons;

    // "Map: Level 1", by the map entity's name.
    private static string TitleFor(MapMode maps, Entity root)
    {
        string name = maps.HasLive(root) && maps.World.Has(root, Components.Of<EntityName>())
            ? maps.World.GetManaged<EntityName>(root).Value
            : "";
        return name.Length > 0 ? $"Map: {name}" : "Map";
    }

    private void Flip(bool x)
    {
        if (x)
        {
            _maps.Map.FlipX = !_maps.Map.FlipX;
        }
        else
        {
            _maps.Map.FlipY = !_maps.Map.FlipY;
        }
        _maps.RaiseMapToolChanged();
    }

    private void Set(Action set)
    {
        if (!_refreshing)
        {
            set();
            View.InvalidateVisual();
        }
    }

    // Its map still open on its entity.
    public override bool KeepOpen() => _maps.Map.IsOpen && _maps.Map.Root.Value == Entity.Value && base.KeepOpen();

    public override void Closed()
    {
        _maps.Map.Close();
        if (_maps.MapTab == this)
        {
            _maps.MapTab = null;
        }
    }

    // Its map's state (MapEditor.Tick), and its parts, which the editor
    // doesn't tick: they're inside it, not panels of their own.
    public override void Tick()
    {
        _maps.Map.Tick();
        Palette.Tick();
        Layers.Tick();
        _refreshing = true;
        foreach ((MapTool tool, Button button) in _toolButtons)
        {
            button.Classes.Set("primary", tool == _maps.Map.Tool);
        }
        _flipX.IsOn = _maps.Map.FlipX;
        _flipY.IsOn = _maps.Map.FlipY;
        _showCollision.IsOn = _maps.Map.ShowCollision;
        _refreshing = false;
    }
}
