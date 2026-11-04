using System.Numerics;
using Crucible.Assets;
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
    private TextLayout? _layout;
    private (ulong Font, float Size, float Scale, GlyphRendering Rendering, float WrapWidth, string Text) _shaped;
    private AssetId _fontId;
    private FontHandle _acquired;

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
    public TextLayout? Layout => _layout;

    protected override Vector2 MeasureCore(Vector2 available)
    {
        FontHandle font = ResolveFont();
        if (font.Generation == 0)
        {
            DropLayout();
            return Vector2.Zero;
        }
        float wrapWidth = _wrap && float.IsFinite(available.X) ? MathF.Max(available.X, 1) : 0;
        var wanted = (((ulong)font.Index << 32) | font.Generation, FontSize, Root?.Scale ?? 1, TextRendering, wrapWidth, _text);
        if (_layout is null || wanted != _shaped)
        {
            DropLayout();
            _layout = TextLayout.Shape(font, new TextStyle
            {
                Size = wanted.Item2,
                Scale = wanted.Item3,
                Rendering = wanted.Item4,
            }, _text, wrapWidth);
            _shaped = wanted;
        }
        return _layout.Size;
    }

    protected override void OnDraw(Canvas canvas)
    {
        if (_layout is not null)
        {
            canvas.DrawText(_layout, Vector2.Zero, Foreground);
        }
    }

    protected override void OnDetached(UIRoot root)
    {
        DropLayout();
        ReleaseFont();
    }

    // The style's font asset (acquired once, released when it changes), or the root's default.
    private FontHandle ResolveFont()
    {
        AssetId id = Font;
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
        return !_fontId.IsNone ? _acquired : Root?.DefaultFont ?? default;
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

    private void DropLayout()
    {
        _layout?.Dispose();
        _layout = null;
    }
}
