using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using System.Text.Json.Nodes;
using Crucible.Assets;
using Crucible.Ecs;
using Crucible.UI;

namespace Crucible.Editor;

/// <summary>
/// Extensions (ADR 0117): the <see cref="EditorExtension"/>s of the
/// project's editor assembly and its source plugins' (ADR 0118), started
/// after each load and stopped before the next, their panels made again in
/// place with their state, their commands in the menus; and compiled
/// plugins', started once when the project opens, kept across reloads.
/// </summary>
internal sealed partial class Shell
{
    // Kept: a compiled plugin's, which outlives the game's reloads.
    private readonly List<(EditorExtension Extension, EditorContext Context, bool Kept)> _extensions = [];
    // The panels extensions registered, by ID, and whose they are.
    private readonly Dictionary<string, EditorContext> _extensionPanels = new(StringComparer.Ordinal);
    // Menus extensions added to: the separator before their items in a menu of
    // the editor's own, and how many there are.
    private readonly Dictionary<Menu, (MenuSeparator? Separator, int Items)> _extensionSections = [];
    private Menu? _windowMenu;
    // While a layout is restored: its panels' saved states.
    private JsonObject? _layoutStates;

    /// <summary>The extensions running now, for the checks.</summary>
    internal IEnumerable<EditorExtension> Extensions => _extensions.Select(extension => extension.Extension);

    /// <summary>An error in the Log, marked and coloured as the core's are.</summary>
    public void LogError(string message)
    {
        Console.WriteLine($"Crucible.Editor: Error: {message}");
        LogPanel?.Add($"Error: {message}", "log-error");
        Logged?.Invoke(message);
        Main.StatusBar.Text = $"Error: {message}";
    }

    /// <summary>Opens a registered panel, or shows it if it's open; false for one not registered.</summary>
    public bool OpenPanel(string id)
    {
        if (_open.TryGetValue(id, out EditorPanel? open))
        {
            open.Group?.Activate(open);
            return true;
        }
        if (!Panels.Contains(id) || Panel(id) is not { } made)
        {
            return false;
        }
        Main.DockSpace.Dock(made, null, DockTarget.Center);
        return true;
    }

    public void ClosePanel(string id)
    {
        if (_open.TryGetValue(id, out EditorPanel? panel))
        {
            ClosePanel(panel);
        }
    }

    // -----------------------------------------------------------------------
    // Starting and stopping
    // -----------------------------------------------------------------------

    // After a load: the source plugins' extensions, in their order, then the game's.
    private void StartExtensions()
    {
        if (_game is { } game)
        {
            foreach (Assembly? assembly in (Assembly?[])[.. game.PluginEditorAssemblies, game.EditorAssembly])
            {
                if (assembly is not null)
                {
                    StartExtensions(assembly, kept: false);
                }
            }
        }
        ResolvePending();
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = JitOnly)]
    [UnconditionalSuppressMessage("Trimming", "IL2072", Justification = JitOnly)]
    private void StartExtensions(Assembly assembly, bool kept)
    {
        foreach (Type type in ExtensionTypes(assembly))
        {
            string name = type.FullName ?? type.Name;
            EditorExtension extension;
            try
            {
                extension = (EditorExtension)Activator.CreateInstance(type)!;
            }
            catch (Exception exception) when (exception is MissingMethodException or MemberAccessException or TargetInvocationException)
            {
                LogError($"Can't make {name}: {Message(exception)}");
                continue;
            }
            EditorContext context = new(this, name);
            _extensions.Add((extension, context, kept));
            Guard(context, "Start", () => extension.Start(context));
        }
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = JitOnly)]
    private static IEnumerable<Type> ExtensionTypes(Assembly assembly)
    {
        Type?[] types;
        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            types = exception.Types;
        }
        return types.OfType<Type>()
            .Where(type => type is { IsClass: true, IsAbstract: false } && type.IsSubclassOf(typeof(EditorExtension)))
            .OrderBy(type => type.FullName, StringComparer.Ordinal);
    }

    /// <summary>
    /// Stops the extensions that reload with the game (<paramref name="all"/>:
    /// compiled plugins' too) and takes out what they added. Their open
    /// panels become placeholders holding their state (<paramref name="keepPanels"/>,
    /// a reload) or close (another project). Then nothing of the old code is
    /// left for it to unload: the handlers it added to the selection and the
    /// history, and undo steps of its own types, the history cut after the
    /// newest of them.
    /// </summary>
    private void StopExtensions(bool keepPanels, bool all = false)
    {
        List<(EditorExtension Extension, EditorContext Context, bool Kept)> stopping =
            [.. _extensions.Where(extension => all || !extension.Kept)];
        if (stopping.Count == 0)
        {
            return;
        }
        for (int i = stopping.Count - 1; i >= 0; --i)
        {
            (EditorExtension extension, EditorContext context, _) = stopping[i];
            Guard(context, "Stop", extension.Stop);
        }
        HashSet<EditorContext> contexts = [.. stopping.Select(extension => extension.Context)];
        foreach ((string id, EditorContext owner) in _extensionPanels.ToArray())
        {
            if (!contexts.Contains(owner))
            {
                continue;
            }
            if (_open.TryGetValue(id, out EditorPanel? panel) && panel is not PendingPanel)
            {
                if (keepPanels)
                {
                    // A workspace's tab (ADR 0119) waits for its workspace again.
                    JsonObject? workspace = panel is Workspace open ? WorkspaceInfo(open) : null;
                    PanelState state = StateOf(panel);
                    if (panel is Workspace closing)
                    {
                        ClosedGuarded(closing);
                    }
                    Replace(panel, new PendingPanel(id, panel.Title, state) { Workspace = workspace });
                }
                else
                {
                    ClosePanel(panel);
                }
            }
            Panels.Unregister(id);
            _extensionPanels.Remove(id);
        }
        for (int i = stopping.Count - 1; i >= 0; --i)
        {
            stopping[i].Context.Cleanup();
        }
        _extensions.RemoveAll(extension => contexts.Contains(extension.Context));
        // The game's context, where these extensions' code came from.
        Assembly? loaded = _game?.EditorAssembly ?? _game?.PluginEditorAssemblies.FirstOrDefault();
        if (loaded is not null && AssemblyLoadContext.GetLoadContext(loaded) is { } old)
        {
            Forget(old);
        }
    }

    private void Forget(AssemblyLoadContext old)
    {
        bool Old(Type? type) => type is not null && AssemblyLoadContext.GetLoadContext(type.Assembly) == old;
        bool OldHandler(Delegate handler) => Old(handler.Method.DeclaringType) || Old(handler.Target?.GetType());
        Selection.RemoveHandlers(OldHandler);
        History.RemoveHandlers(OldHandler);
        (int dropped, string? title) = History.DropSteps(step => Old(step.GetType()));
        if (_historyAtPlay is { } atPlay)
        {
            _historyAtPlay = UndoHistory.DropSteps(atPlay, step => Old(step.GetType()));
        }
        if (dropped > 0)
        {
            LogWarning($"Undo can't go back past \"{title}\": its step came from the editor code just reloaded ({dropped} steps dropped)");
        }
    }

    // Placeholders whose panels are registered now are made again, with
    // their state; the rest close (an extension that no longer has them).
    private void ResolvePending()
    {
        foreach (EditorPanel panel in _open.Values.ToArray())
        {
            if (panel is not PendingPanel pending || pending.Workspace is not null)
            {
                continue;
            }
            if (!Panels.Contains(pending.Id) || Panels.Create(pending.Id) is not { } made)
            {
                ClosePanel(pending);
                continue;
            }
            Replace(pending, made);
            if (pending.Saved is { IsEmpty: false } state)
            {
                LoadStateOf(made, state);
            }
        }
    }

    // One panel for another in the same place: its group, its tab's index,
    // and shown if it was.
    private void Replace(EditorPanel old, EditorPanel made)
    {
        _open[old.Id] = made;
        if (old.Group is not { } group)
        {
            return;
        }
        int at = 0;
        while (at < group.Panels.Count && group.Panels[at] != old)
        {
            ++at;
        }
        EditorPanel? shown = group.ActivePanel;
        group.Add(made, at);
        group.Remove(old);
        group.Activate(shown == old ? made : shown ?? made);
    }

    private PanelState StateOf(EditorPanel panel)
    {
        if (panel is PendingPanel pending)
        {
            return pending.Saved ?? new PanelState();
        }
        PanelState state = new();
        try
        {
            panel.SaveState(state);
        }
        catch (Exception exception)
        {
            LogError($"The {panel.Title} panel's state can't be saved: {Message(exception)}");
        }
        return state;
    }

    private void LoadStateOf(EditorPanel panel, PanelState state)
    {
        try
        {
            panel.LoadState(state);
        }
        catch (Exception exception)
        {
            LogError($"The {panel.Title} panel's state can't be loaded: {Message(exception)}");
        }
    }

    // The layout's panel states (ADR 0117): those with any, and every
    // extension's panel, so a placeholder can stand in for it until its code
    // has loaded.
    private JsonObject SavePanelStates()
    {
        JsonObject states = [];
        foreach ((string id, EditorPanel panel) in _open)
        {
            bool extension = panel is PendingPanel || _extensionPanels.ContainsKey(id);
            PanelState state = StateOf(panel);
            // A workspace's tab (ADR 0119): which it is and what it works on.
            JsonObject? workspace = panel is Workspace open ? WorkspaceInfo(open)
                : (panel as PendingPanel)?.Workspace?.DeepClone().AsObject();
            if (extension || workspace is not null || !state.IsEmpty)
            {
                states[id] = new JsonObject
                {
                    ["Title"] = panel.Title,
                    ["Extension"] = extension,
                    ["Values"] = state.ToJson(),
                    ["Workspace"] = workspace,
                };
            }
        }
        return states;
    }

    // A panel the restored layout names: made, with its saved state; a
    // extension's not registered yet, a placeholder; null for neither.
    private EditorPanel? RestorePanel(string id)
    {
        JsonObject? saved = _layoutStates?[id] as JsonObject;
        PanelState? state = saved?["Values"] is JsonObject values ? new PanelState(values) : null;
        // A workspace's tab (ADR 0119) waits until what it works on is open.
        if (saved?["Workspace"] is JsonObject workspace)
        {
            string tab = saved["Title"]?.GetValueKind() == JsonValueKind.String ? saved["Title"]!.GetValue<string>() : id;
            PendingPanel waiting = new(id, tab, state, $"{tab} opens again when what it works on is open.")
            {
                Workspace = workspace.DeepClone().AsObject(),
            };
            _open[id] = waiting;
            return waiting;
        }
        if (Panel(id) is { } panel)
        {
            if (state is { IsEmpty: false } && panel is not PendingPanel)
            {
                LoadStateOf(panel, state);
            }
            return panel;
        }
        // "Plugin" in layouts saved before ADR 0118 renamed it.
        if (saved is null || (saved["Extension"] ?? saved["Plugin"])?.GetValueKind() != JsonValueKind.True)
        {
            return null;
        }
        string title = saved["Title"]?.GetValueKind() == JsonValueKind.String ? saved["Title"]!.GetValue<string>() : id;
        PendingPanel pending = new(id, title, state);
        _open[id] = pending;
        return pending;
    }

    // -----------------------------------------------------------------------
    // What extensions add
    // -----------------------------------------------------------------------

    internal bool AddExtensionPanel(EditorContext context, string id, string title, Func<EditorPanel> create)
    {
        if (Panels.Contains(id))
        {
            LogWarning($"{context.Extension}: a panel \"{id}\" is already registered");
            return false;
        }
        Panels.Register(id, title, () => MakeExtensionPanel(context, id, title, create));
        _extensionPanels[id] = context;
        string commandId = $"Window.{id}";
        if (Commands.Find(commandId) is null)
        {
            Command toggle = Commands.Add(new Command(commandId, title, () => TogglePanel(id))
            {
                IsChecked = () => _open.ContainsKey(id),
            });
            context.OnStop(() => Commands.Remove(toggle.Id));
            if (_windowMenu is { } window)
            {
                MenuItem item = new(toggle);
                Place(window, item);
                context.OnStop(() => Unplace(window, item));
            }
        }
        return true;
    }

    private EditorPanel MakeExtensionPanel(EditorContext context, string id, string title, Func<EditorPanel> create)
    {
        EditorPanel? made;
        try
        {
            made = create();
        }
        catch (Exception exception)
        {
            LogError($"{context.Extension}: the {title} panel can't be made: {Message(exception)}");
            return new PendingPanel(id, title, null, $"{title} couldn't be made: {Message(exception)}");
        }
        if (made is null || made.Id != id)
        {
            LogError($"{context.Extension}: the {title} panel was made with the ID \"{made?.Id}\", not \"{id}\"");
            return new PendingPanel(id, title, null, $"{title} couldn't be made.");
        }
        return made;
    }

    internal bool AddExtensionCommand(EditorContext context, Command command, string? menu)
    {
        if (Commands.Find(command.Id) is not null)
        {
            LogWarning($"{context.Extension}: a command \"{command.Id}\" is already registered");
            return false;
        }
        Shortcut? shortcut = command.Shortcut;
        if (shortcut is { } wanted && Commands.Commands.FirstOrDefault(other => other.Shortcut == wanted) is { } taken)
        {
            LogWarning($"{command.Id} wants {wanted}, already used by {taken.Id}; it has no shortcut");
            shortcut = null;
        }
        // The extension's code runs guarded: an exception is logged, not the editor's end.
        Func<bool>? isChecked = command.IsChecked;
        Func<string>? currentTitle = command.CurrentTitle;
        Command wrapped = Commands.Add(new Command(command.Id, command.Title,
            () => Guard(context, command.Id, () => command.Execute()),
            () => Guard(context, command.Id, () => command.CanExecute, false))
        {
            Shortcut = shortcut,
            IsChecked = isChecked is null ? null : () => Guard(context, command.Id, isChecked, false),
            CurrentTitle = currentTitle is null ? null : () => Guard(context, command.Id, currentTitle, command.Title),
        });
        context.OnStop(() => Commands.Remove(wrapped.Id));
        if (menu is not null && MenuAt(context, menu) is { } target)
        {
            MenuItem item = new(wrapped);
            Place(target, item);
            context.OnStop(() => Unplace(target, item));
        }
        return true;
    }

    // The menu a path names ("Tools/Walker"), made as needed: a top-level
    // menu before Window, submenus as items. Null for an empty path.
    private Menu? MenuAt(EditorContext context, string path)
    {
        string[] parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return null;
        }
        MenuBar bar = Main.MenuBar;
        Menu? menu = bar.Menus.FirstOrDefault(menu => menu.Title == parts[0]);
        if (menu is null)
        {
            Menu made = new(parts[0]);
            int window = _windowMenu is null ? -1 : IndexOf(bar.Menus, _windowMenu);
            bar.Insert(window < 0 ? bar.Menus.Count : window, made);
            context.OnStop(() =>
            {
                if (made.Items.Count == 0)
                {
                    bar.Remove(made);
                }
            });
            menu = made;
        }
        foreach (string part in parts.Skip(1))
        {
            if (menu.Items.OfType<MenuItem>().FirstOrDefault(item => item.Submenu is not null && item.Title == part) is { } existing)
            {
                menu = existing.Submenu!;
                continue;
            }
            Menu parent = menu;
            Menu submenu = new(part);
            MenuItem item = new(part) { Submenu = submenu };
            Place(parent, item);
            context.OnStop(() =>
            {
                if (submenu.Items.Count == 0)
                {
                    Unplace(parent, item);
                }
            });
            menu = submenu;
        }
        return menu;
    }

    private static int IndexOf(IReadOnlyList<Menu> menus, Menu menu)
    {
        for (int i = 0; i < menus.Count; ++i)
        {
            if (menus[i] == menu)
            {
                return i;
            }
        }
        return -1;
    }

    // An extension's item at the end of the extensions' part of a menu: after a
    // separator in a menu of the editor's own; in Window, before Reset Layout.
    private void Place(Menu menu, MenuItem item)
    {
        (MenuSeparator? separator, int items) = _extensionSections.GetValueOrDefault(menu);
        if (separator is null && items == 0 && menu.Items.Count > 0)
        {
            separator = menu.Insert(SectionEnd(menu), new MenuSeparator());
        }
        menu.Insert(SectionEnd(menu), item);
        _extensionSections[menu] = (separator, items + 1);
    }

    private void Unplace(Menu menu, MenuItem item)
    {
        menu.Remove(item);
        if (!_extensionSections.TryGetValue(menu, out (MenuSeparator? Separator, int Items) section))
        {
            return;
        }
        if (section.Items > 1)
        {
            _extensionSections[menu] = (section.Separator, section.Items - 1);
            return;
        }
        if (section.Separator is { } separator)
        {
            menu.Remove(separator);
        }
        _extensionSections.Remove(menu);
    }

    private int SectionEnd(Menu menu)
    {
        if (menu != _windowMenu)
        {
            return menu.Items.Count;
        }
        for (int i = menu.Items.Count - 1; i >= 0; --i)
        {
            if (menu.Items[i] is MenuSeparator)
            {
                return i;
            }
        }
        return menu.Items.Count;
    }

    // -----------------------------------------------------------------------
    // Edits for extensions
    // -----------------------------------------------------------------------

    internal bool SetExtensionComponent<T>(Entity entity, in T value, string title, bool adding) where T : unmanaged
    {
        string name = Components.NameOf(typeof(T));
        if (!HasLive(entity) || SavedComponents.Find(name) is null || World.Has(entity, Components.Of<T>()) == adding)
        {
            return false;
        }
        JsonNode? before = SceneIds.Read(World, entity, name);
        World.Set(entity, value);
        RecordComponent(entity, name, before, title);
        return true;
    }

    internal bool RemoveExtensionComponent<T>(Entity entity, string title) where T : unmanaged
    {
        string name = Components.NameOf(typeof(T));
        if (!HasLive(entity) || SavedComponents.Find(name) is null || !World.Has(entity, Components.Of<T>()))
        {
            return false;
        }
        SetComponent(entity, name, null, title);
        return true;
    }

    internal bool WriteExtensionFile(string path, byte[] contents, string title)
    {
        if (Project is not { } project || string.IsNullOrWhiteSpace(path))
        {
            return false;
        }
        // The game's Assets, or a plugin's ("Dialogue:Notes/Todo.txt", ADR 0118).
        string relative = path.Replace('\\', '/');
        string prefix = AssetPaths.RootOf(relative);
        string within = AssetPaths.WithinRoot(relative);
        if (AssetPaths.PluginOf(relative) is { } plugin && project.PluginNamed(plugin) is null)
        {
            LogWarning($"Can't write {path}: there's no plugin {plugin}");
            return false;
        }
        string root = Path.GetFullPath(project.FileOf(prefix));
        string file = Path.GetFullPath(Path.Combine(root, within));
        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (Path.IsPathRooted(within) || !file.StartsWith(root + Path.DirectorySeparatorChar, comparison))
        {
            LogWarning($"Can't write {path}: it isn't in the Assets folder");
            return false;
        }
        relative = prefix + Path.GetRelativePath(root, file).Replace('\\', '/');
        if (!CanChange(relative))
        {
            return false;
        }
        byte[]? before;
        try
        {
            before = File.Exists(file) ? File.ReadAllBytes(file) : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Log($"Can't read {relative}: {exception.Message}");
            return false;
        }
        if (before is not null && before.AsSpan().SequenceEqual(contents))
        {
            return true;
        }
        byte[] after = [.. contents];
        if (!AssetFileEdit.Write(this, relative, after))
        {
            return false;
        }
        History.Push(new AssetFileEdit(this, title, relative, before, after));
        return true;
    }

    // -----------------------------------------------------------------------
    // Running extension code
    // -----------------------------------------------------------------------

    internal void GuardExtension(EditorContext context, string what, Action run) => Guard(context, what, run);

    private void Guard(EditorContext context, string what, Action run)
    {
        try
        {
            run();
        }
        catch (Exception exception)
        {
            LogError($"{context.Extension}: {what} failed: {Message(exception)}");
        }
    }

    private T Guard<T>(EditorContext context, string what, Func<T> run, T fallback)
    {
        try
        {
            return run();
        }
        catch (Exception exception)
        {
            LogError($"{context.Extension}: {what} failed: {Message(exception)}");
            return fallback;
        }
    }

    private static string Message(Exception exception) =>
        exception is TargetInvocationException { InnerException: { } inner } ? Message(inner) : $"{exception.GetType().Name}: {exception.Message}";
}

/// <summary>
/// An extension's panel while its code is building (ADR 0117), holding the
/// panel's place in the layout and its state; made again once the code has
/// loaded, or closed if the extension no longer has it.
/// </summary>
internal sealed class PendingPanel : EditorPanel
{
    public PendingPanel(string id, string title, PanelState? state, string? note = null) : base(id, title)
    {
        Saved = state;
        Label label = new(note ?? $"{title} opens when the project's editor code has built.")
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        label.Classes.Add("placeholder");
        AddChild(label);
    }

    public PanelState? Saved { get; }

    /// <summary>A workspace's tab waiting (ADR 0119): which workspace, and what it works on.</summary>
    public JsonObject? Workspace { get; init; }
}
