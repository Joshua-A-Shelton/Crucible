using System.Numerics;
using Crucible.Ecs;
using Crucible.Loop;
using Crucible.Platform;
using Crucible.Scenes;
using Crucible.UI;
using H = Crucible.Editor.HierarchyChecks;
using V = Crucible.Editor.ViewportChecks;

namespace Crucible.Editor;

/// <summary>
/// The asset browser checking itself headless (ADR 0071), for CTest's
/// launcher.jit.assets: run with the Diorama's project,
/// <c>--game-arg check-assets=&lt;file.png&gt;</c>, and a settings file of its
/// own. It copies the project beside the capture first, so files it adds
/// never touch the sources. Folders and tiles show; a file copied in is
/// noticed and imported; a model dragged into the viewport is placed under
/// the pointer and undoes as one step, and leaving the viewport takes it
/// away; a texture isn't placed; a model dropped on a hierarchy row goes
/// under it; Escape cancels a drag. The window is captured mid-drag.
/// </summary>
[System(Phases.RenderPrepare)]
internal sealed class AssetChecks : GameSystem
{
    private const int ScancodeEscape = 41;

    private IEnumerator<int>? _script;
    private int _wait;
    private int _checks;
    private int _failures;
    private string _capture = "";
    private FrameContext? _context;

    public override void Start(FrameContext context)
    {
        if (!context.GameArguments.TryGetValue("check-assets", out string? capture))
        {
            Enabled = false;
            return;
        }
        _capture = capture;
        _context = context;
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
            ? $"AssetChecks: all {_checks} checks passed"
            : $"AssetChecks: {_failures} of {_checks} checks failed");
        context.QuitRequested = true;
    }

    private IEnumerable<int> Script()
    {
        Shell shell = Shell.Current ?? throw new InvalidOperationException("No editor shell");
        // A copy of the project to change freely.
        string source = Path.GetDirectoryName(shell.Project?.ProjectFilePath ?? "") ?? "";
        string copy = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(_capture))!, "assets-project");
        if (Directory.Exists(copy))
        {
            Directory.Delete(copy, recursive: true);
        }
        CopyProject(source, copy);
        shell.OpenProject(Path.Combine(copy, Path.GetFileName(shell.Project!.ProjectFilePath)));
        shell.OpenScene(Path.Combine(copy, "Assets", "Scenes", "Village.crscene"));
        shell.RestoreLayout(null);
        yield return 3;
        AssetsPanel assets = shell.Main.DockSpace.Panels.OfType<AssetsPanel>().Single();
        assets.Group?.Activate(assets);
        yield return 2;
        UIRoot root = shell.Main.Root;
        World world = _context!.World;

        // The top folder's tiles: its folders.
        Check(Names(assets) == "Models,Scenes,Textures", "the Assets folder shows its folders", Names(assets));
        AssetTile models = assets.Tiles.First(tile => tile.Path == "Models");
        Check(models.IsFolder && models.Kind == "Folder", "as folders");
        Vector2 at = V.Center(models);
        H.Send(root, V.Motion(at), V.Button(at, true), V.Button(at, false), V.Button(at, true), V.Button(at, false));
        yield return 2;
        Check(assets.Folder == "Models", "a double-click opens a folder", assets.Folder);
        AssetTile? house = assets.Tiles.FirstOrDefault(tile => tile.Path == "Models/House.gltf");
        Check(house is { Kind: "Model", CanPlace: true }, "a model's tile is a model", house?.Kind ?? "missing");
        Check(assets.Folders.SelectedNode is not null, "the folder tree follows");

        // A file copied in is noticed, imported, and shown.
        assets.ShowFolder("Textures");
        yield return 2;
        File.Copy(Path.Combine(copy, "Assets", "Textures", "Grass.png"), Path.Combine(copy, "Assets", "Textures", "Grass Copy.png"));
        // Up to five seconds: the watcher waits for changes to settle, whatever the frame rate.
        long started = Environment.TickCount64;
        while (assets.Tiles.All(tile => tile.Path != "Textures/Grass Copy.png") && Environment.TickCount64 - started < 5000)
        {
            yield return 2;
        }
        long waited = Environment.TickCount64 - started;
        AssetTile? added = assets.Tiles.FirstOrDefault(tile => tile.Path == "Textures/Grass Copy.png");
        Check(added is { Kind: "Texture" }, "a copied file is noticed and listed", $"after {waited} ms");
        Check(added?.Asset is { } entry && shell.Database?.ImportedFile(entry.Id) is not null, "and imported");
        // A file that can't import is logged once, not at every rescan.
        File.WriteAllBytes(Path.Combine(copy, "Assets", "Textures", "Broken.png"), [1, 2, 3]);
        LogPanel? log = shell.LogPanel;
        int before = log?.LineCount ?? 0;
        for (int i = 0; i < 3; ++i)
        {
            shell.RefreshAssets();
        }
        yield return 2;
        Check(log is null || log.LineCount - before == 1, "a failing import is logged once", $"{(log?.LineCount ?? 0) - before} lines");
        AssetTile grass = assets.Tiles.First(tile => tile.Path == "Textures/Grass.png");
        grass.Focus();
        yield return 2;

        // A texture dragged over the viewport isn't placed.
        ViewportPanel viewport = shell.Main.DockSpace.Panels.OfType<ViewportPanel>().Single();
        SceneView view = viewport.View;
        Vector2 middle = new(view.Bounds.X + view.Bounds.Width / 2, view.Bounds.Y + view.Bounds.Height * 0.6f);
        Drag(root, V.Center(grass), middle);
        yield return 3;
        Check(root.IsDragging && shell.Placing.IsNull, "a texture isn't placed");
        H.Send(root, H.KeyDown(ScancodeEscape, Keycode.Escape));
        Check(!root.IsDragging, "Escape cancels a drag");
        H.Send(root, V.Button(middle, false)); // the button let go afterwards

        // A model: placed under the pointer as it moves, then dropped.
        assets.ShowFolder("Models");
        yield return 2;
        house = assets.Tiles.First(tile => tile.Path == "Models/House.gltf");
        house.Focus(); // scrolled into view
        yield return 2;
        int entities = world.EntityCount;
        Drag(root, V.Center(house), middle);
        yield return 6;
        Entity placing = shell.Placing;
        Check(!placing.IsNull && world.IsAlive(placing), "a model dragged into the viewport is placed",
            $"dragging {root.IsDragging}, payload {root.DragPayload}, can place {root.DragPayload is { } p && shell.CanPlace(p)}, " +
            $"over {root.HitTest(middle)?.GetType().Name}, importer {house.Asset?.Importer}");
        if (placing.IsNull)
        {
            yield break;
        }
        Vector3 first = world.Read<LocalTransform>(placing).Position;
        Check(MathF.Abs(first.Y) < 3, "on what's under the pointer", first.ToString());
        H.Send(root, V.Motion(middle + new Vector2(60, 0)));
        yield return 6;
        Vector3 moved = world.Read<LocalTransform>(placing).Position;
        Check(Vector3.Distance(moved, first) > 0.5f, "and follows it", $"{first} to {moved}");
        V.Save(shell.Main.Surface, _capture);
        int steps = shell.History.UndoCount;
        H.Send(root, V.Button(middle + new Vector2(60, 0), false));
        yield return 2;
        Check(world.IsAlive(placing) && shell.Selection.Selected.Value == placing.Value, "a drop keeps it, selected");
        Check(shell.History.UndoCount == steps + 1 && shell.History.NextUndo?.Title == "Place House", "as one step",
            shell.History.NextUndo?.Title ?? "");
        shell.Undo();
        yield return 2;
        Check(!world.IsAlive(placing) && world.EntityCount == entities, "undo takes it away");

        // Leaving the viewport takes the instance away. (Pressed off the last
        // press's point: soon after it, the same point is a double-click.)
        Drag(root, V.Center(house) + new Vector2(12, -12), middle);
        yield return 4;
        Check(!shell.Placing.IsNull, "placed again");
        H.Send(root, V.Motion(V.Center(house)));
        yield return 2;
        Check(shell.Placing.IsNull && world.EntityCount == entities, "leaving the viewport removes it");
        H.Send(root, V.Button(V.Center(house), false));
        yield return 2;

        // Carried in from another window, as the shell does with a platform drag.
        AssetDrag carried = new(house.Asset!.Id, house.Path);
        for (int i = 0; i < 4; ++i)
        {
            root.ContinueDrag(carried, null, middle + new Vector2(i * 10, 0));
            yield return 2;
        }
        Check(!shell.Placing.IsNull, "a drag carried in from another window places it", $"dragging {root.IsDragging}");
        // Placing undone mid-drag (the drag handed back and forth): the next move starts it again.
        shell.CancelPlacement();
        root.ContinueDrag(carried, null, middle + new Vector2(35, 0));
        yield return 2;
        Check(!shell.Placing.IsNull, "a move over the view starts placing again");
        root.DropAt(middle + new Vector2(30, 0));
        yield return 2;
        Check(shell.Placing.IsNull && shell.History.NextUndo?.Title == "Place House", "and the drop keeps it",
            shell.History.NextUndo?.Title ?? "");
        shell.Undo();
        yield return 2;

        // On a hierarchy row: under it.
        HierarchyPanel hierarchy = shell.Main.DockSpace.Panels.OfType<HierarchyPanel>().Single();
        ulong rowNode = hierarchy.Tree.RowNode(0);
        Drag(root, V.Center(house) + new Vector2(-12, 12), V.Center(H.RowItem(hierarchy.Tree, rowNode)));
        yield return 2;
        H.Send(root, V.Button(V.Center(H.RowItem(hierarchy.Tree, rowNode)), false));
        yield return 2;
        Entity under = shell.Selection.Selected;
        Check(!under.IsNull && world.ParentOf(under).Value == rowNode, "a drop on a row places it under the row");
    }

    // A press on a tile, a move past the drag threshold, and on to a point, held.
    private static void Drag(UIRoot root, Vector2 from, Vector2 to) =>
        H.Send(root, V.Motion(from), V.Button(from, true), V.Motion(from + new Vector2(8, 0)), V.Motion(to));

    private static string Names(AssetsPanel assets) => string.Join(",", assets.Tiles.Select(tile => tile.Path));

    internal static void CopyProject(string from, string to)
    {
        foreach (string directory in Directory.EnumerateDirectories(from, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(from, directory);
            if (!relative.Split(Path.DirectorySeparatorChar).Contains("Cache"))
            {
                Directory.CreateDirectory(Path.Combine(to, relative));
            }
        }
        Directory.CreateDirectory(to);
        foreach (string file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(from, file);
            if (!relative.Split(Path.DirectorySeparatorChar).Contains("Cache"))
            {
                File.Copy(file, Path.Combine(to, relative));
            }
        }
    }

    private void Check(bool passed, string what, string detail = "")
    {
        ++_checks;
        if (!passed)
        {
            ++_failures;
            Console.WriteLine($"AssetChecks: FAILED: {what} {detail}");
        }
    }
}
