using System.Numerics;
using System.Runtime.InteropServices;
using Crucible.Assets;
using Crucible.Binary;
using Crucible.Ecs;
using Crucible.Loop;
using Crucible.Scenes;
using Png = Crucible.Samples.Diorama.Png;
using V = Crucible.Editor.ViewportChecks;

namespace Crucible.Editor;

/// <summary>
/// Asset thumbnails checking themselves (ADR 0132), for CTest's
/// launcher.jit.thumbnails: run with the Diorama's project and
/// <c>--game-arg check-thumbnails=&lt;folder&gt;</c>. On a copy, a material
/// shader and material, a prefab, a tileset, and a tilemap are added beside
/// the models. Models, the prefab, the material (on the sphere, in its own
/// color), and the tilemap render thumbnails on a clear background, cached
/// in Cache/Thumbnails and read back after a restart's worth of forgetting;
/// changing the material renders it again; the tileset shows its sheet;
/// other kinds show their icons; the tile size follows its slider and is
/// kept. The window is saved as thumbnails.png.
/// </summary>
[System(Phases.RenderPrepare)]
internal sealed class ThumbnailChecks : GameSystem
{
    private IEnumerator<int>? _script;
    private int _wait;
    private int _checks;
    private int _failures;
    private string _folder = "";

    public override void Start(FrameContext context)
    {
        if (!context.GameArguments.TryGetValue("check-thumbnails", out string? folder))
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
            ? $"ThumbnailChecks: all {_checks} checks passed"
            : $"ThumbnailChecks: {_failures} of {_checks} checks failed");
        context.QuitRequested = true;
    }

    private const string Shader = """
        import crucible.material;

        // A plain lit color, for the thumbnail checks (ADR 0132).
        public struct Swatch : IMaterial
        {
            public float4 color = float4(1, 0.45, 0.1, 1);
            public typealias Shading = PbrShading;

            public SurfaceData surface(SurfaceInput input)
            {
                SurfaceData s = SurfaceData.defaults(input);
                s.albedo = color.rgb;
                s.alpha = color.a;
                return s;
            }

            public PbrData shading(SurfaceInput input)
            {
                return PbrData.defaults();
            }
        }
        """;

    private static string Material(AssetId id, AssetId shader, string color) =>
        $$"""{ "$guid": "{{id}}", "Shader": "{{shader}}", "Type": "Swatch", "Values": { "color": {{color}} } }""";

    private IEnumerable<int> Script()
    {
        Shell shell = Shell.Current ?? throw new InvalidOperationException("No editor shell");
        string copy = Path.Combine(Path.GetFullPath(_folder), "thumbnails-project");
        AssetChecks.OpenCopy(shell, copy, null);
        long opening = Environment.TickCount64;
        while (shell.Database is null && Environment.TickCount64 - opening < 30000)
        {
            yield return 1;
        }
        AssetDatabase database = shell.Database!;
        Thumbnails thumbnails = shell.Thumbnails;
        string models = Path.Combine(copy, "Assets", "Models");

        // What's added beside the models: a shader first, so the material can name it.
        File.WriteAllText(Path.Combine(models, "Swatch.slang"), Shader);
        shell.RefreshAssets();
        AssetId shader = database.Find("Models/Swatch.slang")?.Id ?? AssetId.None;
        Check(!shader.IsNone && database.ImportedFile(shader) is not null, "a material shader imports");
        AssetId material = AssetId.New(), prefab = AssetId.New(), tileset = AssetId.New(), tilemap = AssetId.New();
        string materialFile = Path.Combine(models, "Swatch.crmaterial");
        File.WriteAllText(materialFile, Material(material, shader, "[1, 0.45, 0.1, 1]"));
        AssetId house = database.Find("Models/House.gltf")?.Id ?? AssetId.None;
        using (World made = new())
        {
            Entity root = Prefab.Instantiate(made, house, shell.Prefabs!);
            File.WriteAllBytes(Path.Combine(models, "Cottage" + Prefab.Extension), Prefab.Save(made, root, prefab, shell.Prefabs));
        }
        AssetId grass = database.Find("Textures/Grass.png")?.Id ?? AssetId.None;
        File.WriteAllText(Path.Combine(models, "Grass.crtileset"),
            $$"""{ "$guid": "{{tileset}}", "Texture": "{{grass}}", "TileWidth": 16, "TileHeight": 16 }""");
        File.WriteAllText(Path.Combine(models, "Patch.crtilemap"),
            $$"""{ "$guid": "{{tilemap}}", "Tilesets": [ "{{tileset}}" ], "Layers": [ { "Id": 0, "Name": "Ground", "Cells": [ [0, 0, 0, 0, 0], [1, 0, 0, 1, 0], [0, 1, 0, 4, 0], [1, 1, 0, 5, 0], [2, 1, 0, 2, 0] ] } ] }""");
        shell.RefreshAssets();
        Check(database.ImportedFile(material) is not null, "the material imports in the editor");
        Check(database.ImportedFile(tilemap) is not null && database.ImportedFile(tileset) is not null, "the tileset and tilemap import");

        // The Models folder at a size that shows the badge.
        AssetsPanel assets = shell.Main.DockSpace.Panels.OfType<AssetsPanel>().Single();
        assets.TileSize = 96;
        assets.ShowFolder("Models");
        yield return 2;
        foreach (int frame in Settle(thumbnails))
        {
            yield return frame;
        }
        AssetTile Tile(string path) => assets.Tiles.Single(tile => tile.Path == path);
        string[] rendered = [.. assets.Tiles.Where(tile => Thumbnails.Renders(tile.Kind)).Select(tile => tile.Path)];
        Check(rendered.Length == 8, "five models, a prefab, a material, and a tilemap render", string.Join(", ", rendered));
        foreach (string path in rendered)
        {
            AssetTile tile = Tile(path);
            Check(tile.Thumbnail is { Texture.Generation: not 0 } && tile.ShowsImage && tile.ShowsBadge,
                $"{path} shows a rendered thumbnail with its badge",
                $"{tile.Kind}: thumbnail {(tile.Thumbnail is null ? "none" : tile.Thumbnail.Texture.Generation == 0 ? "not ready" : "ready")}, key {thumbnails.KeyOf(tile.Asset!.Id) ?? "none"}");
        }
        Check(Tile("Models/Grass.crtileset") is { Kind: "Tileset", Thumbnail: null, ShowsImage: true, ShowsBadge: false },
            "a tileset shows its sheet");
        Check(Tile("Models/Swatch.slang") is { Kind: "Shader", ShowsImage: false }, "a shader shows its kind's icon");
        Check(AssetTile.IconOf("Sound") == "audio-lines" && AssetTile.IconOf("Tilemap") == "grid-3x3" && AssetTile.IconOf("Nothing") == "file",
            "each kind has its icon");
        int first = thumbnails.Rendered;
        Check(first == rendered.Length, "each renders once", $"{first}");

        // Cached: on a clear background, the model in the middle.
        string cache = Path.Combine(copy, "Cache", "Thumbnails");
        string? houseKey = Tile("Models/House.gltf").Thumbnail?.Key;
        string houseFile = Path.Combine(cache, houseKey + ".crtex");
        Check(houseKey is not null && File.Exists(houseFile), "a thumbnail is kept in Cache/Thumbnails", houseKey ?? "");
        if (File.Exists(houseFile))
        {
            byte[] pixels = Level0(houseFile);
            Check(Alpha(pixels, 2, 2) == 0 && Alpha(pixels, Thumbnails.Size - 3, 2) == 0, "around it is clear",
                $"{Alpha(pixels, 2, 2)} {Alpha(pixels, Thumbnails.Size - 3, 2)}");
            Check(Alpha(pixels, Thumbnails.Size / 2, Thumbnails.Size / 2) == 255, "the model itself is opaque");
        }
        Vector3 swatch = Middle(Tile("Models/Swatch.crmaterial"), cache);
        Check(swatch.X > swatch.Y + 0.15f && swatch.Y > swatch.Z, "the material shows on the sphere in its own color", $"{swatch}");
        Vector3 tiles = Middle(Tile("Models/Patch.crtilemap"), cache);
        Check(tiles != Vector3.Zero, "the tilemap is drawn");
        V.Save(shell.Main.Surface, Path.Combine(_folder, "thumbnails.png"));
        Sheet([.. rendered.Select(path => Path.Combine(cache, Tile(path).Thumbnail?.Key + ".crtex"))], Path.Combine(_folder, "thumbnails-sheet.png"));

        // Forgotten (as after a restart): read from the cache, not rendered.
        int read = thumbnails.ReadFromCache;
        thumbnails.Clear();
        assets.ShowFolder("Textures");
        yield return 2;
        assets.ShowFolder("Models");
        yield return 2;
        foreach (int frame in Settle(thumbnails))
        {
            yield return frame;
        }
        Check(thumbnails.Rendered == first && thumbnails.ReadFromCache - read == rendered.Length, "a cached thumbnail is read back",
            $"{thumbnails.Rendered - first} rendered, {thumbnails.ReadFromCache - read} read");

        // The material changed: a new key, rendered again in its new color.
        string? before = Tile("Models/Swatch.crmaterial").Thumbnail?.Key;
        File.WriteAllText(materialFile, Material(material, shader, "[0.1, 0.3, 1, 1]"));
        shell.RefreshAssets();
        yield return 2;
        foreach (int frame in Settle(thumbnails))
        {
            yield return frame;
        }
        AssetTile changed = Tile("Models/Swatch.crmaterial");
        Check(changed.Thumbnail is { } now && now.Key != before && thumbnails.Rendered == first + 1, "a reimport renders it again",
            $"{thumbnails.Rendered - first} rendered");
        Vector3 blue = Middle(changed, cache);
        Check(blue.Z > blue.X + 0.15f, "in its new color", $"{blue}");

        // The tile size: the pictures follow it, the badge only on big enough tiles, kept in the settings.
        assets.TileSize = 160;
        yield return 2;
        Check(Tile("Models/House.gltf") is { PictureSize: 160, ShowsBadge: true }, "tiles take the slider's size");
        Check(shell.Settings.AssetTileSize == 160, "and keep it");
        assets.TileSize = 48;
        yield return 2;
        Check(Tile("Models/House.gltf") is { PictureSize: 48, ShowsBadge: false, ShowsImage: true }, "a small tile drops the badge");
        assets.TileSize = 500;
        Check(assets.TileSize == AssetsPanel.MaxTileSize, "the size stays in its range");
        assets.TileSize = EditorSettings.DefaultAssetTileSize;
    }

    // Until nothing's queued or rendering (at most 60 seconds: shaders compile on first use).
    private static IEnumerable<int> Settle(Thumbnails thumbnails)
    {
        long started = Environment.TickCount64;
        yield return 2;
        while (thumbnails.IsBusy && Environment.TickCount64 - started < 60000)
        {
            yield return 1;
        }
        yield return 3; // the last one's texture ready
    }

    // A thumbnail file's full-size level, RGBA.
    private static byte[] Level0(string file)
    {
        using ImportedFile texture = ImportedFile.Parse(File.ReadAllBytes(file), Tag.Of("TXTR"), 1);
        return texture.Read(texture.Find(Tag.Of("MIP0"))).ToArray();
    }

    // The thumbnails side by side over the panel's color, to look at.
    private static void Sheet(string[] files, string path)
    {
        int size = Thumbnails.Size, width = size * files.Length;
        byte[] rgb = new byte[width * size * 3];
        for (int f = 0; f < files.Length; ++f)
        {
            byte[] rgba = File.Exists(files[f]) ? Level0(files[f]) : new byte[size * size * 4];
            for (int y = 0; y < size; ++y)
            {
                for (int x = 0; x < size; ++x)
                {
                    int from = (y * size + x) * 4, to = (y * width + f * size + x) * 3;
                    float a = rgba[from + 3] / 255f;
                    for (int c = 0; c < 3; ++c)
                    {
                        rgb[to + c] = (byte)MathF.Round(rgba[from + c] * a + 0x1e * (1 - a));
                    }
                }
            }
        }
        Png.Write(path, width, size, rgb);
    }

    private static byte Alpha(byte[] rgba, int x, int y) => rgba[(y * Thumbnails.Size + x) * 4 + 3];

    // The average color of the thumbnail's middle, sRGB 0 to 1; zero if there's none.
    private static Vector3 Middle(AssetTile tile, string cache)
    {
        string file = Path.Combine(cache, tile.Thumbnail?.Key + ".crtex");
        if (tile.Thumbnail is null || !File.Exists(file))
        {
            return Vector3.Zero;
        }
        byte[] rgba = Level0(file);
        Vector3 sum = Vector3.Zero;
        int count = 0, half = Thumbnails.Size / 2;
        for (int y = half - 8; y < half + 8; ++y)
        {
            for (int x = half - 8; x < half + 8; ++x)
            {
                int at = (y * Thumbnails.Size + x) * 4;
                if (rgba[at + 3] > 0)
                {
                    sum += new Vector3(rgba[at], rgba[at + 1], rgba[at + 2]) / 255f;
                    ++count;
                }
            }
        }
        return count > 0 ? sum / count : Vector3.Zero;
    }

    private void Check(bool passed, string what, string detail = "")
    {
        ++_checks;
        if (!passed)
        {
            ++_failures;
            Console.WriteLine($"ThumbnailChecks: FAILED: {what} {detail}");
        }
    }
}
