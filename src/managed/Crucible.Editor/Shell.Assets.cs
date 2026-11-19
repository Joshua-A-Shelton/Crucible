using System.Text.Json.Nodes;
using Crucible.Assets;
using Crucible.Assets.Importers;
using Crucible.Platform;
using Crucible.Render;
using Crucible.UI;

namespace Crucible.Editor;

// The project's assets kept current (ADR 0071): a watcher on the Assets
// folder; once changes settle (or a window regains focus, or Refresh is
// chosen), the database rescans and imports what changed; what's loaded on
// the GPU is reloaded in place (ADR 0072).
internal sealed partial class Shell
{
    private AssetWatcher? _watcher;
    // Each failing asset's last error, so a rescan repeats it only when it changes.
    private readonly Dictionary<string, string> _failures = new(StringComparer.Ordinal);

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
        List<ImportResult> results = [.. database.ImportAll()];
        foreach (ImportResult result in results)
        {
            if (result.Outcome == ImportOutcome.Imported)
            {
                ++imported;
                _failures.Remove(result.Path);
                Log($"Imported {result.Path}");
            }
            else if (result.Outcome is not (ImportOutcome.UpToDate or ImportOutcome.NoImporter))
            {
                string error = $"{result.Outcome} {result.Error}";
                if (_failures.GetValueOrDefault(result.Path) != error)
                {
                    ++failed;
                    _failures[result.Path] = error;
                    Log($"Can't import {result.Path}: {error}");
                }
            }
            else
            {
                _failures.Remove(result.Path);
            }
        }
        ReloadOnGpu(results);
        // Prefabs are read again; already placed instances keep what they were made from.
        Prefabs = database.Prefabs();
        if (imported > 0 || failed > 0)
        {
            Status($"{imported} imported" + (failed > 0 ? $", {failed} failed" : ""));
        }
        AssetsChanged?.Invoke();
    }

    /// <summary>Imports one asset again, even if it's up to date; quietly, only a failure is logged.</summary>
    public void Reimport(AssetId id, bool quiet = false)
    {
        if (Database is not { } database || database.Find(id) is null)
        {
            return;
        }
        ImportResult result = database.Import(id, force: true);
        if (result.Outcome != ImportOutcome.Imported)
        {
            Log($"Can't reimport {result.Path}: {result.Outcome} {result.Error}");
        }
        else if (!quiet)
        {
            Log($"Reimported {result.Path}");
        }
        ReloadOnGpu([result]);
        Prefabs = database.Prefabs();
        AssetsChanged?.Invoke();
    }

    /// <summary>
    /// A new chain (ADR 0095) in a folder of the Assets folder: "New
    /// Chain.crchain" (numbered if taken), two points of ground solid below,
    /// imported at once. Returns its path in the Assets folder; null if it
    /// can't be made.
    /// </summary>
    public string? CreateChain(string folder)
    {
        if (Project is not { } project)
        {
            return null;
        }
        string directory = Path.Combine(project.AssetsFolder, folder);
        string name = "New Chain" + ChainImporter.FileExtension;
        for (int n = 2; File.Exists(Path.Combine(directory, name)); ++n)
        {
            name = $"New Chain {n}{ChainImporter.FileExtension}";
        }
        string file = Path.Combine(directory, name);
        try
        {
            File.WriteAllText(file, ChainHandles.Format(
                new JsonObject { ["$guid"] = AssetId.New().ToString(), ["Points"] = null, ["Loop"] = false },
                [new System.Numerics.Vector2(1, 0), new System.Numerics.Vector2(-1, 0)]));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Log($"Can't make {name}: {exception.Message}");
            return null;
        }
        RefreshAssets();
        string path = folder.Length == 0 ? name : $"{folder}/{name}";
        Log($"Made {path}");
        return path;
    }

    /// <summary>
    /// An asset's import settings saved to its sidecar and imported with them
    /// (ADR 0087): the inspector's Apply, and the Collider's reimport button.
    /// </summary>
    public void ApplyImportSettings(AssetId id, JsonObject settings)
    {
        if (Database is not { } database || database.Find(id) is not { } entry)
        {
            return;
        }
        // A texture becoming a sprite opens its sheet, to slice (ADR 0107).
        bool becomesSprite = entry.Importer == "Texture" && IsSpriteUsage(settings) &&
                             !IsSpriteUsage(database.SettingsOf(id));
        try
        {
            database.SetSettings(id, settings);
        }
        catch (Exception exception) when (exception is ArgumentException or System.Text.Json.JsonException)
        {
            Log($"Can't apply {entry.Path}'s import settings: {exception.Message}");
            return;
        }
        Log($"Applied {entry.Path}'s import settings");
        RefreshAssets();
        if (becomesSprite)
        {
            ShowSpriteSheet(id);
        }
    }

    private static bool IsSpriteUsage(JsonObject? settings) =>
        settings?["Usage"]?.ToString() == nameof(Crucible.Importers.TextureUsage.Sprite);

    /// <summary>The Sprite Sheet Editor (ADR 0107) on a texture, opened or brought to the front.</summary>
    public void ShowSpriteSheet(AssetId texture)
    {
        if (!_open.ContainsKey("SpriteSheet"))
        {
            TogglePanel("SpriteSheet");
        }
        if (_open.GetValueOrDefault("SpriteSheet") is SpriteSheetPanel panel)
        {
            panel.Group?.Activate(panel);
            panel.Show(texture);
        }
    }

    // What was imported again, reloaded where it's loaded. Shaders first, so
    // materials reloaded after them are checked against their new types;
    // textures before the materials and models that use them.
    private void ReloadOnGpu(IEnumerable<ImportResult> results)
    {
        if (Database is not { } database)
        {
            return;
        }
        static int Order(string? importer) => importer switch
        {
            "Shader" => 0,
            "Texture" => 1,
            "Material" => 2,
            _ => 3,
        };
        foreach (ImportResult result in results.Where(result => result.Outcome == ImportOutcome.Imported)
                     .OrderBy(result => Order(database.Find(result.Id)?.Importer)))
        {
            // A model's skeleton and clips (its sub-assets), read again by
            // the poses that use them (ADR 0079).
            Crucible.Animation.Api.ReloadAnimationAsset(result.Id);
            foreach (AssetEntry clip in database.SubAssetsOf(result.Id))
            {
                Crucible.Animation.Api.ReloadAnimationAsset(clip.Id);
            }
            // Its collision data, and the colliders built from it (ADR 0086).
            Crucible.Physics.Api.ReloadCollisionAsset(result.Id);
            // A chain's points, and the Chain colliders made from them (ADR 0095).
            Crucible.Physics2D.Api.ReloadChainAsset(result.Id);
            try
            {
                if (RenderAssets.Reload(result.Id))
                {
                    Log($"Reloaded {result.Path} on the GPU");
                }
            }
            catch (CrucibleException exception)
            {
                Log($"Can't reload {result.Path}: {exception.Status}; the last version stays");
            }
        }
    }

    private void ReimportSelected()
    {
        if (_open.GetValueOrDefault("Assets") is AssetsPanel { SelectedAsset: { } asset })
        {
            Reimport(asset.Id);
        }
    }
}
