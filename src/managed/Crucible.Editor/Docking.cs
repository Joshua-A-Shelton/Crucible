using System.Drawing;
using System.Numerics;
using Crucible.Platform;
using Crucible.UI;

namespace Crucible.Editor;

/// <summary>Where a dragged panel goes in a <see cref="DockGroup"/> (ADR 0066): beside it, splitting it, or among its tabs.</summary>
public enum DockTarget
{
    Left,
    Right,
    Top,
    Bottom,
    Center,
}

/// <summary>
/// One window's docking area (ADR 0066): a tree of <see cref="DockSplit"/>s
/// whose leaves are <see cref="DockGroup"/>s of tabbed panels. Docking a
/// panel beside a group splits it; taking out a group's last panel removes
/// the group, and a split left with one child gives way to it.
/// </summary>
public sealed class DockSpace : UIElement
{
    private const float CenterShare = 0.5f; // the middle half of a group docks among its tabs
    private const float Minimum = 48;       // a group's smallest size, in units

    private UIElement? _node;
    private readonly DropPreview _preview = new();

    public DockSpace()
    {
        ClipToBounds = true;
        AddChild(_preview);
    }

    /// <summary>The tree's top: a split or a group; null when nothing is docked.</summary>
    public UIElement? Node
    {
        get => _node;
        set
        {
            if (value is not (null or DockSplit or DockGroup))
            {
                throw new ArgumentException("A dock space holds a DockSplit or a DockGroup", nameof(value));
            }
            if (_node is not null)
            {
                RemoveChild(_node);
            }
            _node = value;
            if (value is not null)
            {
                InsertChild(0, value);
            }
        }
    }

    /// <summary>Every group, in tree order.</summary>
    public IEnumerable<DockGroup> Groups => _node is null ? [] : GroupsIn(_node);

    /// <summary>Every docked panel.</summary>
    public IEnumerable<EditorPanel> Panels => Groups.SelectMany(group => group.Panels);

    /// <summary>The group with the most room: where a panel goes when nothing says where.</summary>
    public DockGroup? LargestGroup =>
        Groups.OrderByDescending(group => group.ActualSize.X * group.ActualSize.Y).FirstOrDefault();

    /// <summary>
    /// Docks a panel (taken from wherever it was): among <paramref name="group"/>'s
    /// tabs, or beside it, splitting it; without a group, as the space's only
    /// group if it's empty, else among the largest group's tabs.
    /// </summary>
    public void Dock(EditorPanel panel, DockGroup? group, DockTarget where)
    {
        ArgumentNullException.ThrowIfNull(panel);
        if (group is not null && group.Space != this)
        {
            throw new ArgumentException("The group isn't in this dock space", nameof(group));
        }
        // Into the same group it's in: just made active.
        if (group is not null && where == DockTarget.Center && panel.Group == group)
        {
            group.Activate(panel);
            return;
        }
        Take(panel);
        if (group is null || group.Parent is null)
        {
            group = LargestGroup;
            where = DockTarget.Center;
            if (group is null)
            {
                DockGroup only = new();
                only.Add(panel);
                Node = only;
                return;
            }
        }
        if (where == DockTarget.Center)
        {
            group.Add(panel);
            return;
        }
        DockGroup added = new();
        added.Add(panel);
        Orientation orientation = where is DockTarget.Left or DockTarget.Right ? Orientation.Horizontal : Orientation.Vertical;
        bool before = where is DockTarget.Left or DockTarget.Top;
        if (group.Parent is DockSplit split && split.Orientation == orientation)
        {
            // A sibling in the split already going this way, sharing the group's room.
            int at = split.IndexOf(group);
            float share = split.Sizes[at] * 0.5f;
            split.SetSize(at, share);
            split.Insert(before ? at : at + 1, added, share);
            return;
        }
        DockSplit made = new(orientation);
        Replace(group, made);
        made.Insert(0, before ? added : group, 0.5f);
        made.Insert(1, before ? group : added, 0.5f);
    }

    /// <summary>Takes a panel out of its group; an emptied group, and a split left with one child, go.</summary>
    public void Take(EditorPanel panel)
    {
        if (panel.Group is not { } group)
        {
            return;
        }
        group.Remove(panel);
        if (group.Panels.Count > 0)
        {
            return;
        }
        if (group == _node)
        {
            Node = null;
            return;
        }
        if (group.Parent is DockSplit split)
        {
            split.Remove(group);
            if (split.Nodes.Count == 1)
            {
                UIElement remaining = split.Nodes[0];
                split.Remove(remaining);
                Replace(split, remaining);
            }
        }
    }

    /// <summary>
    /// Where a panel dropped at a point (in the root's units) would go: the
    /// group under it, and the target from where in it (its middle half
    /// among its tabs, else the nearest side); null over nothing.
    /// </summary>
    public (DockGroup Group, DockTarget Where)? TargetAt(Vector2 point)
    {
        foreach (DockGroup group in Groups)
        {
            RectangleF b = group.Bounds;
            if (!b.Contains(point.X, point.Y) || b.Width <= 0 || b.Height <= 0)
            {
                continue;
            }
            float u = (point.X - b.X) / b.Width, v = (point.Y - b.Y) / b.Height;
            float margin = (1 - CenterShare) * 0.5f;
            if (u >= margin && u <= 1 - margin && v >= margin && v <= 1 - margin)
            {
                return (group, DockTarget.Center);
            }
            // The nearest side.
            float left = u, right = 1 - u, top = v, bottom = 1 - v;
            float nearest = MathF.Min(MathF.Min(left, right), MathF.Min(top, bottom));
            DockTarget where = nearest == left ? DockTarget.Left
                : nearest == right ? DockTarget.Right
                : nearest == top ? DockTarget.Top
                : DockTarget.Bottom;
            return (group, where);
        }
        return null;
    }

    /// <summary>Shows where a drop would go (null to hide it).</summary>
    public void ShowPreview((DockGroup Group, DockTarget Where)? target)
    {
        RectangleF? area = null;
        if (target is ({ } group, var where))
        {
            RectangleF b = group.Bounds;
            area = where switch
            {
                DockTarget.Left => new RectangleF(b.X, b.Y, b.Width * 0.5f, b.Height),
                DockTarget.Right => new RectangleF(b.X + b.Width * 0.5f, b.Y, b.Width * 0.5f, b.Height),
                DockTarget.Top => new RectangleF(b.X, b.Y, b.Width, b.Height * 0.5f),
                DockTarget.Bottom => new RectangleF(b.X, b.Y + b.Height * 0.5f, b.Width, b.Height * 0.5f),
                _ => b,
            };
        }
        _preview.Show(area);
    }

    /// <summary>Where a drop would go now, as shown; null when hidden.</summary>
    internal RectangleF? PreviewArea => _preview.Area;

    internal static float MinimumSize => Minimum;

    private void Replace(UIElement old, UIElement replacement)
    {
        if (old == _node)
        {
            Node = replacement;
        }
        else if (old.Parent is DockSplit parent)
        {
            int at = parent.IndexOf(old);
            float size = parent.Sizes[at];
            parent.Remove(old);
            parent.Insert(at, replacement, size);
        }
    }

    private static IEnumerable<DockGroup> GroupsIn(UIElement node)
    {
        if (node is DockGroup group)
        {
            yield return group;
        }
        else if (node is DockSplit split)
        {
            foreach (UIElement child in split.Nodes)
            {
                foreach (DockGroup inner in GroupsIn(child))
                {
                    yield return inner;
                }
            }
        }
    }

    // The drop preview: a translucent area over the target, drawn above the tree.
    private sealed class DropPreview : UIElement
    {
        public DropPreview()
        {
            IsHitTestVisible = false;
            Classes.Add("dock-preview");
        }

        public RectangleF? Area { get; private set; }

        public void Show(RectangleF? area)
        {
            if (area != Area)
            {
                Area = area;
                InvalidateVisual();
            }
        }

        protected override void OnDraw(Canvas canvas)
        {
            if (Area is not { } area)
            {
                return;
            }
            Vector2 at = new(area.X - Bounds.X, area.Y - Bounds.Y), size = new(area.Width, area.Height);
            Vector4 color = Foreground;
            canvas.FillRect(at + new Vector2(2), size - new Vector2(4), color with { W = color.W * 0.25f }, 4);
            canvas.StrokeRect(at + new Vector2(2), size - new Vector2(4), color, 2, new Vector4(4));
        }
    }
}

/// <summary>
/// Nodes side by side (<see cref="Orientation.Horizontal"/>) or stacked
/// (ADR 0066), with a <see cref="Splitter"/> between each pair; each node
/// has a share of the room (<see cref="Sizes"/>, adding to 1). Shares are
/// given as weights, relative to each other.
/// </summary>
public sealed class DockSplit : UIElement
{
    private readonly List<UIElement> _nodes = [];
    private readonly List<float> _weights = [];
    private readonly List<Splitter> _splitters = [];

    public DockSplit(Orientation orientation) => Orientation = orientation;

    public Orientation Orientation { get; }

    public IReadOnlyList<UIElement> Nodes => _nodes;

    /// <summary>Each node's share of the room along the split, adding to 1.</summary>
    public IReadOnlyList<float> Sizes
    {
        get
        {
            float total = _weights.Sum();
            return [.. _weights.Select(weight => total > 0 ? weight / total : 0)];
        }
    }

    public int IndexOf(UIElement node) => _nodes.IndexOf(node);

    /// <summary>Adds a node (a split or a group) with a weight for its share.</summary>
    public void Insert(int index, UIElement node, float weight)
    {
        if (node is not (DockSplit or DockGroup))
        {
            throw new ArgumentException("A split holds DockSplits and DockGroups", nameof(node));
        }
        _nodes.Insert(index, node);
        _weights.Insert(index, MathF.Max(weight, 0.001f));
        AddChild(node);
        if (_nodes.Count > 1)
        {
            Splitter splitter = new(this);
            _splitters.Add(splitter);
            AddChild(splitter);
        }
        InvalidateMeasure();
    }

    public void Add(UIElement node, float weight) => Insert(_nodes.Count, node, weight);

    public void Remove(UIElement node)
    {
        int at = _nodes.IndexOf(node);
        if (at < 0)
        {
            return;
        }
        _nodes.RemoveAt(at);
        _weights.RemoveAt(at);
        RemoveChild(node);
        if (_splitters.Count > 0)
        {
            Splitter last = _splitters[^1];
            _splitters.RemoveAt(_splitters.Count - 1);
            RemoveChild(last);
        }
        InvalidateMeasure();
    }

    /// <summary>Sets a node's weight.</summary>
    public void SetSize(int index, float weight)
    {
        _weights[index] = MathF.Max(weight, 0.001f);
        InvalidateMeasure();
    }

    // Moves the boundary after node `index` by `delta` units, keeping both
    // sides at least the minimum size.
    internal void MoveBoundary(int index, float delta)
    {
        float room = Room();
        if (room <= 0 || index < 0 || index + 1 >= _nodes.Count)
        {
            return;
        }
        Normalize();
        float pair = (_weights[index] + _weights[index + 1]) * room;
        float first = Math.Clamp(_weights[index] * room + delta, MathF.Min(DockSpace.MinimumSize, pair * 0.5f),
            MathF.Max(pair - DockSpace.MinimumSize, pair * 0.5f));
        _weights[index] = first / room;
        _weights[index + 1] = (pair - first) / room;
        InvalidateMeasure();
    }

    internal int IndexOf(Splitter splitter) => _splitters.IndexOf(splitter);

    // Weights to shares adding to 1.
    private void Normalize()
    {
        float total = _weights.Sum();
        for (int i = 0; i < _weights.Count && total > 0; ++i)
        {
            _weights[i] /= total;
        }
    }

    private bool Horizontal => Orientation == Orientation.Horizontal;

    private float SplitterThickness => _splitters.Count > 0 ? _splitters[0].Thickness : 0;

    // The room along the split, less the splitters.
    private float Room()
    {
        float along = Horizontal ? ActualSize.X - Padding.Horizontal : ActualSize.Y - Padding.Vertical;
        return along - SplitterThickness * _splitters.Count;
    }

    protected override Vector2 MeasureCore(Vector2 available)
    {
        // A split takes the room it's given; its nodes are measured for theirs.
        Normalize();
        Vector2 room = Vector2.Max(available, Vector2.Zero);
        float along = (Horizontal ? room.X : room.Y) - SplitterThickness * _splitters.Count;
        for (int i = 0; i < _nodes.Count; ++i)
        {
            float share = MathF.Max(along * _weights[i], 0);
            _nodes[i].Measure(Horizontal ? new Vector2(share, room.Y) : new Vector2(room.X, share));
        }
        foreach (Splitter splitter in _splitters)
        {
            splitter.Measure(room);
        }
        return new Vector2(float.IsFinite(room.X) ? room.X : 0, float.IsFinite(room.Y) ? room.Y : 0);
    }

    protected override void ArrangeCore(RectangleF content)
    {
        float thickness = SplitterThickness;
        float along = (Horizontal ? content.Width : content.Height) - thickness * _splitters.Count;
        float at = Horizontal ? content.X : content.Y;
        for (int i = 0; i < _nodes.Count; ++i)
        {
            // The last node takes what's left, so rounding never leaves a gap.
            float end = i == _nodes.Count - 1 ? (Horizontal ? content.Right : content.Bottom) : at + along * _weights[i];
            _nodes[i].Arrange(Horizontal
                ? new RectangleF(at, content.Y, MathF.Max(end - at, 0), content.Height)
                : new RectangleF(content.X, at, content.Width, MathF.Max(end - at, 0)));
            if (i < _splitters.Count)
            {
                _splitters[i].Arrange(Horizontal
                    ? new RectangleF(end, content.Y, thickness, content.Height)
                    : new RectangleF(content.X, end, content.Width, thickness));
            }
            at = end + thickness;
        }
    }
}

/// <summary>
/// The bar between two nodes of a <see cref="DockSplit"/> (ADR 0066):
/// dragging it moves the boundary; classes <c>horizontal</c> (between
/// nodes side by side) or <c>vertical</c>.
/// </summary>
public sealed class Splitter : UIElement
{
    private readonly DockSplit _split;
    private float _last;

    internal Splitter(DockSplit split)
    {
        _split = split;
        Classes.Add(split.Orientation == Orientation.Horizontal ? "horizontal" : "vertical");
    }

    /// <summary>Its width across the split: its styled Width (side by side) or Height (stacked), 4 units unset.</summary>
    public float Thickness
    {
        get
        {
            float styled = _split.Orientation == Orientation.Horizontal ? Width : Height;
            return float.IsNaN(styled) ? 4 : styled;
        }
    }

    protected override Vector2 MeasureCore(Vector2 available) => Vector2.Zero;

    protected override void OnPointerDown(PointerEvent e)
    {
        if (e.Button == PointerButton.Left)
        {
            e.Handled = true;
            _last = Along(e.Position);
        }
    }

    protected override void OnPointerMove(PointerEvent e)
    {
        if ((State & UIState.Pressed) == 0)
        {
            return;
        }
        e.Handled = true;
        float at = Along(e.Position);
        _split.MoveBoundary(_split.IndexOf(this), at - _last);
        _last = at;
    }

    private float Along(Vector2 position) => _split.Orientation == Orientation.Horizontal ? position.X : position.Y;
}

/// <summary>
/// Tabbed panels (ADR 0066): a strip of <see cref="DockTab"/>s over the
/// active panel. Clicking a tab shows its panel; dragging one docks its
/// panel elsewhere, or tears it off into a window of its own.
/// </summary>
public sealed class DockGroup : UIElement
{
    private const float MoveThreshold = 4; // units moved before a press on the strip moves the window

    private readonly StackPanel _strip = new() { Orientation = Orientation.Horizontal, Spacing = 1 };
    private readonly List<EditorPanel> _panels = [];
    private int _active = -1;
    private UIElement? _stripEnd;
    private bool _pressed;
    private Vector2 _pressedAt;

    public DockGroup()
    {
        _strip.Classes.Add("dock-tabs");
        AddChild(_strip);
    }

    public IReadOnlyList<EditorPanel> Panels => _panels;

    public EditorPanel? ActivePanel => _active >= 0 ? _panels[_active] : null;

    /// <summary>The dock space it's in.</summary>
    public DockSpace? Space
    {
        get
        {
            UIElement? at = Parent;
            while (at is DockSplit)
            {
                at = at.Parent;
            }
            return at as DockSpace;
        }
    }

    internal IReadOnlyList<UIElement> Tabs => _strip.Children;

    // A floating window's only group is its title bar (ADR 0066): the
    // window's buttons at the strip's end, and the strip's empty part
    // dragging the window (to dock it), a double-click maximizing it, a
    // right-click showing the window's menu.
    internal UIElement? StripEnd
    {
        get => _stripEnd;
        set
        {
            if (_stripEnd == value)
            {
                return;
            }
            if (_stripEnd is not null)
            {
                RemoveChild(_stripEnd);
            }
            _stripEnd = value;
            if (value is not null)
            {
                AddChild(value);
            }
            InvalidateMeasure();
        }
    }

    private bool OnStrip(PointerEvent e) =>
        (e.Source == this || e.Source == _strip) && e.Position.Y < _strip.Bounds.Bottom;

    protected override void OnPointerDown(PointerEvent e)
    {
        if (_stripEnd is null || !OnStrip(e) || Shell.Of(this) is not { } shell || shell.WindowOf(this) is not { } window)
        {
            return;
        }
        e.Handled = true;
        if (e.Button == PointerButton.Right)
        {
            Platform.Api.ShowWindowMenu(window.Window, e.Position * window.Root.Scale);
        }
        else if (e.Button == PointerButton.Left && e.ClickCount == 2)
        {
            _pressed = false;
            Shell.ToggleMaximized(window);
        }
        else if (e.Button == PointerButton.Left)
        {
            _pressed = true;
            _pressedAt = e.Position;
        }
    }

    protected override void OnPointerMove(PointerEvent e)
    {
        if (!_pressed || Vector2.Distance(e.Position, _pressedAt) < MoveThreshold)
        {
            return;
        }
        e.Handled = true;
        _pressed = false;
        if (Shell.Of(this) is { } shell && shell.WindowOf(this) is { } window)
        {
            shell.BeginWindowDrag(window, _pressedAt);
        }
    }

    protected override void OnPointerUp(PointerEvent e) => _pressed = false;

    /// <summary>Adds a panel's tab (at the end, or at <paramref name="index"/>) and shows it.</summary>
    public void Add(EditorPanel panel, int index = -1)
    {
        ArgumentNullException.ThrowIfNull(panel);
        if (panel.Parent is not null)
        {
            throw new InvalidOperationException("The panel is docked elsewhere; take it out first");
        }
        index = index < 0 || index > _panels.Count ? _panels.Count : index;
        _panels.Insert(index, panel);
        _strip.Insert(index, new DockTab(panel));
        AddChild(panel);
        Activate(panel);
    }

    /// <summary>Shows a panel of the group.</summary>
    public void Activate(EditorPanel panel)
    {
        int at = _panels.IndexOf(panel);
        if (at < 0)
        {
            return;
        }
        _active = at;
        for (int i = 0; i < _panels.Count; ++i)
        {
            _panels[i].Visibility = i == at ? Visibility.Visible : Visibility.Collapsed;
            if (_strip.Children[i] is DockTab tab)
            {
                if (i == at)
                {
                    tab.Classes.Add("active");
                }
                else
                {
                    tab.Classes.Remove("active");
                }
            }
        }
        InvalidateMeasure();
    }

    internal void Remove(EditorPanel panel)
    {
        int at = _panels.IndexOf(panel);
        if (at < 0)
        {
            return;
        }
        _panels.RemoveAt(at);
        _strip.Remove(_strip.Children[at]);
        RemoveChild(panel);
        panel.Visibility = Visibility.Visible;
        if (_panels.Count == 0)
        {
            _active = -1;
        }
        else
        {
            Activate(_panels[Math.Min(_active > at ? _active - 1 : _active, _panels.Count - 1)]);
        }
    }

    protected override Vector2 MeasureCore(Vector2 available)
    {
        _stripEnd?.Measure(available);
        _strip.Measure(available);
        float stripHeight = MathF.Max(_strip.DesiredSize.Y, _stripEnd?.DesiredSize.Y ?? 0);
        Vector2 below = new(available.X, MathF.Max(available.Y - stripHeight, 0));
        Vector2 size = new(_strip.DesiredSize.X, 0);
        foreach (EditorPanel panel in _panels)
        {
            panel.Measure(below);
            size = Vector2.Max(size, new Vector2(panel.DesiredSize.X, panel.DesiredSize.Y));
        }
        return new Vector2(size.X, stripHeight + size.Y);
    }

    protected override void ArrangeCore(RectangleF content)
    {
        float stripHeight = MathF.Max(_strip.DesiredSize.Y, _stripEnd?.DesiredSize.Y ?? 0);
        float end = _stripEnd?.DesiredSize.X ?? 0;
        _strip.Arrange(new RectangleF(content.X, content.Y, MathF.Max(content.Width - end, 0), stripHeight));
        _stripEnd?.Arrange(new RectangleF(content.Right - end, content.Y, end, stripHeight));
        RectangleF below = new(content.X, content.Y + stripHeight, content.Width, MathF.Max(content.Height - stripHeight, 0));
        foreach (EditorPanel panel in _panels)
        {
            panel.Arrange(below);
        }
    }
}

/// <summary>
/// A panel's tab (ADR 0066): its title and a close button; class
/// <c>active</c> on the group's shown panel. Pressing shows the panel,
/// dragging it moves the panel, and a right-click offers Close (and, in a
/// floating window, docking back into the main window).
/// </summary>
public sealed class DockTab : UIElement
{
    private const float DragThreshold = 6; // units moved before a press drags
    private const float CloseSize = 14;

    private readonly Label _title;
    private Vector2 _pressedAt;
    private bool _pressed;
    private bool _dragging;

    internal DockTab(EditorPanel panel)
    {
        Panel = panel;
        _title = new Label(panel.Title) { VerticalAlignment = VerticalAlignment.Center };
        AddChild(_title);
    }

    public EditorPanel Panel { get; }

    // Where the close button is, in the root's units.
    private RectangleF CloseArea
    {
        get
        {
            RectangleF b = Bounds;
            float right = b.Right - Padding.Right;
            return new RectangleF(right - CloseSize, b.Y + (b.Height - CloseSize) * 0.5f, CloseSize, CloseSize);
        }
    }

    protected override Vector2 MeasureCore(Vector2 available)
    {
        _title.Measure(available);
        return new Vector2(_title.DesiredSize.X + 8 + CloseSize, _title.DesiredSize.Y);
    }

    protected override void ArrangeCore(RectangleF content)
    {
        _title.Arrange(new RectangleF(content.X, content.Y, _title.DesiredSize.X, content.Height));
    }

    protected override void OnDraw(Canvas canvas)
    {
        // The close button: a cross, shown on the active tab and under the pointer.
        if (!Classes.Contains("active") && (State & UIState.Hover) == 0)
        {
            return;
        }
        RectangleF area = CloseArea;
        Vector2 c = new(area.X - Bounds.X + area.Width * 0.5f, area.Y - Bounds.Y + area.Height * 0.5f);
        float s = CloseSize * 0.22f, width = MathF.Max(1.25f, CloseSize * 0.1f);
        Vector4 color = Foreground with { W = Foreground.W * 0.7f };
        canvas.DrawLine(c - new Vector2(s), c + new Vector2(s), width, color);
        canvas.DrawLine(c + new Vector2(-s, s), c + new Vector2(s, -s), width, color);
    }

    protected override void OnPointerDown(PointerEvent e)
    {
        if (e.Button == PointerButton.Right)
        {
            e.Handled = true;
            Shell.Of(this)?.ShowTabMenu(this, e.Position);
            return;
        }
        if (e.Button != PointerButton.Left)
        {
            return;
        }
        e.Handled = true;
        if (CloseArea.Contains(e.Position.X, e.Position.Y))
        {
            Shell.Of(this)?.ClosePanel(Panel);
            return;
        }
        Panel.Group?.Activate(Panel);
        _pressed = true;
        _dragging = false;
        _pressedAt = e.Position;
    }

    protected override void OnPointerMove(PointerEvent e)
    {
        if (!_pressed)
        {
            return;
        }
        e.Handled = true;
        if (!_dragging && Vector2.Distance(e.Position, _pressedAt) >= DragThreshold)
        {
            _dragging = Shell.Of(this)?.BeginTabDrag(this, e.Position) ?? false;
            if (!_dragging)
            {
                _pressed = false;
            }
            return;
        }
        if (_dragging)
        {
            Shell.Of(this)?.TabDragMoved(this, e.Position);
        }
    }

    protected override void OnPointerUp(PointerEvent e)
    {
        if (e.Button != PointerButton.Left || !_pressed)
        {
            return;
        }
        e.Handled = true;
        _pressed = false;
        if (_dragging)
        {
            _dragging = false;
            Shell.Of(this)?.TabDragReleased(this, e.Position);
        }
    }

    protected override void OnDetached(UIRoot root)
    {
        _pressed = false;
        _dragging = false;
    }
}
