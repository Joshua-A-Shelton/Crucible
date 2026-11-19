using Crucible.Assets;
using Crucible.UI;

namespace Crucible.Editor;

// Building a package (ADR 0098, 0100): the Build menu's Build Settings window
// and Build, which builds the project's code, cooks what the build settings'
// scenes and include list need into Game.crpak, and packages them with the
// launcher and the .NET runtime into Build/<Platform>; headless, the editor's
// --build <folder>.
internal sealed partial class Shell
{
    private void RegisterBuildCommands()
    {
        Commands.Add(new Command("Build.Settings", "Build Settings…", ShowBuildSettings, () => Project is not null));
        Commands.Add(new Command("Build.Build", "Build", () => Build(), () => Project is not null));
    }

    private void ShowBuildSettings()
    {
        if (!_open.ContainsKey("BuildSettings"))
        {
            TogglePanel("BuildSettings");
        }
        else if (_open["BuildSettings"] is BuildSettingsPanel panel)
        {
            panel.Refresh();
        }
    }

    /// <summary>The last build this session, and when it ran; the Build Settings window shows it.</summary>
    internal (PackageResult Result, DateTime At)? LastBuild { get; private set; }

    /// <summary>
    /// Builds the project's package into <paramref name="folder"/> (its
    /// <see cref="Project.PackageFolder"/> by default), waiting for it, and
    /// logs what it did; null without a project. What a failed step would
    /// have replaced is left as it was.
    /// </summary>
    public PackageResult? Build(string? folder = null)
    {
        if (Project is not { } project || _builder is not { } builder)
        {
            Log("Can't build: no project is open.");
            return null;
        }
        folder = Path.GetFullPath(folder ?? project.PackageFolder);
        Log($"Building {project.Name} into {folder}…");
        PackageResult result = new Packager(project, builder, _native, Environment.ProcessPath ?? "", Cook, Log).Package(folder);
        LastBuild = (result, DateTime.Now);
        if (result.Succeeded)
        {
            Log($"Built {project.Name}: {result.Cook!.AssetCount} assets, {result.Size / 1024.0 / 1024.0:0.0} MiB in all, into {folder}");
        }
        else
        {
            foreach (string error in result.Errors)
            {
                Log($"Can't build: {error}");
            }
            Log($"The build failed ({result.Errors.Count} problems).");
        }
        if (_open.GetValueOrDefault("BuildSettings") is BuildSettingsPanel panel)
        {
            panel.Refresh();
        }
        return result;
    }

    /// <summary>
    /// Cooks the project into <paramref name="folder"/>, importing what
    /// changed first, and logs what it did; null without a project. Build
    /// cooks as one of its steps; the checks cook alone.
    /// </summary>
    internal CookResult? Cook(string folder)
    {
        if (Database is not { } database)
        {
            Log("Can't cook: no project is open.");
            return null;
        }
        RefreshAssets();
        CookResult result = new Cooker(database).Cook(folder);
        if (result.Succeeded)
        {
            Log($"Cooked {result.AssetCount} assets ({result.Size / 1024.0 / 1024.0:0.0} MiB) into {result.ArchivePath}");
        }
        else
        {
            foreach (string error in result.Errors)
            {
                Log($"Can't cook: {error}");
            }
        }
        return result;
    }
}
