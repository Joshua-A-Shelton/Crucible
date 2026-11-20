using System.Runtime.InteropServices;
using Crucible.Assets;
using Crucible.Assets.Importers;
using Crucible.Binary;
using Crucible.Ecs;
using Crucible.Loop;
using Crucible.Physics2D;
using Crucible.Render2D;
using Crucible.Scenes;

namespace Crucible.Editor;

/// <summary>
/// Tilesets and tilemaps checking themselves in the editor (ADR 0108), for
/// CTest's launcher.jit.tilemaps: run with the Walker's project and
/// <c>--game-arg check-tilemaps=&lt;folder&gt;</c>. On a copy of the project
/// (in the folder) with a two-tile sheet added: Create > 2D > Tilemap makes
/// an entity and its map file and shows the Tiles panel; a texture dropped
/// on the panel becomes the map's tileset; the Collision tab's presets,
/// flips, Custom, and Clear set tiles' shapes, undoably; the Paint tab's
/// Paint, undo and redo, Erase, Rectangle, Fill, and Pick change the map
/// file, its import following with collision; renaming, duplicating, and
/// deleting the entity rename, copy, and delete its map file, a shared map
/// staying put.
/// </summary>
[System(Phases.RenderPrepare)]
internal sealed class TilemapChecks : GameSystem
{
    private IEnumerator<int>? _script;
    private int _wait;
    private int _checks;
    private int _failures;
    private string _folder = "";

    public override void Start(FrameContext context)
    {
        if (!context.GameArguments.TryGetValue("check-tilemaps", out string? folder))
        {
            Enabled = false;
            return;
        }
        _folder = folder;
        _script = Script().GetEnumerator();
        _wait = 3;
    }

    public override void Run(FrameContext context)
    {
        if (_script is null || _wait-- > 0)
        {
            return;
        }
        try
        {
            if (_script.MoveNext())
            {
                _wait = _script.Current;
                return;
            }
        }
        catch (Exception exception)
        {
            Check(false, "the script ran", exception.ToString());
        }
        _script = null;
        Console.WriteLine(_failures == 0
            ? $"TilemapChecks: all {_checks} checks passed"
            : $"TilemapChecks: {_failures} of {_checks} checks failed");
        context.QuitRequested = true;
    }

    private IEnumerable<int> Script()
    {
        Shell shell = Shell.Current ?? throw new InvalidOperationException("No editor shell");
        string source = Path.GetDirectoryName(AssetChecks.SourceProjectFile(shell)) ?? "";
        string copy = Path.Combine(Path.GetFullPath(_folder), "tilemaps-project");
        if (Directory.Exists(copy))
        {
            Directory.Delete(copy, recursive: true);
        }
        AssetChecks.CopyProject(source, copy, AssetChecks.IsWarm(shell));
        // Two 16x16 tiles side by side, at 16 pixels a meter.
        byte[] tga = new byte[18 + 32 * 16 * 4];
        tga[2] = 2;
        BitConverter.TryWriteBytes(tga.AsSpan(12), (ushort)32);
        BitConverter.TryWriteBytes(tga.AsSpan(14), (ushort)16);
        tga[16] = 32;
        tga[17] = 0x28;
        Array.Fill(tga, (byte)200, 18, tga.Length - 18);
        File.WriteAllBytes(Path.Combine(copy, "Assets", "Ground.tga"), tga);
        shell.OpenProject(Path.Combine(copy, Path.GetFileName(AssetChecks.SourceProjectFile(shell))));
        yield return 3;

        AssetDatabase database = shell.Database!;
        AssetEntry texture = database.Find("Ground.tga")!;

        // Create > 2D > Tilemap: the entity, its map file, the Tiles panel.
        Entity entity = shell.CreateTilemapEntity(Entity.Null);
        World world = shell.World;
        Check(!entity.IsNull && world.Has(entity, Components.Of<Tilemap>()) && world.Has(entity, Components.Of<Transform2D>()) &&
              world.Read<Collider2D>(entity).Shape == ShapeKind2D.Tilemap,
            "Create > 2D > Tilemap makes a 2D entity with a Tilemap and a Tilemap collider");
        string folder = shell.TilemapFolder();
        AssetId map = world.Read<Tilemap>(entity).Map;
        Check(database.Find(map)?.Path == $"{folder}/Tilemap.crtilemap", "its map file is made in the scene's tilemap folder",
            database.Find(map)?.Path ?? "none");
        yield return 3;
        TilesPanel? panel = Panels<TilesPanel>(shell);
        TilePainter painter = shell.Tiles;
        Check(panel is not null && painter.IsEditing && painter.Map == map && !painter.IsPainting,
            "selecting it shows the Tiles panel on its map, not painting yet",
            $"panel {panel is not null}, editing {painter.IsEditing}, importer {database.Find(map)?.Importer ?? "none"}");
        if (panel is null)
        {
            yield break;
        }

        // A texture dropped on the panel: a tileset beside it, the map's.
        panel.DropAsset(texture);
        yield return 3;
        AssetId tileset = database.Find("Ground.crtileset")?.Id ?? AssetId.None;
        Check(!tileset.IsNone && painter.Data.Tileset == tileset, "a texture dropped makes Ground.crtileset, the map's tileset");
        Check(database.SettingsOf(texture.Id)?["Usage"]?.ToString() == "Sprite", "the texture is used as a sprite");
        long started = Environment.TickCount64;
        while (panel.View.Grid.Columns == 0 && Environment.TickCount64 - started < 10000)
        {
            yield return 1;
        }
        Check(panel.View.Grid == (2, 1), "the panel shows the texture's two tiles", $"{panel.View.Grid}");

        // Collision: presets, a flip undone, Clear and Custom.
        panel.SetTab(collision: true);
        panel.View.Selection = (0, 0, 0, 0);
        panel.SetShape(TilePreset.Full);
        panel.View.Selection = (1, 0, 1, 0);
        panel.SetShape(TilePreset.Slope);
        yield return 2;
        Check(TilesetText(copy).Contains("\"Tile\": 1, \"Shapes\": [ [[0, 16], [16, 0], [16, 16]] ]", StringComparison.Ordinal),
            "a preset saves at once", TilesetText(copy));
        panel.MirrorShapes(x: true);
        yield return 2;
        Check(TilesetText(copy).Contains("[[16, 16], [0, 0], [0, 16]]", StringComparison.Ordinal), "Flip X mirrors the shape",
            TilesetText(copy));
        shell.Undo();
        yield return 2;
        Check(TilesetText(copy).Contains("[[0, 16], [16, 0], [16, 16]]", StringComparison.Ordinal), "undo takes the flip back");
        panel.ClearShapes();
        panel.SetDrawing(true);
        foreach ((float x, float y) in new[] { (16f, 16f), (32f, 0f), (32f, 16f), (16f, 16f) })
        {
            panel.AddPoint(new System.Numerics.Vector2(x, y));
        }
        yield return 2;
        TilesetData shapes = TilesetImporter.Read(File.ReadAllBytes(Path.Combine(copy, "Assets", "Ground.crtileset")));
        Check(shapes.Shapes.TryGetValue(1, out var drawn) && drawn.Count == 1 && drawn[0].Count == 3 &&
              shapes.Shapes.TryGetValue(0, out var full) && full[0].Count == 4,
            "Clear and a Custom shape closed on its first point leave tile 1 one triangle, tile 0 Full");

        // Paint: a two-tile brush starts painting; undo, redo.
        panel.SetTab(collision: false);
        painter.SetBrush(0, 0, 1, 0);
        panel.SetTool(TileTool.Paint);
        Check(painter.IsPainting, "choosing a tool starts painting");
        Check(painter.CellSize == System.Numerics.Vector2.One, "a 16-pixel tile at 16 pixels a meter is a meter");
        Check(painter.BeginStroke((0, 0)), "a stroke starts");
        painter.Stroke((0, 1));
        painter.EndStroke();
        yield return 2;
        Check(Cells(copy, map).Count == 4, "a dragged two-tile brush paints four cells", $"{Cells(copy, map).Count}");
        Check(Cells(copy, map).TryGetValue((1, 1), out TileCell painted) && painted.Tile == 1, "the brush's second tile goes right");
        Check(Loops(database, map) > 0, "the imported map has collision outlines");
        shell.Undo();
        yield return 2;
        Check(Cells(copy, map).Count == 0, "undo takes the stroke back");
        shell.Redo();
        yield return 2;
        Check(Cells(copy, map).Count == 4, "redo paints it again");

        // Erase one cell; a Rectangle; fill the empty row between two painted ones.
        panel.SetTool(TileTool.Erase);
        painter.SetBrush(0, 0, 0, 0);
        painter.BeginStroke((1, 1));
        painter.EndStroke();
        yield return 2;
        Check(Cells(copy, map).Count == 3 && !Cells(copy, map).ContainsKey((1, 1)), "Erase empties the cell");
        panel.SetTool(TileTool.Rectangle);
        painter.BeginStroke((0, 3));
        painter.Stroke((1, 4));
        painter.EndStroke();
        yield return 2;
        Check(Cells(copy, map).ContainsKey((0, 3)) && Cells(copy, map).ContainsKey((1, 4)) && Cells(copy, map).Count == 7,
            "Rectangle paints the cells between its corners", string.Join(" ", Cells(copy, map).Keys));
        panel.SetTool(TileTool.Fill);
        painter.BeginStroke((0, 2));
        painter.EndStroke();
        yield return 2;
        Check(Cells(copy, map).ContainsKey((0, 2)) && Cells(copy, map).ContainsKey((1, 2)) && Cells(copy, map).ContainsKey((1, 1)) &&
              Cells(copy, map).ContainsKey((-1, 0)),
            "Fill paints the empty cells joined to the one pressed, out to a cell beyond the painted ones",
            string.Join(" ", Cells(copy, map).Keys));
        shell.Undo();
        yield return 2;

        // Pick takes a cell's tile and goes back to Paint; Escape stops painting.
        panel.SetTool(TileTool.Pick);
        painter.BeginStroke((1, 0));
        painter.EndStroke();
        Check(painter.Tool == TileTool.Paint && painter.Brush.Count == 1 && painter.Brush[0].Tile == 1,
            "Pick makes the cell's tile the brush and Paint the tool");
        painter.IsPainting = false;
        Check(!painter.IsPainting && painter.IsEditing, "Escape stops painting, the map still selected");

        // Renaming renames the map file; duplicating copies it; undo takes the copy back.
        shell.Rename(entity, "Ground");
        yield return 2;
        Check(database.Find(map)?.Path == $"{folder}/Ground.crtilemap" && world.Read<Tilemap>(entity).Map == map,
            "renaming the entity renames its map file, keeping its ID", database.Find(map)?.Path ?? "none");
        Entity twin = shell.Duplicate(entity);
        yield return 2;
        AssetId twinMap = world.IsAlive(twin) ? world.Read<Tilemap>(twin).Map : AssetId.None;
        Check(twinMap != map && database.Find(twinMap)?.Path == $"{folder}/Ground 2.crtilemap" &&
              Cells(copy, twinMap).Count == Cells(copy, map).Count,
            "duplicating copies the map to a file of its own", database.Find(twinMap)?.Path ?? "none");
        shell.Undo();
        yield return 2;
        Check(database.Find(twinMap) is null, "undo takes the copy's file away too");

        // A map used by two entities isn't renamed; deleting asks only for a map of its own.
        twin = shell.Duplicate(entity);
        yield return 2;
        twinMap = world.Read<Tilemap>(twin).Map;
        shell.UseMap(twin, map);
        Check(shell.IsMapShared(map, entity), "Use Another Map shares the map");
        shell.Rename(entity, "Shared");
        yield return 2;
        Check(database.Find(map)?.Path == $"{folder}/Ground.crtilemap", "a shared map keeps its name");
        shell.Delete(twin, deleteMaps: true);
        yield return 2;
        Check(database.Find(map) is not null, "deleting an entity keeps a map another uses");
        ulong entityId = SceneIds.IdOf(world, entity);
        shell.Delete(entity, deleteMaps: true);
        yield return 2;
        Check(database.Find(map) is null, "deleting an entity with Delete File deletes its map");
        shell.Undo();
        yield return 2;
        Check(database.Find(map) is not null && !SceneIds.Find(world, entityId).IsNull, "undo brings the entity and its map back");
    }

    private static T? Panels<T>(Shell shell) where T : EditorPanel =>
        shell.Windows.SelectMany(window => window.DockSpace.Panels).OfType<T>().FirstOrDefault();

    private static Dictionary<(int X, int Y), TileCell> Cells(string copy, AssetId map) =>
        Shell.Current?.Database?.Find(map) is { } entry
            ? TilemapImporter.Read(File.ReadAllBytes(Path.Combine(copy, "Assets", entry.Path))).Cells
            : [];

    private static string TilesetText(string copy) => File.ReadAllText(Path.Combine(copy, "Assets", "Ground.crtileset"));

    private static uint Loops(AssetDatabase database, AssetId map)
    {
        if (database.ImportedFile(map) is not { } path)
        {
            return 0;
        }
        using ImportedFile file = ImportedFile.Open(path, TilemapImporter.Kind, TilemapImporter.KindVersion);
        return MemoryMarshal.Read<TilemapInfo>(file.Read(file.Find(Tag.Of("INFO")))).LoopCount;
    }

    private void Check(bool passed, string what, string detail = "")
    {
        ++_checks;
        if (!passed)
        {
            ++_failures;
            Console.WriteLine($"TilemapChecks: FAILED: {what} {detail}");
        }
    }
}
