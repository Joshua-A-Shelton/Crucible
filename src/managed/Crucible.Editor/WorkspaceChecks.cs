using System.Text.Json.Nodes;
using Crucible.Assets;
using Crucible.Ecs;
using Crucible.Loop;
using Crucible.Platform;
using Crucible.UI;

namespace Crucible.Editor;

/// <summary>
/// Workspaces checking themselves (ADR 0119), for CTest's
/// launcher.jit.workspaces: run with the Walker's project and
/// <c>--game-arg check-workspaces=&lt;folder&gt;</c>. On a copy of the project,
/// an extension's two workspaces, one allowing several tabs and one not:
/// opened for entities as tabs beside the Viewport, shown again for the same
/// entity, moved to another, their views rendered while shown, their keys
/// first while they have focus, closed when their entity goes or by Done,
/// and saved with the layout and across a reload of the code.
/// </summary>
[System(Phases.RenderPrepare)]
internal sealed class WorkspaceChecks : GameSystem
{
    private const int ScancodeK = 14; // SDL's, for K

    private IEnumerator<int>? _script;
    private int _wait;
    private int _checks;
    private int _failures;
    private string _folder = "";
    private readonly List<string> _logged = [];

    public override void Start(FrameContext context)
    {
        if (!context.GameArguments.TryGetValue("check-workspaces", out string? folder))
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
            ? $"WorkspaceChecks: all {_checks} checks passed"
            : $"WorkspaceChecks: {_failures} of {_checks} checks failed");
        context.QuitRequested = true;
    }

    private IEnumerable<int> Script()
    {
        Shell shell = Shell.Current ?? throw new InvalidOperationException("No editor shell");
        shell.Logged += _logged.Add;
        string copy = Path.Combine(Path.GetFullPath(_folder), "workspaces-project");
        AssetChecks.OpenCopy(shell, copy, Path.Combine("Assets", "Scenes", "Walk.crscene"), withoutEditorCode: true);
        shell.RestoreLayout(null);
        yield return 3;
        Project project = shell.Project!;
        string tools = Path.Combine(project.CodeFolder, "Editor", "Tools.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(tools)!);
        File.WriteAllText(tools, Tools(1));
        Check(shell.BuildCode(), "an extension with two workspaces builds", Logged());

        // Two entities to work on.
        Entity a = Create(shell), b = Create(shell);
        Check(!a.IsNull && !b.IsNull, "two entities made");
        ViewportPanel? viewport = shell.Windows.SelectMany(window => window.DockSpace.Panels).OfType<ViewportPanel>().FirstOrDefault();

        // Opened for one: a tab beside the Viewport, shown, knowing its entity.
        Workspace? first = shell.OpenWorkspace("Inspect", a, AssetId.None);
        Check(first is not null && first.Id == "Inspect" && first.Title == $"Inspect {a.Value}" &&
              first.Entity.Value == a.Value && first.Group == viewport?.Group && first.Group?.ActivePanel == first,
            "a workspace opens as a tab beside the Viewport, for its entity", first?.Title ?? "none");
        Check(shell.OpenWorkspace("Inspect", a, AssetId.None) == first, "opened again for it, the same tab");
        // One that wouldn't stay open (an asset the project hasn't) isn't opened at all.
        _logged.Clear();
        Check(shell.OpenWorkspace("Inspect", Entity.Null, AssetId.New()) is null && shell.Workspaces.Count() == 1 &&
              !_logged.Contains("Inspect closed"), "a workspace with nothing to work on doesn't open", Logged());
        yield return 2;
        Check(ViewOf(first)?.Surface.Value != 0, "its view of the scene renders while it's shown");

        // Several tabs where it allows them.
        Workspace? second = shell.OpenWorkspace("Inspect", b, AssetId.None);
        Check(second is not null && second != first && second.Id == "Inspect#2" && shell.Workspaces.Count() == 2,
            "for another entity, a second tab", second?.Id ?? "none");

        // One tab where it doesn't: moved, the old one closed.
        _logged.Clear();
        Workspace? single = shell.OpenWorkspace("Single", a, AssetId.None);
        Workspace? moved = shell.OpenWorkspace("Single", b, AssetId.None);
        Check(single is not null && moved is not null && moved != single && moved.Id == "Single" &&
              shell.Workspaces.Count(workspace => workspace.WorkspaceId == "Single") == 1 && moved.Entity.Value == b.Value &&
              _logged.Contains($"Single closed {a.Value}"),
            "a workspace with one tab moves it to the other entity", Logged());

        // Its keys while it has focus, the editor's elsewhere.
        UIRoot ui = shell.Main.Root;
        first!.Group?.Activate(first);
        yield return 1;
        ViewOf(first)?.Focus();
        _logged.Clear();
        HierarchyChecks.Send(ui, HierarchyChecks.KeyDown(ScancodeK, Keycode.K));
        Check(_logged.Contains("key v1"), "its key runs while it has focus", Logged());
        viewport?.Group?.Activate(viewport);
        viewport?.View.Focus();
        _logged.Clear();
        HierarchyChecks.Send(ui, HierarchyChecks.KeyDown(ScancodeK, Keycode.K));
        Check(!_logged.Contains("key v1"), "and not elsewhere");
        first.Group?.Activate(first);

        // Its toolbar's command, and Done.
        Check(first.Toolbar is not null && shell.Commands.Find("Inspect.Ping") is null, "its commands are its own, not the editor's");

        // Closed when its entity goes.
        _logged.Clear();
        shell.World.Destroy(b);
        yield return 2;
        Check(second is not null && !shell.Workspaces.Contains(second) && moved is not null && !shell.Workspaces.Contains(moved) &&
              _logged.Contains("Inspect closed"), "its entity gone, its tab closes", Logged());

        // Saved with the layout, and back once its entity is.
        first.Columns?.SetSizes([0.3f, 0.7f]);
        JsonObject layout = shell.SaveLayout();
        Check(layout["PanelStates"]?["Inspect"]?["Workspace"]?["Id"]?.GetValue<string>() == "Inspect",
            "the layout keeps its tab and what it works on");
        shell.RestoreLayout(layout);
        Check(shell.Workspaces.All(workspace => workspace.Id != "Inspect"), "restored, it waits a frame for its entity");
        yield return 2;
        Workspace? back = shell.Workspaces.FirstOrDefault(workspace => workspace.Id == "Inspect");
        Check(back is not null && back.Entity.Value == a.Value && back.Columns?.Sizes is [var left, _] && MathF.Abs(left - 0.3f) < 0.01f &&
              _logged.Contains("loaded kept"),
            "and comes back with its splits and its state", Logged());

        // A reload of the code: waits as a placeholder, made again in place.
        // Nothing here may hold the old code's workspaces, or it can't
        // unload: not these, nor this call's temporaries (a debug build keeps
        // them to its end), so the reload is in the next one.
        first = second = single = moved = back = null;
        yield return 1;
        File.WriteAllText(tools, Tools(2));
        _logged.Clear();
        Check(shell.BuildCode(), "the extension rebuilds", Logged());
        yield return 2;
        Workspace? reloaded = shell.Workspaces.FirstOrDefault(workspace => workspace.Id == "Inspect");
        Check(reloaded is not null && reloaded.Entity.Value == a.Value && !_logged.Any(line => line.Contains("didn't unload", StringComparison.Ordinal)),
            "after a reload its tab is made again, the old code unloaded", Logged());
        if (reloaded is not null)
        {
            ViewOf(reloaded)?.Focus();
            _logged.Clear();
            HierarchyChecks.Send(ui, HierarchyChecks.KeyDown(ScancodeK, Keycode.K));
            Check(_logged.Contains("key v2"), "its new version's key", Logged());

            // Done.
            shell.CloseWorkspace(reloaded);
            Check(!shell.Workspaces.Any(), "Done closes it");
        }
        shell.Logged -= _logged.Add;
    }

    private static Entity Create(Shell shell)
    {
        shell.Commands.Find("Hierarchy.CreateEmpty")!.Execute();
        return shell.Selection.Selected;
    }

    private static SceneView? ViewOf(UIElement? element)
    {
        if (element is null)
        {
            return null;
        }
        foreach (UIElement child in element.Children)
        {
            if (child is SceneView view)
            {
                return view;
            }
            if (ViewOf(child) is { } found)
            {
                return found;
            }
        }
        return null;
    }

    private static string Tools(int version) => $$"""
        using Crucible.Editor;
        using Crucible.Platform;
        using Crucible.UI;

        namespace Walker.Tools;

        public sealed class Inspect : Workspace
        {
            private readonly EditorContext _editor;

            public Inspect(EditorContext editor) : base("Inspect")
            {
                _editor = editor;
                Title = $"Inspect {Entity.Value}";
                Arrange(new Label("side"), new SceneView(), null, new Label("bottom"));
                Toolbar.Add(new Command("Inspect.Ping", "Ping", () => editor.Log("ping v{{version}}")));
                Commands.Add(new Command("Inspect.Key", "Key", () => editor.Log("key v{{version}}")) { Shortcut = new Shortcut(KeyModifiers.None, Keycode.K) });
            }

            public override bool AllowsSeveral => true;

            public override void Closed() => _editor.Log("Inspect closed");

            public override void SaveState(PanelState state) => state.Set("Note", "kept");

            public override void LoadState(PanelState state) => _editor.Log("loaded " + state.GetString("Note", ""));
        }

        public sealed class Single : Workspace
        {
            private readonly EditorContext _editor;
            private readonly ulong _entity;

            public Single(EditorContext editor) : base("Single")
            {
                _editor = editor;
                _entity = Entity.Value;
                Arrange(null, new Label("one"));
            }

            public override void Closed() => _editor.Log($"Single closed {_entity}");
        }

        public sealed class Tools : EditorExtension
        {
            public override void Start(EditorContext editor)
            {
                editor.Workspaces.Add("Inspect", "Inspect", () => new Inspect(editor));
                editor.Workspaces.Add("Single", "Single", () => new Single(editor));
            }
        }
        """;

    private string Logged() => string.Join(" | ", _logged);

    private void Check(bool passed, string what, string detail = "")
    {
        ++_checks;
        if (!passed)
        {
            ++_failures;
            Console.WriteLine($"WorkspaceChecks: FAILED: {what}{(detail.Length > 0 ? $" ({detail})" : "")}");
        }
    }
}
