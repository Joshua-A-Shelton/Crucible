using System.Numerics;
using System.Text.Json.Nodes;
using Crucible.Assets;
using Crucible.Assets.Importers;
using Crucible.Ecs;
using Crucible.Editor;
using Crucible.Physics2D;
using Crucible.Render2D;
using Crucible.Scenes;
using Crucible.UI;

namespace Crucible.Tilemaps.Editor;

// Tilemaps (ADR 0108, 0112): a map is an entity (TilemapLayers naming its
// .crtilemap) with a child per layer, each child's Tilemap drawing one layer
// and, if it collides, its Collider2D of shape Tilemap. Map mode edits them
// as one, in a workspace tab of its own (ADR 0119). The map file is kept with
// its entity: Create > 2D > Tilemap makes both, duplicating the entity copies
// the file, renaming it renames the file (unless another entity uses it),
// deleting it asks whether the file goes too. Each file change is part of
// the edit's undo step. All of it on the editor's public API (ADR 0123).
internal sealed class MapMode
{
    private Dialog? _deleteMaps;
    private MapEditor? _mapEditor;

    public MapMode(EditorContext editor)
    {
        Editor = editor;
    }

    /// <summary>The editor, as the plugin's extension was given it.</summary>
    public EditorContext Editor { get; }

    /// <summary>Map mode's editing (ADR 0112).</summary>
    internal MapEditor Map => _mapEditor ??= new MapEditor(this);

    internal bool HasWorld => Editor.Scene.World is not null;

    internal World World => Editor.Scene.World ?? throw new InvalidOperationException("There's no scene open");

    internal AssetDatabase? Database => Editor.Assets.Database;

    internal Project? Project => Editor.Assets.Project;

    internal UndoHistory History => Editor.Undo;

    internal void Log(string text) => Editor.Log(text);

    internal void Status(string text) => Editor.Status = text;

    /// <summary>Raised when assets change (the editor's, ADR 0123).</summary>
    internal event Action? AssetsChanged;

    internal void RaiseAssetsChanged() => AssetsChanged?.Invoke();

    /// <summary>The editor is in map mode: its tab is open (ADR 0119), the tools applying to a map in its view.</summary>
    public bool InMapMode => MapTab is not null;

    /// <summary>Map mode's tab, if it's open: set by the tab itself.</summary>
    internal MapWorkspace? MapTab { get; set; }

    internal bool HasLive(Entity entity) => HasWorld && !entity.IsNull && World.IsAlive(entity);

    /// <summary>
    /// Create > 2D > Tilemap: a map entity under <paramref name="parent"/>
    /// (a Transform2D and TilemapLayers) with one layer, "Layer 1" (a
    /// Transform2D, a Tilemap, and a static Collider2D of shape Tilemap), and
    /// a new map file for it in <see cref="TilemapFolder"/>, named after it;
    /// map mode opens on it.
    /// </summary>
    public Entity CreateTilemapEntity(Entity parent)
    {
        if (!HasWorld || Project is null)
        {
            return Entity.Null;
        }
        const string name = "Tilemap", title = "Create Tilemap";
        TilemapData data = new() { Id = AssetId.New() };
        if (FreeFileName(TilemapFolder(), name, TilemapImporter.FileExtension) is not { } path)
        {
            return Entity.Null;
        }
        History.BeginGroup(title);
        Entity root = Entity.Null;
        if (WriteAssetFile(path, TilemapImporter.Format(data), title))
        {
            root = Editor.Scene.Create(name, HasLive(parent) ? parent : Entity.Null);
            Editor.Scene.Add(root, Transform2D.Identity, title);
            Editor.Scene.Add(root, new TilemapLayers { Map = data.Id }, title);
            MakeLayerEntity(root, data.Id, data.Layers[0], sortingLayer: 0, order: 0, collides: true, title);
        }
        History.EndGroup();
        if (!root.IsNull)
        {
            Editor.Selection.Selected = root;
            EnterMapMode(root);
        }
        return root;
    }

    // A layer's entity under a map's, each part an undo step (the caller groups them).
    private Entity MakeLayerEntity(Entity root, AssetId map, TileLayer layer, uint sortingLayer, int order, bool collides, string title)
    {
        Entity entity = Editor.Scene.Create(layer.Name, root);
        Editor.Scene.Add(entity, Transform2D.Identity, title);
        Editor.Scene.Add(entity, new Tilemap { Map = map, Layer = (uint)layer.Id, SortingLayer = sortingLayer, OrderInLayer = order }, title);
        if (collides)
        {
            Editor.Scene.Add(entity, new Collider2D { Shape = ShapeKind2D.Tilemap }, title);
        }
        return entity;
    }

    // -----------------------------------------------------------------------
    // Map entities and their layers
    // -----------------------------------------------------------------------

    /// <summary>A map entity's map, by TilemapLayers; none if it isn't one.</summary>
    internal AssetId MapOfRoot(Entity root) =>
        HasLive(root) && World.Has(root, Components.Of<TilemapLayers>()) ? World.Read<TilemapLayers>(root).Map : AssetId.None;

    /// <summary>The map entity an entity is (or is a layer of); null if it's neither.</summary>
    internal Entity? MapRootOf(Entity entity)
    {
        if (!HasLive(entity))
        {
            return null;
        }
        if (World.Has(entity, Components.Of<TilemapLayers>()))
        {
            return entity;
        }
        if (World.Has(entity, Components.Of<Tilemap>()) && World.ParentOf(entity) is { IsNull: false } parent &&
            !MapOfRoot(parent).IsNone && MapOfRoot(parent) == World.Read<Tilemap>(entity).Map)
        {
            return parent;
        }
        return null;
    }

    /// <summary>The entity drawing a map's layer: the map entity's child whose Tilemap names it; null if none does.</summary>
    internal Entity? LayerEntity(Entity root, int layer)
    {
        AssetId map = MapOfRoot(root);
        if (map.IsNone)
        {
            return null;
        }
        foreach (Entity child in SceneTree.ChildrenOf(World, root))
        {
            if (World.Has(child, Components.Of<Tilemap>()) && World.Read<Tilemap>(child) is { } tilemap &&
                tilemap.Map == map && tilemap.Layer == (uint)layer)
            {
                return child;
            }
        }
        return null;
    }

    /// <summary>The map's layers with no entity to draw them (deleted or moved away outside map mode).</summary>
    internal IEnumerable<TileLayer> MissingLayers() =>
        Map.IsOpen ? Map.Data.Layers.Where(layer => LayerEntity(Map.Root, layer.Id) is null) : [];

    /// <summary>
    /// A map from before layers (ADR 0108), one entity with its Tilemap,
    /// made a map entity: its Tilemap (and Collider2D and RigidBody2D) moved
    /// to a child for its one layer, as one undo step.
    /// </summary>
    private Entity ConvertToLayers(Entity entity)
    {
        const string title = "Convert to Layers";
        Tilemap tilemap = World.Read<Tilemap>(entity);
        TilemapData data = ReadMap(tilemap.Map) ?? new TilemapData();
        TileLayer layer = data.Layer((int)tilemap.Layer) ?? data.Layers.FirstOrDefault() ?? new TileLayer();
        History.BeginGroup(title);
        Entity child = Editor.Scene.Create(layer.Name, entity);
        Editor.Scene.SetParent(child, entity, 0);
        Editor.Scene.Add(child, Transform2D.Identity, title);
        Editor.Scene.Add(child, tilemap with { Layer = (uint)layer.Id }, title);
        if (World.Has(entity, Components.Of<Collider2D>()))
        {
            Editor.Scene.Add(child, World.Read<Collider2D>(entity), title);
            Editor.Scene.Remove<Collider2D>(entity, title);
        }
        if (World.Has(entity, Components.Of<RigidBody2D>()))
        {
            Editor.Scene.Add(child, World.Read<RigidBody2D>(entity), title);
            Editor.Scene.Remove<RigidBody2D>(entity, title);
        }
        Editor.Scene.Remove<Tilemap>(entity, title);
        Editor.Scene.Add(entity, new TilemapLayers { Map = tilemap.Map }, title);
        History.EndGroup();
        Log($"{LabelOf(entity)}'s map has layers now: its tiles are drawn by its child {layer.Name}");
        return entity;
    }

    internal TilemapData? ReadMap(AssetId map)
    {
        if (AssetPathOf(map) is not { } path || ReadAssetFile(path) is not { } text)
        {
            return null;
        }
        try
        {
            return TilemapImporter.Read(System.Text.Encoding.UTF8.GetBytes(text));
        }
        catch (Exception exception) when (exception is InvalidDataException or System.Text.Json.JsonException)
        {
            Log($"Can't read {path}: {exception.Message}");
            return null;
        }
    }

    // An entity as the editor names it: its name, else "Entity".
    internal string LabelOf(Entity entity) =>
        HasLive(entity) && World.Has(entity, Components.Of<EntityName>()) && World.GetManaged<EntityName>(entity).Value is { Length: > 0 } name
            ? name
            : "Entity";

    // -----------------------------------------------------------------------
    // Map mode
    // -----------------------------------------------------------------------

    /// <summary>
    /// Map mode on the map an entity is (or is a layer of; a map from before
    /// layers is given them first): its tab (ADR 0119) beside the Viewport,
    /// the Palette, its view framed on the map with the map tools above it,
    /// and the Layers. It stays whatever is clicked, until Done or its ×.
    /// </summary>
    public bool EnterMapMode(Entity entity)
    {
        Entity? root = MapRootOf(entity);
        if (root is null && HasLive(entity) && World.Has(entity, Components.Of<Tilemap>()) && !SceneTree.IsInsideInstance(World, entity))
        {
            root = ConvertToLayers(entity);
        }
        if (root is not { } found || MapOfRoot(found) is var map && map.IsNone || !Map.Open(found, map))
        {
            Status("Map mode needs a tilemap: make one with Create > 2D > Tilemap in the Hierarchy");
            return false;
        }
        if (LayerEntity(found, Map.ActiveLayer) is null && Map.Data.Layers.FirstOrDefault(l => LayerEntity(found, l.Id) is not null) is { } drawn)
        {
            Map.ActiveLayer = drawn.Id;
        }
        if (Editor.Workspaces.Open(TilemapsExtension.MapWorkspaceId, found) is null)
        {
            Map.Close();
            return false;
        }
        FrameMap();
        MapModeChanged?.Invoke();
        return true;
    }

    /// <summary>Done: map mode's tab closed.</summary>
    public void ExitMapMode()
    {
        if (MapTab is not { } tab)
        {
            return;
        }
        Editor.Workspaces.Close(tab);
        Status("Left map mode");
        MapModeChanged?.Invoke();
    }

    /// <summary>Raised when map mode starts or ends.</summary>
    public event Action? MapModeChanged;

    /// <summary>The viewport looking straight at the map, all of it in view.</summary>
    public void FrameMap()
    {
        if (!Map.IsOpen || MapTab is not { } tab)
        {
            return;
        }
        (Vector3 center, float radius) = Map.Bounds();
        EditorCamera camera = tab.View.Camera;
        camera.Yaw = 0;
        camera.Pitch = 0;
        camera.Pivot = center;
        camera.Distance = radius * 1.15f / MathF.Tan(camera.FieldOfViewY * 0.5f);
    }

    /// <summary>A tool chosen, by its button or key.</summary>
    internal void SetMapTool(MapTool tool)
    {
        Map.Tool = tool;
        if (tool != MapTool.Select)
        {
            Map.Region = null;
        }
        MapToolChanged?.Invoke();
    }

    /// <summary>Raised when the tool, the flips, or the brush change (the toolbar and the palette follow).</summary>
    public event Action? MapToolChanged;

    internal void RaiseMapToolChanged() => MapToolChanged?.Invoke();

    // -----------------------------------------------------------------------
    // Layers
    // -----------------------------------------------------------------------

    /// <summary>A new layer above the one being edited, its entity under the map's, drawn just above it; it doesn't collide.</summary>
    public int AddLayer()
    {
        if (!Map.IsOpen)
        {
            return -1;
        }
        const string title = "Add Layer";
        Entity root = Map.Root;
        int id = Map.Data.NewLayerId;
        HashSet<string> names = [.. Map.Data.Layers.Select(layer => layer.Name)];
        string name = "Layer 1";
        for (int n = Map.Data.Layers.Count + 1; names.Contains(name = $"Layer {n}"); ++n)
        {
        }
        int above = Map.Data.Layers.FindIndex(layer => layer.Id == Map.ActiveLayer);
        Tilemap? under = LayerEntity(root, Map.ActiveLayer) is { } active ? World.Read<Tilemap>(active) : null;
        int order = Map.Data.Layers.Select(layer => LayerEntity(root, layer.Id)).Where(e => e is not null)
            .Select(e => World.Read<Tilemap>(e!.Value).OrderInLayer).DefaultIfEmpty(-1).Max() + 1;
        History.BeginGroup(title);
        TileLayer made = new() { Id = id, Name = name };
        Map.Change(title, data => data.Layers.Insert(above < 0 ? data.Layers.Count : above + 1, made));
        MakeLayerEntity(root, Map.Map, made, under?.SortingLayer ?? 0, order, collides: false, title);
        ArrangeLayerEntities(root);
        History.EndGroup();
        Map.ActiveLayer = id;
        LayersChanged?.Invoke();
        return id;
    }

    /// <summary>A layer and its entity deleted; the last one stays.</summary>
    public bool DeleteLayer(int id)
    {
        if (!Map.IsOpen || Map.Data.Layers.Count <= 1 || Map.Data.Layer(id) is null)
        {
            Status("A map keeps at least one layer");
            return false;
        }
        History.BeginGroup("Delete Layer");
        if (LayerEntity(Map.Root, id) is { } entity)
        {
            Editor.Scene.Destroy(entity);
        }
        Map.Change("Delete Layer", data => data.Layers.RemoveAll(layer => layer.Id == id));
        History.EndGroup();
        Map.ActiveLayer = Map.Data.Layers[^1].Id;
        Map.CollisionChanged();
        LayersChanged?.Invoke();
        return true;
    }

    /// <summary>A layer renamed, in the file and its entity's name.</summary>
    public void RenameLayer(int id, string name)
    {
        name = name.Trim();
        if (!Map.IsOpen || name.Length == 0 || Map.Data.Layer(id) is not { } layer || layer.Name == name)
        {
            return;
        }
        History.BeginGroup("Rename Layer");
        Map.Change("Rename Layer", data => data.Layer(id)!.Name = name);
        if (LayerEntity(Map.Root, id) is { } entity)
        {
            Editor.Scene.Rename(entity, name);
        }
        History.EndGroup();
        LayersChanged?.Invoke();
    }

    /// <summary>
    /// A layer moved up (+1, drawn over the next) or down (-1) among the
    /// map's: its place in the file, its entity among its siblings, and its
    /// order in its sorting layer swapped with its neighbour's.
    /// </summary>
    public bool MoveLayer(int id, int by)
    {
        if (!Map.IsOpen)
        {
            return false;
        }
        const string title = "Move Layer";
        List<TileLayer> layers = Map.Data.Layers;
        int at = layers.FindIndex(layer => layer.Id == id), to = at + by;
        if (at < 0 || to < 0 || to >= layers.Count)
        {
            return false;
        }
        int other = layers[to].Id;
        History.BeginGroup(title);
        Map.Change(title, data => (data.Layers[at], data.Layers[to]) = (data.Layers[to], data.Layers[at]));
        if (LayerEntity(Map.Root, id) is { } a && LayerEntity(Map.Root, other) is { } b)
        {
            Tilemap ta = World.Read<Tilemap>(a), tb = World.Read<Tilemap>(b);
            if (ta.SortingLayer == tb.SortingLayer && (by > 0 ? ta.OrderInLayer <= tb.OrderInLayer : ta.OrderInLayer >= tb.OrderInLayer))
            {
                (ta.OrderInLayer, tb.OrderInLayer) = (tb.OrderInLayer, ta.OrderInLayer);
                if (ta.OrderInLayer == tb.OrderInLayer)
                {
                    ta.OrderInLayer += by;
                }
                Editor.Scene.Edit(a, ta, title);
                Editor.Scene.Edit(b, tb, title);
            }
        }
        ArrangeLayerEntities(Map.Root);
        History.EndGroup();
        LayersChanged?.Invoke();
        return true;
    }

    // The layers' entities in the file's order (bottom first) among the map entity's children, each move an undo step.
    private void ArrangeLayerEntities(Entity root)
    {
        int index = 0;
        foreach (TileLayer layer in Map.Data.Layers)
        {
            if (LayerEntity(root, layer.Id) is not { } entity)
            {
                continue;
            }
            if (SceneTree.SiblingIndex(World, entity) != index)
            {
                Editor.Scene.SetParent(entity, root, index);
            }
            ++index;
        }
    }

    /// <summary>Whether a layer is drawn: its entity's Tilemap's tint isn't see-through.</summary>
    internal bool IsLayerVisible(int id) => LayerEntity(Map.Root, id) is not { } entity || World.Read<Tilemap>(entity).Tint.W > 0;

    /// <summary>A layer shown or hidden (in the game too): its Tilemap's tint made opaque or see-through.</summary>
    public void SetLayerVisible(int id, bool visible)
    {
        if (!Map.IsOpen || LayerEntity(Map.Root, id) is not { } entity)
        {
            return;
        }
        Tilemap tilemap = World.Read<Tilemap>(entity);
        tilemap.Tint = tilemap.Tint with { W = visible ? 1 : 0 };
        Editor.Scene.Edit(entity, tilemap, visible ? "Show Layer" : "Hide Layer");
        LayersChanged?.Invoke();
    }

    /// <summary>Whether a layer collides: its entity has a Collider2D.</summary>
    internal bool LayerCollides(int id) =>
        LayerEntity(Map.Root, id) is { } entity && World.Has(entity, Components.Of<Collider2D>());

    /// <summary>A layer made to collide (a static Collider2D of shape Tilemap on its entity) or not (its Collider2D removed).</summary>
    public void SetLayerCollides(int id, bool collides)
    {
        if (!Map.IsOpen || LayerEntity(Map.Root, id) is not { } entity || LayerCollides(id) == collides)
        {
            return;
        }
        if (collides)
        {
            Editor.Scene.Add(entity, new Collider2D { Shape = ShapeKind2D.Tilemap }, "Layer Collides");
        }
        else
        {
            Editor.Scene.Remove<Collider2D>(entity, "Layer Doesn't Collide");
        }
        Map.CollisionChanged();
        LayersChanged?.Invoke();
    }

    /// <summary>A layer with no entity given one again (it doesn't collide; turn Collides on if it should).</summary>
    public void RecreateLayerEntity(int id)
    {
        if (!Map.IsOpen || Map.Data.Layer(id) is not { } layer || LayerEntity(Map.Root, id) is not null)
        {
            return;
        }
        const string title = "Re-create Layer";
        int order = Map.Data.Layers.FindIndex(l => l.Id == id);
        History.BeginGroup(title);
        MakeLayerEntity(Map.Root, Map.Map, layer, 0, order, collides: false, title);
        ArrangeLayerEntities(Map.Root);
        History.EndGroup();
        LayersChanged?.Invoke();
    }

    /// <summary>A layer with no entity deleted from the file (Delete Layer in the repair prompt).</summary>
    public void DeleteMissingLayer(int id)
    {
        if (Map.IsOpen && LayerEntity(Map.Root, id) is null && Map.Data.Layers.Count > 1)
        {
            Map.Change("Delete Layer", data => data.Layers.RemoveAll(layer => layer.Id == id));
            LayersChanged?.Invoke();
        }
    }

    /// <summary>Raised when the map's layers change (the Layers panel rebuilds).</summary>
    public event Action? LayersChanged;

    // -----------------------------------------------------------------------
    // Tilesets
    // -----------------------------------------------------------------------

    /// <summary>A tileset added to the map's (a palette tab); refused (said) if its tiles aren't the map's size.</summary>
    public bool AddTilesetToMap(AssetId tileset)
    {
        if (!Map.IsOpen || Map.Data.Tilesets.Contains(tileset) || ReadTileset(tileset) is not { } data)
        {
            return false;
        }
        if (Map.TileSize is var (width, height) && (data.TileWidth != width || data.TileHeight != height))
        {
            Status($"{AssetPathOf(tileset)}'s tiles are {data.TileWidth}x{data.TileHeight}; this map's are {width}x{height}");
            return false;
        }
        return Map.Change("Add Tileset", map => map.Tilesets.Add(tileset));
    }

    /// <summary>A tileset taken off the map's; refused (said) while tiles of it are painted.</summary>
    public bool RemoveTilesetFromMap(int set)
    {
        if (!Map.IsOpen || set < 0 || set >= Map.Data.Tilesets.Count)
        {
            return false;
        }
        int used = Map.Data.Layers.Sum(layer => layer.Cells.Values.Count(cell => cell.Set == set));
        if (used > 0)
        {
            Status($"{used} painted {(used == 1 ? "tile is" : "tiles are")} from that tileset: erase {(used == 1 ? "it" : "them")} first");
            return false;
        }
        return Map.Change("Remove Tileset", map =>
        {
            map.Tilesets.RemoveAt(set);
            foreach (TileLayer layer in map.Layers)
            {
                foreach (TileCell cell in layer.Cells.Values.Where(cell => cell.Set > set).ToList())
                {
                    layer.Cells[(cell.X, cell.Y)] = cell with { Set = cell.Set - 1 };
                }
            }
        });
    }

    internal TilesetData? ReadTileset(AssetId tileset)
    {
        if (AssetPathOf(tileset) is not { } path || ReadAssetFile(path) is not { } text)
        {
            return null;
        }
        try
        {
            return TilesetImporter.Read(System.Text.Encoding.UTF8.GetBytes(text));
        }
        catch (Exception exception) when (exception is InvalidDataException or System.Text.Json.JsonException)
        {
            Log($"Can't read {path}: {exception.Message}");
            return null;
        }
    }

    /// <summary>
    /// A tileset made for a texture (the New Tileset dialog), beside it
    /// ("Ground.crtileset", numbered if taken), cut by the grid given. The
    /// texture is made a sprite first if it isn't one (a texture of any size
    /// imports as one), so the tileset imports. Returns its ID; none if it
    /// can't be made.
    /// </summary>
    public AssetId CreateTileset(AssetId texture, int tileWidth, int tileHeight, int margin, int spacing)
    {
        if (Database is null || AssetPathOf(texture) is not { } texturePath)
        {
            return AssetId.None;
        }
        MakeSprite(texture);
        TilesetData tileset = new()
        {
            Id = AssetId.New(),
            Texture = texture,
            TileWidth = tileWidth,
            TileHeight = tileHeight,
            Margin = margin,
            Spacing = spacing,
        };
        if (FreeFileName(AssetPaths.FolderOf(texturePath), Path.GetFileNameWithoutExtension(texturePath), TilesetImporter.FileExtension) is not { } path ||
            !WriteAssetFile(path, TilesetImporter.Format(tileset), "New Tileset"))
        {
            return AssetId.None;
        }
        Log($"Made {path}");
        return tileset.Id;
    }

    /// <summary>A texture used as a sprite (pixel art: any size imports), as a tileset's must be; said if it changed.</summary>
    internal void MakeSprite(AssetId texture)
    {
        if (Database?.SettingsOf(texture) is { } settings &&
            settings["Usage"]?.ToString() != nameof(Crucible.Importers.TextureUsage.Sprite))
        {
            settings["Usage"] = nameof(Crucible.Importers.TextureUsage.Sprite);
            Editor.Assets.ApplySettings(texture, settings);
            Log($"{AssetPathOf(texture)} is used as a sprite now");
        }
    }

    /// <summary>
    /// The grid a texture's tileset would start with: its sprite sheet's (a
    /// grid slicing), else the map's tile size, else 16x16.
    /// </summary>
    internal (int Width, int Height, int Margin, int Spacing) GridFor(AssetId texture)
    {
        JsonObject? settings = Database?.SettingsOf(texture);
        int Whole(string key, int fallback) => settings?[key]?.GetValue<int>() is int value and > 0 ? value : fallback;
        (int width, int height) = Map.IsOpen && Map.TileSize is { } size ? size : (16, 16);
        bool grid = settings?["Slicing"]?.ToString() == nameof(SpriteSlicing.Grid);
        return grid ? (Whole("CellWidth", width), Whole("CellHeight", height), Whole("Margin", 0), Whole("Spacing", 0)) : (width, height, 0, 0);
    }

    /// <summary>
    /// The New Tileset dialog: an image chosen (or given), its grid set and
    /// seen on it; Create makes the tileset and, in map mode, adds it to the
    /// map. With <paramref name="editing"/>, the dialog changes that
    /// tileset's grid instead.
    /// </summary>
    public TilesetDialog OpenTilesetDialog(AssetId texture, AssetId editing = default)
    {
        TilesetDialog dialog = new(this, texture, editing);
        dialog.Open(Editor.Root);
        LastTilesetDialog = dialog;
        return dialog;
    }

    /// <summary>The New Tileset dialog opened last (the checks answer it).</summary>
    internal TilesetDialog? LastTilesetDialog { get; private set; }

    /// <summary>The Palette panel on its own (not map mode's), while it's open: set by the panel's factory.</summary>
    internal PalettePanel? Palette { get; set; }

    /// <summary>A tileset shown alone in the Palette, its collision to edit (double-clicking one outside map mode).</summary>
    public void ShowTileset(AssetId tileset)
    {
        if (Palette?.Root is null)
        {
            Editor.Panels.Open(TilemapsExtension.PaletteId);
        }
        if (Palette is { Root: not null } panel)
        {
            panel.Group?.Activate(panel);
            panel.ShowAlone(tileset);
        }
    }

    /// <summary>
    /// Where new maps go, in the Assets folder: "Level Tilemaps" beside the
    /// scene Level.crscene; "Tilemaps" while the scene isn't saved (or is
    /// outside the Assets folder).
    /// </summary>
    public string TilemapFolder()
    {
        if (Project is { } project && Editor.Scene.Path is { } scene)
        {
            // Beside the scene, in the game's Assets or a source plugin's (ADR 0118).
            if (project.AssetPathOf(Path.GetDirectoryName(scene) ?? "") is { } folder && !project.IsReadOnly(folder))
            {
                return AssetPaths.Join(folder, Path.GetFileNameWithoutExtension(scene) + " Tilemaps");
            }
        }
        return "Tilemaps";
    }

    // A map file's path in the Assets folder, by its asset ID.
    internal string? AssetPathOf(AssetId id) => id.IsNone ? null : Database?.Find(id)?.Path;

    // "<name><extension>" in a folder of the Assets folder, numbered if
    // taken (or in `taken`), as a path in the Assets folder; null without a
    // project. The name loses characters a file name can't have.
    private string? FreeFileName(string folder, string name, string extension, ICollection<string>? taken = null)
    {
        if (Project is not { } project)
        {
            return null;
        }
        char[] invalid = Path.GetInvalidFileNameChars();
        string clean = new([.. name.Trim().Select(c => invalid.Contains(c) || c is '/' or '\\' or ':' ? '_' : c)]);
        clean = clean.Length == 0 ? "Tilemap" : clean;
        string Of(string file) => AssetPaths.Join(folder, file);
        string path = Of(clean + extension);
        for (int n = 2; File.Exists(project.FileOf(path)) || taken?.Contains(path) == true; ++n)
        {
            path = Of($"{clean} {n}{extension}");
        }
        return path;
    }

    // Writes (or, with null, deletes) a file in the Assets folder as an undo
    // step of its own (the editor's, ADR 0123); false (said) if it can't.
    private bool WriteAssetFile(string path, string? text, string title = "Edit File") =>
        text is null ? Editor.Assets.Delete(path, title) : Editor.Assets.WriteText(path, text, title);

    internal string? ReadAssetFile(string path)
    {
        string? file = Project?.FileOf(path);
        return file is not null && File.Exists(file) ? File.ReadAllText(file) : null;
    }

    // The map file an entity keeps: a map entity's (TilemapLayers), or a
    // map's from before layers (its own Tilemap, not a layer's); null for a
    // layer, a prefab's, or a file that isn't there.
    private (AssetId Map, string Path)? OwnMapOf(Entity entity)
    {
        if (SceneTree.IsInsideInstance(World, entity) || World.Has(entity, Components.Of<PrefabInstance>()))
        {
            return null; // a prefab's maps are the prefab's
        }
        AssetId map = MapOfRoot(entity);
        if (map.IsNone && World.Has(entity, Components.Of<Tilemap>()) && MapRootOf(entity) is null)
        {
            map = World.Read<Tilemap>(entity).Map;
        }
        return AssetPathOf(map) is { } path && File.Exists(Project!.FileOf(path)) ? (map, path) : null;
    }

    /// <summary>
    /// Whether a map is used other than by <paramref name="by"/> and its
    /// layers: by another entity of the open scene, or named in another
    /// scene or prefab file.
    /// </summary>
    public bool IsMapShared(AssetId map, Entity by)
    {
        HashSet<ulong> own = [.. SceneTree.SubtreeOf(World, by).Select(entity => entity.Value)];
        foreach (Entity other in SceneTree.Entities(World))
        {
            if (!own.Contains(other.Value) &&
                ((World.Has(other, Components.Of<Tilemap>()) && World.Read<Tilemap>(other).Map == map) ||
                 (World.Has(other, Components.Of<TilemapLayers>()) && World.Read<TilemapLayers>(other).Map == map)))
            {
                return true;
            }
        }
        if (Project is not { } project)
        {
            return false;
        }
        string id = map.ToString();
        string? open = Editor.Scene.Path is { } scene ? Path.GetFullPath(scene) : null;
        foreach (string file in project.AssetRoots().Where(root => Directory.Exists(root.Folder))
                     .SelectMany(root => Directory.EnumerateFiles(root.Folder, "*", SearchOption.AllDirectories)))
        {
            if ((file.EndsWith(Scene.Extension, StringComparison.OrdinalIgnoreCase) ||
                 file.EndsWith(Prefab.Extension, StringComparison.OrdinalIgnoreCase)) &&
                !string.Equals(Path.GetFullPath(file), open, StringComparison.Ordinal) &&
                File.ReadAllText(file).Contains(id, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    // -----------------------------------------------------------------------
    // The editor's own Duplicate, rename, and Delete (ADR 0122)
    // -----------------------------------------------------------------------

    // After a Duplicate, in its undo step: each map in the copy copied to a
    // file of its own, named after its entity, with a new ID.
    internal void CopyTilemaps(Entity copy)
    {
        List<string> made = [];
        foreach (Entity entity in SceneTree.SubtreeOf(World, copy))
        {
            if (OwnMapOf(entity) is not var (_, path) || ReadAssetFile(path) is not { } text)
            {
                continue;
            }
            TilemapData data;
            try
            {
                data = TilemapImporter.Read(System.Text.Encoding.UTF8.GetBytes(text));
            }
            catch (Exception exception) when (exception is InvalidDataException or System.Text.Json.JsonException)
            {
                continue;
            }
            AssetId old = data.Id;
            data.Id = AssetId.New();
            if (FreeFileName(AssetPaths.FolderOf(path), LabelOf(entity), TilemapImporter.FileExtension, made) is { } to &&
                WriteAssetFile(to, TilemapImporter.Format(data), "Duplicate"))
            {
                made.Add(to);
                // The map entity and its layers (or a map from before layers) pointed at the copy.
                foreach (Entity under in SceneTree.SubtreeOf(World, entity))
                {
                    if (World.Has(under, Components.Of<Tilemap>()) && World.Read<Tilemap>(under) is { } tilemap && tilemap.Map == old)
                    {
                        Editor.Scene.Edit(under, tilemap with { Map = data.Id }, "Duplicate");
                    }
                    if (World.Has(under, Components.Of<TilemapLayers>()) && World.Read<TilemapLayers>(under).Map == old)
                    {
                        Editor.Scene.Edit(under, new TilemapLayers { Map = data.Id }, "Duplicate");
                    }
                }
            }
        }
    }

    // After a rename, in its undo step: an entity's own map file renamed
    // after it, kept in its folder, unless another entity uses the map; a
    // layer's entity renames its layer.
    internal void RenameTilemap(Entity entity, string name)
    {
        if (name.Length > 0 && MapRootOf(entity) is { } root && root.Value != entity.Value)
        {
            RenameLayerOf(root, entity, name);
            return;
        }
        if (name.Length == 0 || OwnMapOf(entity) is not var (map, path) || IsMapShared(map, entity))
        {
            return;
        }
        if (Path.GetFileNameWithoutExtension(path) == name ||
            FreeFileName(AssetPaths.FolderOf(path), name, TilemapImporter.FileExtension) is not { } to || ReadAssetFile(path) is not { } text)
        {
            return;
        }
        if (WriteAssetFile(to, text, "Rename"))
        {
            WriteAssetFile(path, null, "Rename");
        }
    }

    // A layer's entity renamed in the hierarchy: its layer in the file too.
    private void RenameLayerOf(Entity root, Entity entity, string name)
    {
        AssetId map = MapOfRoot(root);
        int id = (int)World.Read<Tilemap>(entity).Layer;
        if (AssetPathOf(map) is not { } path || ReadMap(map) is not { } data || data.Layer(id) is not { } layer || layer.Name == name)
        {
            return;
        }
        layer.Name = name;
        WriteAssetFile(path, TilemapImporter.Format(data), "Rename Layer");
        if (Map.IsOpen && Map.Map == map)
        {
            Map.Read();
            LayersChanged?.Invoke();
        }
    }

    // The map files only the deleted entities use.
    internal List<string> OwnMapFiles(IEnumerable<Entity> entities)
    {
        List<string> own = [];
        foreach (Entity entity in entities.Where(HasLive))
        {
            if (SceneTree.IsInsideInstance(World, entity))
            {
                continue;
            }
            foreach (Entity under in SceneTree.SubtreeOf(World, entity))
            {
                if (OwnMapOf(under) is var (map, path) && !IsMapShared(map, under) && !own.Contains(path))
                {
                    own.Add(path);
                }
            }
        }
        return own;
    }

    // Before a Delete: map files only the deleted entities use go too, are
    // kept, or the Delete is cancelled (ADR 0112, 0122).
    internal void AskAboutMapFiles(SceneChange change)
    {
        List<string> own = OwnMapFiles(change.Entities);
        if (own.Count == 0)
        {
            return;
        }
        if (_deleteMaps?.IsOpen == true)
        {
            change.Cancel(); // already asking about another
            return;
        }
        SceneChangeHold hold = change.Hold();
        string files = own.Count == 1 ? Path.GetFileName(own[0]) : $"its {own.Count} map files";
        _deleteMaps = new Dialog($"Also delete {files}?", "Delete File", "Keep File", "Cancel");
        _deleteMaps.Chosen += (_, chosen) =>
        {
            _deleteMaps = null;
            if (chosen == 0)
            {
                change.Then(() =>
                {
                    foreach (string path in own)
                    {
                        WriteAssetFile(path, null, "Delete");
                    }
                });
                hold.Go();
            }
            else if (chosen == 1)
            {
                hold.Go();
            }
            else
            {
                hold.Cancel();
            }
        };
        _deleteMaps.Open(Editor.Root);
    }

    /// <summary>
    /// A tileset's file changed by the Tiles panel (its grid, its tiles'
    /// shapes), saved and imported at once, as one undo step.
    /// </summary>
    internal bool EditTileset(AssetId tileset, string title, Action<TilesetData> change)
    {
        if (AssetPathOf(tileset) is not { } path || ReadAssetFile(path) is not { } before)
        {
            return false;
        }
        TilesetData data;
        try
        {
            data = TilesetImporter.Read(System.Text.Encoding.UTF8.GetBytes(before));
        }
        catch (Exception exception) when (exception is InvalidDataException or System.Text.Json.JsonException)
        {
            Log($"Can't edit {path}: {exception.Message}");
            return false;
        }
        change(data);
        string after = TilesetImporter.Format(data);
        return after != before && WriteAssetFile(path, after, title);
    }

    // The shape presets' outlines, in a tile's pixels from its top-left, y down.
    internal static List<Vector2> Preset(TilePreset preset, int width, int height)
    {
        float w = width, h = height, half = MathF.Round(height / 2f);
        return preset switch
        {
            TilePreset.Half => [new(0, half), new(w, half), new(w, h), new(0, h)],
            TilePreset.Slope => [new(0, h), new(w, 0), new(w, h)],
            TilePreset.ShallowLow => [new(0, h), new(w, half), new(w, h)],
            TilePreset.ShallowHigh => [new(0, h), new(0, half), new(w, 0), new(w, h)],
            _ => [new(0, 0), new(w, 0), new(w, h), new(0, h)],
        };
    }
}

/// <summary>The Palette's ready-made collision shapes (ADR 0108, 0112).</summary>
public enum TilePreset
{
    Full,        // the whole tile
    Half,        // its lower half
    Slope,       // 45°, rising to the right
    ShallowLow,  // the lower half of a 22.5° slope rising to the right
    ShallowHigh, // its upper half
}

/// <summary>
/// A map's file saved as it was painted, recorded as one undo step after the
/// stroke (ADR 0112, 0123): undo and redo write it back and import it at once.
/// </summary>
internal sealed class MapFileEdit(MapMode maps, string title, AssetId map, string path, string before, string after) : UndoStep(title)
{
    public override bool ChangesScene => false;

    public override void Undo(World world) => Write(before);

    public override void Redo(World world) => Write(after);

    private void Write(string text)
    {
        if (maps.Project is not { } project)
        {
            return;
        }
        try
        {
            File.WriteAllText(project.FileOf(path), text);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            maps.Log($"Can't save {path}: {exception.Message}");
            return;
        }
        maps.Editor.Assets.Reimport(map);
    }
}

/// <summary>The open scene's tree through the world's own hierarchy, for map mode (ADR 0123).</summary>
internal static class SceneTree
{
    /// <summary>Its children, in order.</summary>
    public static Entity[] ChildrenOf(World world, Entity parent)
    {
        Entity[] children = new Entity[16];
        int count = world.ChildrenOf(parent, children);
        if (count > children.Length)
        {
            children = new Entity[count];
            count = world.ChildrenOf(parent, children);
        }
        return children[..count];
    }

    /// <summary>Where it is among its parent's children; -1 at the top.</summary>
    public static int SiblingIndex(World world, Entity entity)
    {
        Entity parent = world.ParentOf(entity);
        return parent.IsNull ? -1 : Array.IndexOf(ChildrenOf(world, parent), entity);
    }

    /// <summary>An entity and everything under it.</summary>
    public static List<Entity> SubtreeOf(World world, Entity root)
    {
        List<Entity> found = [];
        Stack<Entity> pending = new([root]);
        while (pending.Count > 0)
        {
            Entity entity = pending.Pop();
            found.Add(entity);
            foreach (Entity child in ChildrenOf(world, entity))
            {
                pending.Push(child);
            }
        }
        return found;
    }

    /// <summary>Whether it's inside a prefab instance, below its root (whose shape the prefab owns).</summary>
    public static bool IsInsideInstance(World world, Entity entity)
    {
        ComponentId instance = Components.Of<PrefabInstance>();
        for (Entity parent = world.ParentOf(entity); !parent.IsNull; parent = world.ParentOf(parent))
        {
            if (world.Has(parent, instance))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>The scene's entities: those with a scene ID.</summary>
    public static List<Entity> Entities(World world)
    {
        using Query query = world.CreateQuery([Components.Of<SceneEntityId>()]);
        Collector collector = new() { Found = [] };
        query.ForEachChunk(ref collector);
        return collector.Found;
    }

    private struct Collector : IChunkJob
    {
        public List<Entity> Found;

        public readonly void Execute(in ChunkView chunk) => Found.AddRange(chunk.Entities);
    }
}
