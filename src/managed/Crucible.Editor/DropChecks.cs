using System.Numerics;
using System.Text;
using System.Text.Json.Nodes;
using Crucible.Assets;
using Crucible.Ecs;
using Crucible.Loop;
using Crucible.Platform;
using Crucible.Render2D;
using Crucible.Scenes;
using Png = Crucible.Samples.Diorama.Png;

namespace Crucible.Editor;

/// <summary>
/// Importing dropped files checking itself (ADR 0115), for CTest's
/// launcher.jit.drops: run with the Walker's project and <c>--game-arg
/// check-drops=&lt;folder&gt;</c>. A headless run gets no real drop from
/// the system, so the platform's file drag events are handed to the shell
/// as the platform would give them. On a copy of the project: the note and
/// the highlight follow the pointer; images ask how to import, guessing
/// pixel art, texture, or UI; names already there ask, and "Do this for the
/// rest" asks once; a folder comes whole, files nothing imports are copied
/// and logged; a .gltf brings its buffer, renamed beside a different one;
/// dropped on the viewport or the hierarchy, a model or pixel art is placed.
/// </summary>
[System(Phases.RenderPrepare)]
internal sealed class DropChecks : GameSystem
{
    private IEnumerator<int>? _script;
    private int _wait;
    private int _checks;
    private int _failures;
    private string _folder = "";
    private readonly List<string> _logged = [];

    public override void Start(FrameContext context)
    {
        if (!context.GameArguments.TryGetValue("check-drops", out string? folder))
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
            ? $"DropChecks: all {_checks} checks passed"
            : $"DropChecks: {_failures} of {_checks} checks failed");
        context.QuitRequested = true;
    }

    private IEnumerable<int> Script()
    {
        Shell shell = Shell.Current ?? throw new InvalidOperationException("No editor shell");
        shell.Logged += _logged.Add;
        string copy = Path.Combine(Path.GetFullPath(_folder), "drops-project");
        AssetChecks.OpenCopy(shell, copy, null);
        yield return 3;
        shell.NewScene();
        yield return 2;
        string assetsFolder = Path.Combine(copy, "Assets");
        Directory.CreateDirectory(Path.Combine(assetsFolder, "Art"));
        Directory.CreateDirectory(Path.Combine(assetsFolder, "Dropped"));
        shell.RefreshAssets();
        AssetsPanel assets = shell.Main.DockSpace.Panels.OfType<AssetsPanel>().Single();
        assets.ShowFolder("");
        yield return 3;
        AssetDatabase database = shell.Database!;

        // The files other programs drag in.
        string source = Path.Combine(Path.GetFullPath(_folder), "drops-source");
        if (Directory.Exists(source))
        {
            Directory.Delete(source, recursive: true);
        }
        Directory.CreateDirectory(source);
        string hero = WriteImage(source, "Hero.png", 16, 16, (x, y) => (x + y) % 3 == 0 ? 0x203040 : (x * y) % 5 == 0 ? 0xE0C080 : 0x50A050);
        string photo = WriteImage(source, "Photo.png", 64, 64, (x, y) => (x * 4) | (y * 4 << 8) | ((x ^ y) * 4 << 16));
        string odd = WriteImage(source, "Odd.png", 30, 30, (x, y) => (x * 8) | (y * 8 << 8) | ((x + y) * 4 << 16));
        string tree = WriteImage(source, "Tree.png", 8, 16, (x, y) => y < 10 ? 0x208020 : 0x604020);
        string props = Path.Combine(source, "Props");
        Directory.CreateDirectory(Path.Combine(props, "Inner"));
        WriteImage(props, "Rock.png", 8, 8, (x, y) => (x + y) % 2 == 0 ? 0x808080 : 0x606060);
        File.WriteAllText(Path.Combine(props, "Inner", "notes.xyz"), "not an asset");
        File.WriteAllText(Path.Combine(props, ".hidden"), "left out");
        // A model whose buffer differs from the project's Walker.bin.
        string models = Path.Combine(source, "Models");
        Directory.CreateDirectory(models);
        File.Copy(Path.Combine(assetsFolder, "Models", "Walker.gltf"), Path.Combine(models, "Walker.gltf"));
        byte[] buffer = File.ReadAllBytes(Path.Combine(assetsFolder, "Models", "Walker.bin"));
        buffer[^1] ^= 1;
        File.WriteAllBytes(Path.Combine(models, "Walker.bin"), buffer);
        string walker = Path.Combine(models, "Walker.gltf");

        // Over the Assets panel's Art folder: highlighted, the note names it.
        AssetTile art = assets.Tiles.First(tile => tile.Path == "Art");
        Move(shell, Center(art));
        yield return 2;
        Check(art.Classes.Contains("drop-over") && shell.DropNote == "Drop to import into Art/",
            "files dragged over a folder highlight it and the note names it", shell.DropNote);
        // Over the panel elsewhere: the folder it shows.
        Move(shell, new Vector2(assets.Folders.Bounds.X + 8, assets.Folders.Bounds.Bottom - 8));
        yield return 2;
        Check(!art.Classes.Contains("drop-over") && assets.Classes.Contains("drop-over") && shell.DropNote == "Drop to import into Assets/",
            "over the rest of the panel, into the folder it shows", shell.DropNote);
        // Over the viewport: the panel's folder, the panel no longer outlined.
        ViewportPanel viewport = shell.Main.DockSpace.Panels.OfType<ViewportPanel>().Single();
        viewport.Group?.Activate(viewport);
        yield return 2;
        SceneView view = viewport.View;
        Check(shell.Main.Root.HitTest(Center(view)) == view, "the viewport is under the pointer", shell.Main.Root.HitTest(Center(view))?.GetType().Name ?? "nothing");
        Move(shell, Center(view));
        yield return 2;
        Check(!assets.Classes.Contains("drop-over") && shell.DropNote == "Drop to import into Assets/",
            "over the viewport, into the Assets panel's folder", shell.DropNote);
        // Left without a drop.
        shell.ProcessEvents([new InputEvent { Type = InputEventType.FileDragEnded, Window = shell.Main.Window }], []);
        yield return 2;
        Check(shell.Main.Root.DragPayload is null && !shell.Dropping, "a drag that leaves ends without importing");

        // Three images dropped on Art: each asks, guessing from its pixels.
        Drop(shell, Center(art), hero, photo, odd);
        yield return 2;
        Check(shell.DropQuestion is { Message: "Import Hero.png as", Suggested: 0 } q1 && q1.Choices.SequenceEqual(["Pixel Art", "Texture", "UI"]),
            "a 16x16 image of three colors is guessed pixel art", Describe(shell.DropQuestion));
        Check(shell.DropQuestion?.ForTheRest.Parent is not null, "with \"Do this for the rest\" offered while more images ask");
        shell.DropQuestion?.Answer("Pixel Art");
        yield return 2;
        Check(shell.DropQuestion is { Message: "Import Photo.png as", Suggested: 1 }, "a 64x64 image of many colors is guessed a texture", Describe(shell.DropQuestion));
        shell.DropQuestion?.Answer("Texture");
        yield return 2;
        Check(shell.DropQuestion is { Message: "Import Odd.png as", Suggested: 1 } q3 && q3.Choices.SequenceEqual(["Pixel Art", "UI"]) &&
              q3.ForTheRest.Parent is null,
            "a 30x30 photo is guessed UI, Texture not offered (not whole 4x4 blocks), and it's the last to ask", Describe(shell.DropQuestion));
        shell.DropQuestion?.Answer("UI");
        yield return 3;
        Check(!shell.Dropping && Usage(database, "Art/Hero.png") == "Sprite" && Usage(database, "Art/Photo.png") == "Color" &&
              Usage(database, "Art/Odd.png") == "Ui",
            "the images are copied into Art with the usage chosen",
            $"{Usage(database, "Art/Hero.png")}, {Usage(database, "Art/Photo.png")}, {Usage(database, "Art/Odd.png")}");
        Check(new[] { "Art/Hero.png", "Art/Photo.png", "Art/Odd.png" }.All(path => Imported(database, path)), "and imported");
        Check(shell.Windows.SelectMany(window => window.DockSpace.Panels).OfType<SpriteSheetPanel>().Any(),
            "one image made pixel art opens the Sprite Sheet Editor");
        AssetId heroId = database.Find("Art/Hero.png")!.Id;

        // Hero.png again: Keep Both, then (a new image) asked how; Replace keeps its GUID; Skip.
        Drop(shell, Center(assets.Tiles.First(tile => tile.Path == "Art")), hero);
        yield return 2;
        Check(shell.DropQuestion is { Message: "Hero.png is already there", Suggested: 1 } c1 &&
              c1.Choices.SequenceEqual(["Replace", "Keep Both", "Skip"]) && c1.ForTheRest.Parent is null,
            "a name already there asks, Keep Both first, nothing more to do it for", Describe(shell.DropQuestion));
        shell.DropQuestion?.Answer("Keep Both");
        yield return 2;
        Check(shell.DropQuestion is { Message: "Import Hero 2.png as" }, "kept both, the copy is a new image to ask about", Describe(shell.DropQuestion));
        shell.DropQuestion?.Answer("Pixel Art");
        yield return 3;
        Check(Imported(database, "Art/Hero 2.png") && database.Find("Art/Hero.png")?.Id == heroId, "Keep Both adds Hero 2.png beside it");
        Drop(shell, Center(assets.Tiles.First(tile => tile.Path == "Art")), hero, photo);
        yield return 2;
        shell.DropQuestion?.Answer("Replace", forTheRest: true);
        yield return 3;
        Check(shell.DropQuestion is null && !shell.Dropping, "\"Do this for the rest\" answers the next name too", Describe(shell.DropQuestion));
        Check(database.Find("Art/Hero.png")?.Id == heroId && Usage(database, "Art/Hero.png") == "Sprite",
            "a replaced file keeps its GUID and its import settings");
        int files = Directory.GetFiles(Path.Combine(assetsFolder, "Art")).Length;
        Drop(shell, Center(assets.Tiles.First(tile => tile.Path == "Art")), photo);
        yield return 2;
        shell.DropQuestion?.Answer("Skip");
        yield return 3;
        Check(Directory.GetFiles(Path.Combine(assetsFolder, "Art")).Length == files && _logged.Contains("Skipped Photo.png: one is already there"),
            "Skip copies nothing, and says so");

        // Canceled (Escape): nothing copied.
        Drop(shell, Center(assets.Tiles.First(tile => tile.Path == "Art")), tree);
        yield return 2;
        shell.DropQuestion?.Close();
        yield return 2;
        Check(!shell.Dropping && !File.Exists(Path.Combine(assetsFolder, "Art", "Tree.png")), "canceling a question copies nothing");

        // A folder, on the panel: whole, its unknown file copied and logged, hidden ones left.
        Drop(shell, new Vector2(assets.Folders.Bounds.X + 8, assets.Folders.Bounds.Bottom - 8), props);
        yield return 2;
        Check(shell.DropQuestion is { Message: "Import Rock.png as", Suggested: 0 }, "a folder's images ask too", Describe(shell.DropQuestion));
        shell.DropQuestion?.Answer("Pixel Art");
        yield return 3;
        Check(Imported(database, "Props/Rock.png") && File.Exists(Path.Combine(assetsFolder, "Props", "Inner", "notes.xyz")) &&
              !File.Exists(Path.Combine(assetsFolder, "Props", ".hidden")),
            "a folder is copied whole, hidden files left out");
        Check(_logged.Contains("Copied Props/Inner/notes.xyz: nothing imports .xyz files"), "a file nothing imports is copied and logged");

        // A model on the viewport: into the panel's folder with its buffer, placed.
        assets.ShowFolder("Dropped");
        viewport.Group?.Activate(viewport); // the Sprite Sheet Editor opened over it
        yield return 2;
        int before = SceneIds.Of(shell.World).Entities.Count();
        Drop(shell, Center(view), walker);
        yield return 4;
        Check(Imported(database, "Dropped/Walker.gltf") && File.Exists(Path.Combine(assetsFolder, "Dropped", "Walker.bin")),
            "a .gltf dropped brings its buffer", string.Join(" | ", _logged.TakeLast(4)));
        Entity placed = shell.Selection.Selected;
        Check(SceneIds.Of(shell.World).Entities.Count() > before && shell.HasWorld && shell.World.IsAlive(placed) &&
              shell.LabelOf(placed).Contains("Walker", StringComparison.Ordinal),
            "dropped on the viewport, the model is placed and selected", shell.LabelOf(placed));
        shell.Undo();
        yield return 2;
        Check(SceneIds.Of(shell.World).Entities.Count() == before, "one undo takes the placed model away");
        // Pixel art on the viewport: a sprite of it.
        viewport.Group?.Activate(viewport);
        yield return 2;
        Drop(shell, Center(view), tree);
        yield return 2;
        shell.DropQuestion?.Answer("Pixel Art");
        yield return 3;
        Entity sprite = shell.Selection.Selected;
        Check(shell.World.IsAlive(sprite) && shell.World.Has(sprite, Components.Of<Sprite>()) &&
              shell.World.Read<Sprite>(sprite).Texture == database.Find("Dropped/Tree.png")?.Id && shell.LabelOf(sprite) == "Tree",
            "pixel art dropped on the viewport is placed as a sprite");

        // The model again, on Models, where a different Walker.bin is: renamed with it.
        assets.ShowFolder("");
        yield return 2;
        Drop(shell, Center(assets.Tiles.First(tile => tile.Path == "Models")), walker);
        yield return 2;
        Check(shell.DropQuestion is { Message: "Walker.gltf is already there" }, "a model already there asks", Describe(shell.DropQuestion));
        shell.DropQuestion?.Answer("Keep Both");
        yield return 4;
        JsonNode? written = JsonNode.Parse(File.ReadAllText(Path.Combine(assetsFolder, "Models", "Walker 2.gltf")));
        Check(written?["buffers"]?[0]?["uri"]?.GetValue<string>() == "Walker%202.bin" &&
              File.ReadAllBytes(Path.Combine(assetsFolder, "Models", "Walker 2.bin")).AsSpan().SequenceEqual(buffer) &&
              Imported(database, "Models/Walker 2.gltf"),
            "a different buffer of the same name gets a free name, and the copy's URI follows it",
            written?["buffers"]?[0]?["uri"]?.ToString());

        // On the hierarchy: placed at the top.
        Drop(shell, new Vector2(Hierarchy(shell).Bounds.X + 20, Hierarchy(shell).Bounds.Bottom - 10), Path.Combine(source, "Hero.png"));
        yield return 2;
        shell.DropQuestion?.Answer("Pixel Art"); // new in Assets/
        yield return 3;
        Check(shell.World.IsAlive(shell.Selection.Selected) && shell.World.Has(shell.Selection.Selected, Components.Of<Sprite>()) &&
              shell.World.ParentOf(shell.Selection.Selected).IsNull,
            "pixel art dropped on the hierarchy is placed at the top");
        shell.Logged -= _logged.Add;
    }

    private static HierarchyPanel Hierarchy(Shell shell) => shell.Main.DockSpace.Panels.OfType<HierarchyPanel>().Single();

    private static Vector2 Center(UI.UIElement element) =>
        new(element.Bounds.X + element.Bounds.Width / 2, element.Bounds.Y + element.Bounds.Height / 2);

    private static Vector2 Pixels(Shell shell, Vector2 at) => at * shell.Main.Root.Scale;

    // As the platform gives them: moves over the window, then each path, then the end.
    private static void Move(Shell shell, Vector2 at)
    {
        Vector2 pixels = Pixels(shell, at);
        shell.ProcessEvents([new InputEvent { Type = InputEventType.FileDragMoved, Window = shell.Main.Window, X = pixels.X, Y = pixels.Y }], []);
    }

    private static void Drop(Shell shell, Vector2 at, params string[] paths)
    {
        Move(shell, at);
        Vector2 pixels = Pixels(shell, at);
        List<InputEvent> events = [];
        List<byte> text = [];
        foreach (string path in paths)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(path);
            events.Add(new InputEvent
            {
                Type = InputEventType.FileDropped,
                Window = shell.Main.Window,
                X = pixels.X,
                Y = pixels.Y,
                Code = text.Count,
                Value = bytes.Length,
            });
            text.AddRange(bytes);
        }
        events.Add(new InputEvent { Type = InputEventType.FileDragEnded, Window = shell.Main.Window });
        shell.ProcessEvents([.. events], [.. text]);
    }

    private static string WriteImage(string folder, string name, int width, int height, Func<int, int, int> color)
    {
        byte[] rgb = new byte[width * height * 3];
        for (int y = 0; y < height; ++y)
        {
            for (int x = 0; x < width; ++x)
            {
                int c = color(x, y);
                int at = (y * width + x) * 3;
                rgb[at] = (byte)c;
                rgb[at + 1] = (byte)(c >> 8);
                rgb[at + 2] = (byte)(c >> 16);
            }
        }
        string path = Path.Combine(folder, name);
        Png.Write(path, width, height, rgb);
        return path;
    }

    private static string? Usage(AssetDatabase database, string path) =>
        database.Find(path) is { } entry ? database.SettingsOf(entry.Id)?["Usage"]?.ToString() : null;

    private static bool Imported(AssetDatabase database, string path) =>
        database.Find(path) is { } entry && database.ImportedFile(entry.Id) is not null;

    private static string Describe(DropQuestion? question) =>
        question is null ? "no question" : $"\"{question.Message}\" [{string.Join(", ", question.Choices)}] guess {question.Suggested}";

    private void Check(bool condition, string what, string? detail = null)
    {
        ++_checks;
        if (!condition)
        {
            ++_failures;
        }
        Console.WriteLine($"DropChecks: {(condition ? "ok" : "FAILED")}: {what}{(condition || detail is null ? "" : $" ({detail})")}");
    }
}
