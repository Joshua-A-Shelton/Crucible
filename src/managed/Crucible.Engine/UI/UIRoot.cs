using System.Drawing;
using System.Numerics;
using Crucible.Render;

namespace Crucible.UI;

/// <summary>
/// A surface's UI (ADR 0060): its top element, its DPI scale, and the
/// <see cref="Canvas"/> it draws into. It is also the render step that
/// draws it: add it to the schedule as "UI" after Tonemap. Each frame it
/// lays out what changed, to the surface's size in logical units, and
/// redraws the canvas only if something changed. Main thread only.
/// </summary>
public sealed partial class UIRoot : IRenderStep
{
    private readonly Canvas _canvas = new();
    private UIElement? _content;
    private float _scale = 1;
    private Vector2 _size;
    private bool _layoutDirty = true;
    private bool _drawingDirty = true;
    private Theme? _theme = Theme.CreateDefault();
    private int _themeVersion;
    private readonly HashSet<UIElement> _restyle = [];
    private readonly HashSet<UIElement> _animating = [];
    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();

    /// <summary>The top element, laid out to fill the surface.</summary>
    public UIElement? Content
    {
        get => _content;
        set
        {
            if (value == _content)
            {
                return;
            }
            if (value is { } element && (element.Parent is not null || element.Root is not null))
            {
                throw new InvalidOperationException("The element already belongs to a tree");
            }
            if (_content is not null)
            {
                Detached(_content);
                _content.Attach(null);
            }
            _content = value;
            if (value is not null)
            {
                value.Attach(this);
                MarkAll(value);
            }
            InvalidateLayout();
        }
    }

    /// <summary>The font text elements use when no style gives them one (ADR 0063).</summary>
    public Text.FontHandle DefaultFont
    {
        get => _defaultFont;
        set
        {
            _defaultFont = value;
            MarkEverything();
            InvalidateLayout();
        }
    }

    private Text.FontHandle _defaultFont;

    /// <summary>Physical pixels per logical unit: the DPI scale. Changing it lays everything out again.</summary>
    public float Scale
    {
        get => _scale;
        set
        {
            if (!(value > 0))
            {
                throw new ArgumentOutOfRangeException(nameof(value), "The scale is positive");
            }
            if (value != _scale)
            {
                _scale = value;
                _canvas.Scale = value;
                MarkEverything();
                InvalidateLayout();
            }
        }
    }

    /// <summary>The styles for this tree (ADR 0061). Changing it, or the theme itself, restyles the tree.</summary>
    public Theme? Theme
    {
        get => _theme;
        set
        {
            _theme = value;
            _themeVersion = value?.Version ?? 0;
            RestyleEverything();
        }
    }

    /// <summary>Real time in seconds, which transitions and tweens run on.</summary>
    public double Now => TestClock?.Invoke() ?? _clock.Elapsed.TotalSeconds;

    // Tests: a clock of their own.
    internal Func<double>? TestClock { get; set; }

    /// <summary>Whether a transition or tween is running.</summary>
    public bool IsAnimating => _animating.Count > 0;

    /// <summary>The surface's size in logical units, as of the last layout.</summary>
    public Vector2 Size => _size;

    /// <summary>
    /// Lays the tree out for a surface of <paramref name="physicalSize"/>
    /// pixels, if it or anything in the tree changed. The UI step does it
    /// every frame; call it before hit testing a tree changed since.
    /// </summary>
    public void UpdateLayout(Vector2 physicalSize)
    {
        UpdateStyles();
        Vector2 size = physicalSize / _scale;
        if (size != _size)
        {
            _size = size;
            _layoutDirty = true;
        }
        if (!_layoutDirty || (_content is null && _popups.Count == 0 && _dragPreview is null))
        {
            return;
        }
        // Up to three passes: arranging can ask for another (a virtualizing
        // list learning its viewport), settled within the frame.
        for (int pass = 0; pass < 3 && _layoutDirty; ++pass)
        {
            _layoutDirty = false;
            _content?.Measure(size);
            _content?.Arrange(new RectangleF(0, 0, size.X, size.Y));
            LayoutPopups();
            LayoutDragPreview();
        }
        _drawingDirty = true;
    }

    /// <summary>The topmost element at a point in logical units, as last laid out; null if none.</summary>
    public UIElement? HitTest(Vector2 point) => HitLayers(point);

    internal void InvalidateLayout()
    {
        _layoutDirty = true;
        _drawingDirty = true;
    }

    internal void InvalidateDrawing() => _drawingDirty = true;

    internal void Restyle(UIElement element) => _restyle.Add(element);

    // Styles an element and its children now, mid-layout.
    internal void StyleNow(UIElement element)
    {
        _restyle.Remove(element);
        element.ApplyStyle(_theme, Now);
        foreach (UIElement child in element.Children)
        {
            StyleNow(child);
        }
    }

    internal void Animating(UIElement element) => _animating.Add(element);

    /// <summary>
    /// Applies changed classes, states, and themes, then advances animations
    /// to now. <see cref="UpdateLayout"/> does it first.
    /// </summary>
    public void UpdateStyles()
    {
        double now = Now;
        if (_theme is not null && _theme.Version != _themeVersion)
        {
            _themeVersion = _theme.Version;
            RestyleEverything();
        }
        if (_restyle.Count > 0)
        {
            UIElement[] pending = [.. _restyle];
            _restyle.Clear();
            foreach (UIElement element in pending)
            {
                if (element.Root == this)
                {
                    element.ApplyStyle(_theme, now);
                }
            }
        }
        if (_animating.Count > 0)
        {
            _animating.RemoveWhere(element => element.Root != this || !element.UpdateAnimations(now));
        }
        _focused?.OnFocusedTick(now);
    }

    private void RestyleEverything()
    {
        if (_content is not null)
        {
            RestyleAll(_content);
        }
        foreach (Popup popup in _popups)
        {
            RestyleAll(popup);
        }
    }

    private void RestyleAll(UIElement element)
    {
        _restyle.Add(element);
        foreach (UIElement child in element.Children)
        {
            RestyleAll(child);
        }
    }

    void IRenderStep.Prepare() => ((IRenderStep)_canvas).Prepare();

    void IRenderStep.Release() => ((IRenderStep)_canvas).Release();

    void IRenderStep.AddPasses(RenderStepContext context)
    {
        UpdateLayout(new Vector2(context.Width, context.Height));
        if (_drawingDirty)
        {
            _drawingDirty = false;
            _canvas.Clear();
            _content?.Draw(_canvas);
            DrawPopups();
            DrawDragPreview();
        }
        ((IRenderStep)_canvas).AddPasses(context);
    }

    private void MarkEverything()
    {
        if (_content is not null)
        {
            MarkAll(_content);
        }
        foreach (Popup popup in _popups)
        {
            MarkAll(popup);
        }
    }

    private static void MarkAll(UIElement element)
    {
        element.MarkDirty();
        foreach (UIElement child in element.Children)
        {
            MarkAll(child);
        }
    }
}
