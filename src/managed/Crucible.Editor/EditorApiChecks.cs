using System.Text.Json.Nodes;
using Crucible.Assets;
using Crucible.Ecs;
using Crucible.Loop;
using Crucible.Scenes;
using Crucible.UI;

namespace Crucible.Editor;

/// <summary>
/// The public editor API checking itself (ADR 0117), for CTest's
/// launcher.jit.editor-api: run with the Walker's project and
/// <c>--game-arg check-editor-api=&lt;folder&gt;</c>. On a copy of the
/// project, an extension in Code/Editor, changed version by version: its
/// panel, commands, and menus; a component edit, a file write, and a step
/// of its own, each undoable; its panel's state across reloads and in the
/// layout; and nothing of an old version left to keep it loaded.
/// </summary>
[System(Phases.RenderPrepare)]
internal sealed class EditorApiChecks : GameSystem
{
    private IEnumerator<int>? _script;
    private int _wait;
    private int _checks;
    private int _failures;
    private string _folder = "";
    private readonly List<string> _logged = [];

    public override void Start(FrameContext context)
    {
        if (!context.GameArguments.TryGetValue("check-editor-api", out string? folder))
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
            ? $"EditorApiChecks: all {_checks} checks passed"
            : $"EditorApiChecks: {_failures} of {_checks} checks failed");
        context.QuitRequested = true;
    }

    private IEnumerable<int> Script()
    {
        Shell shell = Shell.Current ?? throw new InvalidOperationException("No editor shell");
        shell.Logged += _logged.Add;
        string copy = Path.Combine(Path.GetFullPath(_folder), "editor-api-project");
        AssetChecks.OpenCopy(shell, copy, Path.Combine("Assets", "Scenes", "Walk.crscene"), withoutEditorCode: true);
        shell.RestoreLayout(null);
        yield return 3;
        Project project = shell.Project!;
        string editorFolder = Path.Combine(project.CodeFolder, "Editor");
        Directory.CreateDirectory(editorFolder);
        string toolsFile = Path.Combine(editorFolder, "WalkerTools.cs");
        MenuBar bar = shell.Main.MenuBar;
        Menu edit = bar.Menus.First(menu => menu.Title == "Edit");
        Menu window = bar.Menus.First(menu => menu.Title == "Window");
        int editItems = edit.Items.Count, windowItems = window.Items.Count, menus = bar.Menus.Count;

        // Started: its panel, commands, and menus.
        File.WriteAllText(toolsFile, Tools(1, panel: true));
        _logged.Clear();
        Check(shell.BuildCode(), "the project builds with an extension", Logged());
        Check(shell.Extensions.Count() == 1, "the extension starts");
        Menu? tools = bar.Menus.FirstOrDefault(menu => menu.Title == "Tools");
        Check(tools is not null && IndexOf(bar, tools) == IndexOf(bar, window) - 1, "a Tools menu, before Window");
        Menu? walker = tools?.Items.OfType<MenuItem>().FirstOrDefault(item => item.Title == "Walker")?.Submenu;
        Check(walker?.Items.Count == 6, "its commands under Tools > Walker", $"{walker?.Items.Count}");
        Check(edit.Items.Count == editItems + 2 && edit.Items[^2] is MenuSeparator && Title(edit.Items[^1]) == "Raise",
            "Raise at the end of Edit, after a separator");
        Check(window.Items.Count == windowItems + 2 && Title(window.Items[^3]) == "Walker Stats" && window.Items[^4] is MenuSeparator,
            "Walker Stats in Window, before Reset Layout");
        Check(_logged.Contains("Walker.Save wants Ctrl+S, already used by File.SaveScene; it has no shortcut") &&
              shell.Commands.Find("Walker.Save")?.Shortcut is null,
            "a shortcut in use is left off, with a warning", Logged());

        // Its panel, opened and docked like the editor's own.
        Check(shell.OpenPanel("Walker.Stats"), "its panel opens");
        // Only weakly held here: a strong reference would keep the old code loaded.
        (DockGroup? group, int tab, WeakReference stats) = Locate(shell, "Walker.Stats");
        Check(group is not null && tab >= 0, "docked");
        Run(shell, "Walker.Click");
        Run(shell, "Walker.Click");
        _logged.Clear();
        Run(shell, "Walker.Boom");
        Check(_logged.Contains("Walker.Tools.WalkerTools: Walker.Boom failed: InvalidOperationException: boom"),
            "a command that throws is logged, and the editor goes on", Logged());

        // A component edit, as an undo step.
        Entity entity = shell.CreateEmpty(Entity.Null);
        shell.Selection.Selected = entity;
        Run(shell, "Walker.Raise");
        Check(shell.History.NextUndo?.Title == "Raise" && Y(shell, entity) == 1, "a component edit, one undo step");
        shell.Undo();
        Check(Y(shell, entity) == 0, "undone");
        shell.Redo();
        Check(Y(shell, entity) == 1, "and redone");

        // A file written, as an undo step.
        string note = Path.Combine(project.AssetsFolder, "Notes", "Extension.txt");
        Run(shell, "Walker.Note");
        Check(File.Exists(note) && File.ReadAllText(note) == "v1" && shell.History.NextUndo?.Title == "Write Note",
            "a file written, one undo step");
        shell.Undo();
        Check(!File.Exists(note), "undone, the new file goes");
        shell.Redo();
        Check(File.Exists(note), "and comes back");
        _logged.Clear();
        Run(shell, "Walker.Escape");
        Check(_logged.Any(line => line.StartsWith("Can't write ../Outside.txt", StringComparison.Ordinal)) &&
              !File.Exists(Path.Combine(project.Root, "Outside.txt")),
            "a path out of the Assets folder is refused", Logged());

        // A step of its own: dropped with its code.
        Run(shell, "Walker.Step");
        Check(shell.History.NextUndo?.Title == "Extension Step", "a step of its own");
        yield return 2;

        // Reloaded: everything again, once, and the panel in its place with its state.
        WeakReference first = EditorAssembly(shell);
        File.WriteAllText(toolsFile, Tools(2, panel: true));
        _logged.Clear();
        Check(shell.BuildCode(), "a changed extension builds", Logged());
        Check(!_logged.Any(line => line.Contains("didn't unload", StringComparison.Ordinal)) && Collected(first),
            "the old extension unloads", Logged());
        Check(_logged.Any(line => line.StartsWith("Undo can't go back past \"Extension Step\": its step came from the editor code just reloaded (", StringComparison.Ordinal)),
            "its step dropped, and the steps before it", Logged());
        Check(shell.History.UndoCount == 0, "nothing left to undo past it");
        // The old submenu went with the old version; the new one is made again.
        walker = bar.Menus.FirstOrDefault(menu => menu.Title == "Tools")?.Items.OfType<MenuItem>()
            .FirstOrDefault(item => item.Title == "Walker")?.Submenu;
        Check(shell.Extensions.Count() == 1 && walker?.Items.Count == 6 && window.Items.Count == windowItems + 2 &&
              edit.Items.Count == editItems + 2 && bar.Menus.Count == menus + 1,
            "its commands and menus once, not twice");
        Check(!stats.IsAlive && Locate(shell, "Walker.Stats") is ({ } again, int at, { IsAlive: true }) && again == group && at == tab,
            "its panel made again in place");
        _logged.Clear();
        Run(shell, "Walker.Report");
        Check(_logged.Contains("v2 clicks 2"), "with its state", Logged());
        shell.Selection.Selected = Entity.Null;
        Check(_logged.Contains("v2 selection") && !_logged.Contains("v1 selection"), "the old version's handlers gone", Logged());

        // The layout keeps its state, and a placeholder until its code is there.
        JsonObject layout = shell.SaveLayout();
        Check(layout["PanelStates"]?["Walker.Stats"]?["Values"]?["Clicks"]?.GetValue<double>() == 2 &&
              layout["PanelStates"]?["Walker.Stats"]?["Extension"]?.GetValue<bool>() == true,
            "the layout saves its state", layout["PanelStates"]?.ToJsonString());
        File.WriteAllText(toolsFile, Tools(3, panel: false));
        Check(shell.BuildCode(), "a version without the panel builds");
        Check(OpenPanel(shell, "Walker.Stats") is null && window.Items.Count == windowItems,
            "its panel closes, and leaves the Window menu as it was");
        shell.RestoreLayout(layout);
        Check(OpenPanel(shell, "Walker.Stats") is PendingPanel, "restored without its code: a placeholder");
        File.WriteAllText(toolsFile, Tools(4, panel: true));
        Check(shell.BuildCode(), "the panel back");
        _logged.Clear();
        Run(shell, "Walker.Report");
        Check(OpenPanel(shell, "Walker.Stats") is { } back && back is not PendingPanel && _logged.Contains("v4 clicks 2"),
            "made from the placeholder, with the layout's state", Logged());

        // Gone: nothing of it left.
        File.Delete(toolsFile);
        Check(shell.BuildCode(), "the project builds without its editor code");
        Check(!shell.Extensions.Any() && OpenPanel(shell, "Walker.Stats") is null && bar.Menus.Count == menus &&
              edit.Items.Count == editItems && window.Items.Count == windowItems && shell.Commands.Find("Walker.Click") is null,
            "the extension, its panel, commands, and menus gone");
        shell.Logged -= _logged.Add;
    }

    // Version `version` of the extension; with or without its panel.
    private static string Tools(int version, bool panel) => $$"""
        using Crucible.Ecs;
        using Crucible.Editor;
        using Crucible.Platform;
        using Crucible.Scenes;
        using Crucible.UI;

        namespace Walker.Tools;

        public sealed class StatsPanel : EditorPanel
        {
            public static StatsPanel? Shown;
            public int Clicks;

            public StatsPanel() : base("Walker.Stats", "Walker Stats") => Shown = this;

            public override void SaveState(PanelState state) => state.Set("Clicks", Clicks);

            public override void LoadState(PanelState state) => Clicks = (int)state.GetNumber("Clicks");
        }

        public sealed class ExtensionStep() : UndoStep("Extension Step")
        {
            public override bool ChangesScene => false;
            public override void Undo(World world) { }
            public override void Redo(World world) { }
        }

        public sealed class WalkerTools : EditorExtension
        {
            public override void Start(EditorContext editor)
            {
                {{(panel ? "editor.Panels.Add(\"Walker.Stats\", \"Walker Stats\", () => new StatsPanel());" : "")}}
                editor.Commands.Add(new Command("Walker.Click", "Click", () => { if (StatsPanel.Shown is { } shown) { shown.Clicks++; } }), "Tools/Walker");
                editor.Commands.Add(new Command("Walker.Report", "Report", () => editor.Log($"v{{version}} clicks {StatsPanel.Shown?.Clicks}")), "Tools/Walker");
                editor.Commands.Add(new Command("Walker.Save", "Save", () => { }) { Shortcut = new Shortcut(KeyModifiers.Ctrl, Keycode.S) }, "Tools/Walker");
                editor.Commands.Add(new Command("Walker.Note", "Note", () => editor.Assets.WriteText("Notes/Extension.txt", "v{{version}}", "Write Note")), "Tools/Walker");
                editor.Commands.Add(new Command("Walker.Step", "Step", () => editor.Undo.Push(new ExtensionStep())), "Tools/Walker");
                editor.Commands.Add(new Command("Walker.Boom", "Boom", () => throw new System.InvalidOperationException("boom")), "Tools/Walker");
                editor.Commands.Add(new Command("Walker.Escape", "Escape", () => editor.Assets.WriteText("../Outside.txt", "out", "Escape")), null);
                editor.Commands.Add(new Command("Walker.Raise", "Raise", () =>
                {
                    Entity selected = editor.Selection.Selected;
                    if (editor.Scene.World is { } world && !selected.IsNull)
                    {
                        LocalTransform transform = world.Read<LocalTransform>(selected);
                        transform.Position.Y += 1;
                        editor.Scene.Edit(selected, transform, "Raise");
                    }
                }), "Edit");
                editor.Selection.Changed += _ => editor.Log("v{{version}} selection");
            }
        }
        """;

    private static void Run(Shell shell, string command) => shell.Commands.Find(command)?.Execute();

    private string Logged() => string.Join(" | ", _logged);

    private static string? Title(UIElement item) => (item as MenuItem)?.Title;

    private static float Y(Shell shell, Entity entity) => shell.World.Read<LocalTransform>(entity).Position.Y;

    private static EditorPanel? OpenPanel(Shell shell, string id) =>
        shell.Windows.SelectMany(window => window.DockSpace.Panels).FirstOrDefault(panel => panel.Id == id);

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static (DockGroup? Group, int Tab, WeakReference Panel) Locate(Shell shell, string id)
    {
        EditorPanel? panel = OpenPanel(shell, id);
        return (panel?.Group, panel?.Group is { } group ? IndexOf(group, panel) : -1, new WeakReference(panel));
    }

    private static int IndexOf(MenuBar bar, Menu menu)
    {
        for (int i = 0; i < bar.Menus.Count; ++i)
        {
            if (bar.Menus[i] == menu)
            {
                return i;
            }
        }
        return -1;
    }

    private static int IndexOf(DockGroup group, EditorPanel panel)
    {
        for (int i = 0; i < group.Panels.Count; ++i)
        {
            if (group.Panels[i] == panel)
            {
                return i;
            }
        }
        return -1;
    }

    // Read in a method of its own, so no local keeps the assembly.
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static WeakReference EditorAssembly(Shell shell) => new(shell.Game?.EditorAssembly);

    private static bool Collected(WeakReference reference)
    {
        for (int i = 0; i < 10 && reference.IsAlive; ++i)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
        return !reference.IsAlive;
    }

    private void Check(bool condition, string what, string? detail = null)
    {
        ++_checks;
        if (!condition)
        {
            ++_failures;
        }
        Console.WriteLine($"EditorApiChecks: {(condition ? "ok" : "FAILED")}: {what}{(condition || detail is null ? "" : $" ({detail})")}");
    }
}
