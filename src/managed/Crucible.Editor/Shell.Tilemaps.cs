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

// Tilemaps' files kept with their entities (ADR 0108, as redesigned): Create
// > 2D > Tilemap makes an entity and its map file in a folder beside the
// scene; duplicating the entity copies the file, renaming it renames the
// file (unless another entity uses it), and deleting it asks whether the
// file goes too. Each file change is part of the edit's undo step.
internal sealed partial class Shell
{
    private Dialog? _deleteMaps;

    /// <summary>
    /// Create > 2D > Tilemap: an entity under <paramref name="parent"/> with a
    /// Transform2D, a Tilemap, and a static Collider2D of shape Tilemap, and a
    /// new map file for it in <see cref="TilemapFolder"/>, named after it.
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
        Entity entity = Entity.Null;
        if (WriteAssetFile(path, TilemapImporter.Format(data)))
        {
            entity = World.CreateEntity();
            World.Set(entity, Transform2D.Identity);
            World.Set(entity, new SceneEntityId { Value = SceneIds.NewId() });
            World.SetManaged(entity, new EntityName { Value = name });
            World.Set(entity, new Tilemap { Map = data.Id });
            World.Set(entity, new Collider2D { Shape = ShapeKind2D.Tilemap });
            if (HasLive(parent))
            {
                World.SetParent(entity, parent);
            }
            RecordCreated("Create Tilemap", entity);
        }
        History.EndGroup();
        if (!entity.IsNull)
        {
            Selection.Selected = entity;
            ShowTiles();
        }
        return entity;
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

    // An entity's map, if it has a Tilemap naming a map file that's there.
    private (AssetId Map, string Path)? OwnMapOf(Entity entity)
    {
        if (!World.Has(entity, Components.Of<Tilemap>()) || SceneIds.IsInsideInstance(World, entity) ||
            World.Has(entity, Components.Of<PrefabInstance>()))
        {
            return null; // a prefab's maps are the prefab's
        }
        AssetId map = World.Read<Tilemap>(entity).Map;
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
    /// Whether a map is used other than by <paramref name="by"/>: by another
    /// entity of the open scene, or named in another scene or prefab file.
    /// </summary>
    public bool IsMapShared(AssetId map, Entity by)
    {
        foreach (Entity other in SceneIds.Of(World).Entities)
        {
            if (other.Value != by.Value && World.Has(other, Components.Of<Tilemap>()) && World.Read<Tilemap>(other).Map == map)
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
            data.Id = AssetId.New();
            string folder = Path.GetDirectoryName(path)?.Replace('\\', '/') ?? "";
            if (FreeFileName(folder, LabelOf(entity), TilemapImporter.FileExtension, made) is { } to &&
                WriteAssetFile(to, TilemapImporter.Format(data)))
            {
                made.Add(to);
                Tilemap tilemap = World.Read<Tilemap>(entity);
                tilemap.Map = data.Id;
                World.Set(entity, tilemap);
            }
        }
    }

    // Rename's part: an entity's own map file renamed after it, kept in its
    // folder, unless another entity uses the map.
    private void RenameTilemap(Entity entity, string name)
    {
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

    /// <summary>
    /// A tileset for a texture, beside it ("Ground.crtileset", numbered if
    /// taken), cut like its sprite sheet grid (16x16 without one). A texture
    /// not yet used as a sprite becomes one. Returns its ID; none if it
    /// can't be made.
    /// </summary>
    public AssetId CreateTilesetFor(AssetId texture)
    {
        if (Database is not { } database || AssetPathOf(texture) is not { } texturePath)
        {
            return AssetId.None;
        }
        JsonObject? settings = database.SettingsOf(texture);
        if (settings is not null && settings["Usage"]?.ToString() != nameof(Crucible.Importers.TextureUsage.Sprite))
        {
            settings["Usage"] = nameof(Crucible.Importers.TextureUsage.Sprite);
            ApplyImportSettings(texture, settings);
            Log($"{texturePath} is used as a sprite now");
        }
        int Whole(string key, int fallback) => settings?[key]?.GetValue<int>() is int value and > 0 ? value : fallback;
        bool grid = settings?["Slicing"]?.ToString() is null or nameof(SpriteSlicing.Grid);
        TilesetData tileset = new()
        {
            Id = AssetId.New(),
            Texture = texture,
            TileWidth = grid ? Whole("CellWidth", 16) : 16,
            TileHeight = grid ? Whole("CellHeight", 16) : 16,
            Margin = grid ? Whole("Margin", 0) : 0,
            Spacing = grid ? Whole("Spacing", 0) : 0,
        };
        string folder = Path.GetDirectoryName(texturePath)?.Replace('\\', '/') ?? "";
        if (FreeFileName(folder, Path.GetFileNameWithoutExtension(texturePath), TilesetImporter.FileExtension) is not { } path ||
            !WriteAssetFile(path, TilesetImporter.Format(tileset)))
        {
            return AssetId.None;
        }
        Log($"Made {path}");
        return tileset.Id;
    }

    /// <summary>The Tiles panel (ADR 0108), opened or brought to the front.</summary>
    public void ShowTiles()
    {
        if (!_open.ContainsKey("Tiles"))
        {
            TogglePanel("Tiles");
        }
        if (_open.GetValueOrDefault("Tiles") is TilesPanel panel)
        {
            panel.Group?.Activate(panel);
        }
    }

    /// <summary>The Tiles panel on a tileset alone (double-clicking one): its Collision tab.</summary>
    public void ShowTileset(AssetId tileset)
    {
        ShowTiles();
        (_open.GetValueOrDefault("Tiles") as TilesPanel)?.ShowTileset(tileset);
    }

    /// <summary>A painting tool chosen, as its button or key does: painting starts with it.</summary>
    internal void SetTileTool(TileTool tool)
    {
        Tiles.Tool = tool;
        Tiles.IsPainting = Tiles.IsEditing;
    }

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

    /// <summary>A map's tileset set (Use Another Map leaves files as they are), as one undo step.</summary>
    internal bool SetMapTileset(AssetId map, AssetId tileset)
    {
        if (AssetPathOf(map) is not { } path || ReadAssetFile(path) is not { } before)
        {
            return false;
        }
        TilemapData data = TilemapImporter.Read(System.Text.Encoding.UTF8.GetBytes(before));
        data.Tileset = tileset;
        string after = TilemapImporter.Format(data);
        return after == before || WriteAssetFile(path, after, "Set Tileset");
    }

    /// <summary>Use Another Map: the entity's Tilemap set to a map that's already there.</summary>
    internal void UseMap(Entity entity, AssetId map)
    {
        if (!HasLive(entity) || !World.Has(entity, Components.Of<Tilemap>()))
        {
            return;
        }
        string name = Components.NameOf(typeof(Tilemap));
        JsonNode? before = SceneIds.Read(World, entity, name);
        Tilemap tilemap = World.Read<Tilemap>(entity);
        tilemap.Map = map;
        World.Set(entity, tilemap);
        RecordComponent(entity, name, before, "Use Another Map");
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

/// <summary>The Collision tab's ready-made shapes (ADR 0108).</summary>
public enum TilePreset
{
    Full,        // the whole tile
    Half,        // its lower half
    Slope,       // 45°, rising to the right
    ShallowLow,  // the lower half of a 22.5° slope rising to the right
    ShallowHigh, // its upper half
}
