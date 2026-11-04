using System.Drawing;
using System.Numerics;

namespace Crucible.UI;

public enum HorizontalAlignment
{
    Stretch,
    Start,
    Center,
    End,
}

public enum VerticalAlignment
{
    Stretch,
    Start,
    Center,
    End,
}

public enum Visibility
{
    Visible,
    Hidden,    // keeps its space; not drawn or hit
    Collapsed, // takes no space
}

/// <summary>Space around (margin) or inside (padding) an element, in logical units.</summary>
public readonly record struct Thickness(float Left, float Top, float Right, float Bottom)
{
    public Thickness(float uniform) : this(uniform, uniform, uniform, uniform)
    {
    }

    public float Horizontal => Left + Right;

    public float Vertical => Top + Bottom;

    public Vector2 Total => new(Horizontal, Vertical);
}

/// <summary>
/// An element of the UI tree (ADR 0060): laid out in two passes, measure
/// then arrange (WPF's), drawn with a <see cref="Canvas"/>, and hit by
/// pointers. Panels lay out children; a custom-draw element is any element
/// that overrides <see cref="OnDraw"/>. Positions are logical units; each
/// element's edges are snapped to whole physical pixels. Main thread only.
/// </summary>
public class UIElement
{
    private readonly List<UIElement> _children = [];
    private Vector2 _lastAvailable = new(float.NaN);
    private RectangleF _lastSlot = new(float.NaN, 0, 0, 0);
    private bool _measureDirty = true;
    private bool _arrangeDirty = true;

    private float _width = float.NaN, _height = float.NaN;
    private float _minWidth, _minHeight, _maxWidth = float.PositiveInfinity, _maxHeight = float.PositiveInfinity;
    private Thickness _margin, _padding;
    private HorizontalAlignment _horizontal;
    private VerticalAlignment _vertical;
    private Visibility _visibility;
    private bool _clipToBounds;
    private Matrix3x2 _renderTransform = Matrix3x2.Identity;
    private Vector2 _transformOrigin = new(0.5f);
    private Vector4 _background;
    private Vector4 _cornerRadii;

    // Panels' per-child settings (Grid.SetRow, DockPanel.SetDock, AnchorPanel.SetAnchors).
    internal int GridRow, GridColumn, GridRowSpan = 1, GridColumnSpan = 1;
    internal Dock DockSide;
    internal Anchors Anchors = Anchors.Fill;

    public UIElement? Parent { get; private set; }

    internal UIRoot? Root { get; private set; }

    /// <summary>The element's own children, in drawing order (back to front).</summary>
    public IReadOnlyList<UIElement> Children => _children;

    /// <summary>A fixed width in logical units; NaN (the default) sizes it to its content or slot.</summary>
    public float Width { get => _width; set => SetLayout(ref _width, value); }

    public float Height { get => _height; set => SetLayout(ref _height, value); }

    public float MinWidth { get => _minWidth; set => SetLayout(ref _minWidth, value); }

    public float MinHeight { get => _minHeight; set => SetLayout(ref _minHeight, value); }

    public float MaxWidth { get => _maxWidth; set => SetLayout(ref _maxWidth, value); }

    public float MaxHeight { get => _maxHeight; set => SetLayout(ref _maxHeight, value); }

    public Thickness Margin { get => _margin; set => SetLayout(ref _margin, value); }

    public Thickness Padding { get => _padding; set => SetLayout(ref _padding, value); }

    public HorizontalAlignment HorizontalAlignment { get => _horizontal; set => SetLayout(ref _horizontal, value); }

    public VerticalAlignment VerticalAlignment { get => _vertical; set => SetLayout(ref _vertical, value); }

    public Visibility Visibility
    {
        get => _visibility;
        set
        {
            if (_visibility != value)
            {
                bool collapsing = value == Visibility.Collapsed || _visibility == Visibility.Collapsed;
                _visibility = value;
                if (collapsing)
                {
                    InvalidateMeasure();
                }
                InvalidateVisual();
            }
        }
    }

    /// <summary>Whether pointers can hit it (its children are tested either way).</summary>
    public bool IsHitTestVisible { get; set; } = true;

    /// <summary>Clips its own and its children's drawing, and hits, to its bounds.</summary>
    public bool ClipToBounds { get => _clipToBounds; set => SetVisual(ref _clipToBounds, value); }

    /// <summary>
    /// Applied when drawing and hit testing, around <see cref="TransformOrigin"/>,
    /// but not to layout: for animating an element without moving its neighbors.
    /// </summary>
    public Matrix3x2 RenderTransform { get => _renderTransform; set => SetVisual(ref _renderTransform, value); }

    /// <summary>The render transform's origin, as a fraction of the bounds (the center by default).</summary>
    public Vector2 TransformOrigin { get => _transformOrigin; set => SetVisual(ref _transformOrigin, value); }

    /// <summary>Filled behind the element's drawing (sRGB, straight alpha); transparent by default.</summary>
    public Vector4 Background { get => _background; set => SetVisual(ref _background, value); }

    /// <summary>The background's corners: top left, top right, bottom right, bottom left.</summary>
    public Vector4 CornerRadii { get => _cornerRadii; set => SetVisual(ref _cornerRadii, value); }

    /// <summary>The size the last measure asked for, margin included.</summary>
    public Vector2 DesiredSize { get; private set; }

    /// <summary>Where the last arrange put it, in the root's logical units, snapped to physical pixels.</summary>
    public RectangleF Bounds { get; private set; }

    /// <summary>The bounds' size: what <see cref="OnDraw"/> draws within.</summary>
    public Vector2 ActualSize => new(Bounds.Width, Bounds.Height);

    /// <summary>
    /// Asks for the element's size given <paramref name="available"/> space
    /// (which may be infinite). Repeats nothing unless the space or the
    /// element changed.
    /// </summary>
    public void Measure(Vector2 available)
    {
        if (!_measureDirty && available == _lastAvailable)
        {
            return;
        }
        _measureDirty = false;
        _lastAvailable = available;
        _arrangeDirty = true;
        if (_visibility == Visibility.Collapsed)
        {
            DesiredSize = Vector2.Zero;
            return;
        }
        Vector2 inner = Vector2.Max(available - _margin.Total, Vector2.Zero);
        (Vector2 min, Vector2 max) = Limits();
        inner = Vector2.Clamp(inner, min, max);
        Vector2 content = MeasureCore(Vector2.Max(inner - _padding.Total, Vector2.Zero)) + _padding.Total;
        Vector2 size = Vector2.Clamp(content, min, max);
        DesiredSize = Vector2.Max(size + _margin.Total, Vector2.Zero);
    }

    /// <summary>
    /// Places the element in <paramref name="slot"/> (its parent's choice,
    /// margin included), by its alignment, then its children.
    /// </summary>
    public void Arrange(RectangleF slot)
    {
        if (!_arrangeDirty && slot == _lastSlot)
        {
            return;
        }
        _arrangeDirty = false;
        _lastSlot = slot;
        if (_visibility == Visibility.Collapsed)
        {
            Bounds = new RectangleF(slot.X, slot.Y, 0, 0);
            return;
        }
        Vector2 space = Vector2.Max(new Vector2(slot.Width, slot.Height) - _margin.Total, Vector2.Zero);
        Vector2 desired = Vector2.Max(DesiredSize - _margin.Total, Vector2.Zero);
        (Vector2 min, Vector2 max) = Limits();
        Vector2 size = new(
            _horizontal == HorizontalAlignment.Stretch && float.IsNaN(_width) ? space.X : MathF.Min(desired.X, space.X),
            _vertical == VerticalAlignment.Stretch && float.IsNaN(_height) ? space.Y : MathF.Min(desired.Y, space.Y));
        size = Vector2.Clamp(size, min, max);
        float x = slot.X + _margin.Left + _horizontal switch
        {
            HorizontalAlignment.Center => (space.X - size.X) * 0.5f,
            HorizontalAlignment.End => space.X - size.X,
            _ => 0,
        };
        float y = slot.Y + _margin.Top + _vertical switch
        {
            VerticalAlignment.Center => (space.Y - size.Y) * 0.5f,
            VerticalAlignment.End => space.Y - size.Y,
            _ => 0,
        };
        Bounds = Snap(new RectangleF(x, y, size.X, size.Y));
        ArrangeCore(new RectangleF(x + _padding.Left, y + _padding.Top, MathF.Max(size.X - _padding.Horizontal, 0),
            MathF.Max(size.Y - _padding.Vertical, 0)));
        InvalidateVisual();
    }

    /// <summary>The content's desired size within <paramref name="available"/> (padding removed). By default, the largest child's.</summary>
    protected virtual Vector2 MeasureCore(Vector2 available)
    {
        Vector2 size = Vector2.Zero;
        foreach (UIElement child in _children)
        {
            child.Measure(available);
            size = Vector2.Max(size, child.DesiredSize);
        }
        return size;
    }

    /// <summary>Arranges the children within <paramref name="content"/> (padding removed). By default, each fills it.</summary>
    protected virtual void ArrangeCore(RectangleF content)
    {
        foreach (UIElement child in _children)
        {
            child.Arrange(content);
        }
    }

    /// <summary>
    /// Draws the element in its own coordinates: (0, 0) to
    /// <see cref="ActualSize"/>, over its background and under its children.
    /// </summary>
    protected virtual void OnDraw(Canvas canvas)
    {
    }

    /// <summary>The element needs measuring again (its size may change); so do its ancestors.</summary>
    public void InvalidateMeasure()
    {
        for (UIElement? element = this; element is not null && !element._measureDirty; element = element.Parent)
        {
            element._measureDirty = true;
            element._arrangeDirty = true;
        }
        Root?.InvalidateLayout();
    }

    /// <summary>The element needs arranging again (its children move, not its size).</summary>
    public void InvalidateArrange()
    {
        for (UIElement? element = this; element is not null && !element._arrangeDirty; element = element.Parent)
        {
            element._arrangeDirty = true;
        }
        Root?.InvalidateLayout();
    }

    /// <summary>The element looks different: the root draws again.</summary>
    public void InvalidateVisual() => Root?.InvalidateDrawing();

    protected void AddChild(UIElement child) => InsertChild(_children.Count, child);

    protected void InsertChild(int index, UIElement child)
    {
        ArgumentNullException.ThrowIfNull(child);
        if (child.Parent is not null || child.Root is { } root && root.Content == child)
        {
            throw new InvalidOperationException("The element already has a parent");
        }
        for (UIElement? ancestor = this; ancestor is not null; ancestor = ancestor.Parent)
        {
            if (ancestor == child)
            {
                throw new InvalidOperationException("An element can't contain itself");
            }
        }
        _children.Insert(index, child);
        child.Parent = this;
        child.Attach(Root);
        InvalidateMeasure();
    }

    protected void RemoveChild(UIElement child)
    {
        if (_children.Remove(child))
        {
            child.Parent = null;
            child.Attach(null);
            InvalidateMeasure();
        }
    }

    protected void ClearChildren()
    {
        foreach (UIElement child in _children)
        {
            child.Parent = null;
            child.Attach(null);
        }
        _children.Clear();
        InvalidateMeasure();
    }

    internal void Attach(UIRoot? root)
    {
        Root = root;
        foreach (UIElement child in _children)
        {
            child.Attach(root);
        }
    }

    // Drawing order: background, OnDraw, children; clipped and transformed.
    internal void Draw(Canvas canvas)
    {
        if (_visibility != Visibility.Visible)
        {
            return;
        }
        bool transformed = !_renderTransform.IsIdentity;
        if (transformed)
        {
            canvas.PushTransform(TransformAroundOrigin());
        }
        if (_clipToBounds)
        {
            canvas.PushClip(new Vector2(Bounds.X, Bounds.Y), ActualSize);
        }
        canvas.PushTransform(Matrix3x2.CreateTranslation(Bounds.X, Bounds.Y));
        if (_background.W > 0)
        {
            canvas.FillRect(Vector2.Zero, ActualSize, _background, _cornerRadii);
        }
        OnDraw(canvas);
        canvas.PopTransform();
        foreach (UIElement child in _children)
        {
            child.Draw(canvas);
        }
        if (_clipToBounds)
        {
            canvas.PopClip();
        }
        if (transformed)
        {
            canvas.PopTransform();
        }
    }

    // The topmost element at `point` (the parent's coordinates), children
    // first, back to front.
    internal UIElement? HitTest(Vector2 point)
    {
        if (_visibility != Visibility.Visible)
        {
            return null;
        }
        if (!_renderTransform.IsIdentity)
        {
            if (!Matrix3x2.Invert(TransformAroundOrigin(), out Matrix3x2 inverse))
            {
                return null;
            }
            point = Vector2.Transform(point, inverse);
        }
        bool inside = Bounds.Contains(point.X, point.Y);
        if (_clipToBounds && !inside)
        {
            return null;
        }
        for (int i = _children.Count - 1; i >= 0; --i)
        {
            if (_children[i].HitTest(point) is { } hit)
            {
                return hit;
            }
        }
        return inside && IsHitTestVisible ? this : null;
    }

    internal void MarkDirty()
    {
        _measureDirty = true;
        _arrangeDirty = true;
    }

    private Matrix3x2 TransformAroundOrigin()
    {
        Vector2 origin = new(Bounds.X + Bounds.Width * _transformOrigin.X, Bounds.Y + Bounds.Height * _transformOrigin.Y);
        return Matrix3x2.CreateTranslation(-origin) * _renderTransform * Matrix3x2.CreateTranslation(origin);
    }

    private (Vector2 Min, Vector2 Max) Limits()
    {
        Vector2 max = new(float.IsNaN(_width) ? _maxWidth : _width, float.IsNaN(_height) ? _maxHeight : _height);
        Vector2 min = new(float.IsNaN(_width) ? _minWidth : _width, float.IsNaN(_height) ? _minHeight : _height);
        min = Vector2.Max(min, Vector2.Zero);
        return (min, Vector2.Max(min, max));
    }

    // Edges to whole physical pixels, each on its own, so neighbors share
    // the edge between them.
    private RectangleF Snap(RectangleF rect)
    {
        float scale = Root?.Scale ?? 1;
        float x0 = MathF.Round(rect.Left * scale) / scale, y0 = MathF.Round(rect.Top * scale) / scale;
        float x1 = MathF.Round(rect.Right * scale) / scale, y1 = MathF.Round(rect.Bottom * scale) / scale;
        return new RectangleF(x0, y0, x1 - x0, y1 - y0);
    }

    private void SetLayout<T>(ref T field, T value)
    {
        if (!EqualityComparer<T>.Default.Equals(field, value))
        {
            field = value;
            InvalidateMeasure();
        }
    }

    private void SetVisual<T>(ref T field, T value)
    {
        if (!EqualityComparer<T>.Default.Equals(field, value))
        {
            field = value;
            InvalidateVisual();
        }
    }
}

/// <summary>An element whose children the API user adds and removes.</summary>
public abstract class Panel : UIElement
{
    public void Add(UIElement child) => AddChild(child);

    public void Insert(int index, UIElement child) => InsertChild(index, child);

    public void Remove(UIElement child) => RemoveChild(child);

    public void Clear() => ClearChildren();
}
