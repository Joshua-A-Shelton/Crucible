using System.Numerics;
using Crucible.Text;

namespace Crucible.UI;

/// <summary>
/// An icon (ADR 0124): one character of an icon font loaded in code
/// (<see cref="IconFont"/>), drawn in the element's <c>Foreground</c> at its
/// <c>FontSize</c>, so it follows the theme as text does. Its size is the
/// font size, square, whatever the glyph's own bounds.
/// </summary>
public class Icon : UIElement
{
    private readonly ShapedText _shaped = new();
    private string _glyph = "";

    public Icon()
    {
    }

    public Icon(FontHandle font, string glyph)
    {
        _shaped.Loaded = font;
        _glyph = glyph ?? "";
    }

    /// <summary>The icon font, loaded in code (<see cref="Fonts.Load"/>); the element's Font isn't used.</summary>
    public FontHandle IconFont
    {
        get => _shaped.Loaded;
        set
        {
            _shaped.Loaded = value;
            InvalidateMeasure();
        }
    }

    /// <summary>The icon's character in the font (Lucide's are in the private use area).</summary>
    public string Glyph
    {
        get => _glyph;
        set
        {
            if (_glyph != value)
            {
                _glyph = value ?? "";
                InvalidateMeasure();
            }
        }
    }

    protected override Vector2 MeasureCore(Vector2 available)
    {
        _shaped.Shape(this, GlyphRendering.Exact, _glyph, 0);
        return new Vector2(FontSize);
    }

    // The glyph's ink centered in its square (ADR 0133): centering its line
    // instead leaves a glyph that sits on the baseline low, by however much
    // the font's line reaches below and above it.
    protected override void OnDraw(Canvas canvas)
    {
        if (_shaped.Layout is { } layout)
        {
            Vector2 size = new(FontSize);
            Text.TextInk ink = Text.Api.TextInk(layout.Handle);
            Vector2 at = ink.Right > ink.Left
                ? (size - new Vector2(ink.Right - ink.Left, ink.Bottom - ink.Top)) * 0.5f - new Vector2(ink.Left, ink.Top)
                : (size - layout.Size) * 0.5f;
            canvas.DrawText(layout, at, Foreground);
        }
    }

    protected override void OnAttached(UIRoot root) => InvalidateMeasure();

    protected override void OnDetached(UIRoot root) => _shaped.Detach();
}
