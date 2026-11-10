using System.Numerics;
using Crucible.Ecs;
using Crucible.Loop;
using Crucible.Platform;
using Crucible.Scenes;
using Crucible.Serialization;
using Crucible.UI;
using H = Crucible.Editor.HierarchyChecks;
using V = Crucible.Editor.ViewportChecks;

namespace Crucible.Editor;

/// <summary>
/// Play mode and the project's code checking themselves headless (ADR 0072),
/// for CTest's launcher.jit.play: run with the Diorama's project,
/// <c>--game-arg check-play=&lt;file.png&gt;</c>, and a settings file of its own.
/// On a copy of the project (beside the capture): its code is built and its
/// Spin component known before the village opens; Play turns the hero and
/// shows the Game panel with the input; edits while playing are undone by
/// Stop; a file added to Code is built and hot-reloaded; a build error is
/// reported with its file and line and the last build keeps running.
/// </summary>
[System(Phases.RenderPrepare)]
internal sealed class PlayChecks : GameSystem
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
        if (!context.GameArguments.TryGetValue("check-play", out string? capture))
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
            ? $"PlayChecks: all {_checks} checks passed"
            : $"PlayChecks: {_failures} of {_checks} checks failed");
        context.QuitRequested = true;
    }

    private IEnumerable<int> Script()
    {
        Shell shell = Shell.Current ?? throw new InvalidOperationException("No editor shell");
        string source = Path.GetDirectoryName(shell.Project?.ProjectFilePath ?? "") ?? "";
        string copy = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(_capture))!, "play-project");
        if (Directory.Exists(copy))
        {
            Directory.Delete(copy, recursive: true);
        }
        AssetChecks.CopyProject(source, copy);
        string code = Path.Combine(copy, "Code");

        // Opening the project builds its code: the game's components are known before the scene.
        shell.OpenProject(Path.Combine(copy, Path.GetFileName(shell.Project!.ProjectFilePath)));
        Check(shell.Game is not null, "opening the project builds and loads its code", LogText(shell));
        SavedComponent? spin = SavedComponents.Find("Diorama.Spin");
        Check(spin is not null, "the game's components are registered");
        shell.OpenScene(Path.Combine(copy, "Assets", "Scenes", "Village.crscene"));
        shell.RestoreLayout(null);
        yield return 3;
        World world = _context!.World;
        Entity hero = spin is null ? Entity.Null
            : SceneIds.Of(world).Entities.FirstOrDefault(entity => world.Has(entity, spin.Id));
        Check(!hero.IsNull, "the village's hero has its Spin");
        if (hero.IsNull)
        {
            yield break;
        }
        ulong heroId = SceneIds.IdOf(world, hero);
        Quaternion before = world.Read<LocalTransform>(hero).Rotation;
        int entities = world.EntityCount, steps = shell.History.UndoCount;

        // Play: the hero turns; the Game panel shows, with the input.
        shell.Commands.Find("Play.Toggle")!.Execute();
        yield return 10;
        Check(shell.IsPlaying && shell.Main.TitleBar.Title.Contains("Playing"), "Play starts playing", shell.Main.TitleBar.Title);
        Quaternion turning = world.Read<LocalTransform>(hero).Rotation;
        Check(MathF.Abs(Quaternion.Dot(turning, before)) < 0.9999f, "the game's system turns the hero");
        GamePanel? game = shell.Main.DockSpace.Panels.OfType<GamePanel>().FirstOrDefault();
        Check(game is not null && game.Group?.ActivePanel == game && game.View.HasInput, "the Game panel shows, with the input");
        yield return 4;
        V.Save(shell.Main.Surface, _capture);
        if (game is not null)
        {
            H.Send(shell.Main.Root, H.KeyDown(ScancodeEscape, Keycode.Escape));
            Check(!game.View.HasInput, "Escape gives the input back");
        }

        // Edits while playing don't outlast Stop.
        shell.CreateEmpty(Entity.Null);
        shell.SetSelectedTransform(Matrix4x4.CreateTranslation(50, 0, 0));
        yield return 2;
        shell.Commands.Find("Play.Toggle")!.Execute();
        yield return 3;
        Entity back = SceneIds.Find(world, heroId);
        Check(!shell.IsPlaying && !shell.Main.TitleBar.Title.Contains("Playing"), "Stop stops");
        Check(!back.IsNull && Quaternion.Dot(world.Read<LocalTransform>(back).Rotation, before) > 0.99999f,
            "Stop puts the hero back");
        Check(world.EntityCount == entities, "what was made while playing is gone", $"{entities} then {world.EntityCount}");
        Check(shell.History.UndoCount == steps, "and its undo steps with it");

        // A file added to Code: built and hot-reloaded, with the inspector
        // showing the hero's Spin and a number typed into it, not yet
        // entered. Only the old build's identity is kept here: holding it
        // (or its Spin, or the inspector's field) would keep it from
        // unloading.
        shell.Selection.Selected = SceneIds.Find(world, heroId);
        spin = null;
        yield return 2;
        if (TypeInto(shell, "PerFrame", "0.25") is { } typing)
        {
            Check(false, "the inspector shows the hero's Spin", typing);
        }
        yield return 2;
        int first = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(shell.Game);
        File.WriteAllText(Path.Combine(code, "Marker.cs"), """
            using Crucible.Ecs;
            using Crucible.Serialization;

            namespace Diorama;

            [Component, Serialized]
            public partial struct Marker
            {
                public int Value;
            }
            """);
        long started = Environment.TickCount64;
        while (System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(shell.Game) == first &&
               Environment.TickCount64 - started < 60000)
        {
            yield return 2;
        }
        Check(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(shell.Game) != first &&
              SavedComponents.Find("Diorama.Marker") is not null,
            "a file added to Code is built and hot-reloaded", $"after {Environment.TickCount64 - started} ms; {LogText(shell)}");
        Check(SavedComponents.Find("Diorama.Spin") is { } spun && world.Has(SceneIds.Find(world, heroId), spun.Id),
            "the hero keeps its Spin across the reload");
        string all = string.Join(" | ", H.Descendants(shell.LogPanel!).OfType<Label>().Select(label => label.Text));
        Check(!all.Contains("didn't unload", StringComparison.Ordinal),
            "and the old build unloads, a field of its Spin being edited", LogText(shell));
        Check(SceneIds.Read(world, SceneIds.Find(world, heroId), "Diorama.Spin")?["PerFrame"]?.GetValue<float>() == 0.25f,
            "the edit is entered before the reload");

        // A build error: reported with its file, line, and column; the last build keeps running.
        Hosting.LoadedGame? good = shell.Game;
        int lines = shell.LogPanel?.LineCount ?? 0;
        File.WriteAllText(Path.Combine(code, "Broken.cs"), "namespace Diorama;\npublic class Broken { int x = ; }\n");
        started = Environment.TickCount64;
        while (!LogText(shell).Contains("Build error:", StringComparison.Ordinal) && Environment.TickCount64 - started < 60000)
        {
            yield return 2;
        }
        string log = LogText(shell);
        Check(log.Contains("Broken.cs(2,31)", StringComparison.Ordinal), "a build error names its file, line, and column", log);
        Check(shell.Game == good, "the last good build stays loaded");
        File.Delete(Path.Combine(code, "Broken.cs"));
        File.Delete(Path.Combine(code, "Marker.cs"));
        _ = lines;

        // Assets the village draws, changed and imported again: reloaded on
        // the GPU in place (6b).
        string assets = Path.Combine(copy, "Assets");
        File.Copy(Path.Combine(assets, "Textures", "Leaves.png"), Path.Combine(assets, "Textures", "Grass.png"), overwrite: true);
        File.AppendAllText(Path.Combine(assets, "Models", "Hero.gltf"), "\n");
        shell.RefreshAssets();
        yield return 3;
        string reloads = string.Join(" | ", H.Descendants(shell.LogPanel!).OfType<Label>().Select(label => label.Text));
        Check(reloads.Contains("Grass.png on the GPU", StringComparison.Ordinal), "a changed texture reloads on the GPU",
            LogText(shell));
        Check(reloads.Contains("Hero.gltf on the GPU", StringComparison.Ordinal), "and a changed model", LogText(shell));
        Check(!reloads.Contains("Can't reload", StringComparison.Ordinal), "without failing", LogText(shell));
        Check(world.IsAlive(SceneIds.Find(world, heroId)), "the hero is still there");
    }

    // Focuses a number field of the selected entity's components and types
    // into it, without Enter; null, or what's missing.
    private static string? TypeInto(Shell shell, string field, string text)
    {
        InspectorPanel? inspector = shell.Main.DockSpace.Panels.OfType<InspectorPanel>().FirstOrDefault();
        NumberField? box = inspector?.Sections.SelectMany(section => section.Editors)
            .Where(editor => editor.Field.Name == field)
            .SelectMany(editor => editor.Controls.SelectMany(control => H.Descendants(control).Prepend(control)))
            .OfType<NumberField>().FirstOrDefault();
        if (box is null)
        {
            return $"no {field} field";
        }
        box.Focus();
        box.SelectAll();
        H.Type(shell.Main.Root, text);
        return null;
    }

    private static string LogText(Shell shell) =>
        shell.LogPanel is { } log
            ? string.Join(" | ", H.Descendants(log).OfType<Label>().Select(label => label.Text).TakeLast(6))
            : "";

    private void Check(bool passed, string what, string detail = "")
    {
        ++_checks;
        if (!passed)
        {
            ++_failures;
            Console.WriteLine($"PlayChecks: FAILED: {what} {detail}");
        }
    }
}
