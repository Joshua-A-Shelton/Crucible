using System.Numerics;
using System.Text.Json.Nodes;
using Crucible.Assets;
using Crucible.Assets.Importers;
using Crucible.Ecs;
using Crucible.Physics2D;
using Crucible.Render2D;
using Crucible.Scenes;
using Crucible.UI;

namespace Crucible.Editor;

// Tilemaps (ADR 0108, 0112): a map is an entity (TilemapLayers naming its
// .crtilemap) with a child per layer, each child's Tilemap drawing one layer
// and, if it collides, its Collider2D of shape Tilemap. Map mode edits them
// as one, in a layout of its own. The map file is kept with its entity:
// Create > 2D > Tilemap makes both, duplicating the entity copies the file,
// renaming it renames the file (unless another entity uses it), deleting it
// asks whether the file goes too. Each file change is part of the edit's
// undo step.
internal sealed partial class Shell
{
    private Dialog? _deleteMaps;
    private JsonObject? _normalLayout; // the layout map mode replaced
    private MapEditor? _mapEditor;

    /// <summary>Map mode's editing (ADR 0112).</summary>
    internal MapEditor Map => _mapEditor ??= new MapEditor(this);

    /// <summary>The editor is in map mode: its own layout, the tools applying to a map.</summary>
    public bool InMapMode => _normalLayout is not null;

    private static readonly string _tilemapName = Components.NameOf(typeof(Tilemap));
    private static readonly string _layersName = Components.NameOf(typeof(TilemapLayers));
    private static readonly string _colliderName = Components.NameOf(typeof(Collider2D));

    /// <summary>
    /// Create > 2D > Tilemap: a map entity under <paramref name="parent"/>
    /// (a Transform2D and TilemapLayers) with one layer, "Layer 1" (a
    /// Transform2D, a Tilemap, and a static Collider2D of shape Tilemap), and
    /// a new map file for it in <see cref="TilemapFolder"/>, named after it;
    /// map mode opens on it.
    /// </summary>
    public Entity CreateTilemapEntity(Entity parent)
    {
        if (!_context.HasWorld || Project is null)
        {
            return Entity.Null;
        }
        const string name = "Tilemap";
        TilemapData data = new() { Id = AssetId.New() };
        if (FreeFileName(TilemapFolder(), name, TilemapImporter.FileExtension) is not { } path)
        {
            return Entity.Null;
        }
        History.BeginGroup("Create Tilemap");
        Entity root = Entity.Null;
        if (WriteAssetFile(path, TilemapImporter.Format(data)))
        {
            root = World.CreateEntity();
            World.Set(root, Transform2D.Identity);
            World.Set(root, new SceneEntityId { Value = SceneIds.NewId() });
            World.SetManaged(root, new EntityName { Value = name });
            World.Set(root, new TilemapLayers { Map = data.Id });
            if (HasLive(parent))
            {
                World.SetParent(root, parent);
            }
            MakeLayerEntity(root, data.Id, data.Layers[0], sortingLayer: 0, order: 0, collides: true);
            RecordCreated("Create Tilemap", root);
        }
        History.EndGroup();
        if (!root.IsNull)
        {
            Selection.Selected = root;
            EnterMapMode(root);
        }
        return root;
    }

    // A layer's entity under a map's, not recorded (the caller records it).
    private Entity MakeLayerEntity(Entity root, AssetId map, TileLayer layer, uint sortingLayer, int order, bool collides)
    {
        Entity entity = World.CreateEntity();
        World.Set(entity, Transform2D.Identity);
        World.Set(entity, new SceneEntityId { Value = SceneIds.NewId() });
        World.SetManaged(entity, new EntityName { Value = layer.Name });
        World.Set(entity, new Tilemap { Map = map, Layer = (uint)layer.Id, SortingLayer = sortingLayer, OrderInLayer = order });
        if (collides)
        {
            World.Set(entity, new Collider2D { Shape = ShapeKind2D.Tilemap });
        }
        World.SetParent(entity, root);
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
        foreach (Entity child in SceneIds.ChildrenOf(World, root))
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
        Tilemap tilemap = World.Read<Tilemap>(entity);
        TilemapData data = ReadMap(tilemap.Map) ?? new TilemapData();
        TileLayer layer = data.Layer((int)tilemap.Layer) ?? data.Layers.FirstOrDefault() ?? new TileLayer();
        History.BeginGroup("Convert to Layers");
        string rigidName = Components.NameOf(typeof(RigidBody2D));
        JsonNode? collider = SceneIds.Read(World, entity, _colliderName), rigid = SceneIds.Read(World, entity, rigidName);
        Entity child = World.CreateEntity();
        World.Set(child, Transform2D.Identity);
        World.Set(child, new SceneEntityId { Value = SceneIds.NewId() });
        World.SetManaged(child, new EntityName { Value = layer.Name });
        World.Set(child, tilemap with { Layer = (uint)layer.Id });
        SceneIds.Write(World, child, _colliderName, collider?.DeepClone());
        SceneIds.Write(World, child, rigidName, rigid?.DeepClone());
        World.SetParent(child, entity, 0);
        RecordCreated("Convert to Layers", child);
        SetComponent(entity, _tilemapName, null, "Convert to Layers");
        SetComponent(entity, _colliderName, null, "Convert to Layers");
        SetComponent(entity, rigidName, null, "Convert to Layers");
        JsonNode? before = SceneIds.Read(World, entity, _layersName);
        World.Set(entity, new TilemapLayers { Map = tilemap.Map });
        RecordComponent(entity, _layersName, before, "Convert to Layers");
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

    // -----------------------------------------------------------------------
    // Map mode
    // -----------------------------------------------------------------------

    /// <summary>
    /// Map mode on the map an entity is (or is a layer of; a map from before
    /// layers is given them first): the Palette on the left, the viewport
    /// framed on the map with the map tools above it, the Layers and the
    /// Inspector on the right. It stays whatever is clicked, until Done.
    /// </summary>
    public bool EnterMapMode(Entity entity)
    {
        Entity? root = MapRootOf(entity);
        if (root is null && HasLive(entity) && World.Has(entity, Components.Of<Tilemap>()) &&
            !SceneIds.IsInsideInstance(World, entity))
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
        if (!InMapMode)
        {
            _normalLayout = SaveLayout();
            RestoreLayout(MapLayout());
        }
        FrameMap();
        MapModeChanged?.Invoke();
        return true;
    }

    /// <summary>Done: map mode left, the layout before it back.</summary>
    public void ExitMapMode()
    {
        if (_normalLayout is not { } normal)
        {
            return;
        }
        Map.Close();
        _normalLayout = null;
        RestoreLayout(normal);
        MapModeChanged?.Invoke();
    }

    /// <summary>Raised when map mode starts or ends.</summary>
    public event Action? MapModeChanged;

    // The layout to save: the one map mode replaced, if it's on.
    private JsonObject LayoutToSave() => _normalLayout?.DeepClone().AsObject() ?? SaveLayout();

    // Palette | viewport | Layers over the Inspector.
    private static JsonObject MapLayout() => new()
    {
        ["Main"] = new JsonObject
        {
            ["Split"] = "Horizontal",
            ["Sizes"] = new JsonArray(0.24f, 0.54f, 0.22f),
            ["Children"] = new JsonArray(
                new JsonObject { ["Panels"] = new JsonArray("Palette") },
                new JsonObject { ["Panels"] = new JsonArray("Viewport") },
                new JsonObject
                {
                    ["Split"] = "Vertical",
                    ["Sizes"] = new JsonArray(0.45f, 0.55f),
                    ["Children"] = new JsonArray(
                        new JsonObject { ["Panels"] = new JsonArray("Layers") },
                        new JsonObject { ["Panels"] = new JsonArray("Inspector") }),
                }),
        },
        ["Floating"] = new JsonArray(),
    };

    /// <summary>The viewport looking straight at the map, all of it in view.</summary>
    public void FrameMap()
    {
        if (!Map.IsOpen || _open.GetValueOrDefault("Viewport") is not ViewportPanel viewport)
        {
            return;
        }
        (Vector3 center, float radius) = Map.Bounds();
        EditorCamera camera = viewport.View.Camera;
        camera.Yaw = 0;
        camera.Pitch = 0;
        camera.Pivot = center;
        camera.Distance = radius * 1.15f / MathF.Tan(camera.FieldOfViewY * 0.5f);
    }

    // Each frame: map mode ends if its map entity went (undone, deleted, another scene).
    internal void TickMapMode()
    {
        Map.Tick();
        if (InMapMode && !Map.IsOpen)
        {
            ExitMapMode();
        }
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
        History.BeginGroup("Add Layer");
        TileLayer made = new() { Id = id, Name = name };
        Map.Change("Add Layer", data => data.Layers.Insert(above < 0 ? data.Layers.Count : above + 1, made));
        Entity entity = MakeLayerEntity(root, Map.Map, made, under?.SortingLayer ?? 0, order, collides: false);
        ArrangeLayerEntities(root);
        RecordCreated("Add Layer", entity);
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
            Delete(entity);
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
            JsonNode? before = SceneIds.Read(World, entity, _nameName);
            World.SetManaged(entity, new EntityName { Value = name });
            RecordComponent(entity, _nameName, before, "Rename Layer");
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
        List<TileLayer> layers = Map.Data.Layers;
        int at = layers.FindIndex(layer => layer.Id == id), to = at + by;
        if (at < 0 || to < 0 || to >= layers.Count)
        {
            return false;
        }
        int other = layers[to].Id;
        History.BeginGroup("Move Layer");
        Map.Change("Move Layer", data => (data.Layers[at], data.Layers[to]) = (data.Layers[to], data.Layers[at]));
        if (LayerEntity(Map.Root, id) is { } a && LayerEntity(Map.Root, other) is { } b)
        {
            JsonNode? beforeA = SceneIds.Read(World, a, _tilemapName), beforeB = SceneIds.Read(World, b, _tilemapName);
            Tilemap ta = World.Read<Tilemap>(a), tb = World.Read<Tilemap>(b);
            if (ta.SortingLayer == tb.SortingLayer && (by > 0 ? ta.OrderInLayer <= tb.OrderInLayer : ta.OrderInLayer >= tb.OrderInLayer))
            {
                (ta.OrderInLayer, tb.OrderInLayer) = (tb.OrderInLayer, ta.OrderInLayer);
                if (ta.OrderInLayer == tb.OrderInLayer)
                {
                    ta.OrderInLayer += by;
                }
                World.Set(a, ta);
                World.Set(b, tb);
                RecordComponent(a, _tilemapName, beforeA, "Move Layer");
                RecordComponent(b, _tilemapName, beforeB, "Move Layer");
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
            int was = SceneIds.SiblingIndex(World, entity);
            if (was != index)
            {
                ulong parent = SceneIds.IdOf(World, root);
                World.SetParent(entity, root, (uint)index);
                History.Push(new ParentEdit("Move Layer", SceneIds.IdOf(World, entity), (parent, was, default),
                    (parent, SceneIds.SiblingIndex(World, entity), default)));
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
        JsonNode? before = SceneIds.Read(World, entity, _tilemapName);
        Tilemap tilemap = World.Read<Tilemap>(entity);
        tilemap.Tint = tilemap.Tint with { W = visible ? 1 : 0 };
        World.Set(entity, tilemap);
        RecordComponent(entity, _tilemapName, before, visible ? "Show Layer" : "Hide Layer");
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
        JsonNode? before = SceneIds.Read(World, entity, _colliderName);
        if (collides)
        {
            World.Set(entity, new Collider2D { Shape = ShapeKind2D.Tilemap });
        }
        else
        {
            World.Remove(entity, Components.Of<Collider2D>());
        }
        RecordComponent(entity, _colliderName, before, collides ? "Layer Collides" : "Layer Doesn't Collide");
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
        int order = Map.Data.Layers.FindIndex(l => l.Id == id);
        History.BeginGroup("Re-create Layer");
        Entity entity = MakeLayerEntity(Map.Root, Map.Map, layer, 0, order, collides: false);
        ArrangeLayerEntities(Map.Root);
        RecordCreated("Re-create Layer", entity);
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
        string folder = Path.GetDirectoryName(texturePath)?.Replace('\\', '/') ?? "";
        if (FreeFileName(folder, Path.GetFileNameWithoutExtension(texturePath), TilesetImporter.FileExtension) is not { } path ||
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
            ApplyImportSettings(texture, settings, openSheet: false);
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
        dialog.Open(Main.Root);
        LastTilesetDialog = dialog;
        return dialog;
    }

    /// <summary>The New Tileset dialog opened last (the checks answer it).</summary>
    internal TilesetDialog? LastTilesetDialog { get; private set; }

    /// <summary>A tileset shown alone in the Palette, its collision to edit (double-clicking one outside map mode).</summary>
    public void ShowTileset(AssetId tileset)
    {
        if (!_open.ContainsKey("Palette"))
        {
            TogglePanel("Palette");
        }
        if (_open.GetValueOrDefault("Palette") is PalettePanel panel)
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
        if (Project is { } project && ScenePath is { } scene)
        {
            string folder = Path.GetRelativePath(project.AssetsFolder, Path.GetDirectoryName(scene) ?? "").Replace('\\', '/');
            if (!folder.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(folder))
            {
                string own = Path.GetFileNameWithoutExtension(scene) + " Tilemaps";
                return folder is "." or "" ? own : $"{folder}/{own}";
            }
        }
        return "Tilemaps";
    }

    // A map file's path in the Assets folder, by its asset ID.
    private string? AssetPathOf(AssetId id) => id.IsNone ? null : Database?.Find(id)?.Path;

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
        string Of(string file) => folder.Length == 0 ? file : $"{folder}/{file}";
        string path = Of(clean + extension);
        for (int n = 2; File.Exists(Path.Combine(project.AssetsFolder, path)) || taken?.Contains(path) == true; ++n)
        {
            path = Of($"{clean} {n}{extension}");
        }
        return path;
    }

    // Writes (or, with null, deletes) a file in the Assets folder as an undo
    // step of its own, and rescans; false (logged) if it can't.
    private bool WriteAssetFile(string path, string? text, string title = "Edit File")
    {
        string? before = ReadAssetFile(path);
        if (!AssetFileEdit.Write(this, path, text))
        {
            return false;
        }
        History.Push(new AssetFileEdit(this, title, path, before, text));
        return true;
    }

    private string? ReadAssetFile(string path)
    {
        string file = Path.Combine(Project!.AssetsFolder, path);
        return File.Exists(file) ? File.ReadAllText(file) : null;
    }

    // The map file an entity keeps: a map entity's (TilemapLayers), or a
    // map's from before layers (its own Tilemap, not a layer's); null for a
    // layer, a prefab's, or a file that isn't there.
    private (AssetId Map, string Path)? OwnMapOf(Entity entity)
    {
        if (SceneIds.IsInsideInstance(World, entity) || World.Has(entity, Components.Of<PrefabInstance>()))
        {
            return null; // a prefab's maps are the prefab's
        }
        AssetId map = MapOfRoot(entity);
        if (map.IsNone && World.Has(entity, Components.Of<Tilemap>()) && MapRootOf(entity) is null)
        {
            map = World.Read<Tilemap>(entity).Map;
        }
        return AssetPathOf(map) is { } path && File.Exists(Path.Combine(Project!.AssetsFolder, path)) ? (map, path) : null;
    }

    // An entity and everything under it.
    private List<Entity> SubtreeOf(Entity root)
    {
        List<Entity> found = [];
        Stack<Entity> pending = new([root]);
        while (pending.Count > 0)
        {
            Entity entity = pending.Pop();
            found.Add(entity);
            foreach (Entity child in SceneIds.ChildrenOf(World, entity))
            {
                pending.Push(child);
            }
        }
        return found;
    }

    /// <summary>
    /// Whether a map is used other than by <paramref name="by"/> and its
    /// layers: by another entity of the open scene, or named in another
    /// scene or prefab file.
    /// </summary>
    public bool IsMapShared(AssetId map, Entity by)
    {
        HashSet<ulong> own = [.. SubtreeOf(by).Select(entity => entity.Value)];
        foreach (Entity other in SceneIds.Of(World).Entities)
        {
            if (!own.Contains(other.Value) &&
                ((World.Has(other, Components.Of<Tilemap>()) && World.Read<Tilemap>(other).Map == map) ||
                 (World.Has(other, Components.Of<TilemapLayers>()) && World.Read<TilemapLayers>(other).Map == map)))
            {
                return true;
            }
        }
        if (Project is not { } project || !Directory.Exists(project.AssetsFolder))
        {
            return false;
        }
        string id = map.ToString();
        string? open = ScenePath is { } scene ? Path.GetFullPath(scene) : null;
        foreach (string file in Directory.EnumerateFiles(project.AssetsFolder, "*", SearchOption.AllDirectories))
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

    // Duplicate's part (before its step is recorded): each map in the copy
    // copied to a file of its own, named after its entity, with a new ID.
    private void CopyTilemaps(Entity copy)
    {
        List<string> made = [];
        foreach (Entity entity in SubtreeOf(copy))
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
            string folder = Path.GetDirectoryName(path)?.Replace('\\', '/') ?? "";
            if (FreeFileName(folder, LabelOf(entity), TilemapImporter.FileExtension, made) is { } to &&
                WriteAssetFile(to, TilemapImporter.Format(data)))
            {
                made.Add(to);
                // The map entity and its layers (or a map from before layers) pointed at the copy.
                foreach (Entity under in SubtreeOf(entity))
                {
                    if (World.Has(under, Components.Of<Tilemap>()) && World.Read<Tilemap>(under) is { } tilemap && tilemap.Map == old)
                    {
                        World.Set(under, tilemap with { Map = data.Id });
                    }
                    if (World.Has(under, Components.Of<TilemapLayers>()) && World.Read<TilemapLayers>(under).Map == old)
                    {
                        World.Set(under, new TilemapLayers { Map = data.Id });
                    }
                }
            }
        }
    }

    // Rename's part: an entity's own map file renamed after it, kept in its
    // folder, unless another entity uses the map.
    private void RenameTilemap(Entity entity, string name)
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
        string folder = Path.GetDirectoryName(path)?.Replace('\\', '/') ?? "";
        if (Path.GetFileNameWithoutExtension(path) == name ||
            FreeFileName(folder, name, TilemapImporter.FileExtension) is not { } to || ReadAssetFile(path) is not { } text)
        {
            return;
        }
        if (WriteAssetFile(to, text))
        {
            WriteAssetFile(path, null);
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

    /// <summary>
    /// Deletes an entity. If it (or what's under it) has a map file no other
    /// entity uses, <paramref name="deleteMaps"/> says whether those go too;
    /// null asks first, deleting nothing until answered.
    /// </summary>
    public void Delete(Entity entity, bool? deleteMaps)
    {
        if (!HasLive(entity))
        {
            return;
        }
        List<string> own = [];
        if (!SceneIds.IsInsideInstance(World, entity))
        {
            foreach (Entity under in SubtreeOf(entity))
            {
                if (OwnMapOf(under) is var (map, path) && !IsMapShared(map, under) && !own.Contains(path))
                {
                    own.Add(path);
                }
            }
        }
        if (own.Count == 0)
        {
            Delete(entity);
            return;
        }
        if (deleteMaps is { } choice)
        {
            History.BeginGroup("Delete");
            Delete(entity);
            if (choice)
            {
                foreach (string path in own)
                {
                    WriteAssetFile(path, null);
                }
            }
            History.EndGroup();
            return;
        }
        if (_deleteMaps?.IsOpen == true)
        {
            return;
        }
        string files = own.Count == 1 ? Path.GetFileName(own[0]) : $"its {own.Count} map files";
        _deleteMaps = new Dialog($"Also delete {files}?", "Delete File", "Keep File", "Cancel");
        _deleteMaps.Chosen += (_, chosen) =>
        {
            _deleteMaps = null;
            if (chosen is 0 or 1 && HasLive(entity))
            {
                Delete(entity, chosen == 0);
            }
        };
        _deleteMaps.Open(Main.Root);
    }

    /// <summary>A file's change already made, recorded as an undo step (map mode's edits to its map).</summary>
    internal void PushFileEdit(string title, string path, string? before, string? after) =>
        History.Push(new AssetFileEdit(this, title, path, before, after));

    // A file in the Assets folder written or deleted (null), and back on undo.
    private sealed class AssetFileEdit(Shell shell, string title, string path, string? before, string? after) : UndoStep(title)
    {
        public override bool ChangesScene => false;

        public override void Undo(World world) => Write(shell, path, before);

        public override void Redo(World world) => Write(shell, path, after);

        public static bool Write(Shell shell, string path, string? text)
        {
            if (shell.Project is not { } project)
            {
                return false;
            }
            string file = Path.Combine(project.AssetsFolder, path);
            try
            {
                if (text is null)
                {
                    File.Delete(file);
                }
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                    File.WriteAllText(file, text);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                shell.Log($"Can't {(text is null ? "delete" : "save")} {path}: {exception.Message}");
                return false;
            }
            shell.RefreshAssets();
            return true;
        }
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
        if (after == before || !AssetFileEdit.Write(this, path, after))
        {
            return false;
        }
        History.Push(new AssetFileEdit(this, title, path, before, after));
        return true;
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
