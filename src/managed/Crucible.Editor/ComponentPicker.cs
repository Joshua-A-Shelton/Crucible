using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Crucible.Ecs;
using Crucible.UI;

namespace Crucible.Editor;

/// <summary>
/// Add Component's dialog (ADR 0127, 0131): every saved component the
/// entity hasn't, grouped by category (<see cref="CategoryAttribute"/>, a
/// path: "Physics/2D" is a group inside Physics; a game's own components
/// without one under the game's name), each with its icon and its
/// <see cref="DescriptionAttribute"/>. Groups fold open and shut (a click on
/// the heading), remembered per person. The search field has focus: typing
/// shows the matches by name, category, and description, flat, each with
/// its group; Down moves into the list; a click or Enter adds the component
/// and closes it, Escape or Cancel closes it.
/// </summary>
public sealed class ComponentPicker : Popup
{
    private const string JitOnly = "The editor runs JIT only (ADR 0016)";
    private const int ScancodeReturn = 40, ScancodeDown = 81, ScancodeKeypadEnter = 88; // SDL's
    private const float Indent = 14;

    private readonly Shell _shell;
    private readonly Entity _entity;
    private readonly List<Choice> _all;
    private readonly List<Choice?> _shown = []; // null rows are headings
    private readonly List<(string Group, int Depth)> _rows = [];
    private bool _searching; // the list shows matches, flat

    /// <summary>One component it offers.</summary>
    public sealed record Choice(string Name, string Title, string Category, string Description, string Icon = "");

    internal ComponentPicker(Shell shell, Entity entity, IEnumerable<SavedComponent> components)
    {
        _shell = shell;
        _entity = entity;
        IsModal = true;
        _all = [.. components.Select(Describe).OrderBy(choice => choice.Category, StringComparer.Ordinal)
            .ThenBy(choice => choice.Title, StringComparer.Ordinal)];
        List = new ListBox { Width = 420, Height = 360, EstimatedItemHeight = 36 };
        List.CreateItem = () => new PickerRow();
        List.BindItem = (content, index) =>
        {
            PickerRow row = (PickerRow)content;
            (string group, int depth) = _rows[index];
            if (_shown[index] is { } choice)
            {
                bool searching = _searching;
                string about = searching && choice.Description.Length > 0 ? $"{choice.Category} · {choice.Description}"
                    : searching ? choice.Category : choice.Description;
                row.ShowChoice(choice.Title, choice.Icon, about, searching ? 0 : (depth + 1) * Indent);
            }
            else
            {
                row.ShowGroup(Leaf(group), Folded(group), depth * Indent);
            }
        };
        List.ItemActivated += (_, index) =>
        {
            if (index < 0 || index >= _shown.Count)
            {
                return;
            }
            if (_shown[index] is { } choice)
            {
                Pick(choice);
            }
            else
            {
                Fold(_rows[index].Group);
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

    /// <summary>The group (a category path) of each row shown, as <see cref="Shown"/> is ordered.</summary>
    public IReadOnlyList<string> ShownGroups => [.. _rows.Select(row => row.Group)];

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

    /// <summary>Folds a group shut, or opens it, as a click on its heading does; remembered in the person's settings.</summary>
    public void Fold(string group)
    {
        HashSet<string> folded = _shell.Settings.CollapsedCategories;
        if (!folded.Remove(group))
        {
            folded.Add(group);
        }
        Filter();
    }

    private bool Folded(string group) => _shell.Settings.CollapsedCategories.Contains(group);

    private static string Leaf(string group) => group[(group.LastIndexOf('/') + 1)..];

    // Grouped (and folded) while the search is empty; otherwise the matches, by name.
    private void Filter()
    {
        string query = Search.Text.Trim();
        _searching = query.Length > 0;
        _shown.Clear();
        _rows.Clear();
        if (query.Length == 0)
        {
            HashSet<string> headed = [];
            foreach (Choice choice in _all)
            {
                string[] parts = choice.Category.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                bool hidden = false;
                for (int depth = 0; depth < parts.Length && !hidden; ++depth)
                {
                    string group = string.Join('/', parts[..(depth + 1)]);
                    if (headed.Add(group))
                    {
                        _shown.Add(null);
                        _rows.Add((group, depth));
                    }
                    hidden = Folded(group); // what's inside a folded group isn't shown
                }
                if (!hidden)
                {
                    _shown.Add(choice);
                    _rows.Add((string.Join('/', parts), parts.Length - 1));
                }
            }
        }
        else
        {
            foreach (Choice choice in _all.Where(choice => Matches(choice, query)).OrderBy(choice => choice.Title, StringComparer.Ordinal))
            {
                _shown.Add(choice);
                _rows.Add((choice.Category, 0));
            }
        }
        List.ItemCount = _shown.Count;
        List.SelectedIndex = _shown.FindIndex(choice => choice is not null);
    }

    private static bool Matches(Choice choice, string query) =>
        choice.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
        choice.Category.Contains(query, StringComparison.OrdinalIgnoreCase) ||
        choice.Description.Contains(query, StringComparison.OrdinalIgnoreCase);

    // A component's title, category, description, and icon, from its type's attributes.
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = JitOnly)]
    private static Choice Describe(SavedComponent saved)
    {
        Type type = saved.Type;
        string category = type.GetCustomAttribute<CategoryAttribute>()?.Category is { Length: > 0 } named
            ? named
            : type.Assembly.GetName().Name ?? "Other";
        string description = type.GetCustomAttribute<DescriptionAttribute>()?.Description ?? "";
        return new Choice(saved.Name, InspectorPanel.Pretty(type.Name), category, description, EditorIcons.OfComponent(type));
    }

    // A row of the list: a group's heading (a chevron, its name, a rule) or
    // a component (its icon, its name, its description under it).
    private sealed class PickerRow : UIElement
    {
        private readonly Icon _icon = EditorIcons.Icon("");
        private readonly Label _title = new() { VerticalAlignment = VerticalAlignment.Center, Trimming = TextTrimming.End };
        private readonly Label _about = new() { Wrap = true };
        private readonly UIElement _rule = new() { Height = 1, VerticalAlignment = VerticalAlignment.Center };

        public PickerRow()
        {
            _about.Classes.Add("hint");
            _rule.Classes.Add("picker-rule");
            _icon.Margin = new Thickness(0, 0, 6, 0);
            DockPanel line = new();
            DockPanel.SetDock(_icon, Dock.Left);
            DockPanel.SetDock(_title, Dock.Left);
            _title.Margin = new Thickness(0, 0, 8, 0);
            line.Add(_icon);
            line.Add(_title);
            line.Add(_rule);
            StackPanel all = new() { Spacing = 1 };
            all.Add(line);
            all.Add(_about);
            AddChild(all);
        }

        public void ShowGroup(string name, bool folded, float indent)
        {
            Classes.Set("picker-category", true);
            Margin = new Thickness(indent, 0, 0, 0);
            _icon.Glyph = EditorIcons.Glyph(folded ? "chevron-right" : "chevron-down");
            _icon.Visibility = Visibility.Visible;
            _title.Text = name;
            _title.Classes.Set("picker-category", true);
            _rule.Visibility = Visibility.Visible;
            _about.Visibility = Visibility.Collapsed;
        }

        public void ShowChoice(string title, string icon, string about, float indent)
        {
            Classes.Set("picker-category", false);
            Margin = new Thickness(indent, 0, 0, 0);
            _icon.Glyph = EditorIcons.Glyph(icon);
            _icon.Visibility = _icon.Glyph.Length > 0 ? Visibility.Visible : Visibility.Hidden;
            _title.Text = title;
            _title.Classes.Set("picker-category", false);
            _rule.Visibility = Visibility.Collapsed;
            _about.Text = about;
            _about.Visibility = about.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
