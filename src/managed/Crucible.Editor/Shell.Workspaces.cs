using System.Text.Json;
using System.Text.Json.Nodes;
using Crucible.Assets;
using Crucible.Ecs;
using Crucible.UI;

namespace Crucible.Editor;

/// <summary>
/// Workspaces (ADR 0119): registered by ID (extensions', official plugins'
/// among them: map mode, ADR 0123), opened as tabs beside the Viewport for what they work
/// on, asked each frame whether they go on, their views rendered while
/// shown, their keys first while they have focus, and saved with the layout
/// to come back once what they work on is open again.
/// </summary>
internal sealed partial class Shell
{
    private readonly Dictionary<string, (string Title, Func<Workspace> Create, EditorContext? Owner)> _workspaceTypes =
        new(StringComparer.Ordinal);

    /// <summary>The workspaces open now, as tabs.</summary>
    internal IEnumerable<Workspace> Workspaces => _open.Values.OfType<Workspace>();

    internal bool AddWorkspace(string id, string title, Func<Workspace> create, EditorContext? owner)
    {
        if (_workspaceTypes.ContainsKey(id) || Panels.Contains(id))
        {
            LogWarning($"{owner?.Extension ?? "The editor"}: a workspace \"{id}\" is already registered");
            return false;
        }
        _workspaceTypes[id] = (title, create, owner);
        owner?.OnStop(() => _workspaceTypes.Remove(id));
        return true;
    }

    /// <summary>
    /// A workspace's tab for an entity of the open scene or an asset (or
    /// neither): the one already open for it shown; else, for a workspace
    /// with one tab, that tab moved to it; else a new tab beside the
    /// Viewport. Null if it can't be opened.
    /// </summary>
    internal Workspace? OpenWorkspace(string id, Entity entity, AssetId asset)
    {
        if (!_workspaceTypes.TryGetValue(id, out var type))
        {
            LogWarning($"There's no workspace \"{id}\"");
            return null;
        }
        ulong key = 0;
        AssetId scene = AssetId.None;
        if (!entity.IsNull)
        {
            key = HasLive(entity) ? SceneIds.IdOf(World, entity) : 0;
            if (key == 0)
            {
                return null;
            }
            scene = SceneId;
        }
        Workspace[] tabs = [.. Workspaces.Where(workspace => workspace.WorkspaceId == id)];
        if (tabs.FirstOrDefault(tab => tab.EntityKey == key && tab.Scene == scene && tab.Asset == asset) is { } same)
        {
            same.Group?.Activate(same);
            return same;
        }
        Workspace? replaced = tabs.FirstOrDefault(tab => !tab.AllowsSeveral);
        string tabId = replaced?.Id ?? (tabs.Length == 0 ? id : FreeTabId(id));
        if (MakeWorkspace(type, new WorkspaceSubject(id, tabId, key, scene, asset)) is not { } made)
        {
            return null;
        }
        // One that wouldn't stay open (nothing it can work on) isn't opened,
        // rather than shown for a frame; it was never open, so not Closed.
        if (!KeepsOpen(made))
        {
            return null;
        }
        if (replaced is not null)
        {
            ClosedGuarded(replaced);
            Replace(replaced, made);
        }
        else if (_open.GetValueOrDefault("Viewport")?.Group is { } beside)
        {
            beside.Add(made);
        }
        else
        {
            Main.DockSpace.Dock(made, null, DockTarget.Center);
        }
        _open[tabId] = made;
        if (type.Owner is { } owner)
        {
            _extensionPanels[tabId] = owner;
        }
        made.Group?.Activate(made);
        return made;
    }

    private string FreeTabId(string id)
    {
        int n = 2;
        while (_open.ContainsKey($"{id}#{n}"))
        {
            ++n;
        }
        return $"{id}#{n}";
    }

    // Made with its subject known to its constructor; its code guarded.
    private Workspace? MakeWorkspace((string Title, Func<Workspace> Create, EditorContext? Owner) type, WorkspaceSubject subject)
    {
        Workspace.Opening = subject;
        try
        {
            Workspace made = type.Create();
            if (made.Id != subject.TabId || made.WorkspaceId != subject.WorkspaceId)
            {
                LogError($"{subject.WorkspaceId}: its workspace was made outside its own making");
                return null;
            }
            return made;
        }
        catch (Exception exception)
        {
            LogError($"{type.Owner?.Extension ?? "The editor"}: the {type.Title} workspace can't be opened: {Message(exception)}");
            return null;
        }
        finally
        {
            Workspace.Opening = null;
        }
    }

    /// <summary>Closes a workspace's tab: Done, as its × does.</summary>
    internal void CloseWorkspace(Workspace workspace)
    {
        if (_open.TryGetValue(workspace.Id, out EditorPanel? open) && open == workspace)
        {
            ClosePanel(workspace);
        }
    }

    private bool KeepsOpen(Workspace workspace)
    {
        try
        {
            return workspace.KeepOpen();
        }
        catch (Exception exception)
        {
            LogError($"{workspace.Title}: KeepOpen failed: {Message(exception)}");
            return true;
        }
    }

    private void ClosedGuarded(Workspace workspace)
    {
        try
        {
            workspace.Closed();
        }
        catch (Exception exception)
        {
            LogError($"{workspace.Title}: closing failed: {Message(exception)}");
        }
    }

    /// <summary>A workspace's entity: in the open scene, the scene it was opened in; else none.</summary>
    internal Entity WorkspaceEntity(Workspace workspace)
    {
        if (workspace.EntityKey == 0 || workspace.Scene != SceneId || !_context.HasWorld)
        {
            return Entity.Null;
        }
        Entity entity = SceneIds.Find(World, workspace.EntityKey);
        return HasLive(entity) ? entity : Entity.Null;
    }

    // Each frame: workspaces asked whether they go on, their toolbars and
    // parts brought up to date; placeholders made into workspaces once
    // what they work on is open.
    private void TickWorkspaces()
    {
        foreach (Workspace workspace in Workspaces.ToArray())
        {
            if (!KeepsOpen(workspace))
            {
                ClosePanel(workspace);
                continue;
            }
            workspace.Toolbar.Refresh();
        }
        foreach (PendingPanel pending in _open.Values.OfType<PendingPanel>().Where(panel => panel.Workspace is not null).ToArray())
        {
            ResolveWorkspace(pending);
        }
    }

    // A placeholder made into its workspace, once it's registered and what
    // it works on is open: in its place, with its sizes and state.
    private void ResolveWorkspace(PendingPanel pending)
    {
        JsonObject info = pending.Workspace!;
        string? id = Text(info["Id"]);
        if (id is null || !_workspaceTypes.TryGetValue(id, out var type))
        {
            return;
        }
        ulong key = ulong.TryParse(Text(info["Entity"]), out ulong parsed) ? parsed : 0;
        AssetId scene = AssetId.TryParse(Text(info["Scene"]) ?? "", out AssetId sceneId) ? sceneId : AssetId.None;
        AssetId asset = AssetId.TryParse(Text(info["Asset"]) ?? "", out AssetId assetId) ? assetId : AssetId.None;
        bool ready = (key == 0 || (scene == SceneId && _context.HasWorld && HasLive(SceneIds.Find(World, key)))) &&
                     (asset.IsNone || Database?.Find(asset) is not null);
        if (!ready || MakeWorkspace(type, new WorkspaceSubject(id, pending.Id, key, scene, asset)) is not { } made)
        {
            return;
        }
        Replace(pending, made);
        made.Columns?.SetSizes(Sizes(info["Columns"]));
        made.Rows?.SetSizes(Sizes(info["Rows"]));
        if (type.Owner is { } owner)
        {
            _extensionPanels[pending.Id] = owner;
        }
        if (pending.Saved is { IsEmpty: false } state)
        {
            LoadStateOf(made, state);
        }
    }

    // What the layout keeps of a workspace's tab: which it is, what it works on, its splits' sizes.
    private static JsonObject WorkspaceInfo(Workspace workspace) => new()
    {
        ["Id"] = workspace.WorkspaceId,
        ["Entity"] = workspace.EntityKey == 0 ? null : workspace.EntityKey.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["Scene"] = workspace.Scene.IsNone ? null : workspace.Scene.ToString(),
        ["Asset"] = workspace.Asset.IsNone ? null : workspace.Asset.ToString(),
        ["Columns"] = SizesJson(workspace.Columns),
        ["Rows"] = SizesJson(workspace.Rows),
    };

    private static JsonArray? SizesJson(DockSplit? split) =>
        split is null ? null : new JsonArray([.. split.Sizes.Select(size => (JsonNode)JsonValue.Create(size))]);

    private static float[] Sizes(JsonNode? node) =>
        node is JsonArray sizes
            ? [.. sizes.Select(size => size?.GetValueKind() == JsonValueKind.Number ? size.GetValue<float>() : float.NaN)]
            : [];

    private static string? Text(JsonNode? node) => node?.GetValueKind() == JsonValueKind.String ? node.GetValue<string>() : null;

    // Keys go to the workspace focus is in, before the editor's.
    private bool WorkspaceKey(UIElement element, KeyEvent e)
    {
        for (UIElement? at = element.Root?.FocusedElement; at is not null; at = at.Parent)
        {
            if (at is Workspace workspace)
            {
                return workspace.Commands.HandleShortcut(e);
            }
        }
        return false;
    }

    // The views of the workspaces shown, rendered with the Viewport's.
    private void RenderWorkspaces(World world, ReadOnlySpan<Entity> outlined)
    {
        foreach (Workspace workspace in Workspaces)
        {
            if (workspace.Root is not null && workspace.Group?.ActivePanel == workspace)
            {
                RenderViews(workspace, world, outlined);
            }
        }
    }

    private static void RenderViews(UIElement element, World world, ReadOnlySpan<Entity> outlined)
    {
        foreach (UIElement child in element.Children)
        {
            if (child is SceneView view)
            {
                view.Render(world, view.ShowsOutlines ? outlined : []);
            }
            else
            {
                RenderViews(child, world, outlined);
            }
        }
    }
}
