using Crucible.Assets;
using Crucible.Ecs;
using Crucible.UI;

namespace Crucible.Editor;

/// <summary>
/// A workspace (ADR 0119): a tab among the editor's panels for working on
/// one thing (a map, a character), opened from what it's for and docked
/// beside the Viewport. It has its own parts in fixed splits
/// (<see cref="Arrange"/>), often a <see cref="SceneView"/> of its own; tools
/// above them (<see cref="Toolbar"/>, ending in Done); and keys
/// (<see cref="Commands"/>) that win over the editor's while it has focus.
/// It stays open until Done, its tab's ×, or <see cref="KeepOpen"/> saying
/// what it works on is gone, and comes back after a restart.
/// </summary>
/// <remarks>
/// Its subject, <see cref="Entity"/> or <see cref="Asset"/>, is set before
/// the subclass's constructor runs, so the title can name it.
/// </remarks>
public abstract class Workspace : EditorPanel
{
    // What the workspace being made is for, set by the shell around its making.
    [ThreadStatic]
    internal static WorkspaceSubject? Opening;

    private readonly DockPanel _page = new();
    private UIElement? _body;

    protected Workspace(string title) : base(Opening?.TabId ?? "Workspace", title)
    {
        WorkspaceSubject subject = Opening ?? new WorkspaceSubject("Workspace", "Workspace", 0, AssetId.None, AssetId.None);
        WorkspaceId = subject.WorkspaceId;
        EntityKey = subject.EntityKey;
        Scene = subject.Scene;
        Asset = subject.Asset;
        Toolbar = new WorkspaceToolbar(this);
        DockPanel.SetDock(Toolbar, Dock.Top);
        _page.Add(Toolbar);
        AddChild(_page);
    }

    /// <summary>The entity it works on, if it was opened for one and it's in the open scene; else none.</summary>
    public Entity Entity => Shell.Current?.WorkspaceEntity(this) ?? Entity.Null;

    /// <summary>The asset it works on, if it was opened for one; else none.</summary>
    public AssetId Asset { get; }

    /// <summary>Its tools, above its parts; Done at the end closes it.</summary>
    public WorkspaceToolbar Toolbar { get; }

    /// <summary>Its keys: run while focus is in its tab, before the editor's own.</summary>
    public CommandRegistry Commands { get; } = new();

    /// <summary>
    /// Whether it may have several tabs at once, one per subject; when not,
    /// opening it for something else moves its one tab there.
    /// </summary>
    public virtual bool AllowsSeveral => false;

    /// <summary>
    /// Asked as it opens (false: it isn't opened) and each frame after
    /// (false closes its tab: what it works on is gone). By
    /// default, while its entity is in the scene it was opened in, and its
    /// asset is in the project.
    /// </summary>
    public virtual bool KeepOpen()
    {
        Shell? shell = Shell.Current;
        if (shell is null)
        {
            return true;
        }
        if (EntityKey != 0 && Entity.IsNull)
        {
            return false;
        }
        return Asset.IsNone || shell.Database?.Find(Asset) is not null;
    }

    /// <summary>Called as its tab closes, by Done, its ×, or <see cref="KeepOpen"/>.</summary>
    public virtual void Closed()
    {
    }

    /// <summary>
    /// Its parts in fixed splits: the left, the center, and the right side
    /// by side, the bottom under them all. Splits are resizable, and their
    /// sizes are remembered.
    /// </summary>
    protected void Arrange(UIElement? left, UIElement center, UIElement? right = null, UIElement? bottom = null)
    {
        ArgumentNullException.ThrowIfNull(center);
        if (_body is not null)
        {
            _page.Remove(_body);
        }
        Columns = new DockSplit(Orientation.Horizontal) { HoldsAnything = true };
        if (left is not null)
        {
            Columns.Add(left, 0.24f);
        }
        Columns.Add(center, 0.52f);
        if (right is not null)
        {
            Columns.Add(right, 0.24f);
        }
        Rows = null;
        _body = Columns;
        if (bottom is not null)
        {
            Rows = new DockSplit(Orientation.Vertical) { HoldsAnything = true };
            Rows.Add(Columns, 0.72f);
            Rows.Add(bottom, 0.28f);
            _body = Rows;
        }
        _page.Add(_body);
    }

    // The registered workspace it is, and what it was opened for.
    internal string WorkspaceId { get; }

    internal ulong EntityKey { get; }

    internal AssetId Scene { get; }

    internal DockSplit? Columns { get; private set; }

    internal DockSplit? Rows { get; private set; }

    /// <summary>Each frame while it's open, for the editor's own workspaces' parts.</summary>
    internal virtual void Tick()
    {
    }
}

/// <summary>A workspace's tools (ADR 0119): commands as buttons, other controls, and Done.</summary>
public sealed class WorkspaceToolbar : UIElement
{
    private readonly StackPanel _items = new() { Orientation = Orientation.Horizontal, Spacing = 4 };
    private readonly List<(Button Button, Command Command)> _commands = [];

    internal WorkspaceToolbar(Workspace workspace)
    {
        DockPanel row = new();
        row.Classes.Add("viewport-tools");
        Button done = new("Done") { VerticalAlignment = VerticalAlignment.Center };
        done.Classes.Add("primary");
        done.Clicked += _ => Shell.Current?.CloseWorkspace(workspace);
        DockPanel.SetDock(done, Dock.Right);
        row.Add(done);
        row.Add(_items);
        AddChild(row);
    }

    /// <summary>A command's button: it runs the command, and shows whether it's on (an on-off command).</summary>
    public Button Add(Command command)
    {
        ArgumentNullException.ThrowIfNull(command);
        Button button = new(command.Title) { VerticalAlignment = VerticalAlignment.Center };
        button.Classes.Add("tool");
        button.Clicked += _ => command.Execute();
        _items.Add(button);
        _commands.Add((button, command));
        return button;
    }

    /// <summary>Any other control (a toggle, a row of its own), after what's there.</summary>
    public void Add(UIElement item) => _items.Add(item);

    // Each frame: the buttons follow their commands.
    internal void Refresh()
    {
        foreach ((Button button, Command command) in _commands)
        {
            button.Classes.Set("on", command.IsChecked?.Invoke() == true);
            button.Text = command.CurrentTitle?.Invoke() ?? command.Title;
        }
    }
}

/// <summary>What a workspace is being opened for, and the tab it's in.</summary>
internal sealed record WorkspaceSubject(string WorkspaceId, string TabId, ulong EntityKey, AssetId Scene, AssetId Asset);
