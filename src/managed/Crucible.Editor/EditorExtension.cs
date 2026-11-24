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
        Scene = new EditorScene(shell);
        Assets = new EditorAssets(shell);
    }

    /// <summary>The extension's type, as messages name it.</summary>
    internal string Extension { get; }

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

    internal EditorScene(Shell shell) => _shell = shell;

    /// <summary>The world the editor shows (the playing one while playing); null before there's one.</summary>
    public World? World => _shell.HasWorld ? _shell.World : null;

    /// <summary>Sets a component the entity has; false if it hasn't, or the component isn't saved.</summary>
    public bool Edit<T>(Entity entity, in T value, string title) where T : unmanaged =>
        _shell.SetExtensionComponent(entity, value, title, adding: false);

    /// <summary>Adds a component the entity hasn't, with a value; false if it has, or the component isn't saved.</summary>
    public bool Add<T>(Entity entity, in T value, string title) where T : unmanaged =>
        _shell.SetExtensionComponent(entity, value, title, adding: true);

    /// <summary>Removes a component; false if the entity hasn't it, or the component isn't saved.</summary>
    public bool Remove<T>(Entity entity, string title) where T : unmanaged =>
        _shell.RemoveExtensionComponent<T>(entity, title);
}

/// <summary>
/// The project's assets, for an extension (ADR 0117): read through the asset
/// database, written as whole-file undo steps and imported at once.
/// </summary>
public sealed class EditorAssets
{
    private readonly Shell _shell;

    internal EditorAssets(Shell shell) => _shell = shell;

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
}
