using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Crucible.Ecs;
using Crucible.UI;

namespace Crucible.Editor;

/// <summary>
/// Add Component's dialog (ADR 0127): every saved component the entity
/// hasn't, grouped by category (<see cref="CategoryAttribute"/>; a game's own
/// components without one under the game's name), each with its
/// <see cref="DescriptionAttribute"/>. The search field has focus: typing
/// filters across the groups by name, category, and description; Down moves
/// into the list; a click or Enter adds the component and closes it, Escape
/// or Cancel closes it.
/// </summary>
public sealed class ComponentPicker : Popup
{
    private const string JitOnly = "The editor runs JIT only (ADR 0016)";
    private const int ScancodeReturn = 40, ScancodeDown = 81, ScancodeKeypadEnter = 88; // SDL's

    private readonly Shell _shell;
    private readonly Entity _entity;
    private readonly List<Choice> _all;
    private readonly List<Choice?> _shown = []; // null rows are headings
    private readonly List<string> _headings = [];

    /// <summary>One component it offers.</summary>
    public sealed record Choice(string Name, string Title, string Category, string Description);

    internal ComponentPicker(Shell shell, Entity entity, IEnumerable<SavedComponent> components)
    {
        _shell = shell;
        _entity = entity;
        IsModal = true;
        _all = [.. components.Select(Describe).OrderBy(choice => choice.Category, StringComparer.Ordinal)
            .ThenBy(choice => choice.Title, StringComparer.Ordinal)];
        List = new ListBox { Width = 420, Height = 360, EstimatedItemHeight = 40 };
        List.CreateItem = () =>
        {
            StackPanel row = new() { Spacing = 1 };
            row.Add(new Label());
            Label about = new() { Wrap = true };
            about.Classes.Add("hint");
            row.Add(about);
            return row;
        };
        List.BindItem = (content, index) =>
        {
            StackPanel row = (StackPanel)content;
            Label title = (Label)row.Children[0], about = (Label)row.Children[1];
            if (_shown[index] is { } choice)
            {
                title.Text = choice.Title;
                title.Classes.Remove("field-header");
                about.Text = choice.Description;
                about.Visibility = choice.Description.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            else
            {
                title.Text = _headings[index];
                title.Classes.Add("field-header");
                about.Visibility = Visibility.Collapsed;
            }
        };
        List.ItemActivated += (_, index) =>
        {
            if (index >= 0 && index < _shown.Count && _shown[index] is { } choice)
            {
                Pick(choice);
            }
        };
        Search = new TextField { MinWidth = 420 };
        Search.TextChanged += _ => Filter();
        Search.KeyDown += (_, e) =>
        {
            if (e.Scancode == ScancodeDown)
            {
                e.Handled = List.FocusItem(_shown.FindIndex(choice => choice is not null));
            }
            else if (e.Scancode is ScancodeReturn or ScancodeKeypadEnter &&
                     _shown.FirstOrDefault(choice => choice is not null) is { } first)
            {
                e.Handled = true;
                Pick(first); // the first match
            }
        };
        Button cancel = new("Cancel") { HorizontalAlignment = HorizontalAlignment.End };
        cancel.Clicked += _ => Close();
        StackPanel body = new() { Spacing = 8 };
        body.Classes.Add("dialog");
        Label heading = new("Add Component");
        heading.Classes.Add("inspector-title");
        body.Add(heading);
        body.Add(Search);
        body.Add(List);
        body.Add(cancel);
        Content = body;
        Filter();
    }

    /// <summary>The search field (focused when it opens).</summary>
    public TextField Search { get; }

    /// <summary>The list: headings, then their components.</summary>
    public ListBox List { get; }

    /// <summary>What the list shows now, in order; null for a heading.</summary>
    public IReadOnlyList<Choice?> Shown => _shown;

    /// <summary>Raised when a component is added (its name), before it closes.</summary>
    public event Action<string>? Picked;

    /// <summary>Opens over the main window, the search field focused.</summary>
    internal void Show(UIRoot root)
    {
        Open(root);
        Search.Focus();
    }

    /// <summary>Adds a component, as a click or Enter on it does.</summary>
    public void Pick(Choice choice)
    {
        _shell.AddComponent(_entity, choice.Name);
        Picked?.Invoke(choice.Name);
        Close();
    }

    // Grouped while the search is empty; otherwise the matches, by name.
    private void Filter()
    {
        string query = Search.Text.Trim();
        _shown.Clear();
        _headings.Clear();
        if (query.Length == 0)
        {
            string? group = null;
            foreach (Choice choice in _all)
            {
                if (choice.Category != group)
                {
                    group = choice.Category;
                    _shown.Add(null);
                    _headings.Add(group);
                }
                _shown.Add(choice);
                _headings.Add("");
            }
        }
        else
        {
            foreach (Choice choice in _all.Where(choice => Matches(choice, query)).OrderBy(choice => choice.Title, StringComparer.Ordinal))
            {
                _shown.Add(choice);
                _headings.Add("");
            }
        }
        List.ItemCount = _shown.Count;
        List.SelectedIndex = _shown.FindIndex(choice => choice is not null);
    }

    private static bool Matches(Choice choice, string query) =>
        choice.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
        choice.Category.Contains(query, StringComparison.OrdinalIgnoreCase) ||
        choice.Description.Contains(query, StringComparison.OrdinalIgnoreCase);

    // A component's title, category, and description, from its type's attributes.
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = JitOnly)]
    private static Choice Describe(SavedComponent saved)
    {
        Type type = saved.Type;
        string category = type.GetCustomAttribute<CategoryAttribute>()?.Category is { Length: > 0 } named
            ? named
            : type.Assembly.GetName().Name ?? "Other";
        string description = type.GetCustomAttribute<DescriptionAttribute>()?.Description ?? "";
        return new Choice(saved.Name, InspectorPanel.Pretty(type.Name), category, description);
    }
}
