using System.Drawing;
using System.Numerics;
using Crucible.Text;

namespace Crucible.UI;

/// <summary>How a label's text ends when it's given less room than it needs (ADR 0130).</summary>
public enum TextTrimming
{
    /// <summary>Drawn whole, past its edge.</summary>
    None,

    /// <summary>Cut at the end, "…" after what fits: "Tilemaps/Tilemap 4.crti…".</summary>
    End,

    /// <summary>
    /// For paths: the last <c>/</c> segment kept whole, cut before it,
    /// "Tilemaps/…/Tilemap 4.crtilemap"; cut at the end if even that
    /// doesn't fit.
    /// </summary>
    Path,
}

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

    /// <summary>How its text ends when it's given less room than it needs (ADR 0130); styled, None by default.</summary>
    public static readonly StyledProperty<TextTrimming> TrimmingProperty =
        StyledProperty<TextTrimming>.Register<Label>("Trimming", TextTrimming.None, Invalidates.Measure,
            StyleValues.ReadEnum<TextTrimming>);

    private string _text = "";
    private bool _wrap;
    private readonly ShapedText _shaped = new();
    private readonly TrimmedText _trimmed = new(); // the cut text, while it's trimmed

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

    public TextTrimming Trimming
    {
        get => GetValue(TrimmingProperty);
        set => SetValue(TrimmingProperty, value);
    }

    /// <summary>Whether its text was cut to fit when it was last arranged; then, unless it has its own, its tooltip is the whole text.</summary>
    public bool IsTrimmed { get; private set; }

    /// <summary>The shaped text, as last measured; null without a font.</summary>
    public TextLayout? Layout => _shaped.Layout;

    /// <summary>The text drawn, as last arranged: the cut text while it's trimmed.</summary>
    public string ShownText => IsTrimmed && _trimmed.Layout is { } cut ? cut.Text : _text;

    internal override string ToolTipText => ToolTip.Length > 0 ? ToolTip : IsTrimmed ? _text : "";

    protected override Vector2 MeasureCore(Vector2 available)
    {
        float wrapWidth = _wrap && float.IsFinite(available.X) ? MathF.Max(available.X, 1) : 0;
        return _shaped.Shape(this, TextRendering, _text, wrapWidth)?.Size ?? Vector2.Zero;
    }

    protected override void ArrangeCore(RectangleF content)
    {
        TextLayout? layout = _shaped.Layout;
        IsTrimmed = layout is not null && !_wrap &&
                    _trimmed.Fit(this, TextRendering, layout, content.Width, Trimming);
    }

    protected override void OnDraw(Canvas canvas)
    {
        TextLayout? layout = IsTrimmed && _trimmed.Layout is { } cut ? cut : _shaped.Layout;
        if (layout is not null)
        {
            canvas.DrawText(layout, Vector2.Zero, Foreground);
        }
    }

    // Detaching let go of the shaped text; attached again (a list's row
    // reused for the same text), it's shaped anew.
    protected override void OnAttached(UIRoot root) => InvalidateMeasure();

    protected override void OnDetached(UIRoot root)
    {
        _shaped.Detach();
        _trimmed.Detach();
        IsTrimmed = false;
    }
}

// A text's cut form for the room it's given (ADR 0130), shared by Label
// and the text fields: cut again only when the room or the text changes.
internal sealed class TrimmedText
{
    private const string Ellipsis = "\u2026";

    private readonly ShapedText _shaped = new();
    private TextLayout? _cutFrom;
    private float _width = float.NaN;
    private TextTrimming _trimming;

    /// <summary>The cut text, while <see cref="Fit"/> last said it's cut.</summary>
    public TextLayout? Layout { get; private set; }

    /// <summary>Whether <paramref name="layout"/> (one line) needs cutting to fit <paramref name="width"/>; if so, <see cref="Layout"/> is it cut.</summary>
    public bool Fit(UIElement element, GlyphRendering rendering, TextLayout layout, float width, TextTrimming trimming)
    {
        if (trimming == TextTrimming.None || layout.LineCount > 1 || layout.Size.X <= width + 0.5f)
        {
            Clear();
            return false;
        }
        if (Layout is not null && _cutFrom == layout && _width == width && _trimming == trimming)
        {
            return true;
        }
        _cutFrom = layout;
        _width = width;
        _trimming = trimming;
        TextLayout? ellipsis = _shaped.Shape(element, rendering, Ellipsis, 0);
        float dots = ellipsis?.Size.X ?? 0;
        string cut = Cut(layout, width, dots, trimming);
        // Shaping can differ a little from carets (kerning across the cut):
        // a few characters fewer until it fits.
        TextLayout? shaped = null;
        for (int tries = 0; tries < 4; ++tries)
        {
            shaped = _shaped.Shape(element, rendering, cut, 0);
            if (shaped is null || shaped.Size.X <= width + 0.5f || cut.Length <= 1)
            {
                break;
            }
            cut = Shorter(cut);
        }
        Layout = shaped;
        return shaped is not null;
    }

    public void Clear()
    {
        Layout = null;
        _cutFrom = null;
        _width = float.NaN;
    }

    public void Detach()
    {
        Clear();
        _shaped.Detach();
    }

    // The text cut to `width`, the ellipsis `dots` wide.
    private static string Cut(TextLayout layout, float width, float dots, TextTrimming trimming)
    {
        string text = layout.Text;
        float total = layout.Size.X;
        if (trimming == TextTrimming.Path)
        {
            int slash = text.LastIndexOfAny(['/', '\\']);
            if (slash > 0)
            {
                float tail = total - layout.Caret(slash).X;
                if (tail + dots <= width)
                {
                    int keep = FitIndex(layout, width - dots - tail, slash);
                    return text[..keep].TrimEnd() + Ellipsis + text[slash..];
                }
            }
        }
        return text[..FitIndex(layout, width - dots, text.Length)].TrimEnd() + Ellipsis;
    }

    // How much of the text, up to `limit`, fits in `room`: a caret index.
    private static int FitIndex(TextLayout layout, float room, int limit)
    {
        if (room <= 0)
        {
            return 0;
        }
        int index = Math.Min(layout.HitTest(new Vector2(room, 0)), limit);
        while (index > 0 && layout.Caret(index).X > room)
        {
            --index;
        }
        return index;
    }

    // One character fewer before the ellipsis.
    private static string Shorter(string cut)
    {
        int dots = cut.IndexOf(Ellipsis, StringComparison.Ordinal);
        if (dots <= 0)
        {
            return cut.Length > 1 ? cut[..^1] : cut;
        }
        int remove = dots >= 2 && char.IsLowSurrogate(cut[dots - 1]) ? 2 : 1;
        return cut[..(dots - remove)].TrimEnd() + cut[dots..];
    }
}
