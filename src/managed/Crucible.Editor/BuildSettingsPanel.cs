using Crucible.Assets;
using Crucible.UI;

namespace Crucible.Editor;

/// <summary>
/// The Build Settings window (ADR 0098, 0100): the project file's "Build"
/// section, edited, in three cards. Scenes (the first starts the game) and
/// Always Include are added from a drop-down of the project's assets or by
/// dropping them from the Assets panel; Build has the ReadyToRun checkbox,
/// where the package goes, and the last build. Every change is saved into
/// the project file at once.
/// </summary>
public sealed class BuildSettingsPanel : EditorPanel
{
    private readonly Shell _shell;
    private readonly StackPanel _scenes = new() { Spacing = 3 };
    private readonly StackPanel _include = new() { Spacing = 3 };
    private readonly Label _where = new() { Wrap = true };
    private readonly Label _lastBuild = new() { Wrap = true };
    private readonly Toggle _readyToRun = new("Precompile (ReadyToRun)");
    private readonly ComboBox _runtime = new() { Options = ["JIT (allows mods)", "NativeAOT (one executable, no mods)"] };
    private readonly ComboBox _configuration = new() { Options = ["Shipping", "Development (every message)"] };
    private bool _refreshing;
    private Project? _shown;

    internal BuildSettingsPanel(Shell shell) : base("BuildSettings", "Build Settings")
    {
        _shell = shell;
        _where.Classes.Add("hint");
        SceneAdder = new AddPicker("Add scene…", SceneChoices);
        IncludeAdder = new AddPicker("Add asset or folder…", IncludeChoices);
        BuildButton = new Button("Build") { HorizontalAlignment = HorizontalAlignment.Start };
        BuildButton.Classes.Add("primary");
        BuildButton.Clicked += _ => _shell.Build();
        _runtime.SelectionChanged += box =>
        {
            if (!_refreshing && box.SelectedIndex >= 0)
            {
                Change(build => build.Runtime = box.SelectedIndex == 1 ? PackageRuntime.NativeAot : PackageRuntime.Jit);
            }
        };
        _configuration.SelectionChanged += box =>
        {
            if (!_refreshing && box.SelectedIndex >= 0)
            {
                Change(build => build.Configuration =
                    box.SelectedIndex == 1 ? PackageConfiguration.Development : PackageConfiguration.Shipping);
            }
        };
        _readyToRun.Changed += toggle =>
        {
            if (!_refreshing)
            {
                Change(build => build.ReadyToRun = toggle.IsOn);
            }
        };

        StackPanel page = new() { Spacing = 10 };
        ScenesCard = new BuildCard("Scenes", "The first one starts the game.", [_scenes, SceneAdder], TakesScene, AddScene);
        IncludeCard = new BuildCard("Always Include", "Assets game code loads by ID, packed whether or not anything names them.",
            [_include, IncludeAdder], payload => IncludePath(payload) is not null, payload => AddInclude(IncludePath(payload)!));
        page.Add(ScenesCard);
        page.Add(IncludeCard);
        page.Add(new BuildCard("Build", "Builds the code, cooks, and packages the game with .NET for this platform.",
            [ChoiceRow("Runtime", _runtime), ChoiceRow("Configuration", _configuration), _readyToRun, _where, _lastBuild,
                BuildButton], null, null));
        AddChild(new ScrollView { Content = page, Padding = new Thickness(8, 8, 8, 8) });
        Refresh();
    }

    /// <summary>Shows the open project's settings again (after another project opens, or a cook).</summary>
    internal void Refresh()
    {
        _shown = _shell.Project;
        _scenes.Clear();
        _include.Clear();
        _lastBuild.Visibility = Visibility.Collapsed;
        if (_shown is not { } project)
        {
            _where.Text = "No project is open.";
            return;
        }
        BuildSettings build = project.Build;
        for (int i = 0; i < build.Scenes.Count; ++i)
        {
            int at = i;
            AssetEntry? scene = _shell.Database?.Find(build.Scenes[i]);
            _scenes.Add(Row($"{i + 1}", scene is null ? build.Scenes[i].ToString() : Path.GetFileNameWithoutExtension(scene.Path),
                scene is null ? null : FolderOf(scene.Path), start: i == 0,
                at > 0 ? () => Change(b => (b.Scenes[at - 1], b.Scenes[at]) = (b.Scenes[at], b.Scenes[at - 1])) : null,
                at + 1 < build.Scenes.Count ? () => Change(b => (b.Scenes[at], b.Scenes[at + 1]) = (b.Scenes[at + 1], b.Scenes[at])) : null,
                () => Change(b => b.Scenes.RemoveAt(at))));
        }
        if (build.Scenes.Count == 0)
        {
            _scenes.Add(Hint("No scenes yet: add one below, or drag one here from the Assets panel."));
        }
        for (int i = 0; i < build.Include.Count; ++i)
        {
            int at = i;
            string path = build.Include[i];
            (string name, string? detail) = IncludeRow(project, path);
            _include.Add(Row(null, name, detail, start: false, null, null, () => Change(b => b.Include.RemoveAt(at))));
        }
        if (build.Include.Count == 0)
        {
            _include.Add(Hint("Nothing yet: add an asset or folder below, or drag one here from the Assets panel."));
        }
        _refreshing = true;
        _readyToRun.IsOn = build.ReadyToRun;
        _runtime.SelectedIndex = build.Runtime == PackageRuntime.NativeAot ? 1 : 0;
        _configuration.SelectedIndex = build.Configuration == PackageConfiguration.Development ? 1 : 0;
        _refreshing = false;
        // ReadyToRun is the JIT's: NativeAOT compiles everything ahead anyway.
        _readyToRun.State = (_readyToRun.State & ~UIState.Disabled) |
                            (build.Runtime == PackageRuntime.Jit ? 0 : UIState.Disabled);
        _where.Text = $"Writes the package to {Path.GetRelativePath(project.Root, project.PackageFolder).Replace('\\', '/')}.";
        if (_shell.LastBuild is { } last)
        {
            int problems = last.Result.Errors.Count;
            _lastBuild.Text = last.Result.Succeeded
                ? $"Last build: {last.Result.Cook!.AssetCount} assets, {Size(last.Result.Size)} in all, at {last.At:HH:mm}"
                : $"Last build failed: {problems} {(problems == 1 ? "problem" : "problems")}, see the Log";
            _lastBuild.Classes.Set("log-error", !last.Result.Succeeded);
            _lastBuild.Visibility = Visibility.Visible;
        }
    }

    /// <summary>Whether it shows another project than the open one.</summary>
    internal bool IsStale => !ReferenceEquals(_shown, _shell.Project);

    // For BuildSettingsChecks.
    internal AddPicker SceneAdder { get; }

    internal AddPicker IncludeAdder { get; }

    internal Button BuildButton { get; }

    internal Toggle ReadyToRunToggle => _readyToRun;

    internal ComboBox RuntimeChoice => _runtime;

    internal ComboBox ConfigurationChoice => _configuration;

    // A label beside its drop-down.
    private static DockPanel ChoiceRow(string name, ComboBox choice)
    {
        DockPanel row = new();
        Label label = new(name) { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        DockPanel.SetDock(label, Dock.Left);
        row.Add(label);
        row.Add(choice);
        return row;
    }

    internal UIElement ScenesCard { get; }

    internal UIElement IncludeCard { get; }

    internal StackPanel SceneRows => _scenes;

    internal StackPanel IncludeRows => _include;

    internal Label LastBuildLine => _lastBuild;

    // The project's scenes not in the list yet.
    private (string Label, Action Pick)[] SceneChoices()
    {
        if (_shell.Database is not { } database || _shell.Project is not { } project)
        {
            return [];
        }
        return [.. database.Assets
            .Where(asset => IsScene(asset.Path) && !project.Build.Scenes.Contains(asset.Id))
            .OrderBy(asset => asset.Path, StringComparer.Ordinal)
            .Select(asset => (asset.Path, (Action)(() => AddScene(new AssetDrag(asset.Id, asset.Path)))))];
    }

    // The project's folders, then its assets, that aren't in the list yet.
    private (string Label, Action Pick)[] IncludeChoices()
    {
        if (_shell.Database is not { } database || _shell.Project is not { } project)
        {
            return [];
        }
        List<(string, Action)> choices = [];
        // The game's folders, then each plugin's, itself included (ADR 0118).
        foreach ((string root, string prefix) in project.AssetRoots().Where(root => Directory.Exists(root.Folder)))
        {
            IEnumerable<string> folders = prefix.Length == 0 ? Folders(root, "") : [prefix, .. Folders(root, prefix)];
            foreach (string folder in folders.Where(folder => !project.Build.Include.Contains(folder)))
            {
                choices.Add((folder.EndsWith(AssetPaths.PluginSeparator) ? folder : folder + "/", () => AddInclude(folder)));
            }
        }
        foreach (AssetEntry asset in database.Assets.Where(asset => !project.Build.Include.Contains(asset.Path))
                     .OrderBy(asset => asset.Path, StringComparer.Ordinal))
        {
            choices.Add((asset.Path, () => AddInclude(asset.Path)));
        }
        return [.. choices];
    }

    private static IEnumerable<string> Folders(string root, string folder)
    {
        string full = Path.Combine(root, AssetPaths.WithinRoot(folder));
        foreach (string directory in Directory.EnumerateDirectories(full).Where(AssetsPanel.Visible).Order(StringComparer.Ordinal))
        {
            string path = AssetPaths.Join(folder, Path.GetFileName(directory));
            yield return path;
            foreach (string inner in Folders(root, path))
            {
                yield return inner;
            }
        }
    }

    private bool TakesScene(object payload) =>
        payload is AssetDrag drag && IsScene(drag.Path) && _shell.Database?.Find(drag.Id) is not null;

    private void AddScene(object payload)
    {
        if (payload is not AssetDrag drag)
        {
            return;
        }
        Change(build =>
        {
            if (!build.Scenes.Contains(drag.Id))
            {
                build.Scenes.Add(drag.Id);
            }
        });
    }

    // What a drop on Always Include adds: an asset's path, or a folder's.
    private string? IncludePath(object payload) => payload switch
    {
        AssetDrag drag when _shell.Database?.Find(drag.Id) is not null => drag.Path,
        FolderDrag folder => folder.Path,
        _ => null,
    };

    private void AddInclude(string path) => Change(build =>
    {
        if (!build.Include.Contains(path))
        {
            build.Include.Add(path);
        }
    });

    // An include entry's name and what it is: a folder and how many assets
    // it covers, an asset and its folder, or neither any more.
    private (string Name, string? Detail) IncludeRow(Project project, string path)
    {
        string name = path.Contains('/', StringComparison.Ordinal) ? path[(path.LastIndexOf('/') + 1)..] : path;
        if (Directory.Exists(project.FileOf(path)))
        {
            string prefix = path.EndsWith(AssetPaths.PluginSeparator) ? path : path + "/";
            int count = _shell.Database?.Assets.Count(asset => asset.Path.StartsWith(prefix, StringComparison.Ordinal)) ?? 0;
            return (name.EndsWith(AssetPaths.PluginSeparator) ? name : name + "/", $"{FolderOf(path)}folder, {count} {(count == 1 ? "asset" : "assets")}");
        }
        return _shell.Database?.Find(path) is not null ? (name, FolderOf(path)) : (name, null);
    }

    // A copy of the settings, changed, saved, and shown.
    private void Change(Action<BuildSettings> change)
    {
        if (_shell.Project is not { } project)
        {
            return;
        }
        BuildSettings build = new()
        {
            Scenes = [.. project.Build.Scenes],
            Include = [.. project.Build.Include],
            ReadyToRun = project.Build.ReadyToRun,
            Runtime = project.Build.Runtime,
            Configuration = project.Build.Configuration,
        };
        change(build);
        project.SaveBuild(build);
        Refresh();
    }

    private static bool IsScene(string path) => path.EndsWith(Crucible.Scenes.Scene.Extension, StringComparison.OrdinalIgnoreCase);

    private static string FolderOf(string path) => path.Contains('/', StringComparison.Ordinal) ? path[..(path.LastIndexOf('/') + 1)] : "";

    private static string Size(long bytes) =>
        bytes < 1024 * 1024 ? $"{Math.Max(bytes / 1024.0, 1):0} KB" : $"{bytes / 1024.0 / 1024.0:0.0} MB";

    private static Label Hint(string text)
    {
        Label label = new(text) { Wrap = true };
        label.Classes.Add("hint");
        return label;
    }

    // A row: its number (scenes), its name bright and its folder muted (or
    // "not found" in red), a Start tag after the first scene's name, and
    // icon buttons.
    private static UIElement Row(string? index, string name, string? detail, bool start, Action? up, Action? down, Action remove)
    {
        DockPanel row = new();
        row.Classes.Add("build-row");
        if (index is not null)
        {
            Label number = new(index) { VerticalAlignment = VerticalAlignment.Center };
            number.Classes.Add("row-index");
            DockPanel.SetDock(number, Dock.Left);
            row.Add(number);
        }
        StackPanel buttons = new() { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        if (index is not null)
        {
            buttons.Add(Icon("arrow-up", "Move Up", up));
            buttons.Add(Icon("arrow-down", "Move Down", down));
        }
        buttons.Add(Icon("x", "Remove", remove));
        DockPanel.SetDock(buttons, Dock.Right);
        row.Add(buttons);
        // Clipped, so a long name in a narrow panel stops short of the buttons.
        StackPanel text = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, ClipToBounds = true };
        text.Add(new Label(name) { VerticalAlignment = VerticalAlignment.Center });
        if (start)
        {
            Label tag = new("Start") { VerticalAlignment = VerticalAlignment.Center };
            tag.Classes.Add("start-tag");
            text.Add(tag);
        }
        Label about = new(detail ?? "not found") { VerticalAlignment = VerticalAlignment.Center };
        about.Classes.Add(detail is null ? "log-error" : "row-detail");
        text.Add(about);
        row.Add(text);
        return row;
    }

    private static IconButton Icon(string shows, string toolTip, Action? clicked)
    {
        IconButton button = new(shows, toolTip);
        if (clicked is null)
        {
            button.State |= UIState.Disabled;
        }
        button.Clicked += _ => clicked?.Invoke();
        return button;
    }

    /// <summary>
    /// One of the window's sections: a title, a muted hint, its content; it
    /// takes the drops <c>takes</c> accepts (outlined while one is over it).
    /// </summary>
    private sealed class BuildCard : UIElement
    {
        private readonly Func<object, bool>? _takes;
        private readonly Action<object>? _drop;

        public BuildCard(string title, string? hint, UIElement[] content, Func<object, bool>? takes, Action<object>? drop)
        {
            _takes = takes;
            _drop = drop;
            AllowDrop = takes is not null;
            StackPanel body = new() { Spacing = 6 };
            Label heading = new(title);
            heading.Classes.Add("card-title");
            body.Add(heading);
            if (hint is not null)
            {
                body.Add(Hint(hint));
            }
            foreach (UIElement element in content)
            {
                body.Add(element);
            }
            AddChild(body);
        }

        protected override void OnDragOver(DragEvent e)
        {
            e.Accepted = _takes?.Invoke(e.Payload) == true;
            Classes.Set("drop-over", e.Accepted);
        }

        protected override void OnDragLeave(DragEvent e) => Classes.Remove("drop-over");

        protected override void OnDrop(DragEvent e)
        {
            Classes.Remove("drop-over");
            _drop?.Invoke(e.Payload);
        }
    }
}

/// <summary>
/// A drop-down that keeps its label ("Add scene…"): its choices are read
/// each time it opens, and picking one runs it.
/// </summary>
internal sealed class AddPicker : ComboBox
{
    private (string Label, Action Pick)[] _choices = [];

    public AddPicker(string text, Func<(string Label, Action Pick)[]> choices)
    {
        ComboBoxArrow arrow = new() { VerticalAlignment = VerticalAlignment.Center };
        DockPanel row = new();
        DockPanel.SetDock(arrow, Dock.Right);
        row.Add(arrow);
        row.Add(new Label(text) { VerticalAlignment = VerticalAlignment.Center });
        Content = row;
        Opening += _ =>
        {
            _choices = choices();
            Options = _choices.Length > 0 ? [.. _choices.Select(choice => choice.Label)] : ["Nothing left to add"];
        };
        SelectionChanged += _ =>
        {
            int picked = SelectedIndex;
            if (picked < 0)
            {
                return;
            }
            SelectedIndex = -1;
            if (picked < _choices.Length)
            {
                _choices[picked].Pick();
            }
        };
    }
}
