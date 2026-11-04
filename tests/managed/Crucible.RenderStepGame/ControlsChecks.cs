using System.Numerics;
using Crucible.Loop;
using Crucible.Platform;
using Crucible.Render;
using Crucible.Text;
using Crucible.UI;
using Png = Crucible.Samples.Diorama.Png;

namespace Crucible.RenderStepGame;

// Run headless by CTest (launcher.jit.controls) with --game-arg
// test=controls --game-arg font=<NotoSans-Regular.ttf> [--game-arg
// capture=<file.png>]: the controls with the default theme on a dark
// panel at a DPI scale of 1.25: a hovered button, a toggle switched on by
// a synthetic click, a slider at half, an image, and a scroll view of
// buttons scrolled by a synthetic wheel. Checked once the transitions have
// run (real time).
[System(Phases.RenderPrepare)]
public sealed class ControlsChecks : GameSystem
{
    private const int Width = 1280, Height = 720;
    private const float Scale = 1.25f;
    private readonly UIRoot _root = new() { Scale = Scale };
    private FontHandle _font;
    private TextureHandle _texture;
    private Button? _button;
    private Toggle? _toggle;
    private Slider? _slider;
    private ScrollView? _scroll;
    private ComboBox? _combo;
    private Spinner? _spinner;
    private ListBox? _list;
    private int _frame;
    private double _actedAt = double.NaN;
    private int _checks;
    private int _failures;

    public override void Start(FrameContext context)
    {
        if (!context.GameArguments.TryGetValue("test", out string? test) || test != "controls")
        {
            Enabled = false;
            return;
        }
        _font = Fonts.Load(CanvasChecks.ImportedFile("FONT", ("DATA", File.ReadAllBytes(context.GameArguments["font"]))));
        _texture = Render.Api.LoadTexture(CanvasChecks.TextureFile(2, 2,
            [255, 0, 0, 255, 0, 255, 0, 255, 0, 0, 255, 255, 255, 255, 255, 255]));
        _root.DefaultFont = _font;

        StackPanel column = new() { Spacing = 16, Padding = new Thickness(24), Background = new Vector4(0.12f, 0.13f, 0.15f, 1) };
        _button = new Button("Play") { HorizontalAlignment = HorizontalAlignment.Start };
        _toggle = new Toggle("Subtitles") { HorizontalAlignment = HorizontalAlignment.Start };
        _slider = new Slider { Value = 0.5f, HorizontalAlignment = HorizontalAlignment.Start };
        Image image = new() { Texture = _texture, Width = 64, Height = 64, HorizontalAlignment = HorizontalAlignment.Start };
        StackPanel items = new() { Spacing = 4 };
        for (int i = 0; i < 12; ++i)
        {
            items.Add(new Button($"Level {i + 1}"));
        }
        _scroll = new ScrollView { Content = items, Width = 220, Height = 160, HorizontalAlignment = HorizontalAlignment.Start };
        foreach (UIElement element in new UIElement[] { _button, _toggle, _slider, image, _scroll })
        {
            column.Add(element);
        }
        // A second column: a spinner, a dropdown (opened on frame 3), and a long list.
        StackPanel second = new() { Spacing = 16, Padding = new Thickness(24), Width = 300 };
        _spinner = new Spinner { Options = ["Easy", "Normal", "Hard"], SelectedIndex = 1, HorizontalAlignment = HorizontalAlignment.Start };
        _combo = new ComboBox { Options = ["Low", "Medium", "High", "Ultra"], SelectedIndex = 2, HorizontalAlignment = HorizontalAlignment.Start };
        _list = new ListBox
        {
            ItemCount = 500,
            Height = 200,
            CreateItem = () => new Label(),
            BindItem = (element, index) => ((Label)element).Text = $"Save slot {index + 1}",
            SelectedIndex = 1,
        };
        second.Add(_spinner);
        second.Add(_combo);
        second.Add(_list);
        DockPanel page = new() { Background = new Vector4(0.12f, 0.13f, 0.15f, 1) };
        column.Background = default;
        DockPanel.SetDock(column, Dock.Left);
        page.Add(column);
        page.Add(second);
        _root.Content = page;
        RenderSchedule.Default.InsertAfter("Tonemap", "UI", _root);
    }

    public override void Run(FrameContext context)
    {
        ++_frame;
        if (_frame == 3)
        {
            // Hover the button, click the toggle, wheel the scroll view (physical pixels).
            Vector2 Physical(UIElement e) => new Vector2(e.Bounds.X + e.Bounds.Width / 2, e.Bounds.Y + e.Bounds.Height / 2) * Scale;
            Vector2 toggleAt = Physical(_toggle!), buttonAt = Physical(_button!), scrollAt = Physical(_scroll!);
            Vector2 comboAt = Physical(_combo!);
            _root.ProcessInput([
                new InputEvent { Type = InputEventType.MouseMotion, X = toggleAt.X, Y = toggleAt.Y },
                new InputEvent { Type = InputEventType.MouseButtonDown, X = toggleAt.X, Y = toggleAt.Y, Code = 1 },
                new InputEvent { Type = InputEventType.MouseButtonUp, X = toggleAt.X, Y = toggleAt.Y, Code = 1 },
                new InputEvent { Type = InputEventType.MouseWheel, X = scrollAt.X, Y = scrollAt.Y, Dy = -1 },
                new InputEvent { Type = InputEventType.MouseMotion, X = buttonAt.X, Y = buttonAt.Y },
                new InputEvent { Type = InputEventType.MouseButtonDown, X = comboAt.X, Y = comboAt.Y, Code = 1 },
                new InputEvent { Type = InputEventType.MouseButtonUp, X = comboAt.X, Y = comboAt.Y, Code = 1 },
                new InputEvent { Type = InputEventType.MouseMotion, X = buttonAt.X, Y = buttonAt.Y },
            ]);
            _actedAt = _root.Now;
            return;
        }
        if (double.IsNaN(_actedAt) || _root.IsAnimating || _root.Now - _actedAt < 0.2 || _frame < 8)
        {
            return;
        }
        byte[] bgra = new byte[Width * Height * 4];
        Render.Api.ReadSurfacePixels(context.Surface, bgra);
        (int R, int G, int B) Pixel(Vector2 logical)
        {
            int x = (int)(logical.X * Scale), y = (int)(logical.Y * Scale);
            int i = (y * Width + x) * 4;
            return (bgra[i + 2], bgra[i + 1], bgra[i]);
        }
        void At(Vector2 logical, (int, int, int) expected, string what, int tolerance = 4)
        {
            (int r, int g, int b) = Pixel(logical);
            (int er, int eg, int eb) = expected;
            Check(Math.Abs(r - er) <= tolerance && Math.Abs(g - eg) <= tolerance && Math.Abs(b - eb) <= tolerance, what,
                $"{logical} is {Pixel(logical)}");
        }

        System.Drawing.RectangleF button = _button!.Bounds;
        At(new Vector2(button.X + 4, button.Y + button.Height / 2), (0x43, 0x49, 0x54), "the hovered button's background");
        Check(CountLight(Pixel, button, 0xc0) > 20, "the button's text is drawn");
        Check(_toggle!.IsOn, "a click switched the toggle on");
        System.Drawing.RectangleF box = _toggle.Box.Bounds;
        At(new Vector2(box.X + 2, box.Y + box.Height / 2), (0x4c, 0x8b, 0xf5), "the toggle's box, on");
        At(new Vector2(box.X + box.Width / 2, box.Y + box.Height / 2), (0xe8, 0xe8, 0xec), "its mark, faded in");
        System.Drawing.RectangleF thumb = _slider!.Thumb.Bounds;
        Check(MathF.Abs(thumb.X + thumb.Width / 2 - (_slider.Bounds.X + _slider.Bounds.Width / 2)) < 1,
            "the slider's thumb at half", thumb.ToString());
        At(new Vector2(thumb.X + thumb.Width / 2, thumb.Y + thumb.Height / 2), (0xe8, 0xe8, 0xec), "the thumb");
        Check(_scroll!.Offset.Y == 48, "the wheel scrolled the list a notch", _scroll.Offset.ToString());
        System.Drawing.RectangleF bar = _scroll.VerticalBar.Thumb.Bounds;
        Check(bar.Height > 20 && bar.Y > _scroll.Bounds.Y, "its scrollbar shows where it is", bar.ToString());
        Check(_combo!.IsOpen, "a click opened the dropdown");
        System.Drawing.RectangleF popup = _combo.Popup.Bounds;
        Check(MathF.Abs(popup.Top - _combo.Bounds.Bottom) < 0.01f, "its list opened below it", popup.ToString());
        At(new Vector2(popup.X + 2, popup.Y + popup.Height / 2), (0x35, 0x3a, 0x42), "the popup's background over the page");
        Check(CountLight(Pixel, popup, 0xc0) > 40, "its options are drawn");
        Check(_list!.RealizedCount < 20, "the long list made only the rows in view", _list.RealizedCount.ToString());
        Check(CountLight(Pixel, _list.Bounds, 0xc0) > 100, "and drew them");
        Check(CountLight(Pixel, _spinner!.Bounds, 0xc0) > 20, "the spinner shows its option");

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
        _combo.Popup.Close();
        RenderSchedule.Default.Remove("UI");
        _root.Content = null;
        Fonts.Unload(_font);
        Render.Api.UnloadTexture(_texture);
        Console.WriteLine(_failures == 0
            ? $"ControlsChecks: all {_checks} checks passed"
            : $"ControlsChecks: {_failures} of {_checks} checks failed");
        context.QuitRequested = true;
        Enabled = false;
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
            Console.WriteLine($"ControlsChecks: FAILED: {what} {detail}");
        }
    }
}
