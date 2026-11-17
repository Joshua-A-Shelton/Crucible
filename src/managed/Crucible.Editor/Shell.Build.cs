using Crucible.Assets;
using Crucible.UI;

namespace Crucible.Editor;

// Cooking (ADR 0098): the Build menu's Build Settings window and Cook, which
// packs what the build settings' scenes and include list need into
// Game.crpak; headless, the editor's --cook <folder>.
internal sealed partial class Shell
{
    private void RegisterBuildCommands()
    {
        Commands.Add(new Command("Build.Settings", "Build Settings…", ShowBuildSettings, () => Project is not null));
        Commands.Add(new Command("Build.Cook", "Cook", () => Cook(), () => Project is not null));
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

    /// <summary>The last cook this session, and when it ran; the Build Settings window shows it.</summary>
    internal (CookResult Result, DateTime At)? LastCook { get; private set; }

    /// <summary>
    /// Cooks the project into <paramref name="folder"/> (its
    /// <see cref="Project.CookedFolder"/> by default), importing what changed
    /// first, and logs what it did; null without a project.
    /// </summary>
    public CookResult? Cook(string? folder = null)
    {
        if (Database is not { } database)
        {
            Log("Can't cook: no project is open.");
            return null;
        }
        RefreshAssets();
        CookResult result = new Cooker(database).Cook(folder);
        LastCook = (result, DateTime.Now);
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
            Log($"The cook failed ({result.Errors.Count} problems); nothing was written.");
        }
        if (_open.GetValueOrDefault("BuildSettings") is BuildSettingsPanel panel)
        {
            panel.Refresh();
        }
        return result;
    }
}
