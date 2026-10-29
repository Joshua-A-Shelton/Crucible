using System.Text.Json;
using Crucible.Assets;
using Crucible.Ecs;

namespace Crucible.Scenes;

/// <summary>
/// An entity's ID inside scene files (ADR 0041): random, kept across saves,
/// so references and diffs stay put. Saving gives one to entities without it.
/// </summary>
public struct SceneEntityId
{
    public ulong Value;
}

/// <summary>What loading a scene made.</summary>
/// <param name="Id">The scene's own asset ID.</param>
/// <param name="Roots">The entities it created at the top of the hierarchy, in file order.</param>
/// <param name="SkippedComponents">Component names in the file this build doesn't save, which were left out.</param>
public sealed record SceneLoad(AssetId Id, IReadOnlyList<Entity> Roots, IReadOnlyList<string> SkippedComponents);

/// <summary>
/// Scenes (ADR 0021, 0041): a world's entities as authored JSON, with their
/// saved components and their hierarchy, children nested in order.
/// <code>
/// {
///   "$guid": "…",
///   "Entities": [
///     { "Id": "6c1e9a03d7b4f210", "Components": { "Game.Health": { "Value": 10 } }, "Children": [ … ] }
///   ]
/// }
/// </code>
/// Only <c>[Component, Serialized]</c> types are saved. Entity references in
/// components are written as scene IDs and mapped back on load. Prefab
/// instances are written as their prefab plus their changes (ADR 0045).
/// Names are placeholders (docs/naming.md).
/// </summary>
public static class Scene
{
    public const string Extension = ".scene";

    /// <summary>The property holding the scene's own asset ID, written first (ADR 0021: Crucible's files keep their GUID inside).</summary>
    public const string GuidProperty = "$guid";

    // ---------------------------------------------------------------------
    // Saving
    // ---------------------------------------------------------------------

    /// <summary>
    /// Writes every entity in the world as the scene <paramref name="id"/>.
    /// Prefab instances are written as their prefab plus their changes, which
    /// needs <paramref name="prefabs"/> (ADR 0045).
    /// </summary>
    /// <remarks>
    /// Entities without a scene ID get one, so call it between frames: it is
    /// a structural change.
    /// </remarks>
    public static byte[] Save(World world, AssetId id, PrefabLibrary? prefabs = null) =>
        SceneWriter.Write(world, null, id, prefabs);

    // ---------------------------------------------------------------------
    // Loading
    // ---------------------------------------------------------------------

    /// <summary>
    /// Creates the scene's entities in <paramref name="world"/>, beside any it
    /// has. With <paramref name="newIds"/>, they get new scene IDs (loading
    /// one file twice into a world). Instances of prefabs are read through
    /// <paramref name="prefabs"/>.
    /// </summary>
    public static SceneLoad Load(World world, ReadOnlySpan<byte> json, bool newIds = false, PrefabLibrary? prefabs = null)
    {
        EngineComponents.Register();
        SceneFile file = SceneFile.Parse(json);
        SceneLoader loader = new(world, prefabs);
        List<Entity> roots = [];
        Frame frame = loader.Load(file, Entity.Null, outermost: true, roots);

        // Scene IDs: the file's, or with newIds a fresh one per ID in the
        // file, with instances' entities derived from their instance's.
        Dictionary<ulong, ulong> renamed = [];
        ulong Rename(ulong id)
        {
            if (!newIds)
            {
                return id;
            }
            if (!renamed.TryGetValue(id, out ulong fresh))
            {
                renamed[id] = fresh = PrefabIds.NewId();
            }
            return fresh;
        }
        foreach (Frame.Entry entry in frame.Entries)
        {
            ulong id = entry.Instance == 0 ? Rename(entry.Id)
                : entry.Inner == 0 ? Rename(entry.Instance)
                : PrefabIds.Derive(Rename(entry.Instance), entry.Inner);
            world.Set(entry.Entity, new SceneEntityId { Value = id });
        }
        return new SceneLoad(file.Id, roots, [.. loader.Skipped.Distinct()]);
    }

    /// <summary>A scene file's own asset ID, from its first property; None if it has none.</summary>
    public static AssetId ReadId(ReadOnlySpan<byte> json)
    {
        Utf8JsonReader reader = new(json);
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject || !reader.Read() ||
            reader.TokenType != JsonTokenType.PropertyName || !reader.ValueTextEquals(GuidProperty) || !reader.Read())
        {
            return AssetId.None;
        }
        return reader.TokenType == JsonTokenType.String && AssetId.TryParse(reader.GetString(), out AssetId id)
            ? id
            : AssetId.None;
    }
}
