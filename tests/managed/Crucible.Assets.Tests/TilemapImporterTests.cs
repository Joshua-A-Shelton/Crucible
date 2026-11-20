using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using Crucible.Assets;
using Crucible.Assets.Importers;
using Crucible.Binary;

namespace Crucible.Tests;

// Tilesets and tilemaps (ADR 0108): their files read and written, collision
// merged into outlines, and a map imported with its tileset's texture.
public class TilemapImporterTests
{
    // Tile 0 is solid, tile 1 a slope rising to the right (solid below it),
    // tile 2 has nothing; 16x16 tiles.
    private static TilesetData Tiles() => new()
    {
        TileWidth = 16,
        TileHeight = 16,
        Shapes =
        {
            [0] = [[new(0, 0), new(16, 0), new(16, 16), new(0, 16)]],
            [1] = [[new(0, 16), new(16, 0), new(16, 16)]], // y down: the bottom-right triangle
        },
    };

    private static List<List<Vector2>> Outlines(params TileCell[] cells) => TilemapImporter.Outlines(Tiles(), cells);

    private static float Area(List<Vector2> loop)
    {
        float area = 0;
        for (int i = 0; i < loop.Count; ++i)
        {
            Vector2 a = loop[i], b = loop[(i + 1) % loop.Count];
            area += a.X * b.Y - b.X * a.Y;
        }
        return area / 2;
    }

    [Test]
    public async Task NeighboringSolidTilesMergeIntoOneOutline()
    {
        List<List<Vector2>> loops = Outlines(new TileCell(0, 0, 0, 0), new TileCell(1, 0, 0, 0), new TileCell(2, 0, 0, 0));
        await Assert.That(loops.Count).IsEqualTo(1);
        await Assert.That(loops[0].Count).IsEqualTo(4); // no points mid-edge
        await Assert.That(Area(loops[0])).IsEqualTo(48f * 16); // counterclockwise: solid inside
    }

    [Test]
    public async Task ASlopeMeetingABlockSharesItsEdge()
    {
        // The slope at (0, 0) rises to the block at (1, 0): one outline, its
        // top running up the slope and along the block.
        List<List<Vector2>> loops = Outlines(new TileCell(0, 0, 1, 0), new TileCell(1, 0, 0, 0));
        await Assert.That(loops.Count).IsEqualTo(1);
        await Assert.That(Area(loops[0])).IsEqualTo(16f * 16 / 2 + 16 * 16);
        await Assert.That(loops[0]).Contains(new Vector2(0, 0));
        await Assert.That(loops[0]).Contains(new Vector2(16, 16));
    }

    [Test]
    public async Task TilesTouchingAtACornerKeepTheirOwnOutlines()
    {
        List<List<Vector2>> loops = Outlines(new TileCell(0, 0, 0, 0), new TileCell(1, 1, 0, 0));
        await Assert.That(loops.Count).IsEqualTo(2);
        await Assert.That(loops.All(loop => loop.Count == 4 && Area(loop) > 0)).IsTrue();
    }

    [Test]
    public async Task ARingOfTilesHasAHoleOutlinedTheOtherWay()
    {
        List<TileCell> ring = [];
        for (int y = 0; y < 3; ++y)
        {
            for (int x = 0; x < 3; ++x)
            {
                if (x != 1 || y != 1)
                {
                    ring.Add(new TileCell(x, y, 0, 0));
                }
            }
        }
        List<List<Vector2>> loops = Outlines([.. ring]);
        await Assert.That(loops.Count).IsEqualTo(2);
        await Assert.That(string.Join(" ", loops.Select(Area).Order())).IsEqualTo($"{-16 * 16} {48 * 48}");
    }

    [Test]
    public async Task AFlippedCellMirrorsItsShapes()
    {
        // Flipped on X the slope rises to the left: its tall side at x 0.
        List<Vector2> flipped = Outlines(new TileCell(0, 0, 1, 1)).Single();
        await Assert.That(flipped).Contains(new Vector2(0, 16));
        await Assert.That(flipped).DoesNotContain(new Vector2(16, 16));
        await Assert.That(Area(flipped)).IsGreaterThan(0f);
    }

    [Test]
    public async Task FilesRoundTrip()
    {
        TilesetData tileset = Tiles();
        tileset.Id = AssetId.New();
        tileset.Texture = AssetId.New();
        TilesetData read = TilesetImporter.Read(Encoding.UTF8.GetBytes(TilesetImporter.Format(tileset)));
        await Assert.That((read.Id, read.Texture, read.TileWidth, read.Shapes.Count)).IsEqualTo((tileset.Id, tileset.Texture, 16, 2));
        await Assert.That(read.Shapes[1][0].SequenceEqual(tileset.Shapes[1][0])).IsTrue();

        AssetId other = AssetId.New();
        TilemapData map = new() { Id = AssetId.New(), Tilesets = [tileset.Id, other] };
        map.Layers[0].Name = "Ground \"main\"";
        map.Layers[0].Cells[(3, -2)] = new TileCell(3, -2, 1, 3);
        map.Layers[0].Cells[(0, 0)] = new TileCell(0, 0, 0, 0);
        map.Layers.Add(new TileLayer { Id = 4, Name = "Trees" });
        map.Layers[1].Cells[(1, 1)] = new TileCell(1, 1, 5, 0, Set: 1);
        TilemapData again = TilemapImporter.Read(Encoding.UTF8.GetBytes(TilemapImporter.Format(map)));
        await Assert.That((again.Id, again.Tilesets.Count, again.Tilesets[1], again.Layers.Count)).IsEqualTo((map.Id, 2, other, 2));
        await Assert.That((again.Layers[0].Name, again.Layers[0].Cells.Count, again.Layers[0].Cells[(3, -2)])).IsEqualTo(
            ("Ground \"main\"", 2, new TileCell(3, -2, 1, 3)));
        await Assert.That((again.Layers[1].Id, again.Layers[1].Name, again.Layers[1].Cells[(1, 1)])).IsEqualTo(
            (4, "Trees", new TileCell(1, 1, 5, 0, 1)));
    }

    // A map from before layers (ADR 0108) reads as one layer of id 0.
    [Test]
    public async Task AMapFromBeforeLayersReadsAsOneLayer()
    {
        AssetId tileset = AssetId.New();
        string old = $$"""
            { "$guid": "{{AssetId.New()}}", "Tileset": "{{tileset}}",
              "Cells": [ [0, 0, 5, 1], [2, -1, 3, 0] ] }
            """;
        TilemapData map = TilemapImporter.Read(Encoding.UTF8.GetBytes(old));
        await Assert.That((map.Tilesets.Single(), map.Layers.Count, map.Layers[0].Id, map.Layers[0].Name)).IsEqualTo((tileset, 1, 0, "Layer 1"));
        await Assert.That(map.Layers[0].Cells[(0, 0)]).IsEqualTo(new TileCell(0, 0, 5, 1, 0));
    }

    private sealed class Scratch : IDisposable
    {
        public Project Project { get; } =
            Project.Create(Path.Combine(Path.GetTempPath(), "crucible-tilemap-" + Guid.NewGuid().ToString("N")), "Game");

        public string PathOf(string assetPath) => Path.Combine(Project.AssetsFolder, assetPath);

        public AssetDatabase Open() =>
            AssetDatabase.Open(Project, [new TextureImporter(), new TilesetImporter(), new TilemapImporter()]);

        public void Dispose() => Directory.Delete(Project.Root, recursive: true);
    }

    // A 32x16 sheet of two tiles (solid, slope) at 32 pixels a meter, a
    // tileset of it, and a map painting both side by side.
    private static (AssetDatabase Database, TilesetData Tileset, TilemapData Map) Project(Scratch scratch)
    {
        byte[] tga = new byte[18 + 32 * 16 * 4];
        tga[2] = 2;
        BitConverter.TryWriteBytes(tga.AsSpan(12), (ushort)32);
        BitConverter.TryWriteBytes(tga.AsSpan(14), (ushort)16);
        tga[16] = 32;
        tga[17] = 0x28;
        Array.Fill(tga, (byte)255, 18, tga.Length - 18);
        File.WriteAllBytes(scratch.PathOf("Tiles.tga"), tga);
        AssetDatabase database = scratch.Open();
        database.ImportAll();
        AssetId texture = database.Find("Tiles.tga")!.Id;
        System.Text.Json.Nodes.JsonObject settings = database.SettingsOf(texture)!;
        settings["Usage"] = "Sprite";
        settings["PixelsPerUnit"] = 32;
        database.SetSettings(texture, settings);
        TilesetData tileset = Tiles();
        tileset.Id = AssetId.New();
        tileset.Texture = texture;
        File.WriteAllText(scratch.PathOf("Ground.crtileset"), TilesetImporter.Format(tileset));
        TilemapData map = new() { Id = AssetId.New(), Tilesets = [tileset.Id] };
        map.Layers[0].Cells[(0, 0)] = new TileCell(0, 0, 1, 0);
        map.Layers[0].Cells[(1, 0)] = new TileCell(1, 0, 0, 1);
        File.WriteAllText(scratch.PathOf("Level.crtilemap"), TilemapImporter.Format(map));
        database.Refresh();
        return (database, tileset, map);
    }

    [Test]
    public async Task AMapImportsItsCellsAndCollisionInMeters()
    {
        using Scratch scratch = new();
        (AssetDatabase database, TilesetData tileset, TilemapData map) = Project(scratch);
        ImportResult[] results = [.. database.ImportAll()];
        await Assert.That(results.All(r => r.Outcome is ImportOutcome.Imported or ImportOutcome.UpToDate)).IsTrue()
            .Because(string.Join("; ", results.Select(r => r.Error)));
        using ImportedFile file = ImportedFile.Open(database.ImportedFile(map.Id)!, TilemapImporter.Kind, TilemapImporter.KindVersion);
        TilemapInfo info = MemoryMarshal.Read<TilemapInfo>(file.Read(file.Find(Tag.Of("INFO"))));
        await Assert.That((info.TextureCount, info.LayerCount, info.TileWidth, info.CellCount, info.LoopCount)).IsEqualTo((1u, 1u, 16u, 2u, 1u));
        await Assert.That(MemoryMarshal.Read<AssetId>(file.Read(file.Find(Tag.Of("TXTR"))))).IsEqualTo(tileset.Texture);
        await Assert.That(info.CellWidth).IsEqualTo(0.5f);
        TilemapCell[] cells = MemoryMarshal.Cast<byte, TilemapCell>(file.Read(file.Find(Tag.Of("CELL")))).ToArray();
        await Assert.That((cells[0].X, cells[0].PixelX, cells[1].X, cells[1].PixelX, cells[1].Flags)).IsEqualTo((0, 16u, 1, 0u, 1u));
        Vector2[] points = MemoryMarshal.Cast<byte, Vector2>(file.Read(file.Find(Tag.Of("PNTS")))).ToArray();
        await Assert.That(points.Max(p => p.X)).IsEqualTo(1f); // two half-meter cells
    }

    [Test]
    public async Task ChangingTheTilesetImportsItsMapsAgain()
    {
        using Scratch scratch = new();
        (AssetDatabase database, TilesetData tileset, TilemapData map) = Project(scratch);
        database.ImportAll();
        tileset.Shapes.Clear();
        File.WriteAllText(scratch.PathOf("Ground.crtileset"), TilesetImporter.Format(tileset));
        database.Refresh();
        ImportResult[] again = [.. database.ImportAll()];
        await Assert.That(again.Any(r => r.Id == map.Id && r.Outcome == ImportOutcome.Imported)).IsTrue();
        using ImportedFile file = ImportedFile.Open(database.ImportedFile(map.Id)!, TilemapImporter.Kind, TilemapImporter.KindVersion);
        await Assert.That(MemoryMarshal.Read<TilemapInfo>(file.Read(file.Find(Tag.Of("INFO")))).LoopCount).IsEqualTo(0u);
    }

    [Test]
    public async Task AMapNamingTilesItsTilesetHasntFails()
    {
        using Scratch scratch = new();
        (AssetDatabase database, _, TilemapData map) = Project(scratch);
        map.Layers[0].Cells[(5, 5)] = new TileCell(5, 5, 7, 0);
        File.WriteAllText(scratch.PathOf("Level.crtilemap"), TilemapImporter.Format(map));
        database.Refresh();
        ImportResult result = database.ImportAll().Single(r => r.Id == map.Id);
        await Assert.That(result.Outcome).IsEqualTo(ImportOutcome.Failed);
        await Assert.That(result.Error).Contains("layer Layer 1's cell at (5, 5) is tile 7, but its tileset has 2");
    }

    [Test]
    public async Task ANewMapWithoutATilesetImportsEmpty()
    {
        using Scratch scratch = new();
        (AssetDatabase database, _, _) = Project(scratch);
        TilemapData fresh = new() { Id = AssetId.New() };
        File.WriteAllText(scratch.PathOf("Fresh.crtilemap"), TilemapImporter.Format(fresh));
        database.Refresh();
        ImportResult result = database.ImportAll().Single(r => r.Id == fresh.Id);
        await Assert.That(result.Outcome).IsEqualTo(ImportOutcome.Imported).Because(result.Error ?? "");
        using ImportedFile file = ImportedFile.Open(database.ImportedFile(fresh.Id)!, TilemapImporter.Kind, TilemapImporter.KindVersion);
        await Assert.That(MemoryMarshal.Read<TilemapInfo>(file.Read(file.Find(Tag.Of("INFO")))).CellCount).IsEqualTo(0u);
    }

    [Test]
    public async Task AMapWithCellsButNoTilesetFails()
    {
        using Scratch scratch = new();
        (AssetDatabase database, _, _) = Project(scratch);
        TilemapData orphan = new() { Id = AssetId.New() };
        orphan.Layers[0].Cells[(0, 0)] = new TileCell(0, 0, 0, 0);
        File.WriteAllText(scratch.PathOf("Orphan.crtilemap"), TilemapImporter.Format(orphan));
        database.Refresh();
        ImportResult result = database.ImportAll().Single(r => r.Id == orphan.Id);
        await Assert.That(result.Outcome).IsEqualTo(ImportOutcome.Failed);
    }

    // Two tilesets (the second its own texture) and two layers: each
    // layer's cells and outlines apart, each cell naming its texture.
    [Test]
    public async Task LayersAndTilesetsImportApart()
    {
        using Scratch scratch = new();
        (AssetDatabase database, TilesetData tileset, TilemapData map) = Project(scratch);
        File.Copy(scratch.PathOf("Tiles.tga"), scratch.PathOf("More.tga"));
        database.Refresh();
        database.ImportAll();
        AssetId texture = database.Find("More.tga")!.Id;
        System.Text.Json.Nodes.JsonObject settings = database.SettingsOf(texture)!;
        settings["Usage"] = "Sprite";
        settings["PixelsPerUnit"] = 32;
        database.SetSettings(texture, settings);
        TilesetData more = Tiles();
        more.Id = AssetId.New();
        more.Texture = texture;
        File.WriteAllText(scratch.PathOf("More.crtileset"), TilesetImporter.Format(more));
        map.Tilesets.Add(more.Id);
        map.Layers.Add(new TileLayer { Id = 3, Name = "Upper" });
        map.Layers[1].Cells[(0, 2)] = new TileCell(0, 2, 0, 0, Set: 1); // a solid block, from the other texture
        File.WriteAllText(scratch.PathOf("Level.crtilemap"), TilemapImporter.Format(map));
        database.Refresh();
        ImportResult result = database.ImportAll().Single(r => r.Id == map.Id);
        await Assert.That(result.Outcome).IsEqualTo(ImportOutcome.Imported).Because(result.Error ?? "");
        using ImportedFile file = ImportedFile.Open(database.ImportedFile(map.Id)!, TilemapImporter.Kind, TilemapImporter.KindVersion);
        TilemapInfo info = MemoryMarshal.Read<TilemapInfo>(file.Read(file.Find(Tag.Of("INFO"))));
        await Assert.That((info.TextureCount, info.LayerCount, info.CellCount, info.LoopCount)).IsEqualTo((2u, 2u, 3u, 2u));
        TilemapLayer[] layers = MemoryMarshal.Cast<byte, TilemapLayer>(file.Read(file.Find(Tag.Of("LAYR")))).ToArray();
        await Assert.That((layers[0].Id, layers[0].CellCount, layers[0].LoopCount)).IsEqualTo((0u, 2u, 1u));
        await Assert.That((layers[1].Id, layers[1].FirstCell, layers[1].CellCount, layers[1].FirstLoop, layers[1].LoopCount))
            .IsEqualTo((3u, 2u, 1u, 1u, 1u));
        TilemapCell[] cells = MemoryMarshal.Cast<byte, TilemapCell>(file.Read(file.Find(Tag.Of("CELL")))).ToArray();
        await Assert.That((cells[0].Texture, cells[2].Texture, cells[2].Y)).IsEqualTo((0u, 1u, 2));
        AssetId[] textures = MemoryMarshal.Cast<byte, AssetId>(file.Read(file.Find(Tag.Of("TXTR")))).ToArray();
        await Assert.That((textures[0], textures[1])).IsEqualTo((tileset.Texture, texture));
    }

    // A map is one grid: tilesets of another tile size are refused, saying which.
    [Test]
    public async Task TilesetsOfAnotherTileSizeFail()
    {
        using Scratch scratch = new();
        (AssetDatabase database, TilesetData tileset, TilemapData map) = Project(scratch);
        TilesetData big = new() { Id = AssetId.New(), Texture = tileset.Texture, TileWidth = 8, TileHeight = 8 };
        File.WriteAllText(scratch.PathOf("Big.crtileset"), TilesetImporter.Format(big));
        map.Tilesets.Add(big.Id);
        File.WriteAllText(scratch.PathOf("Level.crtilemap"), TilemapImporter.Format(map));
        database.Refresh();
        ImportResult result = database.ImportAll().Single(r => r.Id == map.Id);
        await Assert.That(result.Outcome).IsEqualTo(ImportOutcome.Failed);
        await Assert.That(result.Error).Contains("its tilesets' tiles aren't one size: Ground.crtileset's are 16x16, Big.crtileset's 8x8");
    }
}
