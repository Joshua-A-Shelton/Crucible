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
public sealed class UIRoot : IRenderStep
{
    private readonly Canvas _canvas = new();
    private UIElement? _content;
    private float _scale = 1;
    private Vector2 _size;
    private bool _layoutDirty = true;
    private bool _drawingDirty = true;

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
            _content?.Attach(null);
            _content = value;
            if (value is not null)
            {
                value.Attach(this);
                MarkAll(value);
            }
            InvalidateLayout();
        }
    }

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
                if (_content is not null)
                {
                    MarkAll(_content);
                }
                InvalidateLayout();
            }
        }
    }

    /// <summary>The surface's size in logical units, as of the last layout.</summary>
    public Vector2 Size => _size;

    /// <summary>
    /// Lays the tree out for a surface of <paramref name="physicalSize"/>
    /// pixels, if it or anything in the tree changed. The UI step does it
    /// every frame; call it before hit testing a tree changed since.
    /// </summary>
    public void UpdateLayout(Vector2 physicalSize)
    {
        Vector2 size = physicalSize / _scale;
        if (size != _size)
        {
            _size = size;
            _layoutDirty = true;
        }
        if (!_layoutDirty || _content is null)
        {
            return;
        }
        _layoutDirty = false;
        _content.Measure(size);
        _content.Arrange(new RectangleF(0, 0, size.X, size.Y));
        _drawingDirty = true;
    }

    /// <summary>The topmost element at a point in logical units, as last laid out; null if none.</summary>
    public UIElement? HitTest(Vector2 point) => _content?.HitTest(point);

    internal void InvalidateLayout()
    {
        _layoutDirty = true;
        _drawingDirty = true;
    }

    internal void InvalidateDrawing() => _drawingDirty = true;

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
        }
        ((IRenderStep)_canvas).AddPasses(context);
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
