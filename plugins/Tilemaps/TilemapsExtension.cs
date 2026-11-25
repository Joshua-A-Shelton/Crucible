using Crucible.Assets;
using Crucible.Assets.Importers;
using Crucible.Ecs;
using Crucible.Editor;
using Crucible.Render2D;

namespace Crucible.Tilemaps.Editor;

/// <summary>
/// The official plugin Tilemaps (ADR 0123): map mode (ADR 0112) as a
/// workspace, the Palette on its own for a tileset's collision, Create > 2D >
/// Tilemap in the Hierarchy, Create > Tileset… in Assets, tilesets and maps
/// opening from Assets and the Inspector, and the map files following the
/// editor's own Duplicate, rename, and Delete (ADR 0122). Only the editor's
/// public API, so a project that leaves the plugin out loses nothing else.
/// </summary>
public sealed class TilemapsExtension : EditorExtension
{
    internal const string MapWorkspaceId = "Map";
    internal const string PaletteId = "Palette";

    /// <summary>The running plugin's map mode (the checks drive it); null while it isn't running.</summary>
    internal static MapMode? Current { get; private set; }

    private MapMode? _maps;

    public override void Start(EditorContext editor)
    {
        MapMode maps = new(editor);
        _maps = maps;
        Current = maps;
        editor.Workspaces.Add(MapWorkspaceId, "Map", () => new MapWorkspace(maps));
        editor.Panels.Add(PaletteId, "Palette", () => maps.Palette = new PalettePanel(maps));

        // Opening (ADR 0120): a tileset alone in the Palette, a map through
        // the entity that uses it, and map mode from a map's components.
        editor.Assets.AddOpener(PaletteId, "Palette", ["Tileset"], asset => maps.ShowTileset(asset.Id));
        editor.Assets.AddOpener(MapWorkspaceId, "Map", [TilemapImporter.FileExtension], asset => OpenTilemapFile(maps, asset));
        editor.Scene.AddOpener<TilemapLayers>(MapWorkspaceId, "Map", entity => maps.EnterMapMode(entity));
        editor.Scene.AddOpener<Tilemap>(MapWorkspaceId, "Map", entity => maps.EnterMapMode(entity));

        // Making them: a map with its entity (ADR 0108, 0112), a tileset
        // through New Tileset, with the texture clicked if it's one.
        editor.Scene.AddCreate("2D/Tilemap", parent => maps.CreateTilemapEntity(parent));
        editor.Assets.AddCreate("Tileset…", (_, asset) =>
            maps.OpenTilesetDialog(asset is { Importer: "Texture" } texture ? texture.Id : AssetId.None));

        // Map files kept with their entities through the editor's own edits.
        editor.Scene.Duplicated += copies =>
        {
            foreach ((Entity _, Entity copy) in copies)
            {
                maps.CopyTilemaps(copy);
            }
        };
        editor.Scene.Renamed += (entity, _, after) => maps.RenameTilemap(entity, after);
        editor.Scene.Deleting += maps.AskAboutMapFiles;
        editor.Assets.Changed += maps.RaiseAssetsChanged;
    }

    public override void Stop()
    {
        _maps?.Map.Close();
        if (Current == _maps)
        {
            Current = null;
        }
        _maps = null;
    }

    // A map's file: map mode on the one entity of the open scene using it;
    // with several, which; with none, said.
    private static void OpenTilemapFile(MapMode maps, AssetEntry asset)
    {
        Entity[] users = maps.HasWorld
            ? [.. SceneTree.Entities(maps.World).Where(entity => maps.World.Has(entity, Components.Of<TilemapLayers>()) &&
                                                                maps.World.Read<TilemapLayers>(entity).Map == asset.Id)]
            : [];
        string name = Path.GetFileName(AssetPaths.WithinRoot(asset.Path));
        if (users.Length == 1)
        {
            maps.EnterMapMode(users[0]);
            return;
        }
        if (users.Length == 0)
        {
            string scene = maps.Editor.Scene.Path is { } path ? Path.GetFileName(path) : "the open scene";
            maps.Editor.LogWarning($"No entity in {scene} uses {name}");
            return;
        }
        maps.Editor.Ask($"Which map uses {name}?", [.. users.Select(maps.LabelOf), "Cancel"], choice =>
        {
            if (choice >= 0 && choice < users.Length && maps.HasLive(users[choice]))
            {
                maps.EnterMapMode(users[choice]);
            }
        });
    }
}
