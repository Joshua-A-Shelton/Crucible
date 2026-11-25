using System.Numerics;
using Crucible.UI;

namespace Crucible.Tilemaps.Editor;

internal enum TileGlyph
{
    Stamp,
    Rectangle,
    Fill,
    Eraser,
    Select,
}

/// <summary>A map tool's symbol (ADR 0108, 0112), drawn in its <c>Foreground</c>.</summary>
internal sealed class TileGlyphView : UIElement
{
    private readonly TileGlyph _glyph;

    public TileGlyphView(TileGlyph glyph)
    {
        _glyph = glyph;
        Width = 14;
        Height = 14;
        VerticalAlignment = VerticalAlignment.Center;
    }

    protected override void OnDraw(Canvas canvas)
    {
        Vector4 color = Foreground;
        switch (_glyph)
        {
            case TileGlyph.Stamp: // a stamp: handle, block, and its mark
                canvas.FillRect(new Vector2(5, 1), new Vector2(4, 5), color, 1.5f);
                canvas.FillRect(new Vector2(2, 6), new Vector2(10, 4), color, 1);
                canvas.DrawLine(new Vector2(2, 13), new Vector2(12, 13), 1.5f, color);
                break;
            case TileGlyph.Rectangle:
                canvas.StrokeRect(new Vector2(1.5f, 3.5f), new Vector2(11, 8), color, 1.5f);
                break;
            case TileGlyph.Eraser: // an eraser on its side
                canvas.DrawLine(new Vector2(3, 11), new Vector2(10, 4), 5, color);
                canvas.DrawLine(new Vector2(1, 13), new Vector2(13, 13), 1, color);
                break;
            case TileGlyph.Fill: // a bucket and a drop
                canvas.StrokeRect(new Vector2(2, 5), new Vector2(7, 7), color, 1.5f);
                canvas.FillRect(new Vector2(11, 8), new Vector2(3, 4), color, 1.5f);
                break;
            case TileGlyph.Select: // a dashed rectangle
                for (int i = 0; i < 4; ++i)
                {
                    canvas.DrawLine(new Vector2(1.5f + i * 3, 2.5f), new Vector2(3 + i * 3, 2.5f), 1.5f, color);
                    canvas.DrawLine(new Vector2(1.5f + i * 3, 11.5f), new Vector2(3 + i * 3, 11.5f), 1.5f, color);
                }
                canvas.DrawLine(new Vector2(1.5f, 2.5f), new Vector2(1.5f, 11.5f), 1.5f, color);
                canvas.DrawLine(new Vector2(12.5f, 2.5f), new Vector2(12.5f, 11.5f), 1.5f, color);
                break;
        }
    }
}

internal static class TileGlyphs
{
    // A map tool's button (ADR 0108, 0112): its glyph and label.
    internal static Button GlyphButton(TileGlyph glyph, string label)
    {
        StackPanel content = new() { Orientation = Orientation.Horizontal, Spacing = 5, HorizontalAlignment = HorizontalAlignment.Center };
        content.Add(new TileGlyphView(glyph));
        content.Add(new Label(label) { VerticalAlignment = VerticalAlignment.Center });
        Button button = new() { Content = content, VerticalAlignment = VerticalAlignment.Center };
        button.Classes.Add("tool");
        return button;
    }

}
