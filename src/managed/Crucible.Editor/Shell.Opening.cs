using Crucible.Assets;
using Crucible.Ecs;
using Crucible.Render2D;
using Crucible.UI;

namespace Crucible.Editor;

/// <summary>
/// Opening editors (ADR 0120): which editors open which kinds of asset (by
/// importer, or by file extension) and which components, the editor's own
/// and extensions' workspaces alike. A double-click in Assets, the
/// Inspector's Open button on a component, and "Open in …" in the menus
/// open the one editor there is; with several, the one chosen with Always
/// Open With, else they ask.
/// </summary>
internal sealed partial class Shell
{
    private readonly List<AssetOpener> _assetOpeners = [];
    private readonly List<ComponentOpener> _componentOpeners = [];

    private sealed record AssetOpener(string Id, string Title, string Kind, Action<AssetEntry> Open);

    private sealed record ComponentOpener(string Id, string Title, string Component, Func<Entity, bool> Has, Action<Entity> Open);

    /// <summary>An editor opening assets of a kind: an importer's name ("Texture") or a file extension (".crtilemap").</summary>
    internal void AddAssetOpener(string id, string title, string kind, Action<AssetEntry> open, EditorContext? owner = null)
    {
        AssetOpener opener = new(id, title, kind.StartsWith('.') ? kind.ToLowerInvariant() : kind, open);
        _assetOpeners.Add(opener);
        owner?.OnStop(() => _assetOpeners.Remove(opener));
    }

    /// <summary>An editor opening entities with a component (by its name).</summary>
    internal void AddComponentOpener(string id, string title, string component, Func<Entity, bool> has, Action<Entity> open,
        EditorContext? owner = null)
    {
        ComponentOpener opener = new(id, title, component, has, open);
        _componentOpeners.Add(opener);
        owner?.OnStop(() => _componentOpeners.Remove(opener));
    }

    // The editor's own (ADR 0107): textures' sprite sheets. (Map mode's are
    // the Tilemaps plugin's, ADR 0123.)
    private void RegisterOpeners()
    {
        AddAssetOpener("SpriteSheet", "Sprite Sheet Editor", "Texture", asset => ShowSpriteSheet(asset.Id));
    }

    // An extension's workspace opening assets or a component's entities.
    internal bool AddWorkspaceOpener(EditorContext owner, string id, IReadOnlyList<string> kinds)
    {
        if (!_workspaceTypes.TryGetValue(id, out var type))
        {
            LogWarning($"{owner.Extension}: there's no workspace \"{id}\" to open assets with");
            return false;
        }
        foreach (string kind in kinds.Where(kind => kind.Length > 0))
        {
            AddAssetOpener(id, type.Title, kind, asset => OpenWorkspace(id, Entity.Null, asset.Id), owner);
        }
        return true;
    }

    internal bool AddWorkspaceOpener<T>(EditorContext owner, string id)
    {
        if (!_workspaceTypes.TryGetValue(id, out var type))
        {
            LogWarning($"{owner.Extension}: there's no workspace \"{id}\" to open {typeof(T).Name} with");
            return false;
        }
        AddComponentOpener<T>(id, type.Title, entity => OpenWorkspace(id, entity, AssetId.None), owner);
        return true;
    }

    // An editor opening entities with component T (an extension's callback, ADR 0123).
    internal void AddComponentOpener<T>(string id, string title, Action<Entity> open, EditorContext owner) =>
        AddComponentOpener(id, title, Components.NameOf(typeof(T)), entity => HasComponent<T>(entity), open, owner);

    private bool HasComponent<T>(Entity entity)
    {
        try
        {
            return World.Has(entity, Components.Of<T>());
        }
        catch (InvalidOperationException)
        {
            return false; // not registered: nothing has it
        }
    }

    // -----------------------------------------------------------------------
    // Which editors, and opening one
    // -----------------------------------------------------------------------

    // The kind an asset's choice is remembered by: its importer, else its extension.
    private static string KindOf(AssetEntry asset) =>
        asset.Importer.Length > 0 ? asset.Importer : Path.GetExtension(asset.Path).ToLowerInvariant();

    /// <summary>The editors that open an asset, by ID and title, in the order registered.</summary>
    internal IReadOnlyList<(string Id, string Title)> EditorsFor(AssetEntry asset)
    {
        string extension = Path.GetExtension(asset.Path).ToLowerInvariant();
        return [.. _assetOpeners.Where(opener => opener.Kind == asset.Importer || opener.Kind == extension)
            .DistinctBy(opener => opener.Id).Select(opener => (opener.Id, opener.Title))];
    }

    /// <summary>The editors that open an entity by one of its components (or by <paramref name="component"/> only).</summary>
    internal IReadOnlyList<(string Id, string Title)> EditorsFor(Entity entity, string? component = null)
    {
        if (!HasLive(entity))
        {
            return [];
        }
        return [.. _componentOpeners.Where(opener => (component is null || opener.Component == component) && Has(opener, entity))
            .DistinctBy(opener => opener.Id).Select(opener => (opener.Id, opener.Title))];
    }

    private bool Has(ComponentOpener opener, Entity entity)
    {
        try
        {
            return opener.Has(entity);
        }
        catch (Exception exception)
        {
            LogError($"{opener.Title}: {Message(exception)}");
            return false;
        }
    }

    /// <summary>
    /// Opens an asset in its editor (Assets' double-click): the one there is;
    /// with several, the one chosen to always open it, else asks. False if
    /// none opens it.
    /// </summary>
    public bool OpenAsset(AssetEntry asset, string? editor = null)
    {
        string extension = Path.GetExtension(asset.Path).ToLowerInvariant();
        return Open(EditorsFor(asset), KindOf(asset), Path.GetFileName(AssetPaths.WithinRoot(asset.Path)), editor,
            id => Run(_assetOpeners.First(opener => opener.Id == id && (opener.Kind == asset.Importer || opener.Kind == extension)),
                opener => opener.Open(asset)));
    }

    /// <summary>
    /// Opens an entity in the editor for one of its components (the
    /// Inspector's Open button on <paramref name="component"/>), as
    /// <see cref="OpenAsset"/> chooses.
    /// </summary>
    public bool OpenEntity(Entity entity, string component, string? editor = null)
    {
        IReadOnlyList<(string Id, string Title)> editors = EditorsFor(entity, component);
        return Open(editors, component, LabelOf(entity), editor,
            id => Run(_componentOpeners.First(opener => opener.Id == id && opener.Component == component), opener => opener.Open(entity)));
    }

    /// <summary>Opens an entity in an editor of one of its components, by the editor's ID ("Open in …").</summary>
    public bool OpenEntityIn(Entity entity, string editor)
    {
        if (_componentOpeners.FirstOrDefault(opener => opener.Id == editor && Has(opener, entity)) is not { } found)
        {
            return false;
        }
        Run(found, opener => opener.Open(entity));
        return true;
    }

    // The one editor, the remembered one, the one asked for, else the question.
    private bool Open(IReadOnlyList<(string Id, string Title)> editors, string kind, string name, string? editor, Action<string> open)
    {
        if (editors.Count == 0)
        {
            return false;
        }
        string? chosen = editor is not null && editors.Any(found => found.Id == editor) ? editor
            : editors.Count == 1 ? editors[0].Id
            : Settings.OpenWith.TryGetValue(kind, out string? remembered) && editors.Any(found => found.Id == remembered) ? remembered
            : null;
        if (chosen is not null)
        {
            open(chosen);
            return true;
        }
        AskDrop($"Open {name} with:", [.. editors.Select(found => found.Title)], 0, offerRest: true, (choice, always) =>
        {
            if (choice < 0 || choice >= editors.Count)
            {
                return;
            }
            if (always)
            {
                Settings.OpenWith[kind] = editors[choice].Id;
                SaveSettings();
            }
            open(editors[choice].Id);
        }, restLabel: "Always Open With");
        return true;
    }

    private void Run<T>(T opener, Action<T> open) where T : class
    {
        try
        {
            open(opener);
        }
        catch (Exception exception)
        {
            LogError($"Can't open it: {Message(exception)}");
        }
    }

    /// <summary>The editor chosen to always open a kind ("Texture", a component's name), if any.</summary>
    internal string? RememberedEditor(string kind) => Settings.OpenWith.GetValueOrDefault(kind);

    /// <summary>Ask Each Time: the choice forgotten.</summary>
    internal void ForgetEditor(string kind)
    {
        if (Settings.OpenWith.Remove(kind))
        {
            SaveSettings();
        }
    }

    /// <summary>
    /// An asset's menu items (Assets' right-click): "Open in X" for its one
    /// editor; with several, Open With listing them, the remembered one
    /// checked, and Ask Each Time.
    /// </summary>
    internal void AddOpenItems(Menu menu, AssetEntry asset)
    {
        IReadOnlyList<(string Id, string Title)> editors = EditorsFor(asset);
        if (editors.Count == 1)
        {
            menu.Add($"Open in {editors[0].Title}", () => OpenAsset(asset, editors[0].Id));
            return;
        }
        if (editors.Count == 0)
        {
            return;
        }
        string kind = KindOf(asset);
        Menu openWith = new();
        foreach ((string id, string title) in editors)
        {
            MenuItem item = openWith.Add(title, () => OpenAsset(asset, id));
            item.IsChecked = RememberedEditor(kind) == id;
        }
        openWith.AddSeparator();
        MenuItem ask = openWith.Add("Ask Each Time", () => ForgetEditor(kind));
        ask.IsChecked = RememberedEditor(kind) is null;
        menu.Add(new MenuItem("Open With") { Submenu = openWith });
    }
}
