using System.Drawing;
using System.Numerics;

namespace Crucible.UI;

/// <summary>Where a dragged node goes, against the row it's dropped on (ADR 0068).</summary>
public enum TreeDropPosition
{
    Before,
    After,
    Inside, // its last child
}

/// <summary>A node dragged onto another (ADR 0068): <see cref="Target"/> is never the node or inside it.</summary>
public readonly record struct TreeDrop(ulong Node, ulong Target, TreeDropPosition Position);

/// <summary>
/// A tree row's expander (ADR 0068): a chevron pointing right, or down
/// (the class <c>open</c>) while its node is expanded; hidden for nodes
/// without children. A press toggles it.
/// </summary>
public sealed class TreeExpander : UIElement
{
    private readonly TreeRow _row;

    internal TreeExpander(TreeRow row) => _row = row;

    protected override void OnPointerDown(PointerEvent e)
    {
        if (e.Button == PointerButton.Left)
        {
            e.Handled = true;
            _row.Tree.SetExpanded(_row.Node, !_row.Tree.IsExpanded(_row.Node));
        }
    }

    protected override void OnDraw(Canvas canvas)
    {
        float w = Bounds.Width, h = Bounds.Height, s = MathF.Min(w, h) * 0.22f;
        Vector2 c = new(w * 0.5f, h * 0.5f);
        ReadOnlySpan<Vector2> chevron = Classes.Contains("open")
            ? [c + new Vector2(-s, -s * 0.5f), c + new Vector2(0, s * 0.5f), c + new Vector2(s, -s * 0.5f)]
            : [c + new Vector2(-s * 0.5f, -s), c + new Vector2(s * 0.5f, 0), c + new Vector2(-s * 0.5f, s)];
        canvas.DrawPolyline(chevron, 1.5f, Foreground);
    }
}

/// <summary>
/// A tree's row content (ADR 0068): indented by its depth, an expander,
/// then its text, which <see cref="TreeView.BeginRename"/> swaps for a text
/// field. Rows are reused as the tree scrolls: <see cref="TreeView.BindRow"/>
/// fills one whenever it shows a different node.
/// </summary>
public sealed class TreeRow : UIElement
{
    private readonly Label _label = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly RenameField _field = new() { Visibility = Visibility.Collapsed };
    private readonly TreeExpander _expander;
    private bool _editing;

    internal TreeRow(TreeView tree)
    {
        Tree = tree;
        _expander = new TreeExpander(this) { VerticalAlignment = VerticalAlignment.Stretch };
        _field.Ended += EndRename;
        StackPanel line = new() { Orientation = Orientation.Horizontal, Spacing = 2 };
        line.Add(_expander);
        line.Add(_label);
        line.Add(_field);
        AddChild(line);
    }

    public TreeView Tree { get; }

    /// <summary>The node it shows now.</summary>
    public ulong Node { get; private set; }

    /// <summary>0 for a root.</summary>
    public int Depth { get; private set; }

    public string Text
    {
        get => _label.Text;
        set => _label.Text = value;
    }

    public bool IsRenaming => _editing;

    internal TreeExpander Expander => _expander;

    internal Label Label => _label;

    internal TextField Field => _field;

    internal void Bind(ulong node, int depth, bool hasChildren, bool expanded)
    {
        if (_editing && node != Node)
        {
            EndRename(false);
        }
        Node = node;
        Depth = depth;
        Margin = new Thickness(depth * Tree.Indent, 0, 0, 0);
        _expander.Visibility = hasChildren ? Visibility.Visible : Visibility.Hidden;
        _expander.Classes.Set("open", expanded);
        Tree.BindRow?.Invoke(this);
    }

    internal void BeginRename()
    {
        _editing = true;
        _field.Text = _label.Text;
        _label.Visibility = Visibility.Collapsed;
        _field.Visibility = Visibility.Visible;
        _field.MinWidth = MathF.Max(_label.Bounds.Width + 24, 120);
        _field.Focus();
        _field.SelectAll();
    }

    private void EndRename(bool keep)
    {
        if (!_editing)
        {
            return;
        }
        _editing = false;
        bool hadFocus = _field.IsFocused;
        string text = _field.Text;
        _field.Visibility = Visibility.Collapsed;
        _label.Visibility = Visibility.Visible;
        if (hadFocus)
        {
            Tree.FocusNode(Node);
        }
        if (keep && text != _label.Text)
        {
            Tree.RaiseRenamed(Node, text);
        }
    }

    // Enter keeps the text, Escape puts it back (the field already has), and
    // focus leaving keeps it.
    private sealed class RenameField : TextField
    {
        private const int ScancodeReturn = 40, ScancodeEscape = 41, ScancodeKeypadEnter = 88;

        public event Action<bool>? Ended;

        protected override void OnKeyDown(KeyEvent e)
        {
            int code = e.Scancode;
            base.OnKeyDown(e);
            if (code is ScancodeReturn or ScancodeKeypadEnter or ScancodeEscape)
            {
                e.Handled = true;
                Ended?.Invoke(code != ScancodeEscape);
            }
        }

        protected override void OnLostFocus(UIEvent e)
        {
            base.OnLostFocus(e);
            Ended?.Invoke(true);
        }
    }
}

/// <summary>
/// A tree of nodes (ADR 0068), each a <c>ulong</c> key the caller chooses:
/// <see cref="ChildrenOf"/> gives a node's children (null for the roots),
/// and <see cref="BindRow"/> fills a row for one. It's a <see cref="ListBox"/>
/// over the nodes in view, so it's virtualized the same way; call
/// <see cref="Refresh"/> when the nodes change, which keeps what's expanded
/// and selected. One node is selected, following focus. Right expands (then
/// goes to the first child), Left collapses (then goes to the parent).
/// Optionally, rows rename inline (<see cref="BeginRename"/>) and drag onto
/// one another (<see cref="Dropped"/>), with an indicator showing where.
/// </summary>
public class TreeView : UIElement
{
    private const float DragThreshold = 4; // logical units

    private readonly ListBox _list = new() { ItemClass = "tree" };
    private readonly DropIndicator _indicator = new();
    private readonly List<(ulong Node, int Depth, bool HasChildren)> _rows = [];
    private readonly Dictionary<ulong, int> _indexOf = [];
    private readonly HashSet<ulong> _expanded = [];
    private ulong? _selected;
    private bool _syncing;
    private ulong? _pressed;
    private Vector2 _pressedAt;
    private bool _dragging;
    private TreeDrop? _drop;

    public TreeView()
    {
        _list.EstimatedItemHeight = 24;
        _list.CreateItem = () => new TreeRow(this);
        _list.BindItem = (content, index) =>
        {
            (ulong node, int depth, bool hasChildren) = _rows[index];
            ((TreeRow)content).Bind(node, depth, hasChildren, _expanded.Contains(node));
        };
        _list.SelectionChanged += list =>
        {
            if (!_syncing && list.SelectedIndex >= 0)
            {
                Select(_rows[list.SelectedIndex].Node);
            }
        };
        _list.ItemFocused = index =>
        {
            if (index >= 0 && index < _rows.Count)
            {
                Select(_rows[index].Node);
            }
        };
        AddChild(_list);
        AddChild(_indicator);
    }

    /// <summary>A node's children in order; null asks for the roots.</summary>
    public Func<ulong?, IReadOnlyList<ulong>>? ChildrenOf { get; set; }

    /// <summary>Fills a row for its <see cref="TreeRow.Node"/>: its text, its classes.</summary>
    public Action<TreeRow>? BindRow { get; set; }

    /// <summary>How far each level is indented, in logical units.</summary>
    public float Indent { get; set; } = 14;

    /// <summary>Whether rows can be dragged onto one another (raising <see cref="Dropped"/>).</summary>
    public bool CanDrag { get; set; }

    /// <summary>The nodes in view (roots and the children of expanded nodes), in order.</summary>
    public int RowCount => _rows.Count;

    public ulong RowNode(int index) => _rows[index].Node;

    public int RowDepth(int index) => _rows[index].Depth;

    /// <summary>The row index showing a node, or -1 when it isn't in view (collapsed away, or gone).</summary>
    public int IndexOf(ulong node) => _indexOf.GetValueOrDefault(node, -1);

    public ScrollView ScrollView => _list.ScrollView;

    /// <summary>The selected node, if any; setting it doesn't expand its parents.</summary>
    public ulong? SelectedNode
    {
        get => _selected;
        set => Select(value);
    }

    public event Action<TreeView>? SelectionChanged;

    /// <summary>A rename finished with new text: the node and its text. The tree shows the old text until refreshed.</summary>
    public event Action<TreeView, ulong, string>? Renamed;

    /// <summary>A drag finished on a valid target.</summary>
    public event Action<TreeView, TreeDrop>? Dropped;

    /// <summary>A right-click: the node under it (selected first), or null below the rows, and where, in the root's units.</summary>
    public event Action<TreeView, ulong?, Vector2>? ContextRequested;

    public bool IsExpanded(ulong node) => _expanded.Contains(node);

    public void SetExpanded(ulong node, bool expanded)
    {
        if (expanded ? _expanded.Add(node) : _expanded.Remove(node))
        {
            Refresh();
        }
    }

    /// <summary>Reads the nodes again from <see cref="ChildrenOf"/>; every row is bound again.</summary>
    public void Refresh()
    {
        _rows.Clear();
        _indexOf.Clear();
        if (ChildrenOf is { } childrenOf)
        {
            Add(childrenOf, null, 0);
        }
        _syncing = true;
        _list.ItemCount = _rows.Count;
        _list.SelectedIndex = _selected is { } selected ? IndexOf(selected) : -1;
        _syncing = false;
        _drop = null;
        _indicator.Show(null);
    }

    private void Add(Func<ulong?, IReadOnlyList<ulong>> childrenOf, ulong? parent, int depth)
    {
        IReadOnlyList<ulong> children = childrenOf(parent);
        for (int i = 0; i < children.Count; ++i)
        {
            ulong node = children[i];
            if (!_indexOf.TryAdd(node, _rows.Count))
            {
                continue; // a cycle, or a node listed twice: shown once
            }
            bool expanded = _expanded.Contains(node);
            // Asked once more for expanded nodes; collapsed ones only need to know there are any.
            bool hasChildren = childrenOf(node).Count > 0;
            _rows.Add((node, depth, hasChildren));
            if (expanded && hasChildren)
            {
                Add(childrenOf, node, depth + 1);
            }
        }
    }

    public void ScrollIntoView(ulong node) => _list.ScrollIntoView(IndexOf(node));

    /// <summary>Scrolls to the node's row and focuses it (which selects it).</summary>
    public bool FocusNode(ulong node) => _list.FocusItem(IndexOf(node));

    /// <summary>The row showing a node, if it's in view and realized.</summary>
    public TreeRow? RowOf(ulong node) => _list.RowOf(IndexOf(node))?.Content as TreeRow;

    /// <summary>Swaps the node's text for a text field: Enter or leaving keeps the edit (raising <see cref="Renamed"/>), Escape doesn't.</summary>
    public bool BeginRename(ulong node)
    {
        int index = IndexOf(node);
        if (index < 0 || !_list.FocusItem(index) || RowOf(node) is not { } row)
        {
            return false;
        }
        row.BeginRename();
        return true;
    }

    internal void RaiseRenamed(ulong node, string text) => Renamed?.Invoke(this, node, text);

    private void Select(ulong? node)
    {
        if (node == _selected)
        {
            return;
        }
        _selected = node;
        _syncing = true;
        _list.SelectedIndex = node is { } n ? IndexOf(n) : -1;
        _syncing = false;
        SelectionChanged?.Invoke(this);
    }

    protected override void OnNavigate(NavigationEvent e)
    {
        if (e.Direction is not (NavigationDirection.Left or NavigationDirection.Right) ||
            Root?.FocusedElement is not ListItem { } item || item.List != _list || item.Index < 0)
        {
            return;
        }
        (ulong node, int depth, bool hasChildren) = _rows[item.Index];
        bool expanded = _expanded.Contains(node);
        e.Handled = true;
        if (e.Direction == NavigationDirection.Right)
        {
            if (hasChildren && !expanded)
            {
                SetExpanded(node, true);
            }
            else if (hasChildren)
            {
                _list.FocusItem(item.Index + 1);
            }
        }
        else if (expanded)
        {
            SetExpanded(node, false);
        }
        else
        {
            // The parent: the nearest row above that's a level up.
            for (int i = item.Index - 1; i >= 0; --i)
            {
                if (_rows[i].Depth < depth)
                {
                    _list.FocusItem(i);
                    break;
                }
            }
        }
    }

    protected override void OnPointerDown(PointerEvent e)
    {
        int index = _list.IndexAt(e.Position);
        _pressed = null;
        _dragging = false;
        if (e.Button == PointerButton.Left && index >= 0)
        {
            _pressed = _rows[index].Node;
            _pressedAt = e.Position;
        }
        else if (e.Button == PointerButton.Right)
        {
            e.Handled = true;
            ulong? node = index >= 0 ? _rows[index].Node : null;
            if (node is { } n)
            {
                FocusNode(n);
                Select(n);
            }
            ContextRequested?.Invoke(this, node, e.Position);
        }
    }

    protected override void OnPointerMove(PointerEvent e)
    {
        if (_pressed is not { } node || !CanDrag)
        {
            return;
        }
        if (!_dragging && Vector2.Distance(e.Position, _pressedAt) < DragThreshold)
        {
            return;
        }
        _dragging = true;
        e.Handled = true;
        _drop = DropAt(node, e.Position, out RectangleF? area, out bool inside);
        _indicator.Show(_drop is null ? null : area, inside);
    }

    protected override void OnPointerUp(PointerEvent e)
    {
        bool dragged = _dragging;
        TreeDrop? drop = _drop;
        _pressed = null;
        _dragging = false;
        _drop = null;
        _indicator.Show(null);
        if (dragged)
        {
            e.Handled = true;
            if (drop is { } d)
            {
                Dropped?.Invoke(this, d);
            }
        }
    }

    // Where a node dragged to a point would go: the top quarter of a row is
    // before it, the bottom quarter after, the middle inside; below the
    // rows, after the last root. Null on the node itself or inside it.
    internal TreeDrop? DropAt(ulong node, Vector2 point, out RectangleF? area, out bool inside)
    {
        area = null;
        inside = false;
        int from = IndexOf(node);
        if (from < 0 || _rows.Count == 0)
        {
            return null;
        }
        int index = _list.IndexAt(point);
        TreeDropPosition position;
        if (index >= 0 && _list.RowOf(index) is { } row)
        {
            float t = (point.Y - row.Bounds.Y) / MathF.Max(row.Bounds.Height, 1);
            position = t < 0.25f ? TreeDropPosition.Before : t > 0.75f ? TreeDropPosition.After : TreeDropPosition.Inside;
        }
        else if (_rows.Count > 0 && _list.RowOf(_rows.Count - 1) is { } last && point.Y >= last.Bounds.Bottom)
        {
            index = _rows.FindLastIndex(r => r.Depth == 0);
            position = TreeDropPosition.After;
        }
        else
        {
            return null;
        }
        // The node itself and what's shown inside it can't take it.
        int end = from + 1;
        while (end < _rows.Count && _rows[end].Depth > _rows[from].Depth)
        {
            ++end;
        }
        if (index >= from && index < end)
        {
            return null;
        }
        // After an expanded node with children reads as before its first child.
        (ulong target, int depth, bool hasChildren) = _rows[index];
        if (position == TreeDropPosition.After && hasChildren && _expanded.Contains(target) && index + 1 < _rows.Count)
        {
            if (index + 1 == from)
            {
                return null; // already there
            }
            ++index;
            (target, depth, _) = _rows[index];
            position = TreeDropPosition.Before;
        }
        if (_list.RowOf(index) is not { } at)
        {
            return null;
        }
        RectangleF b = at.Bounds;
        float x = b.X + depth * Indent;
        area = position switch
        {
            TreeDropPosition.Before => new RectangleF(x, b.Y - 1, b.Right - x, 2),
            TreeDropPosition.After => new RectangleF(x, b.Bottom - 1, b.Right - x, 2),
            _ => b,
        };
        inside = position == TreeDropPosition.Inside;
        return new TreeDrop(node, target, position);
    }

    // The drop indicator: a line between rows, or an outline around one, drawn above them.
    private sealed class DropIndicator : UIElement
    {
        private RectangleF? _area;
        private bool _inside;

        public DropIndicator()
        {
            IsHitTestVisible = false;
            Classes.Add("tree-drop");
        }

        public void Show(RectangleF? area, bool inside = false)
        {
            if (area != _area || inside != _inside)
            {
                _area = area;
                _inside = inside;
                InvalidateVisual();
            }
        }

        protected override void OnDraw(Canvas canvas)
        {
            if (_area is not { } area)
            {
                return;
            }
            Vector2 at = new(area.X - Bounds.X, area.Y - Bounds.Y), size = new(area.Width, area.Height);
            if (_inside)
            {
                canvas.StrokeRect(at + new Vector2(1), size - new Vector2(2), Foreground, 1.5f, new Vector4(3));
            }
            else
            {
                canvas.FillRect(at, size, Foreground);
            }
        }
    }
}
