using Crucible.Assets;
using Crucible.Ecs;
using Crucible.UI;

namespace Crucible.Editor;

/// <summary>
/// An editor assembly's entry point (ADR 0117): every non-abstract
/// subclass with a public parameterless constructor is made and started,
/// in the order of their full names, each time the project's code loads.
/// Before a reload unloads it, <see cref="Stop"/> runs and everything it
/// added through its <see cref="EditorContext"/> is taken out again.
/// </summary>
public abstract class EditorExtension
{
    /// <summary>Adds the extension's panels and commands.</summary>
    public abstract void Start(EditorContext editor);

    /// <summary>Called before the code unloads, for anything the extension started itself.</summary>
    public virtual void Stop()
    {
    }
}

/// <summary>
/// The editor as an extension sees it (ADR 0117): one per extension, handed to
/// <see cref="EditorExtension.Start"/>. It remembers what the extension adds, so
/// a reload takes all of it out. The shell behind it stays internal, so the
/// editor's insides can change without breaking extensions.
/// </summary>
public sealed class EditorContext
{
    private readonly Shell _shell;
    // What the extension added, undone newest first when it stops.
    private readonly List<Action> _cleanup = [];

    internal EditorContext(Shell shell, string extension)
    {
        _shell = shell;
        Extension = extension;
        Panels = new EditorPanels(shell, this);
        Workspaces = new EditorWorkspaces(shell, this);
        Tools = new EditorTools(shell, this);
        Commands = new EditorCommands(shell, this);
        Scene = new EditorScene(shell, this);
        Assets = new EditorAssets(shell, this);
    }

    /// <summary>The extension's type, as messages name it.</summary>
    internal string Extension { get; }

    /// <summary>The official plugin it's from (ADR 0123), if it is: saved with its panels in the layout.</summary>
    internal string? OfficialPlugin { get; init; }

    public EditorPanels Panels { get; }

    public EditorWorkspaces Workspaces { get; }

    public EditorTools Tools { get; }

    public EditorCommands Commands { get; }

    /// <summary>What's selected. Handlers added to its event are removed when the extension's code reloads.</summary>
    public Selection Selection => _shell.Selection;

    /// <summary>The editor's undo history: an extension's own steps, groups, undo and redo.</summary>
    public UndoHistory Undo => _shell.History;

    public EditorScene Scene { get; }

    public EditorAssets Assets { get; }

    /// <summary>The status bar's text.</summary>
    public string Status
    {
        get => _shell.Main.StatusBar.Text;
        set => _shell.Status(value ?? "");
    }

    public void Log(string text) => _shell.Log(text ?? "");

    public void LogWarning(string text) => _shell.LogWarning(text ?? "");

    public void LogError(string text) => _shell.LogError(text ?? "");

    /// <summary>
    /// Asks a question in the editor's own dialog over the main window (ADR
    /// 0122): <paramref name="answered"/> gets the chosen button's index (the
    /// first is the primary one), or -1 when it's closed with Escape.
    /// </summary>
    public void Ask(string message, IReadOnlyList<string> choices, Action<int> answered)
    {
        ArgumentNullException.ThrowIfNull(choices);
        ArgumentNullException.ThrowIfNull(answered);
        Dialog dialog = new(message ?? "", [.. choices]);
        dialog.Chosen += (_, chosen) => _shell.GuardExtension(this, "Ask", () => answered(chosen));
        dialog.Open(_shell.Main.Root);
    }

    /// <summary>The main window's UI, for an extension's own popups and dialogs (ADR 0122).</summary>
    public UIRoot Root => _shell.Main.Root;

    internal void OnStop(Action cleanup) => _cleanup.Add(cleanup);

    /// <summary>Takes out everything the extension added (its panels already dealt with by the shell).</summary>
    internal void Cleanup()
    {
        for (int i = _cleanup.Count - 1; i >= 0; --i)
        {
            _cleanup[i]();
        }
        _cleanup.Clear();
    }
}

/// <summary>An extension's panels (ADR 0117): listed in the Window menu, docked like the editor's own.</summary>
public sealed class EditorPanels
{
    private readonly Shell _shell;
    private readonly EditorContext _context;

    internal EditorPanels(Shell shell, EditorContext context)
    {
        _shell = shell;
        _context = context;
    }

    /// <summary>
    /// Registers a panel; false (with a warning in the Log) if its ID is
    /// taken. <paramref name="create"/> must make a panel with this ID.
    /// </summary>
    public bool Add(string id, string title, Func<EditorPanel> create)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentNullException.ThrowIfNull(create);
        return _shell.AddExtensionPanel(_context, id, title ?? id, create);
    }

    /// <summary>Opens a registered panel (or shows it, if it's open); false for one not registered.</summary>
    public bool Open(string id) => _shell.OpenPanel(id);

    /// <summary>Closes a panel if it's open.</summary>
    public void Close(string id) => _shell.ClosePanel(id);
}

/// <summary>
/// An extension's workspaces (ADR 0119): registered by ID, opened as tabs
/// for what they work on.
/// </summary>
public sealed class EditorWorkspaces
{
    private readonly Shell _shell;
    private readonly EditorContext _context;

    internal EditorWorkspaces(Shell shell, EditorContext context)
    {
        _shell = shell;
        _context = context;
    }

    /// <summary>Registers a workspace; false (with a warning in the Log) if its ID is taken.</summary>
    public bool Add(string id, string title, Func<Workspace> create)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentNullException.ThrowIfNull(create);
        return _shell.AddWorkspace(id, title ?? id, create, _context);
    }

    /// <summary>Opens it for an entity of the open scene (or shows its tab, if it's open for it); null if it can't.</summary>
    public Workspace? Open(string id, Entity entity) => _shell.OpenWorkspace(id, entity, AssetId.None);

    /// <summary>Opens it for an asset.</summary>
    public Workspace? Open(string id, AssetId asset) => _shell.OpenWorkspace(id, Entity.Null, asset);

    /// <summary>Opens it for nothing in particular.</summary>
    public Workspace? Open(string id) => _shell.OpenWorkspace(id, Entity.Null, AssetId.None);

    /// <summary>Closes a workspace's tab, as Done does.</summary>
    public void Close(Workspace workspace) => _shell.CloseWorkspace(workspace);

    /// <summary>
    /// The workspace opens assets of these kinds (ADR 0120): importers'
    /// names ("Texture", "Mesh") or file extensions (".crtilemap"). Double-clicking
    /// one in Assets opens it; false if there's no such workspace.
    /// </summary>
    public bool OpensAssets(string id, params string[] kinds)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentNullException.ThrowIfNull(kinds);
        return _shell.AddWorkspaceOpener(_context, id, kinds);
    }

    /// <summary>
    /// The workspace opens entities with this component (ADR 0120): from the
    /// Inspector's Open button on it, and "Open in …" in the menus.
    /// </summary>
    public bool OpensComponent<T>(string id)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        return _shell.AddWorkspaceOpener<T>(_context, id);
    }
}

/// <summary>An extension's commands (ADR 0117): in menus, and run by their shortcuts.</summary>
public sealed class EditorCommands
{
    private readonly Shell _shell;
    private readonly EditorContext _context;

    internal EditorCommands(Shell shell, EditorContext context)
    {
        _shell = shell;
        _context = context;
    }

    /// <summary>
    /// Registers a command, with an item in the menu named by
    /// <paramref name="menu"/> ("Tools/Walker", "Edit"; null for none).
    /// False (with a warning) if its ID is taken; a shortcut already in use
    /// is left off, with a warning, and the command still runs from its menu.
    /// </summary>
    public bool Add(Command command, string? menu = "Tools")
    {
        ArgumentNullException.ThrowIfNull(command);
        return _shell.AddExtensionCommand(_context, command, menu);
    }
}

/// <summary>
/// The open scene, for an extension (ADR 0117): components changed as undo
/// steps, recorded from their saved form as the Inspector's edits are.
/// Only components scenes save can be changed this way.
/// </summary>
public sealed class EditorScene
{
    private readonly Shell _shell;
    private readonly EditorContext _context;

    internal EditorScene(Shell shell, EditorContext context)
    {
        _shell = shell;
        _context = context;
    }

    /// <summary>The world the editor shows (the playing one while playing); null before there's one.</summary>
    public World? World => _shell.HasWorld ? _shell.World : null;

    /// <summary>The open scene's file (its full path); null while it isn't saved (ADR 0123).</summary>
    public string? Path => _shell.ScenePath;

    /// <summary>
    /// An editor opening entities with component <typeparamref name="T"/>
    /// (ADR 0120, 0123): the Inspector's Open button on it and "Open in …"
    /// call <paramref name="open"/> with the entity.
    /// </summary>
    public void AddOpener<T>(string id, string title, Action<Entity> open)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentNullException.ThrowIfNull(open);
        _shell.AddComponentOpener<T>(id, title ?? id, entity => _shell.GuardExtension(_context, title ?? id, () => open(entity)), _context);
    }

    /// <summary>Sets a component the entity has; false if it hasn't, or the component isn't saved.</summary>
    public bool Edit<T>(Entity entity, in T value, string title) where T : unmanaged =>
        _shell.SetExtensionComponent(entity, value, title, adding: false);

    /// <summary>Adds a component the entity hasn't, with a value; false if it has, or the component isn't saved.</summary>
    public bool Add<T>(Entity entity, in T value, string title) where T : unmanaged =>
        _shell.SetExtensionComponent(entity, value, title, adding: true);

    /// <summary>Removes a component; false if the entity hasn't it, or the component isn't saved.</summary>
    public bool Remove<T>(Entity entity, string title) where T : unmanaged =>
        _shell.RemoveExtensionComponent<T>(entity, title);

    // -----------------------------------------------------------------------
    // Entities (ADR 0122)
    // -----------------------------------------------------------------------

    /// <summary>
    /// A new entity of the open scene, named, under <paramref name="parent"/>
    /// (none: at the top), with no components but its name: add what it
    /// needs. One undo step (several made one with the history's groups).
    /// </summary>
    public Entity Create(string name, Entity parent = default) => _shell.CreateForExtension(name ?? "", parent);

    /// <summary>Destroys an entity and what's under it, as one undo step; no questions, no events.</summary>
    public bool Destroy(Entity entity) => _shell.DestroyForExtension(entity);

    /// <summary>
    /// Moves an entity under <paramref name="parent"/> (none: the top), at
    /// <paramref name="index"/> among its children (-1: last), keeping where
    /// it is in the world; one undo step. False if it can't be moved there.
    /// </summary>
    public bool SetParent(Entity entity, Entity parent, int index = -1) => _shell.Reparent(entity, parent, index);

    /// <summary>Names an entity (empty: no name) as one undo step; no events.</summary>
    public bool Rename(Entity entity, string name) => _shell.RenameForExtension(entity, name ?? "");

    /// <summary>
    /// An item in the Hierarchy's right-click Create menu, at a path
    /// ("2D/Tilemap"), given the entity clicked (the parent) or none.
    /// </summary>
    public void AddCreate(string path, Action<Entity> create)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(create);
        _shell.AddCreateItem(path, create, _context);
    }

    // -----------------------------------------------------------------------
    // The editor's own changes (ADR 0122): before, and after in their undo step
    // -----------------------------------------------------------------------

    /// <summary>Before Duplicate: cancel it, hold it for a question, or add work after it.</summary>
    public event Action<SceneChange>? Duplicating;

    /// <summary>After Duplicate, in its undo step: each original and its copy.</summary>
    public event Action<IReadOnlyList<(Entity Original, Entity Copy)>>? Duplicated;

    /// <summary>Before a rename (F2, the Hierarchy): <see cref="SceneChange.NewName"/> is the new name.</summary>
    public event Action<SceneChange>? Renaming;

    /// <summary>After a rename, in its undo step: the entity, its name before, and after.</summary>
    public event Action<Entity, string, string>? Renamed;

    /// <summary>Before Delete: the entities, with what's under them, still there.</summary>
    public event Action<SceneChange>? Deleting;

    /// <summary>After Delete, in its undo step: the deleted entities' scene IDs.</summary>
    public event Action<IReadOnlyList<ulong>>? Deleted;

    internal void RaiseDuplicating(SceneChange change) => Duplicating?.Invoke(change);

    internal void RaiseDuplicated(IReadOnlyList<(Entity Original, Entity Copy)> copies) => Duplicated?.Invoke(copies);

    internal void RaiseRenaming(SceneChange change) => Renaming?.Invoke(change);

    internal void RaiseRenamed(Entity entity, string before, string after) => Renamed?.Invoke(entity, before, after);

    internal void RaiseDeleting(SceneChange change) => Deleting?.Invoke(change);

    internal void RaiseDeleted(IReadOnlyList<ulong> ids) => Deleted?.Invoke(ids);
}

/// <summary>
/// The project's assets, for an extension (ADR 0117): read through the asset
/// database, written as whole-file undo steps and imported at once.
/// </summary>
public sealed class EditorAssets
{
    private readonly Shell _shell;
    private readonly EditorContext _context;
    // The textures acquired for this extension, released when it stops.
    private readonly Dictionary<AssetId, Crucible.Render.TextureHandle> _textures = [];

    internal EditorAssets(Shell shell, EditorContext context)
    {
        _shell = shell;
        _context = context;
        Action changed = () => _shell.GuardExtension(context, "Assets.Changed", RaiseChanged);
        shell.AssetsChanged += changed;
        context.OnStop(() =>
        {
            shell.AssetsChanged -= changed;
            foreach (Crucible.Render.TextureHandle texture in _textures.Values)
            {
                Crucible.Render.RenderAssets.ReleaseTexture(texture);
            }
            _textures.Clear();
        });
    }

    /// <summary>
    /// A texture asset's GPU texture, to draw in a panel or a tool's overlay
    /// (ADR 0122): loaded on first asking and kept while the extension runs;
    /// null until it's ready (a frame or two), or for an asset that isn't an
    /// imported texture.
    /// </summary>
    public Crucible.Render.TextureHandle? Texture(AssetId texture)
    {
        if (!_textures.TryGetValue(texture, out Crucible.Render.TextureHandle handle))
        {
            if (texture.IsNone || _shell.Database?.Find(texture) is not { Importer: "Texture" } ||
                _shell.Database.ImportedFile(texture) is null)
            {
                return null;
            }
            handle = Crucible.Render.RenderAssets.AcquireTexture(texture);
            _textures[texture] = handle;
        }
        return handle.IsReady ? handle : null;
    }

    /// <summary>The open project's assets; null with no project open.</summary>
    public AssetDatabase? Database => _shell.Database;

    /// <summary>
    /// Writes a file under the Assets folder (<paramref name="path"/> relative
    /// to it, as "Stats/Walker.json"), as one undo step that writes the old
    /// contents back (or deletes a file that wasn't there). False if it
    /// can't be written, or the path leaves the Assets folder.
    /// </summary>
    public bool Write(string path, byte[] contents, string title)
    {
        ArgumentNullException.ThrowIfNull(contents);
        return _shell.WriteExtensionFile(path, contents, title);
    }

    public bool WriteText(string path, string text, string title)
    {
        ArgumentNullException.ThrowIfNull(text);
        return _shell.WriteExtensionFile(path, System.Text.Encoding.UTF8.GetBytes(text), title);
    }

    /// <summary>Deletes a file under the Assets folder as one undo step that writes it back (ADR 0123); true if it's gone.</summary>
    public bool Delete(string path, string title) => _shell.WriteExtensionFile(path, null, title);

    /// <summary>
    /// Whether a file there can be written (ADR 0123): under the game's or a
    /// source plugin's Assets, not a compiled plugin's; says why in the log if not.
    /// </summary>
    public bool CanWrite(string path) => _shell.CanWriteExtensionFile(path);

    /// <summary>The open project (its folders, plugins, and asset paths' files); null with none open.</summary>
    public Project? Project => _shell.Project;

    /// <summary>
    /// Saves an asset's import settings and imports it with them, as the
    /// Inspector's Apply does (ADR 0123): a texture made a sprite, say.
    /// </summary>
    public void ApplySettings(AssetId asset, System.Text.Json.Nodes.JsonObject settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _shell.ApplyImportSettings(asset, settings, openSheet: false);
    }

    /// <summary>
    /// Imports an asset again at once, quietly, after its file was written
    /// outside <see cref="Write"/> (a map saved as it's painted, ADR 0123).
    /// </summary>
    public void Reimport(AssetId asset) => _shell.Reimport(asset, quiet: true);

    /// <summary>Raised when assets are imported, added, moved, or removed (ADR 0123).</summary>
    public event Action? Changed;

    internal void RaiseChanged() => Changed?.Invoke();

    /// <summary>
    /// An editor opening assets of these kinds (ADR 0120, 0123): importers'
    /// names ("Tileset") or file extensions (".crtilemap"). Double-clicking
    /// one in Assets, or Open With, calls <paramref name="open"/> with it.
    /// </summary>
    public void AddOpener(string id, string title, IReadOnlyList<string> kinds, Action<AssetEntry> open)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentNullException.ThrowIfNull(kinds);
        ArgumentNullException.ThrowIfNull(open);
        foreach (string kind in kinds.Where(kind => kind.Length > 0))
        {
            _shell.AddAssetOpener(id, title ?? id, kind, asset => _shell.GuardExtension(_context, title ?? id, () => open(asset)), _context);
        }
    }

    /// <summary>
    /// An item in the Assets panel's right-click Create menu, at a path
    /// ("Tileset…"), given the folder new assets go in and the asset clicked
    /// (null for a folder) (ADR 0123).
    /// </summary>
    public void AddCreate(string path, Action<string, AssetEntry?> create)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(create);
        _shell.AddAssetCreateItem(path, create, _context);
    }
}
