using Crucible.Assets;
using Crucible.Text;

namespace Crucible.UI;

// An element's text, shaped with its Font (a font asset, acquired through
// the asset reader and released when it changes or the element leaves the
// tree) or the root's default font, at its FontSize and the root's scale
// (ADR 0063). The layout is kept until the text, the style, or the wrap
// width changes. Label and the text fields share it (ADR 0065).
internal sealed class ShapedText
{
    private TextLayout? _layout;
    private (ulong Font, float Size, float Scale, GlyphRendering Rendering, float WrapWidth, string Text) _shaped;
    private AssetId _fontId;
    private FontHandle _acquired;

    /// <summary>The text as last shaped; null without a font.</summary>
    public TextLayout? Layout => _layout;

    /// <summary>Shapes the text for the element, or keeps the last layout if nothing changed; null without a font.</summary>
    public TextLayout? Shape(UIElement element, GlyphRendering rendering, string text, float wrapWidth)
    {
        FontHandle font = ResolveFont(element);
        if (font.Generation == 0)
        {
            Drop();
            return null;
        }
        var wanted = (((ulong)font.Index << 32) | font.Generation, element.FontSize, element.Root?.Scale ?? 1, rendering,
            wrapWidth, text);
        if (_layout is null || wanted != _shaped)
        {
            Drop();
            _layout = TextLayout.Shape(font, new TextStyle
            {
                Size = wanted.Item2,
                Scale = wanted.Item3,
                Rendering = wanted.Item4,
            }, text, wrapWidth);
            _shaped = wanted;
        }
        return _layout;
    }

    /// <summary>Drops the layout and releases the font: the element left the tree.</summary>
    public void Detach()
    {
        Drop();
        ReleaseFont();
    }

    public void Drop()
    {
        _layout?.Dispose();
        _layout = null;
    }

    // The style's font asset (acquired once, released when it changes), or the root's default.
    private FontHandle ResolveFont(UIElement element)
    {
        AssetId id = element.Font;
        if (id != _fontId)
        {
            ReleaseFont();
            if (!id.IsNone)
            {
                // A missing or broken font draws no text rather than failing
                // the frame's layout; it's reported once.
                try
                {
                    _acquired = Fonts.Acquire(id);
                }
                catch (CrucibleException exception)
                {
                    Console.Error.WriteLine($"Crucible.UI: font {id} can't be used: {exception.Message}");
                    _acquired = default;
                }
                _fontId = id;
            }
        }
        return !_fontId.IsNone ? _acquired : element.Root?.DefaultFont ?? default;
    }

    private void ReleaseFont()
    {
        if (!_fontId.IsNone)
        {
            if (_acquired.Generation != 0)
            {
                Fonts.Release(_acquired);
            }
            _acquired = default;
            _fontId = default;
        }
    }
}
