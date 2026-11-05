using System.Text.Json.Nodes;
using Crucible.Assets;
using Crucible.Ecs;
using Crucible.Platform;
using Crucible.Scenes;
using Crucible.UI;

namespace Crucible.Editor;

// The scene being edited (ADR 0068): its file, its undo history, whether
// it's changed since saved (an asterisk in the title), and the edits the
// hierarchy and viewport make, each recorded as an undo step.
internal sealed partial class Shell
{
    private static readonly string _transformName = Components.NameOf(typeof(LocalTransform));
    private static readonly string _nameName = Components.NameOf(typeof(EntityName));

    private bool _savingAs;      // the save dialog is open
    private Action? _afterSave;  // what a Save chosen in the unsaved-changes dialog goes on to
    private bool _quitConfirmed; // the unsaved-changes dialog said quit
    private Dialog? _confirm;

    public UndoHistory History { get; } = new();

    /// <summary>The scene's own asset ID, written into its file.</summary>
    public AssetId SceneId { get; private set; } = AssetId.New();

    public bool IsDirty => History.IsDirty;

    /// <summary>The open scene's entities changed: a scene opened or made new, or a step done, undone, or redone.</summary>
    public event Action? SceneChanged;

    internal bool HasWorld => _context.HasWorld;

    internal World World => _context.World;

    private void RegisterSceneCommands()
    {
        Commands.Add(new Command("File.NewScene", "New Scene", () => ConfirmDiscard("making a new scene", NewScene))
        {
            Shortcut = new Shortcut(KeyModifiers.Ctrl, Keycode.N),
        });
        Commands.Add(new Command("File.SaveScene", "Save Scene", () => SaveScene())
        {
            Shortcut = new Shortcut(KeyModifiers.Ctrl, Keycode.S),
        });
        Commands.Add(new Command("File.SaveSceneAs", "Save Scene As…", SaveSceneAs)
        {
            Shortcut = new Shortcut(KeyModifiers.Ctrl | KeyModifiers.Shift, Keycode.S),
        });
        Commands.Add(new Command("Edit.Undo", "Undo", () => Undo(), () => History.CanUndo)
        {
            Shortcut = new Shortcut(KeyModifiers.Ctrl, Keycode.Z),
        });
        Commands.Add(new Command("Edit.Redo", "Redo", () => Redo(), () => History.CanRedo)
        {
            Shortcut = new Shortcut(KeyModifiers.Ctrl, Keycode.Y),
        });
        Commands.Add(new Command("Edit.Rename", "Rename", RenameSelected, HasSelection)
        {
            Shortcut = new Shortcut(KeyModifiers.None, Keycode.F2),
        });
        Commands.Add(new Command("Edit.Duplicate", "Duplicate", () => Duplicate(Selection.Selected), HasSelection)
        {
            Shortcut = new Shortcut(KeyModifiers.Ctrl, Keycode.D),
        });
        Commands.Add(new Command("Edit.Delete", "Delete", () => Delete(Selection.Selected), HasSelection)
        {
            Shortcut = new Shortcut(KeyModifiers.None, Keycode.Delete),
        });
        Commands.Add(new Command("Hierarchy.CreateEmpty", "Create Empty", () => CreateEmpty(Entity.Null))
        {
            Shortcut = new Shortcut(KeyModifiers.Ctrl | KeyModifiers.Shift, Keycode.N),
        });
    }

    private bool HasSelection() => _context.HasWorld && Selection.Selected.Value != 0 && World.IsAlive(Selection.Selected);

    // -----------------------------------------------------------------------
    // The document
    // -----------------------------------------------------------------------

    /// <summary>Clears the scene for a new one, with no file yet.</summary>
    public void NewScene()
    {
        if (!_context.HasWorld)
        {
            return;
        }
        Selection.Selected = Entity.Null;
        ClearScene(World);
        ScenePath = null;
        SceneId = AssetId.New();
        SceneReplaced();
        Log("New scene");
    }

    // A scene opened or made new: nothing to undo, nothing unsaved.
    private void SceneReplaced()
    {
        History.Clear();
        SceneChanged?.Invoke();
        UpdateTitle();
    }

    private void SceneEdited()
    {
        SceneChanged?.Invoke();
        UpdateTitle();
    }

    /// <summary>The scene's file name, or Untitled.</summary>
    public string SceneName => ScenePath is { } path ? Path.GetFileName(path) : "Untitled";

    private void UpdateTitle()
    {
        string scene = SceneName + (IsDirty ? "*" : "");
        Main.TitleBar.Title = Project is { } project
            ? $"{scene} — {project.Name} — {EngineInfo.Name}"
            : $"{scene} — {EngineInfo.Name}";
    }

    /// <summary>Saves to the scene's file, or asks for one (Save As) if it has none. False when it couldn't save now.</summary>
    public bool SaveScene()
    {
        if (ScenePath is null)
        {
            SaveSceneAs();
            return false;
        }
        return WriteScene(ScenePath);
    }

    /// <summary>Asks where to save (the platform's save dialog), then saves there.</summary>
    public void SaveSceneAs()
    {
        string location = ScenePath ?? (Project is { } project
            ? Path.Combine(project.AssetsFolder, "Scenes", "Untitled" + Scene.Extension)
            : "Untitled" + Scene.Extension);
        _savingAs = true;
        Platform.Api.ShowSaveFileDialog(Main.Window, "Crucible scenes", "crscene", location);
    }

    private void SaveDialogAnswered(string? path)
    {
        _savingAs = false;
        if (path is null)
        {
            _afterSave = null; // canceled: whatever was waiting on the save doesn't happen
            return;
        }
        if (!path.EndsWith(Scene.Extension, StringComparison.OrdinalIgnoreCase))
        {
            path += Scene.Extension;
        }
        WriteScene(path);
    }

    /// <summary>Writes the scene to a file and makes it the scene's file; then does what was waiting on a save.</summary>
    public bool WriteScene(string path)
    {
        if (!_context.HasWorld)
        {
            return false;
        }
        Action? after = _afterSave;
        _afterSave = null;
        try
        {
            byte[] bytes = Scene.Save(World, SceneId, Prefabs);
            string? folder = Path.GetDirectoryName(Path.GetFullPath(path));
            if (folder is not null)
            {
                Directory.CreateDirectory(folder);
            }
            Files.WriteAtomically(path, bytes);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Log($"Can't save {path}: {exception.Message}");
            return false;
        }
        ScenePath = Path.GetFullPath(path);
        History.MarkSaved();
        Log($"Saved {SceneName}");
        after?.Invoke();
        return true;
    }

    /// <summary>
    /// Does <paramref name="then"/> once unsaved changes are dealt with: at
    /// once if there are none, else after a dialog's Save (and the save),
    /// or Don't Save; Cancel does nothing.
    /// </summary>
    public void ConfirmDiscard(string doing, Action then)
    {
        if (!IsDirty)
        {
            then();
            return;
        }
        if (_confirm?.IsOpen == true)
        {
            return;
        }
        _confirm = new Dialog($"Save changes to {SceneName} before {doing}?", "Save", "Don't Save", "Cancel");
        _confirm.Chosen += (_, choice) =>
        {
            _confirm = null;
            if (choice == 0)
            {
                _afterSave = then;
                SaveScene();
            }
            else if (choice == 1)
            {
                then();
            }
        };
        _confirm.Open(Main.Root);
    }

    /// <summary>Quits, after asking about unsaved changes.</summary>
    public void RequestQuit() => ConfirmDiscard("quitting", () =>
    {
        _quitConfirmed = true;
        _context.QuitRequested = true;
    });

    // The OS or the main window's close asked to quit (the engine has set
    // QuitRequested): with unsaved changes, ask first.
    private void KeepUnsaved()
    {
        if (IsDirty && !_quitConfirmed)
        {
            _context.QuitRequested = false;
            RequestQuit();
        }
    }

    // -----------------------------------------------------------------------
    // Edits, each an undo step
    // -----------------------------------------------------------------------

    public bool Undo()
    {
        string? title = History.NextUndo?.Title;
        if (!_context.HasWorld || !History.Undo(World))
        {
            return false;
        }
        Status($"Undid {title}");
        return true;
    }

    public bool Redo()
    {
        string? title = History.NextRedo?.Title;
        if (!_context.HasWorld || !History.Redo(World))
        {
            return false;
        }
        Status($"Redid {title}");
        return true;
    }

    // A saved component changed (it's been set already); recorded against
    // its JSON before.
    private void RecordComponent(Entity entity, string component, JsonNode? before, string title)
    {
        ulong id = SceneIds.IdOf(World, entity);
        if (id == 0)
        {
            return; // not the scene's
        }
        JsonNode? after = SceneIds.Read(World, entity, component);
        if (!JsonNode.DeepEquals(before, after))
        {
            History.Push(new ComponentEdit(title, id, component, before, after));
        }
    }

    // Edits to the shape of a prefab instance below its root aren't saved
    // (ADR 0045's overrides change components and add children only).
    private bool RefuseInsideInstance(Entity entity, string doing)
    {
        if (!SceneIds.IsInsideInstance(World, entity))
        {
            return false;
        }
        Status($"Can't {doing} part of a prefab instance");
        return true;
    }

    /// <summary>Names an entity (F2); an empty name removes its name.</summary>
    public void Rename(Entity entity, string name)
    {
        if (!HasLive(entity))
        {
            return;
        }
        JsonNode? before = SceneIds.Read(World, entity, _nameName);
        name = name.Trim();
        if (name.Length > 0)
        {
            World.SetManaged(entity, new EntityName { Value = name });
        }
        else
        {
            World.Remove(entity, Components.Of<EntityName>());
        }
        RecordComponent(entity, _nameName, before, "Rename");
    }

    /// <summary>A new entity with only a transform and a name, under a parent (or at the top).</summary>
    public Entity CreateEmpty(Entity parent)
    {
        if (!_context.HasWorld)
        {
            return Entity.Null;
        }
        Entity entity = World.CreateEntity();
        World.Set(entity, LocalTransform.Identity);
        World.Set(entity, new SceneEntityId { Value = SceneIds.NewId() });
        World.SetManaged(entity, new EntityName { Value = "Empty" });
        if (HasLive(parent))
        {
            World.SetParent(entity, parent);
        }
        RecordCreated("Create Empty", entity);
        Selection.Selected = entity;
        return entity;
    }

    /// <summary>A copy of an entity and what's under it, with new scene IDs, just after it.</summary>
    public Entity Duplicate(Entity entity)
    {
        if (!HasLive(entity) || RefuseInsideInstance(entity, "duplicate"))
        {
            return Entity.Null;
        }
        Entity parent = World.ParentOf(entity);
        int index = SceneIds.SiblingIndex(World, entity);
        byte[] subtree = SceneIds.Capture(World, entity, Prefabs);
        Entity copy = SceneIds.Restore(World, subtree, SceneIds.IdOf(World, parent), index < 0 ? -1 : index + 1, Prefabs,
            newIds: true);
        RecordCreated("Duplicate", copy);
        Selection.Selected = copy;
        return copy;
    }

    private void RecordCreated(string title, Entity entity)
    {
        Entity parent = World.ParentOf(entity);
        History.Push(new SubtreeEdit(title, SceneIds.IdOf(World, entity), SceneIds.Capture(World, entity, Prefabs),
            SceneIds.IdOf(World, parent), SceneIds.SiblingIndex(World, entity), created: true, Prefabs));
    }

    /// <summary>Destroys an entity and what's under it.</summary>
    public void Delete(Entity entity)
    {
        if (!HasLive(entity) || RefuseInsideInstance(entity, "delete"))
        {
            return;
        }
        SubtreeEdit step = new("Delete", SceneIds.IdOf(World, entity), SceneIds.Capture(World, entity, Prefabs),
            SceneIds.IdOf(World, World.ParentOf(entity)), SceneIds.SiblingIndex(World, entity), created: false, Prefabs);
        if (Selection.Selected.Value == entity.Value)
        {
            Selection.Selected = Entity.Null;
        }
        World.Destroy(entity);
        History.Push(step);
    }

    /// <summary>
    /// Moves an entity under <paramref name="parent"/> (or to the top for
    /// none), at <paramref name="index"/> among its other children (-1:
    /// last), keeping where it is in the world. Roots have no order: they're
    /// listed and saved by scene ID.
    /// </summary>
    public bool Reparent(Entity entity, Entity parent, int index)
    {
        if (!HasLive(entity) || RefuseInsideInstance(entity, "move"))
        {
            return false;
        }
        World world = World;
        for (Entity at = parent; !at.IsNull; at = world.ParentOf(at))
        {
            if (at.Value == entity.Value)
            {
                return false; // into itself
            }
        }
        Entity oldParent = world.ParentOf(entity);
        (ulong, int, LocalTransform) before = (SceneIds.IdOf(world, oldParent), SceneIds.SiblingIndex(world, entity),
            world.Read<LocalTransform>(entity));
        LocalTransform local = ParentEdit.Keeping(world, entity, parent);
        world.SetParent(entity, parent, parent.IsNull || index < 0 ? uint.MaxValue : (uint)index);
        world.Set(entity, local);
        (ulong, int, LocalTransform) after = (SceneIds.IdOf(world, parent), SceneIds.SiblingIndex(world, entity), local);
        if (before.Item1 == after.Item1 && before.Item2 == after.Item2)
        {
            world.Set(entity, before.Item3);
            return false;
        }
        History.Push(new ParentEdit("Move in Hierarchy", SceneIds.IdOf(world, entity), before, after));
        return true;
    }

    /// <summary>
    /// What the hierarchy calls an entity: its name; else an instance's
    /// prefab's file name; else its main component's type ("Camera"); else
    /// "Entity".
    /// </summary>
    public string LabelOf(Entity entity)
    {
        World world = World;
        if (!world.IsAlive(entity))
        {
            return "";
        }
        if (world.Has(entity, Components.Of<EntityName>()) && world.GetManaged<EntityName>(entity).Value is { Length: > 0 } name)
        {
            return name;
        }
        if (world.Has(entity, Components.Of<PrefabInstance>()))
        {
            AssetId prefab = world.Read<PrefabInstance>(entity).Prefab;
            return Database?.Find(prefab) is { } asset ? Path.GetFileNameWithoutExtension(asset.Path) : "Prefab";
        }
        foreach (string component in _mainComponents)
        {
            if (SavedComponents.Find(component) is { } saved && world.Has(entity, saved.Id))
            {
                return saved.Type.Name;
            }
        }
        return "Entity";
    }

    // Which of an entity's components says what it is, most telling first.
    private static readonly string[] _mainComponents =
    [
        Components.NameOf(typeof(Crucible.Render.Camera)),
        Components.NameOf(typeof(Crucible.Render.DirectionalLight)),
        Components.NameOf(typeof(Crucible.Render.PointLight)),
        Components.NameOf(typeof(Crucible.Render.SpotLight)),
        Components.NameOf(typeof(Crucible.Render.Sky)),
        Components.NameOf(typeof(Crucible.Render.AmbientLight)),
        Components.NameOf(typeof(Crucible.Render.PostProcess)),
        Components.NameOf(typeof(MeshInstance)),
    ];

    private void RenameSelected()
    {
        if (_open.GetValueOrDefault("Hierarchy") is HierarchyPanel hierarchy && HasSelection())
        {
            hierarchy.BeginRename(Selection.Selected);
        }
    }

    private bool HasLive(Entity entity) =>
        _context.HasWorld && entity.Value != 0 && World.IsAlive(entity) && SceneIds.IdOf(World, entity) != 0;
}
