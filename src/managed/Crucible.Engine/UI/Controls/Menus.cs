using System.Drawing;
using System.Numerics;

namespace Crucible.UI;

/// <summary>
/// A list of <see cref="MenuItem"/>s and <see cref="MenuSeparator"/>s
/// (ADR 0066), shown in a popup: from a <see cref="MenuBar"/>, beside the
/// item it's the submenu of, or as a <see cref="ContextMenu"/>. Up and down
/// move between items, right opens a submenu, left closes one, Escape or a
/// press outside closes the menus.
/// </summary>
public class Menu : UIElement
{
    private readonly StackPanel _items = new();
    private MenuPopup? _popup;

    public Menu(string title = "")
    {
        Title = title;
        AddChild(_items);
    }

    /// <summary>What a menu bar shows for it: "File".</summary>
    public string Title { get; set; }

    public IReadOnlyList<UIElement> Items => _items.Children;

    public bool IsOpen => _popup?.IsOpen ?? false;

    /// <summary>Adds an item or a separator.</summary>
    public T Add<T>(T item) where T : UIElement
    {
        _items.Add(item);
        return item;
    }

    /// <summary>An item running the command, with its title and shortcut.</summary>
    public MenuItem Add(Command command) => Add(new MenuItem(command));

    public MenuItem Add(string title, Action clicked)
    {
        MenuItem item = new(title);
        item.Clicked += _ => clicked();
        return Add(item);
    }

    public MenuSeparator AddSeparator() => Add(new MenuSeparator());

    /// <summary>Closes it, and the submenus opened from it.</summary>
    public void Close() => _popup?.Close();

    // Opens in the root: below an anchor (a menu bar's item), or at a point.
    internal void Open(UIRoot root, UIElement? anchor, Vector2? position, MenuItem? opener)
    {
        if (IsOpen)
        {
            return;
        }
        Refresh();
        _popup ??= new MenuPopup(this);
        _popup.Anchor = anchor;
        _popup.Position = position;
        _popup.Opener = opener;
        if (opener is not null)
        {
            opener.Classes.Add("open");
        }
        _popup.Open(root);
    }

    internal void Closed(MenuItem? opener) => opener?.Classes.Remove("open");

    // Items running commands show whether they can run and are on, as of now.
    private void Refresh()
    {
        foreach (UIElement child in _items.Children)
        {
            if (child is MenuItem { Command: { } command } item)
            {
                item.State = command.CanExecute ? item.State & ~UIState.Disabled : item.State | UIState.Disabled;
                if (command.IsChecked is { } isChecked)
                {
                    item.IsChecked = isChecked();
                }
            }
        }
    }

    // The menus open in a root, newest first, while they're a chain from
    // one menu bar or context menu.
    internal static void CloseAll(UIRoot root)
    {
        IReadOnlyList<Popup> popups = root.Popups;
        int first = popups.Count;
        while (first > 0 && popups[first - 1] is MenuPopup)
        {
            --first;
        }
        if (first < popups.Count)
        {
            popups[first].Close();
        }
    }
}

// A menu's popup: closes as part of its chain (UIRoot.DismissPopups).
internal sealed class MenuPopup : Popup
{
    private readonly Menu _menu;

    public MenuPopup(Menu menu)
    {
        _menu = menu;
        Content = menu;
        Closed += _ => _menu.Closed(Opener);
    }

    public MenuItem? Opener { get; set; }

    internal override bool IsMenu => true;
}

/// <summary>A menu opened at a point (ADR 0066): a right-click's menu.</summary>
public class ContextMenu : Menu
{
    /// <summary>Opens with its top left at <paramref name="position"/> (the target's root's units), kept on the root.</summary>
    public void Open(UIElement target, Vector2 position)
    {
        UIRoot root = target.Root ?? throw new InvalidOperationException("A context menu opens over an element in a root");
        Menu.CloseAll(root);
        Open(root, null, position, null);
    }
}

/// <summary>A line between groups of items.</summary>
public sealed class MenuSeparator : UIElement
{
}

/// <summary>
/// One row of a <see cref="Menu"/> (ADR 0066): a title, a check while
/// <see cref="IsChecked"/>, its shortcut, and an arrow when it has a
/// <see cref="Submenu"/>. Activated (a click, Enter, the gamepad), it runs
/// its <see cref="Command"/> and raises <see cref="Clicked"/>, then the menus
/// close; with a submenu it opens that instead. In a menu bar it shows just
/// its title (class <c>bar</c>), and its menu has the class <c>open</c> while
/// open.
/// </summary>
public class MenuItem : UIElement
{
    private const float CheckColumn = 22, ArrowColumn = 18, ShortcutGap = 28;

    private readonly Label _title = new();
    private readonly Label _shortcut = new();
    private Command? _command;
    private Shortcut? _shortcutKeys;
    private Menu? _submenu;
    private bool _isChecked;

    public MenuItem(string title = "")
    {
        IsFocusable = true;
        _title.Text = title;
        _shortcut.Classes.Add("shortcut");
        AddChild(_title);
        AddChild(_shortcut);
    }

    public MenuItem(Command command) : this(command?.Title ?? "") => Command = command;

    public string Title
    {
        get => _title.Text;
        set => _title.Text = value;
    }

    /// <summary>What it runs; a menu shows whether it can run (disabled if not) and, for on-off commands, a check.</summary>
    public Command? Command
    {
        get => _command;
        set
        {
            _command = value;
            ShowShortcut();
        }
    }

    /// <summary>The shortcut shown; the command's by default.</summary>
    public Shortcut? Shortcut
    {
        get => _shortcutKeys ?? _command?.Shortcut;
        set
        {
            _shortcutKeys = value;
            ShowShortcut();
        }
    }

    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (_isChecked != value)
            {
                _isChecked = value;
                InvalidateVisual();
            }
        }
    }

    /// <summary>The menu it opens instead of running anything.</summary>
    public Menu? Submenu
    {
        get => _submenu;
        set
        {
            _submenu = value;
            InvalidateMeasure();
        }
    }

    public event Action<MenuItem>? Clicked;

    private bool InBar => Parent?.Parent is MenuBar;

    private Menu? OwnMenu => Parent?.Parent as Menu;

    private void ShowShortcut() => _shortcut.Text = Shortcut?.ToString() ?? "";

    protected override Vector2 MeasureCore(Vector2 available)
    {
        Vector2 infinite = new(float.PositiveInfinity);
        _title.Measure(infinite);
        _shortcut.Measure(infinite);
        if (InBar)
        {
            return _title.DesiredSize;
        }
        float shortcut = _shortcut.Text.Length > 0 ? ShortcutGap + _shortcut.DesiredSize.X : 0;
        float arrow = _submenu is not null ? ArrowColumn : 0;
        return new Vector2(CheckColumn + _title.DesiredSize.X + shortcut + arrow,
            MathF.Max(_title.DesiredSize.Y, _shortcut.DesiredSize.Y));
    }

    protected override void ArrangeCore(RectangleF content)
    {
        float left = InBar ? 0 : CheckColumn;
        float arrow = _submenu is not null && !InBar ? ArrowColumn : 0;
        _title.Arrange(new RectangleF(content.X + left, content.Y, _title.DesiredSize.X, content.Height));
        float shortcutWidth = InBar ? 0 : _shortcut.DesiredSize.X;
        _shortcut.Arrange(new RectangleF(content.Right - arrow - shortcutWidth, content.Y, shortcutWidth, content.Height));
    }

    protected override void OnDraw(Canvas canvas)
    {
        if (InBar)
        {
            return;
        }
        Thickness padding = Padding;
        float height = ActualSize.Y - padding.Vertical;
        if (_isChecked)
        {
            // A tick in the check column.
            float s = MathF.Min(CheckColumn, height) * 0.5f;
            Vector2 c = new(padding.Left + CheckColumn * 0.4f, padding.Top + height * 0.5f);
            canvas.DrawPolyline([c + new Vector2(-s * 0.5f, 0), c + new Vector2(-s * 0.15f, s * 0.35f), c + new Vector2(s * 0.55f, -s * 0.4f)],
                MathF.Max(s * 0.18f, 1.5f), Foreground);
        }
        if (_submenu is not null)
        {
            Vector2 size = new(ArrowColumn, height);
            canvas.PushTransform(Matrix3x2.CreateTranslation(ActualSize.X - padding.Right - ArrowColumn, padding.Top));
            Chevron.Draw(canvas, size, Foreground, NavigationDirection.Right);
            canvas.PopTransform();
        }
    }

    protected override void OnActivate(UIEvent e)
    {
        e.Handled = true;
        if (Root is not { } root)
        {
            return;
        }
        if (_submenu is not null)
        {
            if (_submenu.IsOpen)
            {
                if (InBar)
                {
                    _submenu.Close(); // a second click on the bar closes it
                }
                return;
            }
            OpenSubmenu(root);
            return;
        }
        Menu.CloseAll(root);
        _command?.Execute();
        Clicked?.Invoke(this);
    }

    // Hovering an item in an open menu shows its submenu and closes its
    // siblings'; hovering another bar item while a menu is open switches.
    protected override void OnPointerEnter(PointerEvent e)
    {
        if (Root is not { } root)
        {
            return;
        }
        if (InBar)
        {
            if (Parent?.Parent is MenuBar bar && bar.HasOpenMenu && _submenu is { IsOpen: false })
            {
                Menu.CloseAll(root);
                OpenSubmenu(root);
            }
            return;
        }
        if (OwnMenu is not { } menu)
        {
            return;
        }
        foreach (UIElement sibling in menu.Items)
        {
            if (sibling != this && sibling is MenuItem { Submenu.IsOpen: true } open)
            {
                open.Submenu!.Close();
            }
        }
        if (_submenu is { IsOpen: false } && IsEnabled)
        {
            OpenSubmenu(root);
            Focus(); // the pointer is here; keys go on from here
        }
    }

    // Right opens a submenu; left closes one. In a bar's menu, they move to
    // the next or previous menu along the bar.
    protected override void OnNavigate(NavigationEvent e)
    {
        // The bar's own items move along it as any row does.
        if (Root is not { } root || InBar || e.Direction is not (NavigationDirection.Left or NavigationDirection.Right))
        {
            return;
        }
        if (e.Direction == NavigationDirection.Right && _submenu is not null)
        {
            e.Handled = true;
            if (!_submenu.IsOpen)
            {
                OpenSubmenu(root);
            }
            return;
        }
        // Left closes this submenu (focus goes back to its item); at a bar
        // menu, left and right step along the bar.
        if (OwnMenu?.Parent is MenuPopup popup)
        {
            if (popup.Opener is { InBar: true } barItem && barItem.Parent?.Parent is MenuBar bar)
            {
                e.Handled = true;
                bar.Step(barItem, e.Direction == NavigationDirection.Right ? 1 : -1);
            }
            else if (e.Direction == NavigationDirection.Left)
            {
                e.Handled = true;
                popup.Close();
            }
        }
    }

    internal void OpenSubmenu(UIRoot root)
    {
        if (_submenu is null)
        {
            return;
        }
        if (InBar)
        {
            _submenu.Open(root, this, null, this);
            return;
        }
        // Beside the item, or beside its menu on the left when the right is off the root.
        RectangleF menu = OwnMenu?.Parent?.Bounds ?? Bounds;
        _submenu.Measure(new Vector2(float.PositiveInfinity));
        float width = _submenu.DesiredSize.X + 8; // the popup's padding, roughly
        float x = menu.Right + width <= root.Size.X ? menu.Right : MathF.Max(menu.Left - width, 0);
        _submenu.Open(root, null, new Vector2(x, Bounds.Top - 4), this);
    }
}

/// <summary>
/// A row of menus along a window's top (ADR 0066): File, Edit, Window.
/// Clicking a title opens its menu; while one is open, hovering another
/// switches to it, and left and right step along the bar.
/// </summary>
public class MenuBar : UIElement
{
    private readonly StackPanel _row = new() { Orientation = Orientation.Horizontal };
    private readonly List<Menu> _menus = [];

    public MenuBar() => AddChild(_row);

    public IReadOnlyList<Menu> Menus => _menus;

    internal bool HasOpenMenu => _menus.Exists(menu => menu.IsOpen);

    /// <summary>Adds a menu at the end, with an item showing its title.</summary>
    public Menu Add(Menu menu)
    {
        ArgumentNullException.ThrowIfNull(menu);
        MenuItem item = new(menu.Title) { Submenu = menu };
        item.Classes.Add("bar");
        _row.Add(item);
        _menus.Add(menu);
        return menu;
    }

    public Menu Add(string title) => Add(new Menu(title));

    // Opens the menu `step` places along from the one open from `from`.
    internal void Step(MenuItem from, int step)
    {
        IReadOnlyList<UIElement> items = _row.Children;
        int at = -1;
        for (int i = 0; i < items.Count && at < 0; ++i)
        {
            at = items[i] == from ? i : -1;
        }
        if (at < 0 || from.Root is not { } root)
        {
            return;
        }
        int count = items.Count;
        var next = (MenuItem)items[((at + step) % count + count) % count];
        Menu.CloseAll(root);
        next.OpenSubmenu(root);
    }
}
