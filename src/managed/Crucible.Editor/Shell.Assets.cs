using Crucible.Assets;
using Crucible.Platform;
using Crucible.UI;

namespace Crucible.Editor;

// The project's assets kept current (ADR 0071): a watcher on the Assets
// folder; once changes settle (or a window regains focus, or Refresh is
// chosen), the database rescans and imports what changed.
internal sealed partial class Shell
{
    private AssetWatcher? _watcher;

    /// <summary>The database rescanned: assets may have come, gone, or been imported again.</summary>
    public event Action? AssetsChanged;

    private void RegisterAssetCommands()
    {
        Commands.Add(new Command("Assets.Refresh", "Refresh", RefreshAssets, () => Database is not null)
        {
            Shortcut = new Shortcut(KeyModifiers.Ctrl, Keycode.R),
        });
        Commands.Add(new Command("Assets.Reimport", "Reimport", ReimportSelected,
            () => _open.GetValueOrDefault("Assets") is AssetsPanel { SelectedAsset: not null }));
    }

    private void WatchAssets(Project project)
    {
        _watcher?.Dispose();
        try
        {
            _watcher = new AssetWatcher(project.AssetsFolder);
        }
        catch (Exception exception) when (exception is IOException or ArgumentException or PlatformNotSupportedException)
        {
            _watcher = null;
            Log($"Can't watch {project.AssetsFolder} for changes ({exception.Message}): use Refresh");
        }
    }

    // Each frame: changes that have settled are imported.
    internal void PollAssets()
    {
        if (_watcher is not null && _watcher.TakeSettled(out _, out _))
        {
            RefreshAssets();
        }
    }

    /// <summary>Rescans the Assets folder and imports what changed.</summary>
    public void RefreshAssets()
    {
        if (Database is not { } database)
        {
            return;
        }
        database.Refresh();
        int imported = 0, failed = 0;
        foreach (ImportResult result in database.ImportAll())
        {
            if (result.Outcome == ImportOutcome.Imported)
            {
                ++imported;
                Log($"Imported {result.Path}");
            }
            else if (result.Outcome is not (ImportOutcome.UpToDate or ImportOutcome.NoImporter))
            {
                ++failed;
                Log($"Can't import {result.Path}: {result.Outcome} {result.Error}");
            }
        }
        // Prefabs are read again; already placed instances keep what they were made from.
        Prefabs = database.Prefabs();
        if (imported > 0 || failed > 0)
        {
            Status($"{imported} imported" + (failed > 0 ? $", {failed} failed" : ""));
        }
        AssetsChanged?.Invoke();
    }

    /// <summary>Imports one asset again, even if it's up to date.</summary>
    public void Reimport(AssetId id)
    {
        if (Database is not { } database || database.Find(id) is null)
        {
            return;
        }
        ImportResult result = database.Import(id, force: true);
        Log(result.Outcome == ImportOutcome.Imported
            ? $"Reimported {result.Path}"
            : $"Can't reimport {result.Path}: {result.Outcome} {result.Error}");
        Prefabs = database.Prefabs();
        AssetsChanged?.Invoke();
    }

    private void ReimportSelected()
    {
        if (_open.GetValueOrDefault("Assets") is AssetsPanel { SelectedAsset: { } asset })
        {
            Reimport(asset.Id);
        }
    }
}
