using System.Numerics;
using System.Text;
using Crucible.Loop;
using Crucible.Platform;
using Crucible.Render;
using Crucible.Text;
using Crucible.UI;
using Png = Crucible.Samples.Diorama.Png;

namespace Crucible.RenderStepGame;

// Run headless by CTest (launcher.jit.textfields) with --game-arg
// test=textfields --game-arg font=<NotoSans-Regular.ttf> [--game-arg
// capture=<file.png>]: text fields (ADR 0065) with the default theme at a
// DPI scale of 1.25. A synthetic click focuses the first, typed text
// arrives as TextInput events pointing into polled text (as the platform
// sends it), and Shift+Left selects its last word; a long one-line field
// clips its text, a multi-line one wraps, a number field shows its value.
[System(Phases.RenderPrepare)]
public sealed class TextFieldChecks : GameSystem
{
    private const int Width = 1280, Height = 720;
    private const float Scale = 1.25f;
    private const int ScancodeLeft = 80;
    private readonly UIRoot _root = new() { Scale = Scale };
    private FontHandle _font;
    private TextField? _name;
    private TextField? _long;
    private TextField? _notes;
    private NumberField? _number;
    private int _frame;
    private double _actedAt = double.NaN;
    private int _checks;
    private int _failures;

    public override void Start(FrameContext context)
    {
        if (!context.GameArguments.TryGetValue("test", out string? test) || test != "textfields")
        {
            Enabled = false;
            return;
        }
        _font = Fonts.Load(CanvasChecks.ImportedFile("FONT", ("DATA", File.ReadAllBytes(context.GameArguments["font"]))));
        _root.DefaultFont = _font;
        StackPanel column = new() { Spacing = 16, Padding = new Thickness(24), Background = new Vector4(0.12f, 0.13f, 0.15f, 1) };
        _name = new TextField { Width = 240, HorizontalAlignment = HorizontalAlignment.Start };
        _long = new TextField("A one-line field clips what doesn't fit inside it")
        {
            Width = 180,
            HorizontalAlignment = HorizontalAlignment.Start,
        };
        _notes = new TextField("Several lines of notes, wrapped to the width of the field as they're laid out")
        {
            Multiline = true,
            Width = 240,
            HorizontalAlignment = HorizontalAlignment.Start,
        };
        _number = new NumberField { Step = 0.25, Value = 2.5, HorizontalAlignment = HorizontalAlignment.Start };
        foreach (UIElement element in new UIElement[] { _name, _long, _notes, _number })
        {
            column.Add(element);
        }
        _root.Content = column;
        RenderSchedule.Default.InsertAfter("Tonemap", "UI", _root);
    }

    public override void Run(FrameContext context)
    {
        ++_frame;
        if (_frame == 3)
        {
            Vector2 at = new Vector2(_name!.Bounds.X + 10, _name.Bounds.Y + _name.Bounds.Height / 2) * Scale;
            byte[] text = Encoding.UTF8.GetBytes("Player One");
            _root.ProcessInput([
                new InputEvent { Type = InputEventType.MouseMotion, X = at.X, Y = at.Y },
                new InputEvent { Type = InputEventType.MouseButtonDown, X = at.X, Y = at.Y, Code = 1 },
                new InputEvent { Type = InputEventType.MouseButtonUp, X = at.X, Y = at.Y, Code = 1 },
                new InputEvent { Type = InputEventType.TextInput, Code = 0, Value = 7 },
                new InputEvent { Type = InputEventType.TextInput, Code = 7, Value = 3 },
            ], text);
            for (int i = 0; i < 3; ++i)
            {
                _root.ProcessInput([new InputEvent
                {
                    Type = InputEventType.KeyDown,
                    Code = ScancodeLeft,
                    Modifiers = (uint)KeyModifiers.Shift,
                }]);
            }
            _actedAt = _root.Now;
            return;
        }
        if (double.IsNaN(_actedAt))
        {
            return;
        }
        // Selecting again restarts the caret's blink, so the frame drawn
        // next (and read back after it) shows the caret.
        _name!.Select(10, 7);
        if (_root.IsAnimating || _root.Now - _actedAt < 0.2 || _frame < 8)
        {
            return;
        }
        byte[] bgra = new byte[Width * Height * 4];
        Render.Api.ReadSurfacePixels(context.Surface, bgra);
        (int R, int G, int B) Physical(int x, int y)
        {
            int i = (y * Width + x) * 4;
            return (bgra[i + 2], bgra[i + 1], bgra[i]);
        }
        (int R, int G, int B) Pixel(Vector2 logical) => Physical((int)(logical.X * Scale), (int)(logical.Y * Scale));

        Check(_name.Text == "Player One", "typed text reached the field", _name.Text);
        Check(_name.IsFocused && _name.SelectedText == "One", "Shift+Left selected the last word", _name.SelectedText);
        System.Drawing.RectangleF name = _name.Bounds;
        Thickness padding = _name.Padding;
        TextLayout layout = _name.Layout!;
        TextCaret start = layout.Caret(7), end = layout.Caret(10);
        float left = name.X + padding.Left + start.X, right = name.X + padding.Left + end.X;
        int selected = 0, total = 0;
        for (float y = name.Y + padding.Top + start.Top + 1; y < name.Y + padding.Top + start.Top + start.Height - 1; y += 1 / Scale)
        {
            for (float x = left + 1; x < right - 1; x += 1 / Scale)
            {
                (int r, int g, int b) = Pixel(new Vector2(x, y));
                selected += b > r + 40 ? 1 : 0;
                ++total;
            }
        }
        Check(selected > total / 3, "the selection is drawn behind the word", $"{selected} of {total}");
        Check(CountLight(Pixel, new System.Drawing.RectangleF(name.X + padding.Left, name.Y, left - name.X - padding.Left, name.Height), 0xc0) > 30,
            "the text before it is drawn");
        // The caret: one physical pixel, at the selection's start, in the text's color.
        int caretX = (int)MathF.Round((name.X + padding.Left + start.X) * Scale);
        int caretY = (int)((name.Y + padding.Top + start.Top + start.Height / 2) * Scale);
        (int cr, int cg, int cb) = Physical(caretX, caretY);
        Check(cr > 0xc0 && cg > 0xc0 && cb > 0xc0, "the caret is drawn", $"({cr}, {cg}, {cb}) at {caretX}, {caretY}");
        At(Pixel, new Vector2(name.X + 1, name.Y + name.Height / 2), (0x4c, 0x8b, 0xf5), "the focused field's outline");

        System.Drawing.RectangleF clipped = _long!.Bounds;
        Check(CountLight(Pixel, clipped, 0xc0) > 50, "the long field shows its text");
        Check(CountLight(Pixel, new System.Drawing.RectangleF(clipped.Right + 1, clipped.Y, 60, clipped.Height), 0x80) == 0,
            "and clips it at its edge");
        Check(_notes!.Layout!.LineCount >= 3, "the multi-line field wraps", _notes.Layout.LineCount.ToString());
        System.Drawing.RectangleF notes = _notes.Bounds;
        Check(CountLight(Pixel, new System.Drawing.RectangleF(notes.X, notes.Y + notes.Height * 0.6f, notes.Width, notes.Height * 0.3f), 0xc0) > 30,
            "and draws its lower lines");
        Check(_number!.Text == "2.50", "the number field shows its value", _number.Text);
        Check(CountLight(Pixel, _number.Bounds, 0xc0) > 15, "and draws it");

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
        _root.Content = null;
        Fonts.Unload(_font);
        Console.WriteLine(_failures == 0
            ? $"TextFieldChecks: all {_checks} checks passed"
            : $"TextFieldChecks: {_failures} of {_checks} checks failed");
        context.QuitRequested = true;
        Enabled = false;
    }

    private void At(Func<Vector2, (int R, int G, int B)> pixel, Vector2 logical, (int, int, int) expected, string what,
        int tolerance = 4)
    {
        (int r, int g, int b) = pixel(logical);
        (int er, int eg, int eb) = expected;
        Check(Math.Abs(r - er) <= tolerance && Math.Abs(g - eg) <= tolerance && Math.Abs(b - eb) <= tolerance, what,
            $"{logical} is {pixel(logical)}");
    }

    // Light text pixels within a rectangle (logical units).
    private static int CountLight(Func<Vector2, (int R, int G, int B)> pixel, System.Drawing.RectangleF rect, int light)
    {
        int count = 0;
        for (float y = rect.Top; y < rect.Bottom; y += 1 / Scale)
        {
            for (float x = rect.Left; x < rect.Right; x += 1 / Scale)
            {
                count += pixel(new Vector2(x, y)).R >= light ? 1 : 0;
            }
        }
        return count;
    }

    private void Check(bool passed, string what, string detail = "")
    {
        ++_checks;
        if (!passed)
        {
            ++_failures;
            Console.WriteLine($"TextFieldChecks: FAILED: {what} {detail}");
        }
    }
}
