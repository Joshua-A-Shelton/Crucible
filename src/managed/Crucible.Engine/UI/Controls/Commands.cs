using Crucible.Platform;

namespace Crucible.UI;

/// <summary>
/// A key with modifiers that runs a command (ADR 0066): Ctrl+Shift+S. Keys
/// go by keycode, as the user's layout labels them.
/// </summary>
public readonly record struct Shortcut(KeyModifiers Modifiers, uint Keycode)
{
    private const KeyModifiers Held = KeyModifiers.Ctrl | KeyModifiers.Shift | KeyModifiers.Alt | KeyModifiers.Gui;

    /// <summary>Whether a key press is this shortcut: the key, and exactly these modifiers.</summary>
    public bool Matches(KeyEvent e) => e.Keycode == Keycode && (e.Modifiers & Held) == Modifiers;

    /// <summary>As menus show it: "Ctrl+Shift+S".</summary>
    public override string ToString()
    {
        string text = "";
        text += (Modifiers & KeyModifiers.Ctrl) != 0 ? "Ctrl+" : "";
        text += (Modifiers & KeyModifiers.Shift) != 0 ? "Shift+" : "";
        text += (Modifiers & KeyModifiers.Alt) != 0 ? "Alt+" : "";
        text += (Modifiers & KeyModifiers.Gui) != 0 ? "Super+" : "";
        return text + Platform.Keycode.Name(Keycode);
    }
}

/// <summary>
/// Something the user can do (ADR 0066), from a menu, a shortcut, or a
/// button: one command per action, so they all agree on what it does and
/// whether it can be done now.
/// </summary>
public sealed class Command
{
    private readonly Action _execute;
    private readonly Func<bool>? _canExecute;

    public Command(string id, string title, Action execute, Func<bool>? canExecute = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentNullException.ThrowIfNull(execute);
        Id = id;
        Title = title ?? "";
        _execute = execute;
        _canExecute = canExecute;
    }

    /// <summary>Unique in its registry: "File.Open", "Window.ResetLayout".</summary>
    public string Id { get; }

    public string Title { get; }

    public Shortcut? Shortcut { get; init; }

    /// <summary>For an on-off command ("Show grid"): whether it's on now, shown as a check in menus.</summary>
    public Func<bool>? IsChecked { get; init; }

    public bool CanExecute => _canExecute?.Invoke() ?? true;

    /// <summary>Runs it if it can run now; false if it can't.</summary>
    public bool Execute()
    {
        if (!CanExecute)
        {
            return false;
        }
        _execute();
        return true;
    }
}

/// <summary>
/// Commands by ID, and their shortcuts (ADR 0066). Keys a focused element
/// doesn't use bubble up to whatever calls <see cref="HandleShortcut"/>.
/// </summary>
public sealed class CommandRegistry
{
    private readonly Dictionary<string, Command> _commands = new(StringComparer.Ordinal);

    public IReadOnlyCollection<Command> Commands => _commands.Values;

    /// <summary>Adds a command; throws if its ID is taken.</summary>
    public Command Add(Command command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!_commands.TryAdd(command.Id, command))
        {
            throw new InvalidOperationException($"A command \"{command.Id}\" is already registered");
        }
        return command;
    }

    public bool Remove(string id) => _commands.Remove(id);

    public Command? Find(string id) => _commands.GetValueOrDefault(id);

    /// <summary>
    /// Runs the command whose shortcut the key press is, marking it handled;
    /// false if none matches (a matching command that can't run now still
    /// takes the key).
    /// </summary>
    public bool HandleShortcut(KeyEvent e)
    {
        foreach (Command command in _commands.Values)
        {
            if (command.Shortcut is { } shortcut && shortcut.Matches(e))
            {
                command.Execute();
                e.Handled = true;
                return true;
            }
        }
        return false;
    }
}
