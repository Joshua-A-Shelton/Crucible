using System.Drawing;
using System.Numerics;

namespace Crucible.UI;

/// <summary>
/// A list's row (ADR 0063): focusable, holding the content the list's
/// <see cref="ListBox.CreateItem"/> made; it has the class <c>selected</c>
/// while it's the selection. Activating it (a click, Enter, gamepad South)
/// selects it.
/// </summary>
public sealed class ListItem : UIElement
{
    internal ListItem(ListBox list, UIElement content)
    {
        List = list;
        Content = content;
        IsFocusable = true;
        AddChild(content);
    }

    public ListBox List { get; }

    public UIElement Content { get; }

    /// <summary>The item it shows now; rows are reused as the list scrolls.</summary>
    public int Index { get; internal set; } = -1;

    protected override void OnActivate(UIEvent e)
    {
        e.Handled = true;
        List.Activate(Index);
    }

    protected override void OnGotFocus(UIEvent e) => List.ItemFocused?.Invoke(Index);
}

/// <summary>
/// A scrolling list with one selected item (ADR 0063), virtualized: rows
/// exist only for the items in view, made by <see cref="CreateItem"/>,
/// filled by <see cref="BindItem"/>, and reused as it scrolls. Rows may be
/// any height: each is measured as it comes into view, and the others are
/// estimated (the average measured so far, or
/// <see cref="EstimatedItemHeight"/>), so the scrollbar settles as more are
/// seen. Up and down move focus row by row, scrolling as needed; past
/// either end, focus leaves the list.
/// </summary>
public class ListBox : UIElement
{
    private readonly ScrollView _scroll = new();
    private readonly ListPanel _panel;
    private int _itemCount;
    private int _selected = -1;

    public ListBox()
    {
        _panel = new ListPanel(this);
        _scroll.Content = _panel;
        _scroll.ScrollChanged += _panel.InvalidateMeasure;
        AddChild(_scroll);
    }

    /// <summary>How many items there are; call <see cref="ItemsChanged"/> when they change otherwise.</summary>
    public int ItemCount
    {
        get => _itemCount;
        set
        {
            _itemCount = Math.Max(value, 0);
            ItemsChanged();
        }
    }

    /// <summary>Makes a row's content; called as rows are first needed.</summary>
    public Func<UIElement>? CreateItem { get; set; }

    /// <summary>Fills a row's content for an item: called whenever a row shows a different item.</summary>
    public Action<UIElement, int>? BindItem { get; set; }

    /// <summary>A row's height until any has been measured, in logical units.</summary>
    public float EstimatedItemHeight { get; set; } = 32;

    public ScrollView ScrollView => _scroll;

    /// <summary>The selected item, or -1.</summary>
    public int SelectedIndex
    {
        get => _selected;
        set
        {
            int index = value >= 0 && value < _itemCount ? value : -1;
            if (index == _selected)
            {
                return;
            }
            _selected = index;
            _panel.MarkSelection();
            SelectionChanged?.Invoke(this);
        }
    }

    public event Action<ListBox>? SelectionChanged;

    /// <summary>An item was activated (after it's selected): a click, Enter, or gamepad South.</summary>
    public event Action<ListBox, int>? ItemActivated;

    /// <summary>The rows that exist now, for tests and tools.</summary>
    public int RealizedCount => _panel.Realized.Count;

    /// <summary>Items changed: every row is bound again and heights measured anew.</summary>
    public void ItemsChanged()
    {
        if (_selected >= _itemCount)
        {
            SelectedIndex = -1;
        }
        _panel.Reset();
    }

    /// <summary>Scrolls the least that shows the item.</summary>
    public void ScrollIntoView(int index)
    {
        if (index < 0 || index >= _itemCount)
        {
            return;
        }
        (float top, float height) = _panel.Span(index);
        float offset = _scroll.Offset.Y, viewport = _scroll.Viewport.Y;
        if (top < offset)
        {
            _scroll.ScrollTo(Orientation.Vertical, top);
        }
        else if (top + height > offset + viewport)
        {
            _scroll.ScrollTo(Orientation.Vertical, top + height - viewport);
        }
        _panel.InvalidateMeasure();
    }

    /// <summary>Scrolls to the item and focuses its row.</summary>
    public bool FocusItem(int index)
    {
        if (index < 0 || index >= _itemCount || Root is not { } root)
        {
            return false;
        }
        ScrollIntoView(index);
        root.UpdateLayout(root.Size * root.Scale);
        // Scrolled to an estimate: once its row is measured, show it exactly.
        ScrollIntoView(index);
        root.UpdateLayout(root.Size * root.Scale);
        return _panel.Realized.TryGetValue(index, out ListItem? row) && row.Focus();
    }

    // The item whose row is under a point (the root's units), or -1.
    internal int IndexAt(Vector2 point)
    {
        foreach ((int index, ListItem row) in _panel.Realized)
        {
            if (row.Bounds.Contains(point.X, point.Y))
            {
                return index;
            }
        }
        return -1;
    }

    // The row showing an item, if it's realized.
    internal ListItem? RowOf(int index) => _panel.Realized.GetValueOrDefault(index);

    // A tree view's (ADR 0068): rows take this class too, and focus moving
    // to a row tells it, so selection can follow focus.
    internal string? ItemClass { get; set; }

    internal Action<int>? ItemFocused { get; set; }

    internal void Activate(int index)
    {
        SelectedIndex = index;
        ItemActivated?.Invoke(this, index);
    }

    internal float ViewportGuess { get; private set; }

    protected override Vector2 MeasureCore(Vector2 available)
    {
        // Before the scroll view is arranged, rows are realized for this.
        ViewportGuess = float.IsFinite(available.Y) ? available.Y : 0;
        return base.MeasureCore(available);
    }

    protected override void OnNavigate(NavigationEvent e)
    {
        if (e.Direction is not (NavigationDirection.Up or NavigationDirection.Down) ||
            Root?.FocusedElement is not ListItem { } row || row.List != this)
        {
            return;
        }
        int next = row.Index + (e.Direction == NavigationDirection.Down ? 1 : -1);
        if (next >= 0 && next < _itemCount && FocusItem(next))
        {
            e.Handled = true;
        }
    }

    // The rows in view, laid out by item from the measured and estimated heights.
    private sealed class ListPanel(ListBox list) : UIElement
    {
        private float[] _heights = []; // NaN: not measured yet
        private float _measuredSum;
        private int _measuredCount;
        private readonly Stack<ListItem> _free = new();
        private int _anchor; // the first item in view last time
        private float _width;

        public readonly Dictionary<int, ListItem> Realized = [];

        public void Reset()
        {
            _heights = new float[list._itemCount];
            Array.Fill(_heights, float.NaN);
            _measuredSum = 0;
            _measuredCount = 0;
            foreach (ListItem row in Realized.Values)
            {
                row.Index = -1; // bound again when next shown
            }
            InvalidateMeasure();
        }

        public void MarkSelection()
        {
            foreach ((int index, ListItem row) in Realized)
            {
                row.Classes.Set("selected", index == list._selected);
            }
        }

        private float Estimate => _measuredCount > 0 ? _measuredSum / _measuredCount : list.EstimatedItemHeight;

        private float RowHeight(int index) => float.IsNaN(_heights[index]) ? Estimate : _heights[index];

        // An item's top and height, measured or estimated.
        public (float Top, float Height) Span(int index)
        {
            float top = 0;
            for (int i = 0; i < index; ++i)
            {
                top += RowHeight(i);
            }
            return (top, RowHeight(index));
        }

        protected override Vector2 MeasureCore(Vector2 available)
        {
            int count = list._itemCount;
            if (_heights.Length != count)
            {
                Reset();
            }
            _width = available.X;
            ScrollView scroll = list._scroll;
            float offset = scroll.Offset.Y;
            float viewport = MathF.Max(scroll.Viewport.Y, list.ViewportGuess);
            // The items in view, with one more each side.
            int first = 0;
            float top = 0;
            while (first < count && top + RowHeight(first) <= offset)
            {
                top += RowHeight(first);
                ++first;
            }
            first = Math.Max(first - 1, 0);
            HashSet<int> wanted = [];
            float y = count > 0 ? Span(first).Top : 0;
            for (int i = first; i < count && (y < offset + viewport || wanted.Count < 2); ++i)
            {
                wanted.Add(i);
                y += MeasureRow(i, offset);
            }
            if (wanted.Count > 0 && wanted.Max() + 1 < count)
            {
                wanted.Add(wanted.Max() + 1);
                MeasureRow(wanted.Max(), offset);
            }
            _anchor = first;
            // Rows out of view go back to the pool, but not the focused one.
            foreach ((int index, ListItem row) in Realized.ToArray())
            {
                if (!wanted.Contains(index) && !row.IsFocused)
                {
                    Realized.Remove(index);
                    RemoveChildDuringLayout(row);
                    _free.Push(row);
                }
            }
            float total = 0;
            for (int i = 0; i < count; ++i)
            {
                total += RowHeight(i);
            }
            // As wide as the widest row in view (a stretched list is widened by its parent anyway).
            float widest = 0;
            foreach (ListItem row in Realized.Values)
            {
                widest = MathF.Max(widest, row.DesiredSize.X);
            }
            return new Vector2(widest, total);
        }

        // Realizes and measures a row; a row above the first in view that
        // turns out a different height than estimated moves the offset
        // with it, so what's in view stays still.
        private float MeasureRow(int index, float offset)
        {
            ListItem row = RowFor(index);
            row.Measure(new Vector2(_width, float.PositiveInfinity));
            float height = row.DesiredSize.Y;
            float before = RowHeight(index);
            if (float.IsNaN(_heights[index]))
            {
                _measuredSum += height;
                ++_measuredCount;
            }
            else
            {
                _measuredSum += height - _heights[index];
            }
            _heights[index] = height;
            if (index < _anchor && height != before && offset > 0)
            {
                list._scroll.ShiftOffset(height - before);
            }
            return height;
        }

        private ListItem RowFor(int index)
        {
            if (!Realized.TryGetValue(index, out ListItem? row))
            {
                if (_free.Count > 0)
                {
                    row = _free.Pop();
                }
                else
                {
                    row = new ListItem(list, list.CreateItem?.Invoke() ?? new Label());
                    if (list.ItemClass is { } itemClass)
                    {
                        row.Classes.Add(itemClass);
                    }
                }
                Realized[index] = row;
                AddChildDuringLayout(row);
            }
            if (row.Index != index)
            {
                row.Index = index;
                list.BindItem?.Invoke(row.Content, index);
                row.Classes.Set("selected", index == list._selected);
                row.MarkDirty(); // measured right after; its content's own changes ask for another pass
            }
            return row;
        }

        protected override void ArrangeCore(RectangleF content)
        {
            foreach ((int index, ListItem row) in Realized)
            {
                (float top, float height) = Span(index);
                row.Arrange(new RectangleF(content.X, content.Y + top, content.Width, height));
            }
        }
    }
}
