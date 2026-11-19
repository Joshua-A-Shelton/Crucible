using Crucible.Serialization;

namespace Crucible.Assets;

/// <summary>
/// One of the project's sorting layers (ADR 0104): sprites in a later layer
/// draw over those in an earlier one. In one that y-sorts, a sprite lower on
/// screen draws in front.
/// </summary>
[Serialized]
public sealed partial class SortingLayer
{
    public string Name = "";

    public bool YSort;
}

/// <summary>
/// The project settings a running game needs (ADR 0104), for now its sorting
/// layers. The project file holds them; a cook packs them into the archive
/// under <see cref="Id"/>. The host installs them as the game starts, the
/// editor when it opens a project.
/// </summary>
[Serialized]
public sealed partial class RuntimeSettings
{
    /// <summary>The settings entry's ID in an archive.</summary>
    public static readonly AssetId Id = AssetId.Parse("7f3c1d52-8a64-4b1e-9d27-5e0b6c4a9f13");

    /// <summary>The sorting layers, in drawing order; "Default" alone when there are none.</summary>
    public List<global::Crucible.Assets.SortingLayer> SortingLayers = [];

    /// <summary>The running game's settings: "Default" alone until some are installed.</summary>
    public static RuntimeSettings Current { get; private set; } = Normalized(new RuntimeSettings());

    /// <summary>
    /// Makes <paramref name="settings"/> the running game's: the renderer's
    /// sorting layers that y-sort follow them. Main thread.
    /// </summary>
    public static void Install(RuntimeSettings settings)
    {
        Current = Normalized(settings);
        uint ySort = 0;
        for (int i = 0; i < Math.Min(Current.SortingLayers.Count, 32); ++i)
        {
            ySort |= Current.SortingLayers[i].YSort ? 1u << i : 0;
        }
        Crucible.Render2D.Api.SetYSortLayers(ySort);
    }

    /// <summary>The settings in a project file's JSON (the file's other sections are skipped).</summary>
    public static RuntimeSettings FromJson(ReadOnlySpan<byte> utf8) => Json.Deserialize<RuntimeSettings>(utf8);

    /// <summary>A sorting layer's index by name, for gameplay switching floors; -1 if there's none.</summary>
    public int SortingLayer(string name) => SortingLayers.FindIndex(layer => layer.Name == name);

    private static RuntimeSettings Normalized(RuntimeSettings settings)
    {
        if (settings.SortingLayers.Count == 0)
        {
            settings.SortingLayers.Add(new global::Crucible.Assets.SortingLayer { Name = "Default" });
        }
        return settings;
    }
}
