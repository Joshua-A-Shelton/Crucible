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
public partial class UIElement
{
    private readonly List<UIElement> _children = [];
    private Vector2 _lastAvailable = new(float.NaN);
    private RectangleF _lastSlot = new(float.NaN, 0, 0, 0);
    private bool _measureDirty = true;
    private bool _arrangeDirty = true;

    private bool _clipToBounds;
    private Matrix3x2 _renderTransform = Matrix3x2.Identity;
    private Vector2 _transformOrigin = new(0.5f);

    // Styling (ADR 0061): each styled property's layers, by its index, made
    // on first use; the classes and states selectors match; the animations
    // running.
    private ValueSlot?[] _slots = [];
    private UIState _state;
    private int _animations;
    private bool _styled; // styled since attached: later changes may transition

    // Panels' per-child settings (Grid.SetRow, DockPanel.SetDock, AnchorPanel.SetAnchors).
    internal int GridRow, GridColumn, GridRowSpan = 1, GridColumnSpan = 1;
    internal Dock DockSide;
    internal Anchors Anchors = Anchors.Fill;

    public UIElement? Parent { get; private set; }

    internal UIRoot? Root { get; private set; }

    /// <summary>The element's own children, in drawing order (back to front).</summary>
    public IReadOnlyList<UIElement> Children => _children;

    // The styled properties every element has (ADR 0061).
    public static readonly StyledProperty<float> WidthProperty =
        StyledProperty<float>.Register<UIElement>("Width", float.NaN, Invalidates.Measure, StyleValues.ReadFloat, StyleValues.Lerp);
    public static readonly StyledProperty<float> HeightProperty =
        StyledProperty<float>.Register<UIElement>("Height", float.NaN, Invalidates.Measure, StyleValues.ReadFloat, StyleValues.Lerp);
    public static readonly StyledProperty<float> MinWidthProperty =
        StyledProperty<float>.Register<UIElement>("MinWidth", 0, Invalidates.Measure, StyleValues.ReadFloat, StyleValues.Lerp);
    public static readonly StyledProperty<float> MinHeightProperty =
        StyledProperty<float>.Register<UIElement>("MinHeight", 0, Invalidates.Measure, StyleValues.ReadFloat, StyleValues.Lerp);
    public static readonly StyledProperty<float> MaxWidthProperty = StyledProperty<float>.Register<UIElement>("MaxWidth",
        float.PositiveInfinity, Invalidates.Measure, StyleValues.ReadFloat, StyleValues.Lerp);
    public static readonly StyledProperty<float> MaxHeightProperty = StyledProperty<float>.Register<UIElement>("MaxHeight",
        float.PositiveInfinity, Invalidates.Measure, StyleValues.ReadFloat, StyleValues.Lerp);
    public static readonly StyledProperty<Thickness> MarginProperty = StyledProperty<Thickness>.Register<UIElement>(
        "Margin", default, Invalidates.Measure, StyleValues.ReadThickness, StyleValues.Lerp);
    public static readonly StyledProperty<Thickness> PaddingProperty = StyledProperty<Thickness>.Register<UIElement>(
        "Padding", default, Invalidates.Measure, StyleValues.ReadThickness, StyleValues.Lerp);
    public static readonly StyledProperty<HorizontalAlignment> HorizontalAlignmentProperty =
        StyledProperty<HorizontalAlignment>.Register<UIElement>("HorizontalAlignment", HorizontalAlignment.Stretch,
            Invalidates.Measure, StyleValues.ReadEnum<HorizontalAlignment>);
    public static readonly StyledProperty<VerticalAlignment> VerticalAlignmentProperty =
        StyledProperty<VerticalAlignment>.Register<UIElement>("VerticalAlignment", VerticalAlignment.Stretch,
            Invalidates.Measure, StyleValues.ReadEnum<VerticalAlignment>);
    public static readonly StyledProperty<Visibility> VisibilityProperty = StyledProperty<Visibility>.Register<UIElement>(
        "Visibility", Visibility.Visible, Invalidates.Measure | Invalidates.Visual, StyleValues.ReadEnum<Visibility>);
    public static readonly StyledProperty<Vector4> BackgroundProperty = StyledProperty<Vector4>.Register<UIElement>(
        "Background", Vector4.Zero, Invalidates.Visual, StyleValues.ReadColor, StyleValues.LerpColor);
    public static readonly StyledProperty<Vector4> CornerRadiiProperty = StyledProperty<Vector4>.Register<UIElement>(
        "CornerRadii", Vector4.Zero, Invalidates.Visual, StyleValues.ReadVector4, StyleValues.Lerp);
    public static readonly StyledProperty<float> BorderWidthProperty = StyledProperty<float>.Register<UIElement>(
        "BorderWidth", 0, Invalidates.Visual, StyleValues.ReadFloat, StyleValues.Lerp);
    public static readonly StyledProperty<Vector4> BorderColorProperty = StyledProperty<Vector4>.Register<UIElement>(
        "BorderColor", Vector4.Zero, Invalidates.Visual, StyleValues.ReadColor, StyleValues.LerpColor);
    public static readonly StyledProperty<float> OpacityProperty = StyledProperty<float>.Register<UIElement>(
        "Opacity", 1, Invalidates.Visual, StyleValues.ReadFloat, StyleValues.Lerp);
    public static readonly StyledProperty<Vector4> ForegroundProperty = StyledProperty<Vector4>.Register<UIElement>(
        "Foreground", new Vector4(0, 0, 0, 1), Invalidates.Visual, StyleValues.ReadColor, StyleValues.LerpColor);
    public static readonly StyledProperty<Assets.AssetId> FontProperty = StyledProperty<Assets.AssetId>.Register<UIElement>(
        "Font", default, Invalidates.Measure, StyleValues.ReadAssetId);
    public static readonly StyledProperty<float> FontSizeProperty = StyledProperty<float>.Register<UIElement>(
        "FontSize", 16, Invalidates.Measure, StyleValues.ReadFloat, StyleValues.Lerp);
    public static readonly StyledProperty<Vector2> RenderScaleProperty = StyledProperty<Vector2>.Register<UIElement>(
        "RenderScale", Vector2.One, Invalidates.Visual, StyleValues.ReadVector2, StyleValues.Lerp);
    public static readonly StyledProperty<float> RenderRotationProperty = StyledProperty<float>.Register<UIElement>(
        "RenderRotation", 0, Invalidates.Visual, StyleValues.ReadFloat, StyleValues.Lerp);
    public static readonly StyledProperty<Vector2> RenderOffsetProperty = StyledProperty<Vector2>.Register<UIElement>(
        "RenderOffset", Vector2.Zero, Invalidates.Visual, StyleValues.ReadVector2, StyleValues.Lerp);

    /// <summary>A fixed width in logical units; NaN (the default) sizes it to its content or slot.</summary>
    public float Width { get => GetValue(WidthProperty); set => SetValue(WidthProperty, value); }

    public float Height { get => GetValue(HeightProperty); set => SetValue(HeightProperty, value); }

    public float MinWidth { get => GetValue(MinWidthProperty); set => SetValue(MinWidthProperty, value); }

    public float MinHeight { get => GetValue(MinHeightProperty); set => SetValue(MinHeightProperty, value); }

    public float MaxWidth { get => GetValue(MaxWidthProperty); set => SetValue(MaxWidthProperty, value); }

    public float MaxHeight { get => GetValue(MaxHeightProperty); set => SetValue(MaxHeightProperty, value); }

    public Thickness Margin { get => GetValue(MarginProperty); set => SetValue(MarginProperty, value); }

    public Thickness Padding { get => GetValue(PaddingProperty); set => SetValue(PaddingProperty, value); }

    public HorizontalAlignment HorizontalAlignment
    {
        get => GetValue(HorizontalAlignmentProperty);
        set => SetValue(HorizontalAlignmentProperty, value);
    }

    public VerticalAlignment VerticalAlignment
    {
        get => GetValue(VerticalAlignmentProperty);
        set => SetValue(VerticalAlignmentProperty, value);
    }

    public Visibility Visibility { get => GetValue(VisibilityProperty); set => SetValue(VisibilityProperty, value); }

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
    public Vector4 Background { get => GetValue(BackgroundProperty); set => SetValue(BackgroundProperty, value); }

    /// <summary>The background's and border's corners: top left, top right, bottom right, bottom left.</summary>
    public Vector4 CornerRadii { get => GetValue(CornerRadiiProperty); set => SetValue(CornerRadiiProperty, value); }

    /// <summary>A border inside the bounds, over the background.</summary>
    public float BorderWidth { get => GetValue(BorderWidthProperty); set => SetValue(BorderWidthProperty, value); }

    public Vector4 BorderColor { get => GetValue(BorderColorProperty); set => SetValue(BorderColorProperty, value); }

    /// <summary>Fades the element and its children together (0 to 1).</summary>
    public float Opacity { get => GetValue(OpacityProperty); set => SetValue(OpacityProperty, value); }

    /// <summary>Text color, for elements that draw text (sRGB, straight alpha).</summary>
    public Vector4 Foreground { get => GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }

    /// <summary>The font asset, for elements that draw text.</summary>
    public Assets.AssetId Font { get => GetValue(FontProperty); set => SetValue(FontProperty, value); }

    /// <summary>The text size in logical units per em.</summary>
    public float FontSize { get => GetValue(FontSizeProperty); set => SetValue(FontSizeProperty, value); }

    /// <summary>The render transform's parts, for styles and animation: scale, rotation in degrees, offset.</summary>
    public Vector2 RenderScale { get => GetValue(RenderScaleProperty); set => SetValue(RenderScaleProperty, value); }

    public float RenderRotation { get => GetValue(RenderRotationProperty); set => SetValue(RenderRotationProperty, value); }

    public Vector2 RenderOffset { get => GetValue(RenderOffsetProperty); set => SetValue(RenderOffsetProperty, value); }

    /// <summary>The classes style selectors match (".primary").</summary>
    public Classes Classes => _classes ??= new Classes(this);

    /// <summary>States style selectors match (":hover"); input sets Hover, Pressed, and Focus.</summary>
    public UIState State
    {
        get => _state;
        set
        {
            if (_state != value)
            {
                _state = value;
                StyleChanged();
            }
        }
    }

    private Classes? _classes;

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
        if (Visibility == Visibility.Collapsed)
        {
            DesiredSize = Vector2.Zero;
            return;
        }
        Thickness margin = Margin, padding = Padding;
        Vector2 inner = Vector2.Max(available - margin.Total, Vector2.Zero);
        (Vector2 min, Vector2 max) = Limits();
        inner = Vector2.Clamp(inner, min, max);
        Vector2 content = MeasureCore(Vector2.Max(inner - padding.Total, Vector2.Zero)) + padding.Total;
        Vector2 size = Vector2.Clamp(content, min, max);
        DesiredSize = Vector2.Max(size + margin.Total, Vector2.Zero);
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
        if (Visibility == Visibility.Collapsed)
        {
            Bounds = new RectangleF(slot.X, slot.Y, 0, 0);
            return;
        }
        Thickness margin = Margin, padding = Padding;
        Vector2 space = Vector2.Max(new Vector2(slot.Width, slot.Height) - margin.Total, Vector2.Zero);
        Vector2 desired = Vector2.Max(DesiredSize - margin.Total, Vector2.Zero);
        (Vector2 min, Vector2 max) = Limits();
        Vector2 size = new(
            HorizontalAlignment == HorizontalAlignment.Stretch && float.IsNaN(Width) ? space.X : MathF.Min(desired.X, space.X),
            VerticalAlignment == VerticalAlignment.Stretch && float.IsNaN(Height) ? space.Y : MathF.Min(desired.Y, space.Y));
        size = Vector2.Clamp(size, min, max);
        float x = slot.X + margin.Left + HorizontalAlignment switch
        {
            HorizontalAlignment.Center => (space.X - size.X) * 0.5f,
            HorizontalAlignment.End => space.X - size.X,
            _ => 0,
        };
        float y = slot.Y + margin.Top + VerticalAlignment switch
        {
            VerticalAlignment.Center => (space.Y - size.Y) * 0.5f,
            VerticalAlignment.End => space.Y - size.Y,
            _ => 0,
        };
        Bounds = Snap(new RectangleF(x, y, size.X, size.Y));
        ArrangeCore(new RectangleF(x + padding.Left, y + padding.Top, MathF.Max(size.X - padding.Horizontal, 0),
            MathF.Max(size.Y - padding.Vertical, 0)));
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
        if (_children.Contains(child))
        {
            Root?.Detached(child);
            _children.Remove(child);
            child.Parent = null;
            child.Attach(null);
            InvalidateMeasure();
        }
    }

    protected void ClearChildren()
    {
        foreach (UIElement child in _children)
        {
            Root?.Detached(child);
            child.Parent = null;
            child.Attach(null);
        }
        _children.Clear();
        InvalidateMeasure();
    }

    internal void Attach(UIRoot? root)
    {
        if (root is null)
        {
            StopAllAnimations();
            _styled = false;
        }
        Root = root;
        root?.Restyle(this);
        foreach (UIElement child in _children)
        {
            child.Attach(root);
        }
    }

    // Drawing order: background, OnDraw, children; clipped and transformed.
    internal void Draw(Canvas canvas)
    {
        if (Visibility != Visibility.Visible)
        {
            return;
        }
        float opacity = Opacity;
        if (opacity <= 0)
        {
            return;
        }
        Matrix3x2 transform = Transform();
        bool transformed = !transform.IsIdentity;
        if (transformed)
        {
            canvas.PushTransform(AroundOrigin(transform));
        }
        if (opacity < 1)
        {
            canvas.PushOpacity(opacity);
        }
        if (_clipToBounds)
        {
            canvas.PushClip(new Vector2(Bounds.X, Bounds.Y), ActualSize);
        }
        canvas.PushTransform(Matrix3x2.CreateTranslation(Bounds.X, Bounds.Y));
        Vector4 radii = CornerRadii;
        if (Background.W > 0)
        {
            canvas.FillRect(Vector2.Zero, ActualSize, Background, radii);
        }
        if (BorderWidth > 0 && BorderColor.W > 0)
        {
            canvas.StrokeRect(Vector2.Zero, ActualSize, BorderColor, BorderWidth, radii);
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
        if (opacity < 1)
        {
            canvas.PopOpacity();
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
        if (Visibility != Visibility.Visible || Opacity <= 0)
        {
            return null;
        }
        Matrix3x2 transform = Transform();
        if (!transform.IsIdentity)
        {
            if (!Matrix3x2.Invert(AroundOrigin(transform), out Matrix3x2 inverse))
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

    // The render transform's parts, then RenderTransform.
    private Matrix3x2 Transform()
    {
        Vector2 scale = RenderScale, offset = RenderOffset;
        float rotation = RenderRotation;
        Matrix3x2 parts = scale == Vector2.One && rotation == 0 && offset == Vector2.Zero
            ? Matrix3x2.Identity
            : Matrix3x2.CreateScale(scale) * Matrix3x2.CreateRotation(rotation * MathF.PI / 180) *
              Matrix3x2.CreateTranslation(offset);
        return parts * _renderTransform;
    }

    private Matrix3x2 AroundOrigin(Matrix3x2 transform)
    {
        Vector2 origin = new(Bounds.X + Bounds.Width * _transformOrigin.X, Bounds.Y + Bounds.Height * _transformOrigin.Y);
        return Matrix3x2.CreateTranslation(-origin) * transform * Matrix3x2.CreateTranslation(origin);
    }

    private (Vector2 Min, Vector2 Max) Limits()
    {
        Vector2 max = new(float.IsNaN(Width) ? MaxWidth : Width, float.IsNaN(Height) ? MaxHeight : Height);
        Vector2 min = new(float.IsNaN(Width) ? MinWidth : Width, float.IsNaN(Height) ? MinHeight : Height);
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

    // -----------------------------------------------------------------------
    // Styled properties (ADR 0061)
    // -----------------------------------------------------------------------

    /// <summary>The property's value: an animation's, else one set in code, else the best-matching style's, else its default.</summary>
    public T GetValue<T>(StyledProperty<T> property)
    {
        if (property.Index >= _slots.Length || _slots[property.Index] is not ValueSlot<T> slot)
        {
            return property.Default;
        }
        return slot.Animation is not null ? slot.Animated : slot.HasLocal ? slot.Local : slot.HasStyle ? slot.Style : property.Default;
    }

    /// <summary>Sets the property in code: it wins over styles, and stops an animation of it.</summary>
    public void SetValue<T>(StyledProperty<T> property, T value)
    {
        T before = GetValue(property);
        ValueSlot<T> slot = Slot(property);
        slot.Local = value;
        slot.HasLocal = true;
        StopAnimation(slot);
        Changed(property, before, value);
    }

    /// <summary>Forgets the value set in code: the style's (or the default) shows again.</summary>
    public void ClearValue<T>(StyledProperty<T> property)
    {
        if (property.Index >= _slots.Length || _slots[property.Index] is not ValueSlot<T> slot || !slot.HasLocal)
        {
            return;
        }
        T before = GetValue(property);
        slot.HasLocal = false;
        slot.Local = default!;
        Changed(property, before, GetValue(property));
    }

    /// <summary>Whether code has set the property (rather than a style or its default).</summary>
    public bool IsSet(StyledProperty property) =>
        property.Index < _slots.Length && _slots[property.Index] is { HasLocal: true };

    /// <summary>
    /// Moves the property from its value now to <paramref name="to"/> over
    /// <paramref name="seconds"/> (real time), then sets it in code. Without
    /// a root, or with no time, it's set at once.
    /// </summary>
    public void Animate<T>(StyledProperty<T> property, T to, float seconds, Easing easing)
    {
        if (Root is not { } root || seconds <= 0)
        {
            SetValue(property, to);
            return;
        }
        T from = GetValue(property);
        ValueSlot<T> slot = Slot(property);
        StopAnimation(slot);
        Start(slot, new Animation<T>(property, from, to)
        {
            Start = root.Now,
            Duration = seconds,
            Easing = easing,
            IsTween = true,
        });
    }

    // A theme, a class, or a state changed: restyle on the root's next update.
    internal void StyleChanged() => Root?.Restyle(this);

    // The styles matching now, for each property the winner's value and the
    // winner's transition; a changed value animates if the new winner (for
    // its value or a transition) says so.
    internal void ApplyStyle(Theme? theme, double now)
    {
        List<(int Specificity, int Order, Style Style)> matched = [];
        if (theme is not null)
        {
            for (int i = 0; i < theme.Styles.Count; ++i)
            {
                Style style = theme.Styles[i];
                if (style.Selector.Matches(this))
                {
                    matched.Add((style.Selector.Specificity, i, style));
                }
            }
        }
        matched.Sort((a, b) => a.Specificity != b.Specificity ? a.Specificity.CompareTo(b.Specificity) : a.Order.CompareTo(b.Order));
        Dictionary<StyledProperty, object> values = [];
        Dictionary<StyledProperty, Transition> transitions = [];
        foreach ((_, _, Style style) in matched) // the last wins
        {
            foreach ((StyledProperty property, object value) in style.Values)
            {
                if (property.Owner.IsInstanceOfType(this))
                {
                    values[property] = value;
                }
            }
            foreach ((StyledProperty property, Transition transition) in style.Transitions)
            {
                transitions[property] = transition;
            }
        }
        if (!_styled)
        {
            transitions.Clear(); // a first look doesn't animate
            _styled = true;
        }
        for (int i = 0; i < _slots.Length; ++i)
        {
            if (_slots[i] is { HasStyle: true } slot && !values.Keys.Any(p => p.Index == i))
            {
                slot.Restyle(this, null, transitions, now);
            }
        }
        foreach ((StyledProperty property, object value) in values)
        {
            SlotOf(property).Restyle(this, value, transitions, now);
        }
    }

    // A slot's style layer changed from `before`: animate it if a
    // transition says so, else just report the change.
    internal void StyleValueChanged<T>(StyledProperty<T> property, ValueSlot<T> slot, T before,
        Dictionary<StyledProperty, Transition> transitions, double now)
    {
        T after = slot.HasLocal ? slot.Local : slot.HasStyle ? slot.Style : property.Default;
        if (EqualityComparer<T>.Default.Equals(before, after))
        {
            return;
        }
        if (transitions.TryGetValue(property, out Transition transition) && transition.Duration > 0 && Root is not null &&
            property.Interpolate is not null)
        {
            if (slot.Animation is { IsTween: true })
            {
                return; // a tween ends where code asked
            }
            Start(slot, new Animation<T>(property, before, after)
            {
                Start = now,
                Duration = transition.Duration,
                Easing = transition.Easing,
            });
            return;
        }
        if (slot.Animation is { IsTween: false })
        {
            StopAnimation(slot);
        }
        Changed(property, before, after);
    }

    // Advances the running animations to `now`; false once none are left.
    internal bool UpdateAnimations(double now)
    {
        for (int i = 0; i < _slots.Length && _animations > 0; ++i)
        {
            if (_slots[i] is not { Animation: { } animation } slot)
            {
                continue;
            }
            bool running = animation.Update(slot, now);
            if (!running)
            {
                slot.Animation = null;
                --_animations;
                if (animation.IsTween)
                {
                    slot.SetLocal(animation.Target);
                }
            }
            Affect(slot.Property!);
        }
        return _animations > 0;
    }

    internal void StopAllAnimations()
    {
        foreach (ValueSlot? slot in _slots)
        {
            if (slot?.Animation is not null)
            {
                slot.Animation = null;
            }
        }
        _animations = 0;
    }

    private void Start(ValueSlot slot, Animation animation)
    {
        if (slot.Animation is null)
        {
            ++_animations;
        }
        slot.Animation = animation;
        animation.Update(slot, animation.Start);
        Root?.Animating(this);
        Affect(slot.Property!);
    }

    private void StopAnimation(ValueSlot slot)
    {
        if (slot.Animation is not null)
        {
            slot.Animation = null;
            --_animations;
        }
    }

    private ValueSlot<T> Slot<T>(StyledProperty<T> property) => (ValueSlot<T>)SlotOf(property);

    private ValueSlot SlotOf(StyledProperty property)
    {
        if (property.Index >= _slots.Length)
        {
            Array.Resize(ref _slots, StyledProperty.Count);
        }
        return _slots[property.Index] ??= property.CreateSlot();
    }

    private void Changed<T>(StyledProperty<T> property, T before, T after)
    {
        if (!EqualityComparer<T>.Default.Equals(before, after))
        {
            Affect(property);
        }
    }

    private void Affect(StyledProperty property)
    {
        if ((property.Invalidates & Invalidates.Measure) != 0)
        {
            InvalidateMeasure();
        }
        if ((property.Invalidates & Invalidates.Visual) != 0)
        {
            InvalidateVisual();
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
