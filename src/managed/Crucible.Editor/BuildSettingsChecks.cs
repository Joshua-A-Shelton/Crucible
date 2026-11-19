using System.Numerics;
using Crucible.Assets;
using Crucible.Loop;
using Crucible.UI;
using H = Crucible.Editor.HierarchyChecks;
using V = Crucible.Editor.ViewportChecks;

namespace Crucible.Editor;

/// <summary>
/// The Build Settings window checking itself (ADR 0098, 0100), for CTest's
/// launcher.jit.build-settings: run with the Walker's project and
/// <c>--game-arg check-build-settings=&lt;folder&gt;</c>. On a copy of the
/// project (in the folder): the Level scene's row; a scene added from the
/// drop-down, moved down and up, and removed; a folder dragged from the
/// Assets panel and an asset picked into Always Include; ReadyToRun turned
/// off and on; a build, and its line. The window is saved as
/// build-settings.png.
/// </summary>
[System(Phases.RenderPrepare)]
internal sealed class BuildSettingsChecks : GameSystem
{
    private const int ScancodeEscape = 41;

    private IEnumerator<int>? _script;
    private int _wait;
    private int _checks;
    private int _failures;
    private string _folder = "";

    public override void Start(FrameContext context)
    {
        if (!context.GameArguments.TryGetValue("check-build-settings", out string? folder))
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
            ? $"BuildSettingsChecks: all {_checks} checks passed"
            : $"BuildSettingsChecks: {_failures} of {_checks} checks failed");
        context.QuitRequested = true;
    }

    private IEnumerable<int> Script()
    {
        Shell shell = Shell.Current ?? throw new InvalidOperationException("No editor shell");
        string source = Path.GetDirectoryName(shell.Project?.ProjectFilePath ?? "") ?? "";
        string copy = Path.Combine(Path.GetFullPath(_folder), "build-settings-project");
        if (Directory.Exists(copy))
        {
            Directory.Delete(copy, recursive: true);
        }
        AssetChecks.CopyProject(source, copy);
        string projectFile = Path.Combine(copy, Path.GetFileName(shell.Project!.ProjectFilePath));
        // Known settings, whatever the sources' say: the Level scene alone.
        Project.Open(projectFile).SaveBuild(new BuildSettings { Scenes = [AssetId.Parse("28fe2d80-33ef-4ee4-a409-cc43b935bcc6")] });
        shell.OpenProject(projectFile);
        shell.OpenScene(Path.Combine(copy, "Assets", "Scenes", "Level.crscene"));
        shell.RestoreLayout(null);
        shell.TogglePanel("BuildSettings");
        yield return 3;
        BuildSettingsPanel panel = shell.Main.DockSpace.Panels.OfType<BuildSettingsPanel>().Single();
        UIRoot root = shell.Main.Root;
        AssetDatabase database = shell.Database!;

        // Behind another tab, Build Settings… from the menu brings it back.
        if (panel.Group is { } group && group.Panels.FirstOrDefault(other => other != panel) is { } other)
        {
            group.Activate(other);
            yield return 1;
            shell.Commands.Find("Build.Settings")!.Execute();
            yield return 1;
            Check(group.ActivePanel == panel, "Build Settings… brings the open window to the front",
                group.ActivePanel?.Id ?? "none");
        }
        else
        {
            Check(false, "the window shares its tabs with another panel");
        }

        // The Level scene: its name, its folder, the Start tag.
        Check(Rows(panel.SceneRows) == "1 Level Start Scenes/", "the Level scene's row", Rows(panel.SceneRows));

        // The drop-down lists the other scenes, not Level; picking one adds it
        // and the drop-down keeps its label.
        V.Click(root, V.Center(panel.SceneAdder));
        yield return 2;
        Check(panel.SceneAdder.IsOpen, "a click opens the scene drop-down");
        string offered = string.Join(",", panel.SceneAdder.Options);
        Check(offered.Contains("Scenes/Walk.crscene", StringComparison.Ordinal) && !offered.Contains("Level", StringComparison.Ordinal),
            "it offers the other scenes", offered);
        panel.SceneAdder.SelectedIndex = Array.IndexOf([.. panel.SceneAdder.Options], "Scenes/Walk.crscene");
        panel.SceneAdder.Popup.Close();
        yield return 2;
        Check(Rows(panel.SceneRows) == "1 Level Start Scenes/|2 Walk Scenes/", "picking one adds it", Rows(panel.SceneRows));
        Check(panel.SceneAdder.SelectedIndex == -1 && Texts(panel.SceneAdder) == "Add scene…", "the drop-down keeps its label",
            Texts(panel.SceneAdder));

        // Down on the first moves it below; Up moves it back; Remove.
        V.Click(root, V.Center(Buttons(panel.SceneRows, 0)[1]));
        yield return 2;
        Check(Rows(panel.SceneRows) == "1 Walk Start Scenes/|2 Level Scenes/", "Down moves a scene down, the Start tag stays first",
            Rows(panel.SceneRows));
        V.Click(root, V.Center(Buttons(panel.SceneRows, 1)[0]));
        yield return 2;
        Check(Rows(panel.SceneRows) == "1 Level Start Scenes/|2 Walk Scenes/", "Up moves it back", Rows(panel.SceneRows));
        V.Click(root, V.Center(Buttons(panel.SceneRows, 1)[2]));
        yield return 2;
        Check(Rows(panel.SceneRows) == "1 Level Start Scenes/", "Remove takes it out", Rows(panel.SceneRows));

        // A folder dragged from the Assets panel onto Always Include.
        AssetsPanel assets = shell.Main.DockSpace.Panels.OfType<AssetsPanel>().Single();
        assets.Group?.Activate(assets);
        assets.ShowFolder("");
        yield return 2;
        AssetTile sounds = assets.Tiles.First(tile => tile.Path == "Sounds");
        Vector2 from = V.Center(sounds);
        H.Send(root, V.Motion(from), V.Button(from, true), V.Motion(from + new Vector2(8, 0)));
        yield return 1;
        Check(root.DragPayload is FolderDrag { Path: "Sounds" }, "a folder's tile drags", $"{root.DragPayload}");
        panel.Group?.Activate(panel);
        yield return 2;
        Vector2 card = V.Center(panel.IncludeCard);
        H.Send(root, V.Motion(card), V.Motion(card + new Vector2(4, 0)));
        yield return 1;
        Check(panel.IncludeCard.Classes.Contains("drop-over"), "Always Include outlines while it's over");
        H.Send(root, V.Button(card + new Vector2(4, 0), false));
        yield return 2;
        Check(Rows(panel.IncludeRows) == "Sounds/ folder, 1 asset", "dropping it includes the folder", Rows(panel.IncludeRows));
        Check(!panel.IncludeCard.Classes.Contains("drop-over"), "and the outline goes");

        // The scenes card doesn't take a folder; Escape cancels the drag.
        H.Send(root, V.Motion(from), V.Button(from, true), V.Motion(from + new Vector2(8, 0)), V.Motion(V.Center(panel.ScenesCard)));
        yield return 1;
        Check(!panel.ScenesCard.Classes.Contains("drop-over"), "the scenes card doesn't take a folder");
        H.Send(root, H.KeyDown(ScancodeEscape, 27));
        yield return 1;

        // An asset picked: folders listed first, as folders.
        V.Click(root, V.Center(panel.IncludeAdder));
        yield return 2;
        string[] choices = [.. panel.IncludeAdder.Options];
        Check(choices.Length > 0 && choices[0].EndsWith('/') && !choices.Contains("Sounds/") && choices.Contains("Models/Level.crstates"),
            "the include drop-down offers folders, then assets", string.Join(",", choices));
        panel.IncludeAdder.SelectedIndex = Array.IndexOf(choices, "Models/Level.crstates");
        panel.IncludeAdder.Popup.Close();
        yield return 2;
        Check(Rows(panel.IncludeRows) == "Sounds/ folder, 1 asset|Level.crstates Models/", "picking one includes it",
            Rows(panel.IncludeRows));
        BuildSettings saved = Project.Open(projectFile).Build;
        Check(string.Join(",", saved.Include) == "Sounds,Models/Level.crstates" && saved.Scenes.Count == 1,
            "every change is saved", string.Join(",", saved.Include));

        // ReadyToRun, on by default, turned off and on, saved each time.
        Check(panel.ReadyToRunToggle.IsOn, "ReadyToRun is on by default");
        V.Click(root, V.Center(panel.ReadyToRunToggle));
        yield return 2;
        Check(!Project.Open(projectFile).Build.ReadyToRun && !panel.ReadyToRunToggle.IsOn, "turning ReadyToRun off is saved");
        V.Click(root, V.Center(panel.ReadyToRunToggle));
        yield return 2;
        Check(Project.Open(projectFile).Build.ReadyToRun, "and on again");

        // Build, and its line (ADR 0100).
        V.Click(root, V.Center(panel.BuildButton));
        yield return 2;
        Check(panel.LastBuildLine.Text.StartsWith("Last build: 8 assets, ", StringComparison.Ordinal), "a build shows its line",
            panel.LastBuildLine.Text);
        V.Save(shell.Main.Surface, Path.Combine(_folder, "build-settings.png"));
    }

    // Each row's labels, joined by spaces; rows by '|'.
    private static string Rows(StackPanel rows) => string.Join("|", rows.Children.Select(Texts));

    private static string Texts(UIElement element) =>
        string.Join(" ", H.Descendants(element).OfType<Label>().Select(label => label.Text).Where(text => text.Length > 0));

    private static IconButton[] Buttons(StackPanel rows, int row) => [.. H.Descendants(rows.Children[row]).OfType<IconButton>()];

    private void Check(bool passed, string what, string detail = "")
    {
        ++_checks;
        if (!passed)
        {
            ++_failures;
            Console.WriteLine($"BuildSettingsChecks: FAILED: {what} {detail}");
        }
    }
}
