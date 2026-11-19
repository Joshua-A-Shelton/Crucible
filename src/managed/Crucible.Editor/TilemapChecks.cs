using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using Crucible.Assets;
using Crucible.Assets.Importers;
using Crucible.Binary;
using Crucible.Ecs;
using Crucible.Loop;
using Crucible.Physics2D;
using Crucible.Render2D;

namespace Crucible.Editor;

/// <summary>
/// Tilesets and tilemaps checking themselves in the editor (ADR 0108), for
/// CTest's launcher.jit.tilemaps: run with the Walker's project and
/// <c>--game-arg check-tilemaps=&lt;folder&gt;</c>. On a copy of the project
/// (in the folder) with a two-tile sheet added: Create makes a tileset of it,
/// opened in the Tileset Editor, where a tile is made Full and another given
/// a drawn shape; Create makes a map of the tileset; Edit Tiles on an entity
/// showing it opens the Tile Palette, and Paint, undo and redo, Erase, Fill,
/// and Pick change the map file, its import following with collision.
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
        string source = Path.GetDirectoryName(shell.Project?.ProjectFilePath ?? "") ?? "";
        string copy = Path.Combine(Path.GetFullPath(_folder), "tilemaps-project");
        if (Directory.Exists(copy))
        {
            Directory.Delete(copy, recursive: true);
        }
        AssetChecks.CopyProject(source, copy);
        // Two 16x16 tiles side by side, at 16 pixels a meter.
        byte[] tga = new byte[18 + 32 * 16 * 4];
        tga[2] = 2;
        BitConverter.TryWriteBytes(tga.AsSpan(12), (ushort)32);
        BitConverter.TryWriteBytes(tga.AsSpan(14), (ushort)16);
        tga[16] = 32;
        tga[17] = 0x28;
        Array.Fill(tga, (byte)200, 18, tga.Length - 18);
        File.WriteAllBytes(Path.Combine(copy, "Assets", "Ground.tga"), tga);
        shell.OpenProject(Path.Combine(copy, Path.GetFileName(shell.Project!.ProjectFilePath)));
        yield return 3;

        AssetDatabase database = shell.Database!;
        AssetId texture = database.Find("Ground.tga")!.Id;
        JsonObject settings = database.SettingsOf(texture)!;
        settings["Usage"] = "Sprite";
        shell.ApplyImportSettings(texture, settings);
        yield return 2;

        // Create > Tileset, of the texture, opens it in the Tileset Editor.
        string? tilesetPath = shell.CreateTileset("", texture);
        Check(tilesetPath == "New Tileset.crtileset", "Create > Tileset makes New Tileset.crtileset", tilesetPath ?? "nothing");
        yield return 3;
        TilesetEditorPanel? editor = Panels<TilesetEditorPanel>(shell);
        AssetId tileset = database.Find("New Tileset.crtileset")?.Id ?? AssetId.None;
        Check(editor is not null && editor.Asset == tileset && editor.Edited.Texture == texture,
            "it opens in the Tileset Editor, cutting the texture");
        if (editor is null)
        {
            yield break;
        }
        long started = Environment.TickCount64;
        while (editor.View.Grid.Columns == 0 && Environment.TickCount64 - started < 10000)
        {
            yield return 1;
        }
        Check(editor.View.Grid == (2, 1), "it shows the texture's two tiles", $"{editor.View.Grid}");

        // Tile 0 Full; tile 1 a triangle drawn point by point (closed on its first).
        editor.View.Selection = (0, 0, 0, 0);
        editor.Full();
        editor.View.Selection = (1, 0, 1, 0);
        editor.SetDrawing(true);
        foreach ((float x, float y) in new[] { (16f, 16f), (32f, 0f), (32f, 16f), (16f, 16f) })
        {
            editor.AddPoint(new System.Numerics.Vector2(x, y));
        }
        Check(editor.Edited.Shapes.TryGetValue(1, out var drawn) && drawn.Count == 1 && drawn[0].Count == 3,
            "a shape drawn on tile 1 closes on its first point, in the tile's pixels");
        Check(editor.Edited.Shapes.TryGetValue(0, out var full) && full[0].Count == 4, "Full covers tile 0");
        Check(editor.HasChanges, "edits wait for Apply");
        editor.Apply();
        yield return 3;
        string tilesetText = File.ReadAllText(Path.Combine(copy, "Assets", "New Tileset.crtileset"));
        Check(tilesetText.Contains("\"Tile\": 1, \"Shapes\": [ [[0, 16], [16, 0], [16, 16]] ]", StringComparison.Ordinal),
            "Apply saves the shapes", tilesetText);

        // Create > Tilemap with the tileset, shown by an entity with a Tilemap collider.
        string? mapPath = shell.CreateTilemap("", tileset);
        Check(mapPath == "New Tilemap.crtilemap", "Create > Tilemap makes New Tilemap.crtilemap", mapPath ?? "nothing");
        yield return 2;
        AssetId map = database.Find("New Tilemap.crtilemap")?.Id ?? AssetId.None;
        Entity entity = shell.CreateEmpty(Entity.Null);
        shell.SetComponent(entity, typeof(Tilemap).FullName!, new JsonObject { ["Map"] = map.ToString() }, "Add Component");
        shell.SetComponent(entity, typeof(Collider2D).FullName!, new JsonObject { ["Shape"] = "Tilemap" }, "Add Component");
        shell.Selection.Selected = entity;
        yield return 2;

        // Edit Tiles opens the palette on the tileset.
        TilePainter painter = shell.Tiles;
        Check(painter.CanEdit(entity) && painter.Start(entity), "Edit Tiles starts on the entity's map");
        yield return 3;
        TilePalettePanel? palette = Panels<TilePalettePanel>(shell);
        Check(palette is not null, "Edit Tiles opens the Tile Palette");
        started = Environment.TickCount64;
        while (palette is not null && palette.View.Grid.Columns == 0 && Environment.TickCount64 - started < 10000)
        {
            yield return 1;
        }
        Check(palette?.View.Grid == (2, 1), "the palette shows the tileset", $"{palette?.View.Grid}");
        Check(painter.CellSize == System.Numerics.Vector2.One, "a 16-pixel tile at 16 pixels a meter is a meter");

        // Paint a two-tile brush; undo, redo.
        painter.SetBrush(0, 0, 1, 0);
        shell.SetTileTool(TileTool.Paint);
        Check(painter.BeginStroke((0, 0)), "a stroke starts");
        painter.Stroke((0, 1));
        painter.EndStroke();
        yield return 2;
        Check(Cells(copy).Count == 4, "a dragged two-tile brush paints four cells", $"{Cells(copy).Count}");
        Check(Cells(copy).TryGetValue((1, 1), out TileCell painted) && painted.Tile == 1, "the brush's second tile goes right");
        Check(Loops(database, map) > 0, "the imported map has collision outlines");
        shell.Undo();
        yield return 2;
        Check(Cells(copy).Count == 0, "undo takes the stroke back");
        shell.Redo();
        yield return 2;
        Check(Cells(copy).Count == 4, "redo paints it again");

        // Erase one cell; fill the empty row between two painted ones.
        shell.SetTileTool(TileTool.Erase);
        painter.SetBrush(0, 0, 0, 0);
        painter.BeginStroke((1, 1));
        painter.EndStroke();
        yield return 2;
        Check(Cells(copy).Count == 3 && !Cells(copy).ContainsKey((1, 1)), "Erase empties the cell");
        shell.SetTileTool(TileTool.Paint);
        painter.BeginStroke((0, 3));
        painter.Stroke((1, 3));
        painter.EndStroke();
        shell.SetTileTool(TileTool.Fill);
        painter.BeginStroke((0, 2));
        painter.EndStroke();
        yield return 2;
        Check(Cells(copy).ContainsKey((0, 2)) && Cells(copy).ContainsKey((1, 2)) && Cells(copy).ContainsKey((1, 1)),
            "Fill paints the empty cells joined to the one pressed, within the painted bounds",
            string.Join(" ", Cells(copy).Keys));

        // Pick takes a cell's tile and goes back to Paint.
        shell.SetTileTool(TileTool.Pick);
        painter.BeginStroke((1, 0));
        painter.EndStroke();
        Check(painter.Tool == TileTool.Paint && painter.Brush.Count == 1 && painter.Brush[0].Tile == 1,
            "Pick makes the cell's tile the brush and Paint the tool");
        painter.Stop();
    }

    private static T? Panels<T>(Shell shell) where T : EditorPanel =>
        shell.Windows.SelectMany(window => window.DockSpace.Panels).OfType<T>().FirstOrDefault();

    private static Dictionary<(int X, int Y), TileCell> Cells(string copy) =>
        TilemapImporter.Read(File.ReadAllBytes(Path.Combine(copy, "Assets", "New Tilemap.crtilemap"))).Cells;

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
