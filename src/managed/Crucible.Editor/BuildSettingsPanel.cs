using Crucible.Assets;
using Crucible.UI;

namespace Crucible.Editor;

/// <summary>
/// The Build Settings window (ADR 0098): the project file's "Build" section,
/// edited. The root scenes (the first starts the game), the include list,
/// and Cook. Every change is saved into the project file at once.
/// </summary>
public sealed class BuildSettingsPanel : EditorPanel
{
    private readonly Shell _shell;
    private readonly StackPanel _scenes = new() { Spacing = 2 };
    private readonly StackPanel _include = new() { Spacing = 2 };
    private readonly TextField _path = new();
    private readonly Label _status = new() { Wrap = true };
    private Project? _shown;

    internal BuildSettingsPanel(Shell shell) : base("BuildSettings", "Build Settings")
    {
        _shell = shell;
        Button addScene = new("Add Open Scene");
        addScene.Clicked += _ => AddOpenScene();
        Button addPath = new("Add");
        addPath.Clicked += _ => AddPath();
        _path.Committed += _ => AddPath();
        Button cook = new("Cook");
        cook.Clicked += _ => _shell.Cook();

        StackPanel page = new() { Spacing = 6 };
        page.Add(Heading("Scenes (the first starts the game)"));
        page.Add(_scenes);
        page.Add(addScene);
        page.Add(Heading("Always include"));
        page.Add(new Label("Assets or folders game code loads by ID, by their path in Assets/ (Sounds, Scenes/Menu.crscene).")
        {
            Wrap = true,
        });
        page.Add(_include);
        StackPanel adding = new() { Orientation = Orientation.Horizontal, Spacing = 4 };
        adding.Add(_path);
        adding.Add(addPath);
        page.Add(adding);
        page.Add(Heading("Cook"));
        page.Add(_status);
        page.Add(cook);
        AddChild(new ScrollView { Content = page, Padding = new Thickness(8, 6, 8, 6) });
        Refresh();
    }

    /// <summary>Shows the open project's settings again (after another project opens, or a cook).</summary>
    internal void Refresh()
    {
        _shown = _shell.Project;
        _scenes.Clear();
        _include.Clear();
        if (_shown is not { } project)
        {
            _status.Text = "No project is open.";
            return;
        }
        BuildSettings build = project.Build;
        for (int i = 0; i < build.Scenes.Count; ++i)
        {
            int at = i;
            AssetId scene = build.Scenes[i];
            string name = _shell.Database?.Find(scene)?.Path ?? $"{scene} (not found)";
            _scenes.Add(Row(name, at > 0 ? () => Change(b => (b.Scenes[at - 1], b.Scenes[at]) = (b.Scenes[at], b.Scenes[at - 1])) : null,
                () => Change(b => b.Scenes.RemoveAt(at))));
        }
        if (build.Scenes.Count == 0)
        {
            _scenes.Add(new Label("None: add the open scene."));
        }
        for (int i = 0; i < build.Include.Count; ++i)
        {
            int at = i;
            _include.Add(Row(build.Include[i], null, () => Change(b => b.Include.RemoveAt(at))));
        }
        if (build.Include.Count == 0)
        {
            _include.Add(new Label("Nothing more."));
        }
        _status.Text = $"Writes {Cooker.ArchiveName} to {Path.GetRelativePath(project.Root, project.CookedFolder)}.";
    }

    /// <summary>Whether it shows another project than the open one.</summary>
    internal bool IsStale => !ReferenceEquals(_shown, _shell.Project);

    private void AddOpenScene()
    {
        if (_shell.Database?.Find(_shell.SceneId) is null)
        {
            _shell.Log("Save the scene in the project's Assets first: build settings name scenes by their asset ID.");
            return;
        }
        AssetId scene = _shell.SceneId;
        Change(build =>
        {
            if (!build.Scenes.Contains(scene))
            {
                build.Scenes.Add(scene);
            }
        });
    }

    private void AddPath()
    {
        string path = _path.Text.Trim().Replace('\\', '/').Trim('/');
        if (path.Length == 0)
        {
            return;
        }
        _path.Text = "";
        Change(build =>
        {
            if (!build.Include.Contains(path))
            {
                build.Include.Add(path);
            }
        });
    }

    // A copy of the settings, changed, saved, and shown.
    private void Change(Action<BuildSettings> change)
    {
        if (_shell.Project is not { } project)
        {
            return;
        }
        BuildSettings build = new() { Scenes = [.. project.Build.Scenes], Include = [.. project.Build.Include] };
        change(build);
        project.SaveBuild(build);
        Refresh();
    }

    private static Label Heading(string text)
    {
        Label label = new(text);
        label.Classes.Add("component-title");
        return label;
    }

    private static UIElement Row(string text, Action? up, Action remove)
    {
        DockPanel row = new();
        StackPanel buttons = new() { Orientation = Orientation.Horizontal, Spacing = 4 };
        if (up is not null)
        {
            Button raise = new("Up");
            raise.Clicked += _ => up();
            buttons.Add(raise);
        }
        Button drop = new("Remove");
        drop.Clicked += _ => remove();
        buttons.Add(drop);
        DockPanel.SetDock(buttons, Dock.Right);
        row.Add(buttons);
        row.Add(new Label(text) { VerticalAlignment = VerticalAlignment.Center });
        return row;
    }
}
