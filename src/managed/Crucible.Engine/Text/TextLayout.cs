using System.Numerics;
using System.Text;

namespace Crucible.Text;

/// <summary>
/// Text shaped and broken into lines natively (ADR 0057), kept there for
/// measuring, hit testing, and drawing: shape once, and again only when the
/// text, width, or style changes. Units are logical pixels from the top
/// left; positions in the text are indices into <see cref="Text"/>.
/// Dispose it when done: it holds native memory. Main thread only.
/// </summary>
public sealed class TextLayout : IDisposable
{
    private readonly byte[] _utf8;
    private TextLayoutHandle _handle;

    private TextLayout(TextLayoutHandle handle, string text, TextStyle style, float wrapWidth)
    {
        _handle = handle;
        _utf8 = Encoding.UTF8.GetBytes(text);
        Text = text;
        Style = style;
        WrapWidth = wrapWidth;
    }

    /// <summary>
    /// Shapes <paramref name="text"/> and breaks it into lines no wider than
    /// <paramref name="wrapWidth"/> (0: only at line breaks). Throws a
    /// <see cref="CrucibleException"/> with NotFound for a stale font, or
    /// InvalidData for a size that isn't positive.
    /// </summary>
    public static TextLayout Shape(FontHandle font, in TextStyle style, string text, float wrapWidth = 0) =>
        new(Api.ShapeText(font, style, text, wrapWidth), text, style, wrapWidth);

    public string Text { get; }

    public TextStyle Style { get; }

    public float WrapWidth { get; }

    /// <summary>What drawing takes.</summary>
    public TextLayoutHandle Handle => Live;

    /// <summary>The widest line (without trailing spaces), and all the lines' height.</summary>
    public Vector2 Size
    {
        get
        {
            TextMetrics metrics = Api.TextMetrics(Live);
            return new Vector2(metrics.Width, metrics.Height);
        }
    }

    /// <summary>At least 1, even for empty text.</summary>
    public int LineCount => (int)Api.TextMetrics(Live).LineCount;

    /// <summary>A line, its <see cref="TextLine.Start"/> and <see cref="TextLine.End"/> as indices into <see cref="Text"/>.</summary>
    public TextLine Line(int line)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(line);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(line, LineCount);
        TextLine result = Api.TextLine(Live, (uint)line);
        result.Start = (uint)IndexOf(result.Start);
        result.End = (uint)IndexOf(result.End);
        return result;
    }

    /// <summary>The caret position nearest a point: an index into <see cref="Text"/>.</summary>
    public int HitTest(Vector2 point) => IndexOf(Api.HitTestText(Live, point.X, point.Y));

    /// <summary>Where a caret before <paramref name="index"/> sits; inside a ligature, at its start.</summary>
    public TextCaret Caret(int index) => Api.TextCaret(Live, OffsetOf(index));

    public void Dispose()
    {
        if (_handle.Generation != 0)
        {
            Api.DestroyTextLayout(_handle);
            _handle = default;
        }
    }

    private TextLayoutHandle Live =>
        _handle.Generation == 0 ? throw new ObjectDisposedException(nameof(TextLayout)) : _handle;

    // Native positions are UTF-8 byte offsets.
    private int IndexOf(uint offset) => Encoding.UTF8.GetCharCount(_utf8.AsSpan(0, (int)Math.Min(offset, (uint)_utf8.Length)));

    private uint OffsetOf(int index) => (uint)Encoding.UTF8.GetByteCount(Text.AsSpan(0, Math.Clamp(index, 0, Text.Length)));
}
