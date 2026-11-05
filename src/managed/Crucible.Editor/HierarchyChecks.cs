using System.Numerics;
using System.Text;
using Crucible.Ecs;
using Crucible.Loop;
using Crucible.Platform;
using Crucible.Scenes;
using Crucible.UI;

namespace Crucible.Editor;

/// <summary>
/// The hierarchy, undo, and saving checking themselves headless (ADR 0068),
/// for CTest's launcher.jit.hierarchy: run with a project and scene (the
/// Diorama's village), <c>--game-arg check-hierarchy=&lt;file.png&gt;</c>, and a
/// settings file of its own. The village's entities are listed with their
/// prefabs' names; selecting goes both ways; F2 renames; Create Empty,
/// Delete, Duplicate, and dragging one row onto another change the scene,
/// and each undoes and redoes; the scene saves (beside the capture, never
/// over the village) and loads back as saved; the unsaved-changes dialog
/// cancels or goes on. The window is captured with a row being dragged.
/// </summary>
[System(Phases.RenderPrepare)]
internal sealed class HierarchyChecks : GameSystem
{
    private const int ScancodeReturn = 40, ScancodeF2 = 59;

    private IEnumerator<int>? _script;
    private int _wait;
    private int _checks;
    private int _failures;
    private string _capture = "";
    private FrameContext? _context;

    public override void Start(FrameContext context)
    {
        if (!context.GameArguments.TryGetValue("check-hierarchy", out string? capture))
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
            ? $"HierarchyChecks: all {_checks} checks passed"
            : $"HierarchyChecks: {_failures} of {_checks} checks failed");
        context.QuitRequested = true;
    }

    private IEnumerable<int> Script()
    {
        Shell shell = Shell.Current ?? throw new InvalidOperationException("No editor shell");
        shell.RestoreLayout(null);
        yield return 3;
        HierarchyPanel hierarchy = shell.Main.DockSpace.Panels.OfType<HierarchyPanel>().Single();
        TreeView tree = hierarchy.Tree;
        UIRoot root = shell.Main.Root;
        World world = _context!.World;

        // The village's tops, labeled by their prefabs.
        string[] labels = [.. Enumerable.Range(0, tree.RowCount).Select(i => shell.LabelOf(EntityOf(tree.RowNode(i))))];
        Check(tree.RowCount >= 16, "the village's entities are listed", tree.RowCount.ToString());
        Check(labels.Contains("House") && labels.Contains("Tree"), "rows are labeled by their prefabs", string.Join(",", labels));
        Check(Enumerable.Range(0, tree.RowCount).All(i => tree.RowDepth(i) == 0), "only the tops show at first");
        Check(!shell.IsDirty && !shell.Main.TitleBar.Title.Contains('*'), "a scene just opened is saved",
            shell.Main.TitleBar.Title);

        // Every row draws its text: the tops, then with every top expanded, scrolled through.
        Check(Unlabeled(tree) == "", "every row shows its name", Unlabeled(tree));
        foreach (ulong top in Enumerable.Range(0, tree.RowCount).Select(tree.RowNode).ToArray())
        {
            tree.SetExpanded(top, true);
        }
        yield return 2;
        string missing = "";
        for (int i = 0; i < tree.RowCount; i += 8)
        {
            RowItem(tree, tree.RowNode(i));
            yield return 1;
            missing += Unlabeled(tree);
        }
        Check(missing == "", "every expanded row shows its name", missing);
        foreach (ulong top in Enumerable.Range(0, tree.RowCount).Where(i => tree.RowDepth(i) == 0).Select(tree.RowNode).ToArray())
        {
            tree.SetExpanded(top, false);
        }
        tree.ScrollView.ScrollTo(Orientation.Vertical, 0);
        yield return 2;

        // Selecting: a row selects its entity, and the selection shows in the tree.
        ulong houseNode = tree.RowNode(Array.IndexOf(labels, "House"));
        Entity house = EntityOf(houseNode);
        ViewportChecks.Click(root, ViewportChecks.Center(RowItem(tree, houseNode)));
        yield return 2;
        Check(shell.Selection.Selected.Value == houseNode, "a click on a row selects its entity");
        ulong treeNode = tree.RowNode(Array.IndexOf(labels, "Tree"));
        shell.Selection.Selected = EntityOf(treeNode);
        yield return 2;
        Check(tree.SelectedNode == treeNode, "selecting elsewhere selects the row");

        // F2 renames the selection; undo and redo put the names back and forth.
        Send(root, KeyDown(ScancodeF2, Keycode.F2));
        yield return 2;
        Check(root.FocusedElement is TextField, "F2 starts renaming");
        Type(root, "Old Oak");
        Send(root, KeyDown(ScancodeReturn, Keycode.Return));
        yield return 2;
        Check(shell.LabelOf(EntityOf(treeNode)) == "Old Oak", "Enter renames it", shell.LabelOf(EntityOf(treeNode)));
        Check(shell.IsDirty && shell.Main.TitleBar.Title.Contains('*'), "the title shows unsaved changes",
            shell.Main.TitleBar.Title);
        shell.Undo();
        yield return 2;
        Check(shell.LabelOf(EntityOf(treeNode)) == "Tree" && !shell.IsDirty, "undo puts the name back");
        shell.Redo();
        yield return 2;
        Check(shell.LabelOf(EntityOf(treeNode)) == "Old Oak", "redo renames it again");

        // Create Empty makes a top entity; deleting it, and undoing that, brings it back with its ID.
        int before = tree.RowCount;
        shell.Commands.Find("Hierarchy.CreateEmpty")!.Execute();
        yield return 2;
        Entity empty = shell.Selection.Selected;
        ulong emptyId = SceneIds.IdOf(world, empty);
        Check(tree.RowCount == before + 1 && tree.IndexOf(empty.Value) >= 0, "Create Empty adds a row", tree.RowCount.ToString());
        Check(shell.LabelOf(empty) == "Empty", "named Empty", shell.LabelOf(empty));

        // Dragged onto the house: under it, where it was in the world.
        Vector2 from = ViewportChecks.Center(RowItem(tree, empty.Value)), to = ViewportChecks.Center(RowItem(tree, houseNode));
        Send(root, ViewportChecks.Motion(from), ViewportChecks.Button(from, true), ViewportChecks.Motion(from + new Vector2(0, 8)),
            ViewportChecks.Motion(to));
        yield return 2;
        ViewportChecks.Save(shell.Main.Surface, _capture);
        Send(root, ViewportChecks.Button(to, false));
        yield return 2;
        Check(world.ParentOf(empty).Value == house.Value, "dropping on a row reparents into it");
        Check(tree.IsExpanded(houseNode) && tree.RowDepth(tree.IndexOf(empty.Value)) == 1, "and shows it there, expanded");
        Vector3 placed = world.ComputeWorldTransform(empty).Translation;
        Check(placed.Length() < 1e-3f, "it stays where it was in the world", placed.ToString());
        shell.Undo();
        yield return 2;
        Check(world.ParentOf(empty).IsNull, "undo puts it back at the top");

        shell.Delete(empty);
        yield return 2;
        Check(tree.IndexOf(empty.Value) < 0 && SceneIds.Find(world, emptyId).IsNull, "Delete removes it");
        shell.Undo();
        yield return 2;
        Entity back = SceneIds.Find(world, emptyId);
        Check(!back.IsNull && tree.IndexOf(back.Value) >= 0, "undoing Delete brings it back with its ID");

        // Duplicate: another house, which undo removes.
        int houses = HouseCount(shell, tree);
        shell.Duplicate(house);
        yield return 2;
        Check(HouseCount(shell, tree) == houses + 1, "Duplicate copies the house", HouseCount(shell, tree).ToString());
        Entity copy = shell.Selection.Selected;
        Check(SceneIds.IdOf(world, copy) != SceneIds.IdOf(world, house), "with a new scene ID");
        shell.Undo();
        yield return 2;
        Check(HouseCount(shell, tree) == houses, "undo removes the copy");

        // A handle drag's moves undo as one.
        shell.Selection.Selected = house;
        Vector3 start = world.Read<LocalTransform>(house).Position;
        shell.History.BeginGroup("Move");
        for (int i = 1; i <= 3; ++i)
        {
            shell.SetSelectedTransform(Matrix4x4.CreateTranslation(start + new Vector3(i, 0, 0)));
        }
        shell.History.EndGroup();
        shell.Undo();
        yield return 2;
        Check(Vector3.Distance(world.Read<LocalTransform>(house).Position, start) < 1e-4f, "a drag undoes in one step");

        // Saved beside the capture, then loaded back: the name is kept, and nothing's unsaved.
        string saved = Path.ChangeExtension(_capture, Scene.Extension);
        Check(shell.WriteScene(saved) && !shell.IsDirty && !shell.Main.TitleBar.Title.Contains('*'), "the scene saves",
            shell.Main.TitleBar.Title);
        shell.OpenScene(saved);
        yield return 3;
        Check(Enumerable.Range(0, tree.RowCount).Any(i => shell.LabelOf(EntityOf(tree.RowNode(i))) == "Old Oak"),
            "the saved scene has the new name");
        Check(!shell.History.CanUndo, "opening a scene starts its history afresh");

        // Unsaved changes: New asks first; Cancel keeps the scene, Don't Save clears it.
        shell.Rename(EntityOf(tree.RowNode(0)), "Changed");
        int entities = world.EntityCount;
        shell.Commands.Find("File.NewScene")!.Execute();
        yield return 2;
        Dialog? dialog = root.Popups.OfType<Dialog>().SingleOrDefault();
        Check(dialog is not null, "New asks about unsaved changes");
        if (dialog is not null)
        {
            ViewportChecks.Click(root, ViewportChecks.Center(DialogButton(dialog, "Cancel")));
            yield return 2;
            Check(!dialog.IsOpen && world.EntityCount == entities, "Cancel keeps the scene");
        }
        shell.Commands.Find("File.NewScene")!.Execute();
        yield return 2;
        dialog = root.Popups.OfType<Dialog>().SingleOrDefault();
        if (dialog is not null)
        {
            ViewportChecks.Click(root, ViewportChecks.Center(DialogButton(dialog, "Don't Save")));
            yield return 2;
        }
        Check(tree.RowCount == 0 && !shell.IsDirty && shell.SceneName == "Untitled", "Don't Save makes the new scene",
            tree.RowCount.ToString());
    }

    private static Entity EntityOf(ulong node) => new() { Value = node };

    // Rows in view with text their label can't draw: no shaped text, or no room.
    private static string Unlabeled(TreeView tree)
    {
        string missing = "";
        for (int i = 0; i < tree.RowCount; ++i)
        {
            if (tree.RowOf(tree.RowNode(i)) is { } row && row.Text.Length > 0 && (row.Label.Layout is null || row.Label.Bounds.Width < 1))
            {
                missing += $"{row.Text}@{i} ";
            }
        }
        return missing;
    }

    private static int HouseCount(Shell shell, TreeView tree) =>
        Enumerable.Range(0, tree.RowCount).Count(i => shell.LabelOf(EntityOf(tree.RowNode(i))) == "House");

    // The list's row around a tree row, scrolled into view.
    private static UIElement RowItem(TreeView tree, ulong node)
    {
        tree.ScrollIntoView(node);
        tree.Root!.UpdateLayout(tree.Root.Size * tree.Root.Scale);
        return tree.RowOf(node)?.Parent ?? throw new InvalidOperationException($"No row for {node}");
    }

    private static Button DialogButton(Dialog dialog, string text) =>
        Descendants(dialog).OfType<Button>().First(button => button.Text == text);

    private static IEnumerable<UIElement> Descendants(UIElement element)
    {
        foreach (UIElement child in element.Children)
        {
            yield return child;
            foreach (UIElement inner in Descendants(child))
            {
                yield return inner;
            }
        }
    }

    private static void Send(UIRoot root, params InputEvent[] events) => root.ProcessInput(events);

    private static InputEvent KeyDown(int scancode, uint keycode) =>
        new() { Type = InputEventType.KeyDown, Code = scancode, Keycode = keycode };

    private static void Type(UIRoot root, string typed)
    {
        byte[] utf8 = Encoding.UTF8.GetBytes(typed);
        root.ProcessInput([new InputEvent { Type = InputEventType.TextInput, Code = 0, Value = utf8.Length }], utf8);
    }

    private void Check(bool passed, string what, string detail = "")
    {
        ++_checks;
        if (!passed)
        {
            ++_failures;
            Console.WriteLine($"HierarchyChecks: FAILED: {what} {detail}");
        }
    }
}
