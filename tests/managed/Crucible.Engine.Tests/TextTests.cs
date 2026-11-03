using System.Numerics;
using Crucible.Binary;
using Crucible.Text;

namespace Crucible.Tests;

// Fonts and text layouts (ADR 0057) from C#, under JIT and NativeAOT; the
// native tests cover shaping, breaking, and the glyph cache in depth. Text
// is main-thread-only, so these never run at once.
[NotInParallel("text")]
public class TextTests
{
    private static FontHandle LoadNotoSans()
    {
        byte[] font = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fonts", "NotoSans-Regular.ttf"));
        string path = Path.Combine(Path.GetTempPath(), "crucible-font-" + Guid.NewGuid().ToString("N") + ".crfont");
        try
        {
            using ImportedFileWriter writer = new(Tag.Of("FONT"), 1);
            writer.Add(Tag.Of("DATA"), 1, font).Write(path, Compression.None);
            return Fonts.Load(File.ReadAllBytes(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task LayoutsMeasureBreakAndHitTestInStringIndices()
    {
        FontHandle font = LoadNotoSans();
        try
        {
            // é is two bytes in UTF-8: lines and carets must come back as string indices.
            const string text = "café crème brûlée";
            float wrap;
            using (TextLayout first = TextLayout.Shape(font, new TextStyle { Size = 16 }, "café crème"))
            {
                wrap = first.Size.X + 1;
            }
            using TextLayout layout = TextLayout.Shape(font, new TextStyle { Size = 16, Scale = 1.5f }, text, wrap);
            await Assert.That(layout.LineCount).IsEqualTo(2);
            await Assert.That(layout.Line(1).Start).IsEqualTo((uint)text.IndexOf("brûlée", StringComparison.Ordinal));
            await Assert.That(layout.Line(1).End).IsEqualTo((uint)text.Length);
            await Assert.That(layout.Size.X).IsLessThanOrEqualTo(wrap);
            await Assert.That(layout.Size.Y).IsEqualTo(2 * layout.Line(0).Height);

            TextCaret caret = layout.Caret(text.IndexOf("crème", StringComparison.Ordinal));
            await Assert.That(caret.Line).IsEqualTo(0u);
            await Assert.That(layout.HitTest(new Vector2(caret.X + 0.1f, caret.Top + caret.Height / 2)))
                .IsEqualTo(text.IndexOf("crème", StringComparison.Ordinal));
            await Assert.That(layout.HitTest(new Vector2(1000, 1000))).IsEqualTo(text.Length);
            await Assert.That(() => layout.Line(2)).Throws<ArgumentOutOfRangeException>();
        }
        finally
        {
            Fonts.Unload(font);
        }
    }

    [Test]
    public async Task StaleFontsAndDisposedLayoutsAreRefused()
    {
        FontHandle font = LoadNotoSans();
        TextLayout layout = TextLayout.Shape(font, new TextStyle { Size = 12, Rendering = GlyphRendering.DistanceField }, "hi");
        Fonts.Unload(font);
        // The layout keeps its font.
        await Assert.That(layout.Size.X).IsGreaterThan(0);
        layout.Dispose();
        await Assert.That(() => layout.Size).Throws<ObjectDisposedException>();

        CrucibleException stale = Assert.Throws<CrucibleException>(() => TextLayout.Shape(font, new TextStyle { Size = 12 }, "x"));
        await Assert.That(stale.Status).IsEqualTo(Status.NotFound);
        CrucibleException junk = Assert.Throws<CrucibleException>(() => Fonts.Load([1, 2, 3]));
        await Assert.That(junk.Status).IsEqualTo(Status.InvalidData);
    }
}
