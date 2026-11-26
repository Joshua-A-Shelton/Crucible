using System.Numerics;
using Crucible.Ecs;
using Crucible.Scenes;
using Crucible.UI;

namespace Crucible.Editor;

/// <summary>
/// The scene's entities as a tree (ADR 0068): top-level entities and
/// children each in their order (ADR 0130), as they're saved. Selecting a row selects the entity
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
    private bool _hadProject;
    // Over the tree when it's empty (ADR 0133): no project, or an empty scene.
    private readonly StackPanel _empty = new() { Spacing = 6, VerticalAlignment = VerticalAlignment.Start, IsHitTestVisible = false };
    private readonly Icon _emptyIcon = EditorIcons.Icon("box");
    private readonly Label _emptyTitle = new() { HorizontalAlignment = HorizontalAlignment.Center };
    private readonly Label _emptyHint = new() { Wrap = true, HorizontalAlignment = HorizontalAlignment.Center };
    private bool _syncing;

    internal HierarchyPanel(Shell shell) : base("Hierarchy", "Hierarchy")
    {
        ShowScene(shell.SceneName, shell.IsDirty, shell.ScenePath);
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
        Tree.Renamed += (_, node, text) => _shell.RenameWithChanges(EntityOf(node), text);
        Tree.Dropped += (_, drop) => Move(drop);
        Tree.ContextRequested += (_, node, at) => ShowMenu(node, at);
        AddChild(Tree);
        _empty.Classes.Add("empty-state");
        _emptyIcon.Classes.Add("empty-icon");
        _emptyTitle.Classes.Add("placeholder");
        _emptyHint.Classes.Add("empty-hint");
        _empty.Add(_emptyIcon);
        _empty.Add(_emptyTitle);
        _empty.Add(_emptyHint);
        AddChild(_empty);
        AllowDrop = true;
    }

    // Its tab names the open scene (ADR 0133), a dot while it has unsaved changes, its path as the tooltip.
    internal void ShowScene(string name, bool unsaved, string? path)
    {
        TitleDetail = name;
        HasUnsavedChanges = unsaved;
        TitleToolTip = path ?? "Not saved yet";
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
        // A project opening without a scene changes what the empty tree says.
        _stale |= _hadProject != _shell.Project is not null;
        if (!_stale)
        {
            return;
        }
        _stale = false;
        _syncing = true;
        Tree.Refresh();
        Tree.SelectedNode = _shell.Selection.Selected.Value != 0 ? _shell.Selection.Selected.Value : null;
        _syncing = false;
        ShowEmpty();
    }

    // What the tree is when it's empty, and what to do about it.
    private void ShowEmpty()
    {
        bool project = _shell.Project is not null;
        _hadProject = project;
        bool empty = !project || ChildrenOf(null).Count == 0;
        _empty.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        if (!empty)
        {
            return;
        }
        _emptyIcon.Glyph = EditorIcons.Glyph(project ? "box" : "folder-open");
        _emptyTitle.Text = project ? "This scene is empty" : "No project open";
        _emptyHint.Text = project
            ? "Drag a model in from Assets, or right-click here to add an entity."
            : "Open or create one from the File menu.";
    }

    /// <summary>Whether it shows its empty state, and its title (for the checks).</summary>
    internal string? EmptyTitle => _empty.Visibility == Visibility.Visible ? _emptyTitle.Text : null;

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
            return [.. RootOrder.RootsOf(world).Select(entity => entity.Value)];
        }
        Entity entity = EntityOf(value);
        return world.IsAlive(entity) ? [.. SceneIds.ChildrenOf(world, entity).Select(child => child.Value)] : [];
    }

    private void BindRow(TreeRow row)
    {
        Entity entity = EntityOf(row.Node);
        row.Text = _shell.HasWorld ? _shell.LabelOf(entity) : "";
        // Its icon, and a prefab instance's mark (ADR 0131).
        row.Icon.IconFont = EditorIcons.Font;
        string icon = _shell.HasWorld ? _shell.IconOf(entity) : "";
        row.Icon.Glyph = EditorIcons.Glyph(icon);
        row.Icon.ToolTip = icon == Shell.PrefabIcon ? "A prefab instance" : "";
        row.Mark.IconFont = EditorIcons.Font;
        bool instance = _shell.HasWorld && _shell.World.IsAlive(entity) &&
                        _shell.World.Has(entity, Components.Of<PrefabInstance>()) && icon != Shell.PrefabIcon;
        row.Mark.Glyph = instance ? EditorIcons.Glyph(Shell.PrefabIcon) : "";
        row.Mark.ToolTip = instance ? "A prefab instance" : "";
    }

    // Before or after a row: beside it, under its parent; inside: its last child.
    private void Move(TreeDrop drop)
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
        // Among the parent's children, or the top-level entities (ADR 0130).
        Entity[] others = [.. (parent.IsNull ? RootOrder.RootsOf(world) : SceneIds.ChildrenOf(world, parent))
            .Where(child => child.Value != entity.Value)];
        int index = Array.IndexOf(others, target) + (drop.Position == TreeDropPosition.After ? 1 : 0);
        _shell.Reparent(entity, parent, index);
    }

    private static Entity EntityOf(ulong node) => new() { Value = node };

    // An asset dropped here (ADR 0071): placed under the row it's on, or at the top below the rows.
    // Files from other programs (ADR 0115) too: imported, then placed the same way.
    protected override void OnDragOver(DragEvent e)
    {
        if (e.Payload is FileDrag files)
        {
            e.Accepted = true;
            files.OnHierarchy = true;
            files.Parent = RowAt(e.Position);
            return;
        }
        e.Accepted = _shell.CanPlace(e.Payload);
    }

    private Entity RowAt(System.Numerics.Vector2 position)
    {
        for (int i = 0; i < Tree.RowCount; ++i)
        {
            if (Tree.RowOf(Tree.RowNode(i))?.Parent is { } row && row.Bounds.Contains(position.X, position.Y))
            {
                return EntityOf(Tree.RowNode(i));
            }
        }
        return Entity.Null;
    }

    protected override void OnDrop(DragEvent e)
    {
        if (e.Payload is not AssetDrag drag)
        {
            return;
        }
        Entity parent = RowAt(e.Position);
        Entity placed = _shell.Place(drag, parent);
        if (!placed.IsNull && !parent.IsNull)
        {
            Tree.SetExpanded(parent.Value, true);
        }
    }

    private void ShowMenu(ulong? node, Vector2 at)
    {
        ContextMenu menu = new();
        Entity entity = node is { } value ? EntityOf(value) : Entity.Null;
        menu.Add("Create Empty", () => _shell.CreateEmpty(entity));
        Menu twoD = new();
        twoD.Add("Point Light", () => _shell.CreateLight2D(entity, Light2DKind.Point));
        twoD.Add("Spot Light", () => _shell.CreateLight2D(entity, Light2DKind.Spot));
        twoD.Add("Ambient Light", () => _shell.CreateLight2D(entity, Light2DKind.Ambient));
        menu.Add(new MenuItem("2D") { Submenu = twoD });
        // Extensions' (ADR 0122), by their paths.
        _shell.AddCreateItems(menu, entity);
        // Its editors (ADR 0120): "Open in Map", and extensions'.
        if (node is not null && _shell.HasWorld && _shell.World.IsAlive(entity))
        {
            foreach ((string id, string title) in _shell.EditorsFor(entity))
            {
                menu.Add($"Open in {title}", () => _shell.OpenEntityIn(entity, id));
            }
        }
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
