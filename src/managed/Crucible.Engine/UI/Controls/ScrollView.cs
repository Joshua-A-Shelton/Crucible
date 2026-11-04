using System.Drawing;
using System.Numerics;

namespace Crucible.UI;

/// <summary>A scroll view's bar (ADR 0063), shown only while there's more to see; holds a <see cref="ScrollThumb"/>.</summary>
public sealed class ScrollBar : UIElement
{
    private readonly ScrollThumb _thumb = new();
    private float _viewport = 1, _position;
    private bool _dragging;
    private float _dragStart, _dragOffset;

    internal ScrollBar(Orientation orientation)
    {
        Orientation = orientation;
        Classes.Add(orientation == Orientation.Vertical ? "vertical" : "horizontal");
        AddChild(_thumb);
    }

    public Orientation Orientation { get; }

    public ScrollThumb Thumb => _thumb;

    private ScrollView View => (ScrollView)Parent!;

    // The visible share (0 to 1) and how far along (0 to 1), set by the view before it arranges.
    internal void SetRange(float viewport, float position)
    {
        _viewport = Math.Clamp(viewport, 0, 1);
        _position = Math.Clamp(position, 0, 1);
        ArrangeAgain(); // the view arranges it right after
    }

    protected override void ArrangeCore(RectangleF content)
    {
        bool vertical = Orientation == Orientation.Vertical;
        float length = vertical ? content.Height : content.Width;
        float thumb = ThumbLength(length);
        float at = _position * (length - thumb);
        _thumb.Arrange(vertical
            ? new RectangleF(content.X, content.Y + at, content.Width, thumb)
            : new RectangleF(content.X + at, content.Y, thumb, content.Height));
    }

    // At least the thumb's style minimum (or 20 units), so it stays grabbable.
    private float ThumbLength(float length)
    {
        float minimum = Orientation == Orientation.Vertical ? _thumb.MinHeight : _thumb.MinWidth;
        return Math.Clamp(_viewport * length, MathF.Max(minimum, 20), length);
    }

    protected override void OnPointerDown(PointerEvent e)
    {
        if (e.Button != PointerButton.Left)
        {
            return;
        }
        float pointer = Orientation == Orientation.Vertical ? e.Position.Y : e.Position.X;
        if (e.Source == _thumb)
        {
            _dragging = true;
            _dragStart = pointer;
            _dragOffset = Along(View.Offset);
        }
        else
        {
            // On the bar beside the thumb: a page toward the pointer.
            RectangleF thumb = _thumb.Bounds;
            float thumbStart = Orientation == Orientation.Vertical ? thumb.Top : thumb.Left;
            View.ScrollBy(Orientation, (pointer < thumbStart ? -1 : 1) * Along(View.Viewport));
        }
        e.Handled = true;
    }

    protected override void OnPointerMove(PointerEvent e)
    {
        if (!_dragging)
        {
            return;
        }
        bool vertical = Orientation == Orientation.Vertical;
        float length = vertical ? Bounds.Height : Bounds.Width;
        float travel = MathF.Max(length - ThumbLength(length), 1e-3f);
        float scrollable = Along(View.Extent) - Along(View.Viewport);
        float moved = (vertical ? e.Position.Y : e.Position.X) - _dragStart;
        View.ScrollTo(Orientation, _dragOffset + moved / travel * scrollable);
        e.Handled = true;
    }

    protected override void OnPointerUp(PointerEvent e)
    {
        if (_dragging)
        {
            _dragging = false;
            e.Handled = true;
        }
    }

    private float Along(Vector2 v) => Orientation == Orientation.Vertical ? v.Y : v.X;
}

/// <summary>The draggable part of a <see cref="ScrollBar"/>.</summary>
public sealed class ScrollThumb : UIElement
{
    internal ScrollThumb()
    {
    }
}

/// <summary>
/// Scrolls one content element (ADR 0063), clipped to its bounds:
/// vertically by default, horizontally too if asked. The mouse wheel
/// scrolls it (Shift turns vertical wheels horizontal), the bars can be
/// dragged or clicked beside the thumb, and focus moving to an element
/// inside scrolls it into view.
/// </summary>
public class ScrollView : UIElement
{
    private readonly ScrollBar _vertical = new(Orientation.Vertical) { Visibility = Visibility.Collapsed };
    private readonly ScrollBar _horizontal = new(Orientation.Horizontal) { Visibility = Visibility.Collapsed };
    private UIElement? _content;
    private Vector2 _offset;
    private bool _scrollsHorizontally;
    private bool _scrollsVertically = true;

    public ScrollView()
    {
        ClipToBounds = true;
        AddChild(_vertical);
        AddChild(_horizontal);
    }

    public UIElement? Content
    {
        get => _content;
        set
        {
            if (_content == value)
            {
                return;
            }
            if (_content is not null)
            {
                RemoveChild(_content);
            }
            _content = value;
            if (value is not null)
            {
                InsertChild(0, value); // under the bars
            }
        }
    }

    public bool ScrollsHorizontally
    {
        get => _scrollsHorizontally;
        set
        {
            _scrollsHorizontally = value;
            InvalidateMeasure();
        }
    }

    public bool ScrollsVertically
    {
        get => _scrollsVertically;
        set
        {
            _scrollsVertically = value;
            InvalidateMeasure();
        }
    }

    /// <summary>Logical units per wheel notch.</summary>
    public float WheelStep { get; set; } = 48;

    /// <summary>How far the content is scrolled, clamped to what there is to see.</summary>
    public Vector2 Offset
    {
        get => _offset;
        set
        {
            Vector2 clamped = Vector2.Clamp(value, Vector2.Zero, Vector2.Max(Extent - Viewport, Vector2.Zero));
            if (clamped != _offset)
            {
                _offset = clamped;
                InvalidateArrange();
                ScrollChanged?.Invoke();
            }
        }
    }

    // The offset or the viewport changed: a virtualizing list realizes again.
    internal event Action? ScrollChanged;

    // Moves the offset without arranging again: a list keeping its rows
    // still while rows above it change size, mid-measure.
    internal void ShiftOffset(float dy) => _offset.Y = MathF.Max(_offset.Y + dy, 0);

    /// <summary>The content's size, as last measured.</summary>
    public Vector2 Extent { get; private set; }

    /// <summary>The size it shows, as last arranged.</summary>
    public Vector2 Viewport { get; private set; }

    public ScrollBar VerticalBar => _vertical;

    public ScrollBar HorizontalBar => _horizontal;

    public void ScrollTo(Orientation axis, float offset) =>
        Offset = axis == Orientation.Vertical ? _offset with { Y = offset } : _offset with { X = offset };

    public void ScrollBy(Orientation axis, float delta) =>
        ScrollTo(axis, (axis == Orientation.Vertical ? _offset.Y : _offset.X) + delta);

    /// <summary>Scrolls the least that shows a rectangle (in the root's logical units, as laid out).</summary>
    public void ScrollIntoView(RectangleF target)
    {
        RectangleF view = Bounds;
        Vector2 offset = _offset;
        if (_scrollsVertically)
        {
            if (target.Bottom > view.Bottom)
            {
                offset.Y += target.Bottom - view.Bottom;
            }
            if (target.Top < view.Top)
            {
                offset.Y -= view.Top - target.Top;
            }
        }
        if (_scrollsHorizontally)
        {
            if (target.Right > view.Right)
            {
                offset.X += target.Right - view.Right;
            }
            if (target.Left < view.Left)
            {
                offset.X -= view.Left - target.Left;
            }
        }
        Offset = offset;
    }

    protected override Vector2 MeasureCore(Vector2 available)
    {
        _vertical.Measure(available);
        _horizontal.Measure(available);
        if (_content is null)
        {
            return Vector2.Zero;
        }
        // Unbounded along the axes it scrolls.
        _content.Measure(new Vector2(_scrollsHorizontally ? float.PositiveInfinity : available.X,
            _scrollsVertically ? float.PositiveInfinity : available.Y));
        Extent = _content.DesiredSize;
        return Vector2.Min(Extent, available);
    }

    protected override void ArrangeCore(RectangleF content)
    {
        Vector2 viewport = new(content.Width, content.Height);
        if (viewport != Viewport)
        {
            Viewport = viewport;
            ScrollChanged?.Invoke();
        }
        Vector2 extent = Vector2.Max(Extent, Viewport);
        _offset = Vector2.Clamp(_offset, Vector2.Zero, extent - Viewport);
        _content?.Arrange(new RectangleF(content.X - _offset.X, content.Y - _offset.Y,
            _scrollsHorizontally ? extent.X : content.Width, _scrollsVertically ? extent.Y : content.Height));

        bool showVertical = _scrollsVertically && Extent.Y > Viewport.Y + 0.5f;
        bool showHorizontal = _scrollsHorizontally && Extent.X > Viewport.X + 0.5f;
        _vertical.Visibility = showVertical ? Visibility.Visible : Visibility.Collapsed;
        _horizontal.Visibility = showHorizontal ? Visibility.Visible : Visibility.Collapsed;
        if (showVertical)
        {
            _vertical.SetRange(Viewport.Y / Extent.Y, _offset.Y / (Extent.Y - Viewport.Y));
            float width = _vertical.DesiredSize.X;
            _vertical.Arrange(new RectangleF(content.Right - width, content.Y, width,
                content.Height - (showHorizontal ? _horizontal.DesiredSize.Y : 0)));
        }
        if (showHorizontal)
        {
            _horizontal.SetRange(Viewport.X / Extent.X, _offset.X / (Extent.X - Viewport.X));
            float height = _horizontal.DesiredSize.Y;
            _horizontal.Arrange(new RectangleF(content.X, content.Bottom - height,
                content.Width - (showVertical ? _vertical.DesiredSize.X : 0), height));
        }
    }

    protected override void OnPointerWheel(PointerEvent e)
    {
        Vector2 wheel = e.Wheel;
        if ((e.Modifiers & Platform.KeyModifiers.Shift) != 0 && wheel.X == 0)
        {
            wheel = new Vector2(wheel.Y, 0);
        }
        Vector2 before = _offset;
        Offset = _offset - new Vector2(_scrollsHorizontally ? -wheel.X : 0, _scrollsVertically ? wheel.Y : 0) * WheelStep;
        e.Handled = _offset != before;
    }
}
