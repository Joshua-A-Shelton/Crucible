using System.Numerics;
using Crucible.Text;

namespace Crucible.UI;

/// <summary>
/// Text (ADR 0063), shaped with the element's <c>Font</c> (a font asset,
/// acquired through the asset reader) or the root's default font, at its
/// <c>FontSize</c>, drawn in its <c>Foreground</c>. The layout is kept until
/// the text, the style, or the space for wrapping changes.
/// </summary>
public class Label : UIElement
{
    /// <summary>Exact glyphs (sharp, the default) or distance fields (for text that's scaled, turned, or animated).</summary>
    public static readonly StyledProperty<GlyphRendering> TextRenderingProperty =
        StyledProperty<GlyphRendering>.Register<Label>("TextRendering", GlyphRendering.Exact, Invalidates.Measure,
            StyleValues.ReadEnum<GlyphRendering>);

    private string _text = "";
    private bool _wrap;
    private readonly ShapedText _shaped = new();

    public Label()
    {
    }

    public Label(string text) => _text = text;

    public string Text
    {
        get => _text;
        set
        {
            if (_text != value)
            {
                _text = value ?? "";
                InvalidateMeasure();
            }
        }
    }

    /// <summary>Wraps lines to the width it's given (breaking as ADR 0057 does).</summary>
    public bool Wrap
    {
        get => _wrap;
        set
        {
            _wrap = value;
            InvalidateMeasure();
        }
    }

    public GlyphRendering TextRendering
    {
        get => GetValue(TextRenderingProperty);
        set => SetValue(TextRenderingProperty, value);
    }

    /// <summary>The shaped text, as last measured; null without a font.</summary>
    public TextLayout? Layout => _shaped.Layout;

    protected override Vector2 MeasureCore(Vector2 available)
    {
        float wrapWidth = _wrap && float.IsFinite(available.X) ? MathF.Max(available.X, 1) : 0;
        return _shaped.Shape(this, TextRendering, _text, wrapWidth)?.Size ?? Vector2.Zero;
    }

    protected override void OnDraw(Canvas canvas)
    {
        if (_shaped.Layout is { } layout)
        {
            canvas.DrawText(layout, Vector2.Zero, Foreground);
        }
    }

    protected override void OnDetached(UIRoot root) => _shaped.Detach();
}
