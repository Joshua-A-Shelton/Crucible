using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Crucible.Assets;
using Crucible.Assets.Importers;
using Crucible.Binary;
using Crucible.Ecs;
using Crucible.Loop;
using Crucible.Physics2D;
using Crucible.Platform;
using Crucible.Scenes;
using Crucible.UI;
using H = Crucible.Editor.HierarchyChecks;
using V = Crucible.Editor.ViewportChecks;

namespace Crucible.Editor;

/// <summary>
/// A chain's point handles checking themselves (ADR 0095), for CTest's
/// launcher.jit.chain-handles: run with the Walker's project and
/// <c>--game-arg check-chain-handles=&lt;folder&gt;</c>. On a copy of the
/// project (in the folder), Create > Chain makes a chain; a 2D entity's Chain
/// collider uses it; Edit Points turns the handles on; dragging a middle dot
/// inserts a point, dragging a point (Ctrl) moves it snapped, and Delete
/// removes the selected one, never below two. Each edit is written and
/// imported at once, undoes and redoes, and leaves the scene saved. P turns
/// the handles off. The window is saved as chain-handles.png while editing.
/// </summary>
[System(Phases.RenderPrepare)]
internal sealed class ChainHandleChecks : GameSystem
{
    private const int ScancodeDelete = 76, ScancodeP = 19;

    private IEnumerator<int>? _script;
    private int _wait;
    private int _checks;
    private int _failures;
    private string _folder = "";

    public override void Start(FrameContext context)
    {
        if (!context.GameArguments.TryGetValue("check-chain-handles", out string? folder))
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
            ? $"ChainHandleChecks: all {_checks} checks passed"
            : $"ChainHandleChecks: {_failures} of {_checks} checks failed");
        context.QuitRequested = true;
    }

    private IEnumerable<int> Script()
    {
        Shell shell = Shell.Current ?? throw new InvalidOperationException("No editor shell");
        string source = Path.GetDirectoryName(AssetChecks.SourceProjectFile(shell)) ?? "";
        string copy = Path.Combine(Path.GetFullPath(_folder), "chain-handles-project");
        if (Directory.Exists(copy))
        {
            Directory.Delete(copy, recursive: true);
        }
        AssetChecks.CopyProject(source, copy);
        shell.OpenProject(Path.Combine(copy, Path.GetFileName(AssetChecks.SourceProjectFile(shell))));
        shell.OpenScene(Path.Combine(copy, "Assets", "Scenes", "Walk.crscene"));
        shell.RestoreLayout(null);
        yield return 3;
        AssetDatabase database = shell.Database!;
        UIRoot root = shell.Main.Root;
        SceneView view = shell.Main.DockSpace.Panels.OfType<ViewportPanel>().Single().View;
        InspectorPanel inspector = shell.Main.DockSpace.Panels.OfType<InspectorPanel>().Single();
        ChainHandles chains = shell.Chains;

        // Create > Chain: two points of ground, imported.
        string? made = shell.CreateChain("");
        Check(made == "New Chain.crchain", "Create > Chain made New Chain.crchain", made ?? "nothing");
        AssetEntry? entry = database.Assets.FirstOrDefault(asset => asset.Path == made);
        Check(entry is { Importer: "Chain" }, "it's imported as a chain");
        if (entry is null)
        {
            yield break;
        }
        AssetId chain = entry.Id;
        Check(shell.CreateChain("") == "New Chain 2.crchain", "a second is numbered");

        // A 2D entity at the origin with a Chain collider using it, seen face on.
        Entity ground = shell.CreateEmpty(Entity.Null);
        shell.SetComponent(ground, typeof(Transform2D).FullName!, new JsonObject { ["Scale"] = new JsonArray(1, 1) },
            "Add Component");
        shell.RemoveComponent(ground, typeof(LocalTransform).FullName!);
        shell.SetComponent(ground, typeof(Collider2D).FullName!,
            new JsonObject { ["Shape"] = "Chain", ["Chain"] = chain.ToString() }, "Add Component");
        shell.Selection.Selected = ground;
        view.Camera.Pivot = Vector3.Zero;
        view.Camera.Yaw = 0;
        view.Camera.Pitch = 0;
        view.Camera.Distance = 6;
        yield return 3;

        Button? editPoints = H.Descendants(inspector).OfType<Button>().FirstOrDefault(button => button.Text == "Edit Points");
        Check(editPoints is { Visibility: Visibility.Visible }, "the Chain collider shows Edit Points");
        if (editPoints is null)
        {
            yield break;
        }
        editPoints.Focus(); // scrolled into view, as Tab would
        yield return 2;
        Click(editPoints);
        yield return 2;
        Check(chains.IsEditing && chains.Entity.Value == ground.Value, "Edit Points edits its chain", editPoints.Bounds.ToString());
        Check(editPoints.Classes.Contains("on"), "the button shows it's on");
        Check(chains.Points.Count == 2, "with its two points", chains.Points.Count.ToString());
        shell.History.MarkSaved();

        Vector2 offset = new(view.Bounds.X, view.Bounds.Y);
        Vector2 Screen(Vector2 point) => offset + (view.Camera.Project(new Vector3(point, 0), view.ActualSize) ?? Vector2.Zero);
        void Drag(Vector2 from, Vector2 to, bool ctrl = false)
        {
            InputEvent up = V.Button(Screen(to), false);
            List<InputEvent> events = [V.Motion(Screen(from)), V.Button(Screen(from), true),
                V.Motion(Screen((from + to) / 2)), V.Motion(Screen(to)), up];
            if (ctrl)
            {
                for (int i = 0; i < events.Count; ++i)
                {
                    events[i] = events[i] with { Modifiers = (uint)KeyModifiers.Ctrl };
                }
            }
            root.ProcessInput([.. events]);
        }

        // Dragging the middle dot inserts a point there.
        Drag(new Vector2(0, 0), new Vector2(0, 1));
        yield return 3;
        Check(chains.Points.Count == 3 && Vector2.Distance(chains.Points[1], new Vector2(0, 1)) < 0.05f,
            "dragging the middle dot inserted a point where it was dropped", string.Join(" ", chains.Points));
        Check(FilePoints(shell, chain).Count == 3, "the .crchain has it");
        Check(ImportedCount(database, chain) == 3, "and it was imported at once");
        Check(shell.History.NextUndo is { Title: "Insert Point", ChangesScene: false }, "one undo step, Insert Point");
        Check(!shell.History.IsDirty, "the scene isn't modified");
        V.Save(shell.Main.Surface, Path.Combine(_folder, "chain-handles.png"));

        // Dragging a point with Ctrl moves it, snapped to a quarter meter.
        Drag(new Vector2(1, 0), new Vector2(1.93f, 0.52f), ctrl: true);
        yield return 3;
        Check(Vector2.Distance(chains.Points[0], new Vector2(2, 0.5f)) < 1e-4f, "Ctrl snaps the dragged point",
            chains.Points[0].ToString());
        Check(shell.History.NextUndo?.Title == "Move Point", "one undo step, Move Point");

        // Delete removes the selected point (the one just dragged), not the entity.
        root.ProcessInput([V.Motion(Screen(new Vector2(0, 1))), V.Button(Screen(new Vector2(0, 1)), true),
            V.Button(Screen(new Vector2(0, 1)), false)]);
        yield return 2;
        Check(chains.Selected == 1, "a click selects a point", chains.Selected.ToString());
        root.ProcessInput([H.KeyDown(ScancodeDelete, Keycode.Delete)]);
        yield return 3;
        Check(chains.Points.Count == 2 && shell.World.IsAlive(ground), "Delete removed the point, not the entity",
            string.Join(" ", chains.Points));
        Check(FilePoints(shell, chain).SequenceEqual([new Vector2(2, 0.5f), new Vector2(-1, 0)]), "and saved it",
            string.Join(" ", FilePoints(shell, chain)));
        root.ProcessInput([H.KeyDown(ScancodeDelete, Keycode.Delete)]);
        yield return 2;
        Check(chains.Points.Count == 2 && shell.World.IsAlive(ground), "a chain keeps two points");

        // Undo and redo write the file and show it.
        shell.History.Undo(shell.World);
        yield return 2;
        Check(chains.Points.Count == 3 && FilePoints(shell, chain).Count == 3 && ImportedCount(database, chain) == 3,
            "undo puts the point back, in the file and its import");
        shell.History.Redo(shell.World);
        yield return 2;
        Check(chains.Points.Count == 2 && FilePoints(shell, chain).Count == 2, "redo removes it again");
        Check(!shell.History.IsDirty, "the scene still isn't modified");

        // P turns the handles off (the viewport has the keys since the drags).
        root.ProcessInput([H.KeyDown(ScancodeP, Keycode.P)]);
        yield return 2;
        Check(!chains.IsEditing && !editPoints.Classes.Contains("on"), "P turns Edit Points off");
        root.ProcessInput([H.KeyDown(ScancodeP, Keycode.P)]);
        yield return 2;
        Check(chains.IsEditing, "and on again");
        shell.Selection.Selected = Entity.Null;
        yield return 2;
        Check(!chains.IsEditing, "selecting something else turns it off");
    }

    private static List<Vector2> FilePoints(Shell shell, AssetId chain)
    {
        string file = Path.Combine(shell.Project!.AssetsFolder, shell.Database!.Find(chain)!.Path);
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(file));
        return ChainImporter.Read(document.RootElement).Points;
    }

    private static int ImportedCount(AssetDatabase database, AssetId chain)
    {
        if (database.ImportedFile(chain) is not { } path)
        {
            return -1;
        }
        using ImportedFile file = ImportedFile.Open(path, ChainImporter.Kind, ChainImporter.KindVersion);
        return (int)MemoryMarshal.Read<ChainInfo>(file.Read(file.Find(Tag.Of("INFO")))).PointCount;
    }

    private static void Click(UIElement button) => V.Click(button.Root!, V.Center(button));

    private void Check(bool passed, string what, string detail = "")
    {
        ++_checks;
        if (!passed)
        {
            ++_failures;
            Console.WriteLine($"ChainHandleChecks: FAILED: {what} {detail}");
        }
    }
}
