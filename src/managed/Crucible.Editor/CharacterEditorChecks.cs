using System.Text.Json.Nodes;
using Crucible.Animation;
using Crucible.Assets;
using Crucible.Ecs;
using Crucible.Loop;
using Crucible.Scenes;
using Crucible.UI;
using H = Crucible.Editor.HierarchyChecks;
using V = Crucible.Editor.ViewportChecks;

namespace Crucible.Editor;

/// <summary>
/// The editor extensions demonstration checking itself (ADR 0128), for
/// CTest's launcher.jit.character-editor: run with the Walker's project and
/// <c>--game-arg check-character-editor=&lt;folder&gt;</c>. On a copy of the
/// project with the Characters plugin: its workspace opened from the
/// Walker's Character, the model in its preview, a clip playing, paused and
/// scrubbed; the stats in the Inspector by their drawer, a change undone;
/// and a package with the plugin's game code and none of its editor code.
/// The window is saved as character-editor.png.
/// </summary>
[System(Phases.RenderPrepare)]
internal sealed class CharacterEditorChecks : GameSystem
{
    private IEnumerator<int>? _script;
    private int _wait;
    private int _checks;
    private int _failures;
    private string _folder = "";
    private readonly List<string> _logged = [];

    public override void Start(FrameContext context)
    {
        if (!context.GameArguments.TryGetValue("check-character-editor", out string? folder))
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
            ? $"CharacterEditorChecks: all {_checks} checks passed"
            : $"CharacterEditorChecks: {_failures} of {_checks} checks failed");
        context.QuitRequested = true;
    }

    private IEnumerable<int> Script()
    {
        Shell shell = Shell.Current ?? throw new InvalidOperationException("No editor shell");
        shell.Logged += _logged.Add;
        // A copy without a local Walker's own editor code and plugins, then
        // the sample's Characters plugin put back.
        string source = Path.GetDirectoryName(AssetChecks.SourceProjectFile(shell)) ?? "";
        string copy = Path.Combine(Path.GetFullPath(_folder), "character-editor-project");
        AssetChecks.OpenCopy(shell, copy, null, withoutEditorCode: true);
        CopyFolder(Path.Combine(source, "Plugins", "Characters"), Path.Combine(copy, "Plugins", "Characters"));
        shell.OpenProject(Path.Combine(copy, Path.GetFileName(AssetChecks.SourceProjectFile(shell))));
        shell.OpenScene(Path.Combine(copy, "Assets", "Scenes", "Walk.crscene"));
        shell.RestoreLayout(null);
        yield return 3;
        Check(shell.Project?.PluginNamed("Characters") is not null && shell.BuildCode(), "the Walker builds with the Characters plugin", Logged());
        yield return 3;

        // Its workspace, from the Walker's Character.
        World world = shell.World;
        Entity walker = SceneIds.Of(world).Entities.FirstOrDefault(entity =>
            shell.LabelOf(entity) == "Walker" && world.Has(entity, Components.Of<Crucible.Animation.Animator>()));
        Check(!walker.IsNull && world.Has(walker, Components.Of<Crucible.Animation.Animator>()), "the scene's Walker has its Character");
        Check(shell.EditorsFor(walker, "Characters.Character").Any(editor => editor.Id == "Characters.Character"),
            "the Character opens in its workspace");
        Check(shell.OpenEntity(walker, "Characters.Character"), "and opens");
        yield return 3;
        Workspace? workspace = shell.Workspaces.FirstOrDefault(open => open.WorkspaceId == "Characters.Character");
        Check(workspace?.Title == "Character: Walker", "a tab named after it", workspace?.Title ?? "none");
        PreviewView? preview = workspace is null ? null : H.Descendants(workspace).OfType<PreviewView>().FirstOrDefault();
        Check(preview is not null, "with the model in a preview of its own");
        if (workspace is null || preview is null)
        {
            yield break;
        }
        Entity model = Animated(preview.World);
        long started = Environment.TickCount64;
        while (Environment.TickCount64 - started < 15000 && Animator.TimeOf(preview.World, model, -1) <= 0)
        {
            yield return 1;
            model = Animated(preview.World);
        }
        Check(!model.IsNull && Animator.TimeOf(preview.World, model, -1) > 0, "its first clip plays");
        yield return 3;
        V.Save(shell.Main.Surface, Path.Combine(_folder, "character-editor.png"));

        // Paused, then scrubbed.
        Toggle? pause = H.Descendants(workspace).OfType<Toggle>().FirstOrDefault(toggle => toggle.ToolTip.StartsWith("Pause", StringComparison.Ordinal));
        Slider? scrub = H.Descendants(workspace).OfType<Slider>().FirstOrDefault();
        if (pause is not null)
        {
            V.Click(shell.Main.Root, V.Center(pause));
        }
        yield return 2;
        float held = Animator.TimeOf(preview.World, model, -1);
        yield return 5;
        Check(!preview.IsRunning && Animator.TimeOf(preview.World, model, -1) == held, "Pause holds it");
        if (scrub is not null)
        {
            scrub.Value = 0;
            yield return 1;
            scrub.Value = 0.5f;
        }
        yield return 2;
        float half = Animator.TimeOf(preview.World, model, -1);
        Check(scrub is not null && half > 0.05f && MathF.Abs(half - held) > 1e-3f, "the slider scrubs it", $"{held} then {half}");

        // The stats, by their drawer, changed and undone.
        shell.Selection.Selected = walker;
        yield return 3;
        InspectorPanel? inspector = shell.Windows.SelectMany(window => window.DockSpace.Panels).OfType<InspectorPanel>().FirstOrDefault();
        ComponentSection? stats = inspector?.Sections.FirstOrDefault(section => section.Component == "Characters.Character");
        Check(stats is { HasDrawer: true } && Labels(stats).Contains("Power 30, moving at 1x"), "the stats are drawn by their drawer",
            stats is null ? "" : string.Join(" | ", Labels(stats)));
        if (stats is not null)
        {
            stats.Edit(["Strength"], JsonValue.Create(40), "Edit Strength");
            yield return 2;
            Check(Labels(stats).Contains("Power 90, moving at 1x"), "a change shows at once", string.Join(" | ", Labels(stats)));
            shell.Undo();
            yield return 2;
            Check(Labels(stats).Contains("Power 30, moving at 1x"), "and undoes");
        }

        // A package: the plugin's game code, none of its editor code.
        shell.CloseWorkspace(workspace);
        workspace = null;
        preview = null;
        Project project = shell.Project!;
        project.Build.Configuration = PackageConfiguration.Development;
        project.Build.Runtime = PackageRuntime.Jit;
        project.Build.ReadyToRun = false;
        string package = Path.Combine(Path.GetFullPath(_folder), "character-editor-package");
        PackageResult? result = shell.Build(package);
        Check(result is { Succeeded: true }, "the Walker packages", string.Join(" | ", result?.Errors ?? []));
        string data = Path.Combine(package, "Data");
        Check(File.Exists(Path.Combine(data, "Characters.dll")) && !File.Exists(Path.Combine(data, "Characters.Editor.dll")),
            "the package has the plugin's game code and none of its editor code",
            string.Join(", ", Directory.Exists(data) ? Directory.EnumerateFiles(data, "*.dll").Select(Path.GetFileName) : []));
        shell.Logged -= _logged.Add;
    }

    // The preview's animated model: the entity with an Animator.
    private static Entity Animated(World world)
    {
        using Query query = world.CreateQuery([Components.Of<Animator>()]);
        Collector collector = new() { Found = [] };
        query.ForEachChunk(ref collector);
        return collector.Found.FirstOrDefault();
    }

    private struct Collector : IChunkJob
    {
        public List<Entity> Found;

        public readonly void Execute(in ChunkView chunk) => Found.AddRange(chunk.Entities);
    }

    private static List<string> Labels(UIElement element) => [.. H.Descendants(element).OfType<Label>().Select(label => label.Text)];

    private static void CopyFolder(string from, string to)
    {
        foreach (string file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            string target = Path.Combine(to, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    private string Logged() => string.Join(" | ", _logged);

    private void Check(bool passed, string what, string detail = "")
    {
        ++_checks;
        if (!passed)
        {
            ++_failures;
            Console.WriteLine($"CharacterEditorChecks: FAILED: {what}{(detail.Length > 0 ? $" ({detail})" : "")}");
        }
    }
}
