using System.Numerics;
using System.Runtime.InteropServices;
using Crucible.Assets;
using Crucible.Binary;
using Crucible.Loop;
using Crucible.Render;
using Crucible.Text;
using Crucible.UI;
using Png = Crucible.Samples.Diorama.Png;

namespace Crucible.RenderStepGame;

// Run headless by CTest (launcher.jit.canvas) with --game-arg test=canvas
// --game-arg font=<NotoSans-Regular.ttf> [--game-arg capture=<file.png>]:
// one Canvas, drawn as the UI step, holding every primitive on a white
// background; after enough frames for distance-field glyphs to arrive, the
// frame is read back and checked at chosen pixels.
[System(Phases.RenderPrepare)]
public sealed class CanvasChecks : GameSystem
{
    private const int Width = 1280, Height = 720; // the launcher's headless surface

    private readonly Canvas _canvas = new();
    private FontHandle _font;
    private TextLayout? _exact;
    private TextLayout? _field;
    private TextureHandle _texture;
    private int _frame;
    private int _checks;
    private int _failures;

    public override void Start(FrameContext context)
    {
        if (!context.GameArguments.TryGetValue("test", out string? test) || test != "canvas")
        {
            Enabled = false;
            return;
        }
        _font = Fonts.Load(ImportedFile("FONT", ("DATA", File.ReadAllBytes(context.GameArguments["font"]))));
        _exact = TextLayout.Shape(_font, new TextStyle { Size = 32, Rendering = GlyphRendering.Exact }, "Crucible");
        _field = TextLayout.Shape(_font, new TextStyle { Size = 48, Rendering = GlyphRendering.DistanceField }, "Field");
        // 2x2 sRGB: red, green / blue, white.
        _texture = Render.Api.LoadTexture(TextureFile(2, 2, [255, 0, 0, 255, 0, 255, 0, 255, 0, 0, 255, 255, 255, 255, 255, 255]));
        Draw();
        RenderSchedule.Default.InsertAfter("Tonemap", "UI", _canvas);
    }

    private void Draw()
    {
        Vector4 white = Vector4.One, black = new(0, 0, 0, 1);
        Vector4 red = new(1, 0, 0, 1), green = new(0, 1, 0, 1), blue = new(0, 0, 1, 1);
        Canvas c = _canvas;
        c.FillRect(Vector2.Zero, new Vector2(Width, Height), white);
        c.FillRect(new Vector2(40, 40), new Vector2(200, 100), red);
        c.FillRect(new Vector2(300.5f, 40), new Vector2(200, 100), blue, 30); // half a pixel off, rounded
        c.StrokeRect(new Vector2(560, 40), new Vector2(200, 100), green, 8, new Vector4(12));
        c.FillRect(new Vector2(40, 180), new Vector2(256, 40),
            Gradient.Linear(Vector2.Zero, new Vector2(256, 0), black, white));
        c.FillRect(new Vector2(800, 40), new Vector2(160, 100),
            Gradient.Radial(new Vector2(80, 50), 80, red, blue), new Vector4(8));
        c.DrawLine(new Vector2(340, 200), new Vector2(540, 200), 10, black);
        c.DrawPolyline([new Vector2(1000, 60), new Vector2(1060, 120), new Vector2(1120, 60)], 4, blue);
        c.DrawShadow(new Vector2(600, 180), new Vector2(160, 60), 8, 20, black);
        c.PushClip(new Vector2(800, 180), new Vector2(100, 60));
        c.FillRect(new Vector2(760, 160), new Vector2(200, 100), red);
        c.PopClip();
        c.PushTransform(Matrix3x2.CreateRotation(MathF.PI / 4, new Vector2(1060, 210)));
        c.FillRect(new Vector2(1020, 170), new Vector2(80, 80), blue);
        c.PopTransform();
        c.DrawImage(_texture, new Vector2(40, 260), new Vector2(100, 100));
        c.DrawNineSlice(_texture, new Vector2(180, 260), new Vector2(200, 100), new Vector4(20),
            new Vector4(0.5f), white);
        c.DrawText(_exact!, new Vector2(420.3f, 270), black);
        c.PushTransform(Matrix3x2.CreateScale(1.5f, new Vector2(700, 260)));
        c.DrawText(_field!, new Vector2(700, 260), black);
        c.PopTransform();
    }

    public override void Run(FrameContext context)
    {
        if (++_frame < 30)
        {
            return;
        }
        byte[] bgra = new byte[Width * Height * 4];
        Render.Api.ReadSurfacePixels(context.Surface, bgra);
        (int R, int G, int B) Pixel(int x, int y)
        {
            int i = (y * Width + x) * 4;
            return (bgra[i + 2], bgra[i + 1], bgra[i]);
        }
        bool Near((int R, int G, int B) p, (int R, int G, int B) q, int tolerance = 3) =>
            Math.Abs(p.R - q.R) <= tolerance && Math.Abs(p.G - q.G) <= tolerance && Math.Abs(p.B - q.B) <= tolerance;
        void At(int x, int y, (int, int, int) expected, string what, int tolerance = 3) =>
            Check(Near(Pixel(x, y), expected, tolerance), what, $"({x}, {y}) is {Pixel(x, y)}");

        At(140, 90, (255, 0, 0), "a filled rect");
        At(39, 90, (255, 255, 255), "nothing outside it");
        // Half covered: linear 0.5 between white and blue, encoded to sRGB.
        At(300, 90, (188, 188, 255), "an edge half a pixel in is antialiased in linear light");
        At(302, 41, (255, 255, 255), "a rounded corner leaves its corner empty");
        At(400, 90, (0, 0, 255), "inside the rounded rect");
        At(564, 90, (0, 255, 0), "a stroke's border");
        At(660, 90, (255, 255, 255), "a stroke's empty middle");
        At(168, 200, (188, 188, 188), "a gradient interpolates in linear light", 4);
        // 1.5 pixels in: linear 0.006, which sRGB encodes as 19.
        At(41, 200, (19, 19, 19), "the gradient's start");
        At(880, 90, (255, 0, 0), "a radial gradient's center", 30); // half a pixel out, sRGB lifts the blue
        At(440, 200, (0, 0, 0), "a line");
        At(440, 212, (255, 255, 255), "beside the line");
        At(1060, 120, (0, 0, 255), "a polyline's joint", 30);
        At(680, 210, (0, 0, 0), "a shadow's middle", 6);
        At(680, 290, (255, 255, 255), "well outside the shadow", 3);
        Check(Pixel(680, 245).R is > 20 and < 235, "the shadow fades past its edge", Pixel(680, 245).ToString());
        At(850, 210, (255, 0, 0), "inside the clip");
        At(790, 210, (255, 255, 255), "clipped away");
        At(1060, 210, (0, 0, 255), "a rotated square's middle");
        At(1024, 174, (255, 255, 255), "its unrotated corner, now empty");
        At(64, 284, (255, 0, 0), "an image's top left texel"); // toward the clamped edge: no neighbor mixes in
        At(115, 335, (255, 255, 255), "its bottom right texel");
        At(183, 263, (255, 0, 0), "a nine-slice's top left corner", 6);
        At(377, 357, (255, 255, 255), "its bottom right corner", 6);
        CheckText(Pixel, 420, 270, 200, 50, "exact text");
        CheckText(Pixel, 700, 260, 220, 90, "distance-field text");

        if (context.GameArguments.TryGetValue("capture", out string? capture))
        {
            byte[] rgb = new byte[Width * Height * 3];
            for (int i = 0; i < Width * Height; ++i)
            {
                rgb[i * 3] = bgra[i * 4 + 2];
                rgb[i * 3 + 1] = bgra[i * 4 + 1];
                rgb[i * 3 + 2] = bgra[i * 4];
            }
            Png.Write(capture, Width, Height, rgb);
        }
        RenderSchedule.Default.Remove("UI");
        _exact?.Dispose();
        _field?.Dispose();
        Fonts.Unload(_font);
        Render.Api.UnloadTexture(_texture);
        Console.WriteLine(_failures == 0
            ? $"CanvasChecks: all {_checks} checks passed"
            : $"CanvasChecks: {_failures} of {_checks} checks failed");
        context.QuitRequested = true;
        Enabled = false;
    }

    // Glyphs drawn: some pixels nearly black, some in between (antialiased).
    private void CheckText(Func<int, int, (int R, int G, int B)> pixel, int x0, int y0, int width, int height, string what)
    {
        int dark = 0, gray = 0;
        for (int y = y0; y < y0 + height; ++y)
        {
            for (int x = x0; x < x0 + width; ++x)
            {
                int r = pixel(x, y).R;
                dark += r < 40 ? 1 : 0;
                gray += r is >= 40 and < 215 ? 1 : 0;
            }
        }
        Check(dark > 200 && gray > 50, $"{what} is drawn", $"{dark} dark and {gray} gray pixels");
    }

    // An imported file of one kind, written and read back.
    internal static byte[] ImportedFile(string kind, params (string Tag, byte[] Data)[] sections)
    {
        string path = Path.Combine(Path.GetTempPath(), "crucible-canvas-" + Guid.NewGuid().ToString("N"));
        try
        {
            using ImportedFileWriter writer = new(Tag.Of(kind), 1);
            foreach ((string tag, byte[] data) in sections)
            {
                writer.Add(Tag.Of(tag), 1, data);
            }
            writer.Write(path, Compression.None);
            return File.ReadAllBytes(path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // A .crtexture of RGBA8 sRGB pixels, one mip.
    internal static byte[] TextureFile(uint width, uint height, byte[] rgba)
    {
        TextureInfo info = new() { Format = TextureFormat.Rgba8, Width = width, Height = height, MipCount = 1, Srgb = 1 };
        return ImportedFile("TXTR", ("INFO", MemoryMarshal.AsBytes(new ReadOnlySpan<TextureInfo>(ref info)).ToArray()),
            ("MIP0", rgba));
    }

    private void Check(bool passed, string what, string detail = "")
    {
        ++_checks;
        if (!passed)
        {
            ++_failures;
            Console.WriteLine($"CanvasChecks: FAILED: {what} {detail}");
        }
    }
}
