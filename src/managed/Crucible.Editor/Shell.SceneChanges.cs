using Crucible.Assets;
using Crucible.Ecs;
using Crucible.Scenes;
using Crucible.UI;

namespace Crucible.Editor;

/// <summary>
/// Scene edits for extensions (ADR 0122): entities made, destroyed, moved,
/// and named as undo steps; items in the Hierarchy's Create menu; and the
/// editor's own Duplicate, rename, and Delete run past extensions first
/// (who may cancel, hold for a question, or add work after) and told
/// after, in the same undo step.
/// </summary>
internal sealed partial class Shell
{
    private readonly List<(string Path, Action<Entity> Create, EditorContext Owner)> _createItems = [];
    private readonly List<(string Path, Action<string, AssetEntry?> Create, EditorContext Owner)> _assetCreateItems = [];

    // -----------------------------------------------------------------------
    // Extensions' own edits: no questions, no events
    // -----------------------------------------------------------------------

    internal Entity CreateForExtension(string name, Entity parent)
    {
        if (!_context.HasWorld)
        {
            return Entity.Null;
        }
        Entity entity = World.CreateEntity();
        World.Set(entity, new SceneEntityId { Value = SceneIds.NewId() });
        if (name.Trim().Length > 0)
        {
            World.SetManaged(entity, new EntityName { Value = name.Trim() });
        }
        if (HasLive(parent))
        {
            World.SetParent(entity, parent);
        }
        RecordCreated(name.Trim().Length > 0 ? $"Create {name.Trim()}" : "Create Entity", entity);
        return entity;
    }

    internal bool DestroyForExtension(Entity entity)
    {
        if (!HasLive(entity) || SceneIds.IsInsideInstance(World, entity))
        {
            return false;
        }
        Delete(entity);
        return true;
    }

    internal bool RenameForExtension(Entity entity, string name)
    {
        if (!HasLive(entity))
        {
            return false;
        }
        Rename(entity, name);
        return true;
    }

    internal void AddCreateItem(string path, Action<Entity> create, EditorContext owner)
    {
        (string, Action<Entity>, EditorContext) item = (path, create, owner);
        _createItems.Add(item);
        owner.OnStop(() => _createItems.Remove(item));
    }

    /// <summary>Runs an extension's Hierarchy Create item by its path (the checks); false if there's none.</summary>
    internal bool RunCreateItem(string path, Entity parent)
    {
        foreach ((string at, Action<Entity> create, EditorContext owner) in _createItems.ToArray())
        {
            if (at == path)
            {
                Guard(owner, path, () => create(HasLive(parent) ? parent : Entity.Null));
                return true;
            }
        }
        return false;
    }

    /// <summary>Runs an extension's Assets Create item by its path (the checks); false if there's none.</summary>
    internal bool RunAssetCreateItem(string path, string folder, AssetEntry? asset)
    {
        foreach ((string at, Action<string, AssetEntry?> create, EditorContext owner) in _assetCreateItems.ToArray())
        {
            if (at == path)
            {
                Guard(owner, path, () => create(folder, asset));
                return true;
            }
        }
        return false;
    }

    internal void AddAssetCreateItem(string path, Action<string, AssetEntry?> create, EditorContext owner)
    {
        (string, Action<string, AssetEntry?>, EditorContext) item = (path, create, owner);
        _assetCreateItems.Add(item);
        owner.OnStop(() => _assetCreateItems.Remove(item));
    }

    /// <summary>
    /// Extensions' items in the Hierarchy's Create menu (a context menu or a
    /// submenu of it), by path, given the entity clicked.
    /// </summary>
    internal void AddCreateItems(Menu menu, Entity parent)
    {
        foreach ((string path, Action<Entity> create, EditorContext owner) in _createItems)
        {
            AddPathItem(menu, path, () => Guard(owner, path, () => create(HasLive(parent) ? parent : Entity.Null)));
        }
    }

    /// <summary>Extensions' items in the Assets panel's Create menu (ADR 0123), given the folder and the asset clicked.</summary>
    internal void AddAssetCreateItems(Menu menu, string folder, AssetEntry? asset)
    {
        foreach ((string path, Action<string, AssetEntry?> create, EditorContext owner) in _assetCreateItems)
        {
            AddPathItem(menu, path, () => Guard(owner, path, () => create(folder, asset)));
        }
    }

    // An item at a path ("2D/Tilemap"), joining submenus already there.
    private static void AddPathItem(Menu menu, string path, Action run)
    {
        string[] parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return;
        }
        Menu at = menu;
        foreach (string part in parts[..^1])
        {
            if (at.Items.OfType<MenuItem>().FirstOrDefault(item => item.Submenu is not null && item.Title == part) is { } existing)
            {
                at = existing.Submenu!;
                continue;
            }
            Menu submenu = new();
            at.Add(new MenuItem(part) { Submenu = submenu });
            at = submenu;
        }
        at.Add(parts[^1], run);
    }

    // -----------------------------------------------------------------------
    // The editor's own changes, past extensions first
    // -----------------------------------------------------------------------

    // The extensions running now, each once.
    private IEnumerable<EditorContext> ExtensionContexts => _extensions.Select(extension => extension.Context).Distinct();

    // Asks the extensions (and the editor's own, first), then, once nothing
    // holds it and nothing cancelled it, makes the change in one undo step:
    // the change, the work added after it, and the after events.
    private void RunChange(SceneChange change, string title, Action<EditorScene> before, Func<Action<EditorScene>?> make)
    {
        foreach (EditorContext context in ExtensionContexts.ToArray())
        {
            Guard(context, $"{title} (before)", () => before(context.Scene));
        }
        change.WhenReady(() =>
        {
            History.BeginGroup(title);
            try
            {
                if (make() is not { } after)
                {
                    return;
                }
                foreach (Action then in change.ThenActions)
                {
                    try
                    {
                        then();
                    }
                    catch (Exception exception)
                    {
                        LogError($"{title}: work added to it failed: {Message(exception)}");
                    }
                }
                foreach (EditorContext context in ExtensionContexts.ToArray())
                {
                    Guard(context, $"{title} (after)", () => after(context.Scene));
                }
            }
            finally
            {
                History.EndGroup();
            }
        });
    }

    /// <summary>Delete (the command, the Hierarchy): past extensions and the editor's own map question first.</summary>
    internal void DeleteWithChanges(Entity entity)
    {
        if (!HasLive(entity) || RefuseInsideInstance(entity, "delete"))
        {
            return;
        }
        SceneChange change = new([entity]);
        RunChange(change, "Delete", scene => scene.RaiseDeleting(change), () =>
        {
            if (!HasLive(entity))
            {
                return null;
            }
            ulong[] ids = [.. SubtreeOf(entity).Select(under => SceneIds.IdOf(World, under))];
            Delete(entity);
            return scene => scene.RaiseDeleted(ids);
        });
    }

    /// <summary>Duplicate (the command): past extensions first; they're told each copy after.</summary>
    internal Entity DuplicateWithChanges(Entity entity)
    {
        if (!HasLive(entity) || RefuseInsideInstance(entity, "duplicate"))
        {
            return Entity.Null;
        }
        SceneChange change = new([entity]);
        Entity made = Entity.Null;
        RunChange(change, "Duplicate", scene => scene.RaiseDuplicating(change), () =>
        {
            if (!HasLive(entity) || Duplicate(entity) is not { IsNull: false } copy)
            {
                return null;
            }
            made = copy;
            (Entity Original, Entity Copy)[] copies = [(entity, copy)];
            return scene => scene.RaiseDuplicated(copies);
        });
        return made;
    }

    /// <summary>A rename (the Hierarchy, F2): past extensions first; they're told the names after.</summary>
    internal void RenameWithChanges(Entity entity, string name)
    {
        if (!HasLive(entity))
        {
            return;
        }
        string before = World.Has(entity, Components.Of<EntityName>()) ? World.GetManaged<EntityName>(entity).Value : "";
        string after = name.Trim();
        if (before == after)
        {
            return;
        }
        SceneChange change = new([entity], after);
        RunChange(change, "Rename", scene => scene.RaiseRenaming(change), () =>
        {
            if (!HasLive(entity))
            {
                return null;
            }
            Rename(entity, after);
            return scene => scene.RaiseRenamed(entity, before, after);
        });
    }
}
