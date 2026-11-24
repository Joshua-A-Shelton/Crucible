using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using System.Text.Json.Nodes;
using Crucible.Ecs;
using Crucible.UI;

namespace Crucible.Editor;

/// <summary>
/// Plugins (ADR 0117): the project's editor assembly's
/// <see cref="EditorPlugin"/>s started after each load and stopped before
/// the next, their panels made again in place with their state, their
/// commands in the menus.
/// </summary>
internal sealed partial class Shell
{
    private readonly List<(EditorPlugin Plugin, EditorContext Context)> _plugins = [];
    // The panels plugins registered, by ID.
    private readonly HashSet<string> _pluginPanels = new(StringComparer.Ordinal);
    // Menus plugins added to: the separator before their items in a menu of
    // the editor's own, and how many there are.
    private readonly Dictionary<Menu, (MenuSeparator? Separator, int Items)> _pluginSections = [];
    private Menu? _windowMenu;
    // While a layout is restored: its panels' saved states.
    private JsonObject? _layoutStates;

    /// <summary>The plugins running now, for the checks.</summary>
    internal IEnumerable<EditorPlugin> Plugins => _plugins.Select(plugin => plugin.Plugin);

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

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = JitOnly)]
    [UnconditionalSuppressMessage("Trimming", "IL2072", Justification = JitOnly)]
    private void StartPlugins()
    {
        if (_game?.EditorAssembly is { } assembly)
        {
            foreach (Type type in PluginTypes(assembly))
            {
                string name = type.FullName ?? type.Name;
                EditorPlugin plugin;
                try
                {
                    plugin = (EditorPlugin)Activator.CreateInstance(type)!;
                }
                catch (Exception exception) when (exception is MissingMethodException or MemberAccessException or TargetInvocationException)
                {
                    LogError($"Can't make {name}: {Message(exception)}");
                    continue;
                }
                EditorContext context = new(this, name);
                _plugins.Add((plugin, context));
                Guard(context, "Start", () => plugin.Start(context));
            }
        }
        ResolvePending();
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = JitOnly)]
    private static IEnumerable<Type> PluginTypes(Assembly assembly)
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
            .Where(type => type is { IsClass: true, IsAbstract: false } && type.IsSubclassOf(typeof(EditorPlugin)))
            .OrderBy(type => type.FullName, StringComparer.Ordinal);
    }

    /// <summary>
    /// Stops the plugins and takes out what they added. Their open panels
    /// become placeholders holding their state (<paramref name="keepPanels"/>,
    /// a reload) or close (another project). Then nothing of the old code is
    /// left for it to unload: the handlers it added to the selection and the
    /// history, and undo steps of its own types, the history cut after the
    /// newest of them.
    /// </summary>
    private void StopPlugins(bool keepPanels)
    {
        if (_plugins.Count == 0)
        {
            return;
        }
        for (int i = _plugins.Count - 1; i >= 0; --i)
        {
            (EditorPlugin plugin, EditorContext context) = _plugins[i];
            Guard(context, "Stop", plugin.Stop);
        }
        foreach (string id in _pluginPanels)
        {
            if (_open.TryGetValue(id, out EditorPanel? panel) && panel is not PendingPanel)
            {
                if (keepPanels)
                {
                    Replace(panel, new PendingPanel(id, panel.Title, StateOf(panel)));
                }
                else
                {
                    ClosePanel(panel);
                }
            }
            Panels.Unregister(id);
        }
        _pluginPanels.Clear();
        for (int i = _plugins.Count - 1; i >= 0; --i)
        {
            _plugins[i].Context.Cleanup();
        }
        _plugins.Clear();
        if (_game?.EditorAssembly is { } assembly && AssemblyLoadContext.GetLoadContext(assembly) is { } old)
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
    // their state; the rest close (a plugin that no longer has them).
    private void ResolvePending()
    {
        foreach (EditorPanel panel in _open.Values.ToArray())
        {
            if (panel is not PendingPanel pending)
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
    // plugin's panel, so a placeholder can stand in for it until its code
    // has loaded.
    private JsonObject SavePanelStates()
    {
        JsonObject states = [];
        foreach ((string id, EditorPanel panel) in _open)
        {
            bool plugin = panel is PendingPanel || _pluginPanels.Contains(id);
            PanelState state = StateOf(panel);
            if (plugin || !state.IsEmpty)
            {
                states[id] = new JsonObject
                {
                    ["Title"] = panel.Title,
                    ["Plugin"] = plugin,
                    ["Values"] = state.ToJson(),
                };
            }
        }
        return states;
    }

    // A panel the restored layout names: made, with its saved state; a
    // plugin's not registered yet, a placeholder; null for neither.
    private EditorPanel? RestorePanel(string id)
    {
        JsonObject? saved = _layoutStates?[id] as JsonObject;
        PanelState? state = saved?["Values"] is JsonObject values ? new PanelState(values) : null;
        if (Panel(id) is { } panel)
        {
            if (state is { IsEmpty: false } && panel is not PendingPanel)
            {
                LoadStateOf(panel, state);
            }
            return panel;
        }
        if (saved?["Plugin"]?.GetValueKind() != JsonValueKind.True)
        {
            return null;
        }
        string title = saved["Title"]?.GetValueKind() == JsonValueKind.String ? saved["Title"]!.GetValue<string>() : id;
        PendingPanel pending = new(id, title, state);
        _open[id] = pending;
        return pending;
    }

    // -----------------------------------------------------------------------
    // What plugins add
    // -----------------------------------------------------------------------

    internal bool AddPluginPanel(EditorContext context, string id, string title, Func<EditorPanel> create)
    {
        if (Panels.Contains(id))
        {
            LogWarning($"{context.Plugin}: a panel \"{id}\" is already registered");
            return false;
        }
        Panels.Register(id, title, () => MakePluginPanel(context, id, title, create));
        _pluginPanels.Add(id);
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

    private EditorPanel MakePluginPanel(EditorContext context, string id, string title, Func<EditorPanel> create)
    {
        EditorPanel? made;
        try
        {
            made = create();
        }
        catch (Exception exception)
        {
            LogError($"{context.Plugin}: the {title} panel can't be made: {Message(exception)}");
            return new PendingPanel(id, title, null, $"{title} couldn't be made: {Message(exception)}");
        }
        if (made is null || made.Id != id)
        {
            LogError($"{context.Plugin}: the {title} panel was made with the ID \"{made?.Id}\", not \"{id}\"");
            return new PendingPanel(id, title, null, $"{title} couldn't be made.");
        }
        return made;
    }

    internal bool AddPluginCommand(EditorContext context, Command command, string? menu)
    {
        if (Commands.Find(command.Id) is not null)
        {
            LogWarning($"{context.Plugin}: a command \"{command.Id}\" is already registered");
            return false;
        }
        Shortcut? shortcut = command.Shortcut;
        if (shortcut is { } wanted && Commands.Commands.FirstOrDefault(other => other.Shortcut == wanted) is { } taken)
        {
            LogWarning($"{command.Id} wants {wanted}, already used by {taken.Id}; it has no shortcut");
            shortcut = null;
        }
        // The plugin's code runs guarded: an exception is logged, not the editor's end.
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

    // A plugin's item at the end of the plugins' part of a menu: after a
    // separator in a menu of the editor's own; in Window, before Reset Layout.
    private void Place(Menu menu, MenuItem item)
    {
        (MenuSeparator? separator, int items) = _pluginSections.GetValueOrDefault(menu);
        if (separator is null && items == 0 && menu.Items.Count > 0)
        {
            separator = menu.Insert(SectionEnd(menu), new MenuSeparator());
        }
        menu.Insert(SectionEnd(menu), item);
        _pluginSections[menu] = (separator, items + 1);
    }

    private void Unplace(Menu menu, MenuItem item)
    {
        menu.Remove(item);
        if (!_pluginSections.TryGetValue(menu, out (MenuSeparator? Separator, int Items) section))
        {
            return;
        }
        if (section.Items > 1)
        {
            _pluginSections[menu] = (section.Separator, section.Items - 1);
            return;
        }
        if (section.Separator is { } separator)
        {
            menu.Remove(separator);
        }
        _pluginSections.Remove(menu);
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
    // Edits for plugins
    // -----------------------------------------------------------------------

    internal bool SetPluginComponent<T>(Entity entity, in T value, string title, bool adding) where T : unmanaged
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

    internal bool RemovePluginComponent<T>(Entity entity, string title) where T : unmanaged
    {
        string name = Components.NameOf(typeof(T));
        if (!HasLive(entity) || SavedComponents.Find(name) is null || !World.Has(entity, Components.Of<T>()))
        {
            return false;
        }
        SetComponent(entity, name, null, title);
        return true;
    }

    internal bool WritePluginFile(string path, byte[] contents, string title)
    {
        if (Project is not { } project || string.IsNullOrWhiteSpace(path))
        {
            return false;
        }
        string relative = path.Replace('\\', '/');
        string root = Path.GetFullPath(project.AssetsFolder);
        string file = Path.GetFullPath(Path.Combine(root, relative));
        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (Path.IsPathRooted(relative) || !file.StartsWith(root + Path.DirectorySeparatorChar, comparison))
        {
            LogWarning($"Can't write {path}: it isn't in the Assets folder");
            return false;
        }
        relative = Path.GetRelativePath(root, file).Replace('\\', '/');
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
    // Running plugin code
    // -----------------------------------------------------------------------

    private void Guard(EditorContext context, string what, Action run)
    {
        try
        {
            run();
        }
        catch (Exception exception)
        {
            LogError($"{context.Plugin}: {what} failed: {Message(exception)}");
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
            LogError($"{context.Plugin}: {what} failed: {Message(exception)}");
            return fallback;
        }
    }

    private static string Message(Exception exception) =>
        exception is TargetInvocationException { InnerException: { } inner } ? Message(inner) : $"{exception.GetType().Name}: {exception.Message}";
}

/// <summary>
/// A plugin's panel while its code is building (ADR 0117), holding the
/// panel's place in the layout and its state; made again once the code has
/// loaded, or closed if the plugin no longer has it.
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
}
