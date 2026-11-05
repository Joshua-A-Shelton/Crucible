using System.Numerics;
using Crucible.Ecs;
using Crucible.Scenes;
using Crucible.UI;

namespace Crucible.Editor;

/// <summary>
/// The scene's entities as a tree (ADR 0068): roots by scene ID (the order
/// they're saved in), children in order. Selecting a row selects the entity
/// and the other way around; F2 renames it; dragging
/// a row onto another reparents or reorders it; a right-click opens a menu
/// (Create Empty, Rename, Duplicate, Delete). Each entity is labeled by its
/// <see cref="EntityName"/>, else its prefab's name, else its main
/// component. It's read again a frame after the scene changes.
/// </summary>
public sealed class HierarchyPanel : EditorPanel
{
    private readonly Shell _shell;
    private bool _stale = true;
    private bool _syncing;

    internal HierarchyPanel(Shell shell) : base("Hierarchy", "Hierarchy")
    {
        _shell = shell;
        Tree = new TreeView { CanDrag = true, ChildrenOf = ChildrenOf, BindRow = BindRow };
        Tree.Classes.Add("hierarchy");
        Tree.SelectionChanged += tree =>
        {
            if (!_syncing)
            {
                _shell.Selection.Selected = tree.SelectedNode is { } node ? EntityOf(node) : Entity.Null;
            }
        };
        Tree.Renamed += (_, node, text) => _shell.Rename(EntityOf(node), text);
        Tree.Dropped += (_, drop) => Drop(drop);
        Tree.ContextRequested += (_, node, at) => ShowMenu(node, at);
        AddChild(Tree);
    }

    public TreeView Tree { get; }

    protected override void OnAttached(UIRoot root)
    {
        _shell.SceneChanged += Invalidate;
        _shell.Selection.Changed += SelectionChanged;
        _stale = true;
    }

    protected override void OnDetached(UIRoot root)
    {
        _shell.SceneChanged -= Invalidate;
        _shell.Selection.Changed -= SelectionChanged;
    }

    private void Invalidate() => _stale = true;

    /// <summary>Reads the scene again if it changed; each frame, before the viewports render.</summary>
    internal void Update()
    {
        if (!_stale)
        {
            return;
        }
        _stale = false;
        _syncing = true;
        Tree.Refresh();
        Tree.SelectedNode = _shell.Selection.Selected.Value != 0 ? _shell.Selection.Selected.Value : null;
        _syncing = false;
    }

    /// <summary>Starts renaming an entity's row, showing it first.</summary>
    internal bool BeginRename(Entity entity)
    {
        Reveal(entity);
        return Tree.BeginRename(entity.Value);
    }

    // Selected elsewhere (a pick): its row is shown, its parents expanded.
    private void SelectionChanged(Selection selection)
    {
        if (_syncing)
        {
            return;
        }
        _syncing = true;
        Entity selected = selection.Selected;
        if (selected.Value != 0)
        {
            Reveal(selected);
        }
        Tree.SelectedNode = selected.Value != 0 ? selected.Value : null;
        _syncing = false;
    }

    private void Reveal(Entity entity)
    {
        if (!_shell.HasWorld)
        {
            return;
        }
        World world = _shell.World;
        bool expanded = false;
        for (Entity parent = world.IsAlive(entity) ? world.ParentOf(entity) : Entity.Null; !parent.IsNull;
             parent = world.ParentOf(parent))
        {
            if (!Tree.IsExpanded(parent.Value))
            {
                expanded = true;
                Tree.SetExpanded(parent.Value, true);
            }
        }
        if (expanded || _stale || Tree.IndexOf(entity.Value) < 0)
        {
            _stale = false;
            Tree.Refresh();
        }
        Tree.ScrollIntoView(entity.Value);
    }

    private IReadOnlyList<ulong> ChildrenOf(ulong? node)
    {
        if (!_shell.HasWorld)
        {
            return [];
        }
        World world = _shell.World;
        if (node is not { } value)
        {
            SceneIds ids = SceneIds.Of(world);
            return
            [
                .. ids.Entities.Where(entity => world.ParentOf(entity).IsNull)
                    .Select(entity => (Entity: entity, Id: ids.TryToScene(entity, out ulong id) ? id : 0))
                    .OrderBy(item => item.Id)
                    .Select(item => item.Entity.Value),
            ];
        }
        Entity entity = EntityOf(value);
        return world.IsAlive(entity) ? [.. SceneIds.ChildrenOf(world, entity).Select(child => child.Value)] : [];
    }

    private void BindRow(TreeRow row)
    {
        Entity entity = EntityOf(row.Node);
        row.Text = _shell.HasWorld ? _shell.LabelOf(entity) : "";
    }

    // Before or after a row: beside it, under its parent; inside: its last child.
    private void Drop(TreeDrop drop)
    {
        if (!_shell.HasWorld)
        {
            return;
        }
        World world = _shell.World;
        Entity entity = EntityOf(drop.Node), target = EntityOf(drop.Target);
        if (!world.IsAlive(entity) || !world.IsAlive(target))
        {
            return;
        }
        if (drop.Position == TreeDropPosition.Inside)
        {
            if (_shell.Reparent(entity, target, -1))
            {
                Tree.SetExpanded(target.Value, true);
            }
            return;
        }
        Entity parent = world.ParentOf(target);
        int index = -1;
        if (!parent.IsNull)
        {
            Entity[] others = [.. SceneIds.ChildrenOf(world, parent).Where(child => child.Value != entity.Value)];
            index = Array.IndexOf(others, target) + (drop.Position == TreeDropPosition.After ? 1 : 0);
        }
        _shell.Reparent(entity, parent, index);
    }

    private static Entity EntityOf(ulong node) => new() { Value = node };

    private void ShowMenu(ulong? node, Vector2 at)
    {
        ContextMenu menu = new();
        Entity entity = node is { } value ? EntityOf(value) : Entity.Null;
        menu.Add("Create Empty", () => _shell.CreateEmpty(entity));
        if (node is not null)
        {
            menu.AddSeparator();
            menu.Add(_shell.Commands.Find("Edit.Rename")!);
            menu.Add(_shell.Commands.Find("Edit.Duplicate")!);
            menu.Add(_shell.Commands.Find("Edit.Delete")!);
        }
        menu.Open(Tree, at);
    }
}
