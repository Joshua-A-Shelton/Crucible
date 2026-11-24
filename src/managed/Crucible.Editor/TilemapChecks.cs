using System.Numerics;
using System.Runtime.InteropServices;
using Crucible.Assets;
using Crucible.Assets.Importers;
using Crucible.Binary;
using Crucible.Ecs;
using Crucible.Loop;
using Crucible.Physics2D;
using Crucible.Platform;
using Crucible.Render2D;
using Crucible.Scenes;
using Crucible.UI;
using V = Crucible.Editor.ViewportChecks;

namespace Crucible.Editor;

/// <summary>
/// Map mode checking itself in the editor (ADR 0108, 0112), for CTest's
/// launcher.jit.tilemaps: run with the Walker's project and <c>--game-arg
/// check-tilemaps=&lt;folder&gt;</c>. On a copy of the project (in the
/// folder) with two sheets added, one of a size that doesn't import as a
/// texture: Create > 2D > Tilemap makes a map entity with one layer and its
/// file, and opens map mode in its layout, the viewport looking straight at
/// the map; New Tileset makes a tileset of each sheet (the odd one made a
/// sprite so it imports), refusing a tile size that isn't the map's; the
/// Palette's Collision makes tiles solid with a click, sets presets, flips,
/// Custom shapes, and Clear, undoably; Stamp, undo and redo, mixing the two
/// tilesets, Eraser, Rectangle, Fill, Select's copy, move, and delete, and
/// picking up with the right button change the map file, its import
/// following with collision; layers are added, painted apart, renamed,
/// moved, hidden, made to collide, deleted, and offered back when their
/// entity goes; Done brings the layout back; a map from before layers is
/// given them; renaming, duplicating, and deleting the map entity rename,
/// copy, and delete its file. The window is saved as tilemaps.png in map mode.
/// </summary>
[System(Phases.RenderPrepare)]
internal sealed class TilemapChecks : GameSystem
{
    private const int ScancodeR = 21; // SDL's, for R

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

    // A TGA of solid color, its tiles 16x16.
    private static void WriteSheet(string path, int width, int height, byte shade)
    {
        byte[] tga = new byte[18 + width * height * 4];
        tga[2] = 2;
        BitConverter.TryWriteBytes(tga.AsSpan(12), (ushort)width);
        BitConverter.TryWriteBytes(tga.AsSpan(14), (ushort)height);
        tga[16] = 32;
        tga[17] = 0x28;
        Array.Fill(tga, shade, 18, tga.Length - 18);
        File.WriteAllBytes(path, tga);
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
        // Two 16x16 tiles side by side, at 16 pixels a meter; and two more
        // inside a pixel's margin, 34x18 (not whole 4x4 blocks).
        WriteSheet(Path.Combine(copy, "Assets", "Ground.tga"), 32, 16, 200);
        WriteSheet(Path.Combine(copy, "Assets", "Odd.tga"), 34, 18, 90);
        shell.OpenProject(Path.Combine(copy, Path.GetFileName(AssetChecks.SourceProjectFile(shell))));
        // The default layout, not a last run's (its Sprite Sheet Editor, say).
        shell.RestoreLayout(null);
        yield return 3;

        AssetDatabase database = shell.Database!;
        AssetEntry ground = database.Find("Ground.tga")!, odd = database.Find("Odd.tga")!;
        Check(database.ImportedFile(odd.Id) is null, "the odd sheet doesn't import as it's set (the bug a tileset of it hit)");

        // The camera before map mode, to see it come back.
        (float Pitch, float Distance) cameraBefore = Panel<ViewportPanel>(shell) is { } before
            ? (before.View.Camera.Pitch, before.View.Camera.Distance)
            : default;

        // Create > 2D > Tilemap: the map entity, its layer, its file, map mode.
        Entity root = shell.CreateTilemapEntity(Entity.Null);
        World world = shell.World;
        Entity layer1 = SceneIds.ChildrenOf(world, root).FirstOrDefault();
        AssetId map = world.IsAlive(root) && world.Has(root, Components.Of<TilemapLayers>()) ? world.Read<TilemapLayers>(root).Map : AssetId.None;
        Check(!map.IsNone && world.Has(root, Components.Of<Transform2D>()) && !world.Has(root, Components.Of<Tilemap>()),
            "Create > 2D > Tilemap makes a map entity naming its map");
        Check(!layer1.IsNull && world.Read<Tilemap>(layer1) is { Layer: 0 } first && first.Map == map &&
              world.Read<Collider2D>(layer1).Shape == ShapeKind2D.Tilemap && shell.LabelOf(layer1) == "Layer 1",
            "with one layer, Layer 1: a Tilemap of layer 0 and a Tilemap collider");
        string folder = shell.TilemapFolder();
        // Numbered if the sample has one by that name already.
        Check(database.Find(map)?.Path is { } made && made.StartsWith($"{folder}/Tilemap", StringComparison.Ordinal) &&
              made.EndsWith(TilemapImporter.FileExtension, StringComparison.Ordinal),
            "its map file is made in the scene's tilemap folder, named after it", database.Find(map)?.Path ?? "none");
        yield return 3;
        MapEditor editor = shell.Map;
        MapWorkspace? tab = shell.MapTab;
        Check(shell.InMapMode && editor.IsOpen && editor.Root.Value == root.Value && editor.ActiveLayer == 0,
            "map mode opens on it, editing its layer");
        // A tab of its own (ADR 0119) beside the Viewport; the rest of the editor stays.
        Check(tab is not null && tab.Title == "Map: Tilemap" && tab.Group?.ActivePanel == tab &&
              Panel<ViewportPanel>(shell) is { } beside && beside.Group == tab.Group &&
              Panel<InspectorPanel>(shell) is not null && Panel<HierarchyPanel>(shell) is not null,
            "in a tab of its own beside the Viewport, the Hierarchy and Inspector staying", tab?.Title ?? "no tab");
        if (tab is null)
        {
            yield break;
        }
        PalettePanel palette = tab.Palette;
        LayersPanel layers = tab.Layers;
        EditorCamera camera = tab.View.Camera;
        Vector2 size = new(400, 300);
        Check(camera.Project(Vector3.Zero, size) is { } o && camera.Project(Vector3.UnitX, size) is { } right &&
              camera.Project(Vector3.UnitY, size) is { } up && right.X > o.X && up.Y < o.Y && MathF.Abs(right.Y - o.Y) < 0.5f,
            "the viewport looks straight at the map: x to the right, y up");
        Check(tab.ToolButtons.Count == 5, "the map tools are above its view");
        // Its keys while it has focus (ADR 0119): R is Rectangle in its view, Scale in the Viewport's.
        UIRoot ui = shell.Main.Root;
        tab.View.Focus();
        HierarchyChecks.Send(ui, HierarchyChecks.KeyDown(ScancodeR, Keycode.R));
        Check(editor.Tool == MapTool.Rectangle, "its keys pick its tools while it has focus");
        if (Panel<ViewportPanel>(shell) is { } normal)
        {
            normal.View.Handles.Mode = HandleMode.Move;
            normal.Group?.Activate(normal);
            normal.View.Focus();
            HierarchyChecks.Send(ui, HierarchyChecks.KeyDown(ScancodeR, Keycode.R));
            Check(normal.View.Handles.Mode == HandleMode.Scale && editor.Tool == MapTool.Rectangle,
                "and the editor's keys are the editor's again elsewhere");
            tab.Group?.Activate(tab);
        }
        shell.SetMapTool(MapTool.Stamp);

        // New Tileset with the odd sheet: made a sprite, so it imports and shows; a margin cuts it.
        TilesetDialog dialog = shell.OpenTilesetDialog(odd.Id);
        Check(database.SettingsOf(odd.Id)?["Usage"]?.ToString() == "Sprite" && database.ImportedFile(odd.Id) is not null,
            "choosing a sheet that doesn't import makes it a sprite, which does");
        dialog.Fields.Margin.Value = 1;
        long started = Environment.TickCount64;
        while (dialog.Preview.Grid.Columns == 0 && Environment.TickCount64 - started < 10000)
        {
            yield return 1;
        }
        Check(dialog.Preview.Grid == (2, 1), "the dialog shows the grid on the image: two tiles", $"{dialog.Preview.Grid}");
        dialog.Answer(create: true);
        yield return 3;
        AssetId oddSet = database.Find("Odd.crtileset")?.Id ?? AssetId.None;
        Check(!oddSet.IsNone && editor.Data.Tilesets.SequenceEqual([oddSet]), "Create makes Odd.crtileset, the map's first tileset");
        Check(database.ImportedFile(oddSet) is not null, "and it imports");

        // A sheet dropped on the Palette: New Tileset with it; a tile size not the map's is refused.
        palette.DropAsset(ground);
        TilesetDialog second = shell.LastTilesetDialog!;
        second.Fields.Width.Value = 8;
        second.Answer(create: true);
        Check(second.IsOpen && database.Find("Ground.crtileset") is null, "a tileset of another tile size can't be made for the map");
        second.Fields.Width.Value = 16;
        second.Answer(create: true);
        yield return 3;
        AssetId groundSet = database.Find("Ground.crtileset")?.Id ?? AssetId.None;
        Check(editor.Data.Tilesets.SequenceEqual([oddSet, groundSet]), "the map's tile size, it's made and added: two tabs");
        palette.ShowSet(1);
        started = Environment.TickCount64;
        while (palette.View.Grid.Columns == 0 && Environment.TickCount64 - started < 10000)
        {
            yield return 1;
        }
        Check(palette.View.Grid == (2, 1), "the Palette shows the second tileset's two tiles", $"{palette.View.Grid}");

        // Collision: a click makes a tile solid and clear again; presets, a flip undone, Clear and Custom.
        palette.SetCollision(true);
        palette.View.Click(0, 0);
        yield return 2;
        Check(TilesetText(copy, "Ground").Contains("\"Tile\": 0, \"Shapes\": [ [[0, 0], [16, 0], [16, 16], [0, 16]] ]", StringComparison.Ordinal),
            "clicking a tile makes it solid, saved at once", TilesetText(copy, "Ground"));
        palette.View.Click(0, 0);
        yield return 2;
        Check(!TilesetText(copy, "Ground").Contains("\"Tile\": 0", StringComparison.Ordinal), "clicking it again clears it");
        palette.View.Click(0, 0);
        palette.View.Selection = (1, 0, 1, 0);
        palette.SetShape(TilePreset.Slope);
        yield return 2;
        Check(TilesetText(copy, "Ground").Contains("\"Tile\": 1, \"Shapes\": [ [[0, 16], [16, 0], [16, 16]] ]", StringComparison.Ordinal),
            "a preset saves at once", TilesetText(copy, "Ground"));
        palette.MirrorShapes(x: true);
        yield return 2;
        Check(TilesetText(copy, "Ground").Contains("[[16, 16], [0, 0], [0, 16]]", StringComparison.Ordinal), "Flip X mirrors the shape");
        shell.Undo();
        yield return 2;
        Check(TilesetText(copy, "Ground").Contains("[[0, 16], [16, 0], [16, 16]]", StringComparison.Ordinal), "undo takes the flip back");
        palette.ClearShapes();
        palette.SetDrawing(true);
        foreach ((float x, float y) in new[] { (16f, 16f), (32f, 0f), (32f, 16f), (16f, 16f) })
        {
            palette.AddPoint(new Vector2(x, y));
        }
        yield return 2;
        TilesetData shapes = TilesetImporter.Read(File.ReadAllBytes(Path.Combine(copy, "Assets", "Ground.crtileset")));
        Check(shapes.Shapes.TryGetValue(1, out var drawn) && drawn.Count == 1 && drawn[0].Count == 3 &&
              shapes.Shapes.TryGetValue(0, out var full) && full[0].Count == 4,
            "Clear and a Custom shape closed on its first point leave tile 1 one triangle, tile 0 solid");
        palette.SetCollision(false);

        // Stamp: a two-tile block from the Palette; undo, redo.
        palette.View.Selection = (0, 0, 1, 0);
        editor.SetBrush(1, 0, 0, 1, 0);
        shell.SetMapTool(MapTool.Stamp);
        Check(editor.CellSize == Vector2.One, "a 16-pixel tile at 16 pixels a meter is a meter");
        Check(editor.BeginStroke((0, 0)), "a stroke starts");
        editor.Stroke((0, 1));
        editor.EndStroke();
        yield return 2;
        Check(Cells(copy, map, 0).Count == 4 && Cells(copy, map, 0).Values.All(cell => cell.Set == 1),
            "a dragged two-tile brush paints four cells of the second tileset", $"{Cells(copy, map, 0).Count}");
        Check(Cells(copy, map, 0).TryGetValue((1, 1), out TileCell painted) && painted.Tile == 1, "the brush's second tile goes right");
        Check(Imported(database, map).Loops > 0, "the imported map has collision outlines");
        shell.Undo();
        yield return 2;
        Check(Cells(copy, map, 0).Count == 0, "undo takes the stroke back");
        shell.Redo();
        yield return 2;
        Check(Cells(copy, map, 0).Count == 4, "redo paints it again");

        // Mixing: a tile of the first tileset on the same layer.
        editor.SetBrush(0, 1, 0, 1, 0);
        editor.BeginStroke((3, 0));
        editor.EndStroke();
        yield return 2;
        Check(Cells(copy, map, 0).TryGetValue((3, 0), out TileCell mixed) && mixed is { Set: 0, Tile: 1 } && Imported(database, map).Textures == 2,
            "a layer mixes the tilesets: two textures imported");

        // Eraser; a Rectangle; Fill the empty row between painted ones.
        editor.SetBrush(1, 0, 0, 0, 0);
        shell.SetMapTool(MapTool.Eraser);
        editor.BeginStroke((1, 1));
        editor.EndStroke();
        yield return 2;
        Check(!Cells(copy, map, 0).ContainsKey((1, 1)), "the Eraser empties the cell");
        shell.SetMapTool(MapTool.Rectangle);
        editor.BeginStroke((0, 3));
        editor.Stroke((1, 4));
        editor.EndStroke();
        yield return 2;
        Check(Cells(copy, map, 0).ContainsKey((0, 3)) && Cells(copy, map, 0).ContainsKey((1, 4)), "a Rectangle paints between its corners",
            string.Join(" ", Cells(copy, map, 0).Keys));
        shell.SetMapTool(MapTool.Fill);
        editor.BeginStroke((0, 2));
        editor.EndStroke();
        yield return 2;
        Check(Cells(copy, map, 0).ContainsKey((0, 2)) && Cells(copy, map, 0).ContainsKey((1, 1)) && Cells(copy, map, 0).ContainsKey((-1, 0)),
            "Fill paints the empty cells joined to the one pressed, out to a cell beyond the painted ones");
        shell.Undo();
        yield return 2;

        // Right-dragging picks painted cells up as the brush.
        editor.BeginPick((0, 0));
        editor.Track((1, 0));
        editor.EndPick();
        Check(editor.Tool == MapTool.Stamp && editor.Brush.Count == 2 && editor.Brush.All(b => b.Set == 1),
            "right-dragging over painted cells makes them the brush, Stamp the tool");

        // Select: a region copied, moved, deleted.
        shell.SetMapTool(MapTool.Select);
        editor.BeginStroke((0, 3));
        editor.Stroke((1, 4));
        editor.EndStroke();
        Check(editor.Region == (0, 3, 1, 4), "Select drags out a region");
        Check(editor.Copy() && editor.Brush.Count == 4 && editor.Tool == MapTool.Stamp, "copying the region makes it the brush");
        shell.SetMapTool(MapTool.Select);
        editor.Region = (0, 3, 1, 4);
        editor.BeginStroke((0, 3));
        editor.Stroke((4, 3));
        editor.EndStroke();
        yield return 2;
        Check(!Cells(copy, map, 0).ContainsKey((0, 3)) && Cells(copy, map, 0).ContainsKey((4, 3)) && Cells(copy, map, 0).ContainsKey((5, 4)),
            "dragging the region moves its cells", string.Join(" ", Cells(copy, map, 0).Keys));
        Check(shell.History.NextUndo?.Title == "Move Tiles", "as one undo step, Move Tiles");
        Check(editor.DeleteRegion() && !Cells(copy, map, 0).ContainsKey((4, 3)), "Delete empties the region");
        shell.Undo();
        yield return 2;
        Check(Cells(copy, map, 0).ContainsKey((4, 3)), "undo brings it back");
        Check(Panel<SpriteSheetPanel>(shell) is null, "making tilesets opens no Sprite Sheet Editor");
        editor.SetBrush(1, 0, 0, 1, 0);
        editor.Track((6, 1));
        yield return 3;
        V.Save(shell.Main.Surface, Path.Combine(_folder, "tilemaps.png"));

        // Layers: added above, painted apart, renamed, moved, hidden, made to collide.
        int added = shell.AddLayer();
        yield return 2;
        Entity layer2 = shell.LayerEntity(root, added) ?? Entity.Null;
        Check(added == 1 && editor.ActiveLayer == 1 && !layer2.IsNull && shell.LabelOf(layer2) == "Layer 2" &&
              !world.Has(layer2, Components.Of<Collider2D>()) && world.Read<Tilemap>(layer2).OrderInLayer > world.Read<Tilemap>(layer1).OrderInLayer,
            "Add Layer makes Layer 2 above, its entity drawn over the first, not colliding, painted on now");
        editor.SetBrush(1, 0, 0, 0, 0);
        shell.SetMapTool(MapTool.Stamp);
        editor.BeginStroke((8, 8));
        editor.EndStroke();
        yield return 2;
        Check(Cells(copy, map, 1).ContainsKey((8, 8)) && !Cells(copy, map, 0).ContainsKey((8, 8)), "painting goes on the layer being edited");
        Check(Imported(database, map).Layers == 2, "the imported map has both layers");
        layers.Rename(1, "Trees");
        yield return 2;
        Check(shell.LabelOf(layer2) == "Trees" && ReadMap(copy, map).Layer(1)?.Name == "Trees", "renaming a layer renames its entity too");
        shell.MoveLayer(1, -1);
        yield return 2;
        Check(ReadMap(copy, map).Layers[0].Id == 1 && world.Read<Tilemap>(layer2).OrderInLayer < world.Read<Tilemap>(layer1).OrderInLayer &&
              SceneIds.SiblingIndex(world, layer2) == 0,
            "Down moves it under the other: in the file, its order, and among the children");
        shell.Undo();
        yield return 2;
        Check(ReadMap(copy, map).Layers[0].Id == 0 && SceneIds.SiblingIndex(world, layer2) == 1, "undo moves it back");
        shell.SetLayerVisible(1, false);
        Check(world.Read<Tilemap>(layer2).Tint.W == 0 && !shell.IsLayerVisible(1), "Visible off hides the layer");
        shell.SetLayerCollides(1, true);
        yield return 3;
        Check(world.Read<Collider2D>(layer2).Shape == ShapeKind2D.Tilemap && shell.LayerCollides(1), "Collides gives its entity a collider");
        editor.ShowCollision = true;
        Check(editor.CollidingLayers().Count() == 2, "both layers collide now, shown with Show Collision");

        // A layer's entity deleted outside map mode: offered back.
        shell.Delete(layer2);
        yield return 2;
        Check(shell.MissingLayers().Select(l => l.Id).SequenceEqual([1]), "a layer whose entity is deleted is missing");
        shell.RecreateLayerEntity(1);
        yield return 2;
        Check(shell.LayerEntity(root, 1) is not null && !shell.MissingLayers().Any(), "Re-create gives it an entity again");
        Check(shell.DeleteLayer(1) && ReadMap(copy, map).Layers.Count == 1 && shell.LayerEntity(root, 1) is null,
            "Delete deletes the layer and its entity");
        Check(!shell.DeleteLayer(0), "the last layer stays");

        // Done: its tab closes; the Viewport never moved.
        shell.CloseWorkspace(tab);
        yield return 2;
        Check(!shell.InMapMode && !editor.IsOpen && shell.MapTab is null && Panel<HierarchyPanel>(shell) is not null,
            "Done closes its tab and its map");
        Check(Panel<ViewportPanel>(shell) is { } normalView && normalView.View.Camera.Pitch == cameraBefore.Pitch &&
              normalView.View.Camera.Distance == cameraBefore.Distance,
            "and the Viewport's camera is as it was");
        // Its file double-clicked (ADR 0120): map mode on the entity using it.
        Check(database.Find(map) is { } mapFile && shell.OpenAsset(mapFile) && shell.InMapMode && editor.Root.Value == root.Value,
            "double-clicking its file opens map mode on its entity");
        shell.ExitMapMode();
        yield return 2;
        Check(shell.EnterMapMode(layer1) && editor.Root.Value == root.Value, "Edit Map on a layer opens its map");
        shell.ExitMapMode();
        yield return 2;

        // A map from before layers: given them.
        string oldPath = $"{folder}/Old.crtilemap";
        AssetId oldMap = AssetId.New();
        File.WriteAllText(Path.Combine(copy, "Assets", oldPath),
            $"{{ \"$guid\": \"{oldMap}\", \"Tileset\": \"{groundSet}\", \"Cells\": [ [0, 0, 0, 0] ] }}");
        shell.RefreshAssets();
        // A map file nothing in the scene uses: said, not opened (ADR 0120).
        List<string> said = [];
        shell.Logged += said.Add;
        Check(database.Find(oldPath) is { } oldFile && shell.OpenAsset(oldFile) && !shell.InMapMode &&
              said.Any(line => line.StartsWith("No entity in ", StringComparison.Ordinal) && line.EndsWith(" uses Old.crtilemap", StringComparison.Ordinal)),
            "a map file no entity uses says so", string.Join(" | ", said));
        shell.Logged -= said.Add;
        Entity old = world.CreateEntity();
        world.Set(old, Transform2D.Identity);
        world.Set(old, new SceneEntityId { Value = SceneIds.NewId() });
        world.Set(old, new Tilemap { Map = oldMap });
        world.Set(old, new Collider2D { Shape = ShapeKind2D.Tilemap });
        Check(shell.EnterMapMode(old) && world.Has(old, Components.Of<TilemapLayers>()) && !world.Has(old, Components.Of<Tilemap>()) &&
              SceneIds.ChildrenOf(world, old).FirstOrDefault() is { IsNull: false } moved && world.Read<Tilemap>(moved).Map == oldMap &&
              world.Has(moved, Components.Of<Collider2D>()),
            "Edit Map on a map from before layers moves its Tilemap and collider to a child, its layer");
        shell.ExitMapMode();
        shell.Undo();
        yield return 2;
        Check(world.Has(old, Components.Of<Tilemap>()) && !world.Has(old, Components.Of<TilemapLayers>()), "undo takes that back");
        world.Destroy(old);

        // The map entity renamed, duplicated, deleted: its file follows.
        shell.Rename(root, "Ground");
        yield return 2;
        Check(database.Find(map)?.Path == $"{folder}/Ground.crtilemap", "renaming the map entity renames its file", database.Find(map)?.Path ?? "none");
        Entity twin = shell.Duplicate(root);
        yield return 2;
        AssetId twinMap = world.IsAlive(twin) ? world.Read<TilemapLayers>(twin).Map : AssetId.None;
        Entity twinLayer = world.IsAlive(twin) ? SceneIds.ChildrenOf(world, twin).FirstOrDefault() : Entity.Null;
        Check(twinMap != map && database.Find(twinMap)?.Path == $"{folder}/Ground 2.crtilemap" && !twinLayer.IsNull &&
              world.Read<Tilemap>(twinLayer).Map == twinMap,
            "duplicating copies the map to a file of its own, its layers pointed at it", database.Find(twinMap)?.Path ?? "none");
        shell.Undo();
        yield return 2;
        Check(database.Find(twinMap) is null, "undo takes the copy's file away too");
        ulong rootId = SceneIds.IdOf(world, root);
        shell.Delete(root, deleteMaps: true);
        yield return 2;
        Check(database.Find(map) is null, "deleting the map entity with Delete File deletes its map");
        shell.Undo();
        yield return 2;
        Check(database.Find(map) is not null && !SceneIds.Find(world, rootId).IsNull, "undo brings the entity and its map back");
    }

    private static T? Panel<T>(Shell shell) where T : EditorPanel =>
        shell.Windows.SelectMany(window => window.DockSpace.Panels).OfType<T>().FirstOrDefault();


    private static TilemapData ReadMap(string copy, AssetId map) =>
        Shell.Current?.Database?.Find(map) is { } entry
            ? TilemapImporter.Read(File.ReadAllBytes(Path.Combine(copy, "Assets", entry.Path)))
            : new TilemapData();

    private static Dictionary<(int X, int Y), TileCell> Cells(string copy, AssetId map, int layer) =>
        ReadMap(copy, map).Layer(layer)?.Cells ?? [];

    private static string TilesetText(string copy, string name) => File.ReadAllText(Path.Combine(copy, "Assets", name + ".crtileset"));

    private static (uint Loops, uint Textures, uint Layers) Imported(AssetDatabase database, AssetId map)
    {
        if (database.ImportedFile(map) is not { } path)
        {
            return default;
        }
        using ImportedFile file = ImportedFile.Open(path, TilemapImporter.Kind, TilemapImporter.KindVersion);
        TilemapInfo info = MemoryMarshal.Read<TilemapInfo>(file.Read(file.Find(Tag.Of("INFO"))));
        return (info.LoopCount, info.TextureCount, info.LayerCount);
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
