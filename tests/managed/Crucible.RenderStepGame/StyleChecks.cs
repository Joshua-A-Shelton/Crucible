using System.Numerics;
using Crucible.Loop;
using Crucible.Render;
using Crucible.UI;
using Png = Crucible.Samples.Diorama.Png;

namespace Crucible.RenderStepGame;

// Run headless by CTest (launcher.jit.styles) with --game-arg test=styles
// [--game-arg capture=<file.png>]: a themed tree on white. A card with a
// border; a half-faded red box; a card whose background code sets; and a
// button that, hovered on frame 3, transitions to green and grows 20%.
// Once the transition has run (real time), the frame is checked.
[System(Phases.RenderPrepare)]
public sealed class StyleChecks : GameSystem
{
    private const int Width = 1280, Height = 720;
    private const string ThemeJson = """
        {
          "Styles": [
            { "Selector": "AnchorPanel", "Set": { "Background": "#fff" } },
            { "Selector": ".card", "Set": { "Background": "#3366cc", "BorderWidth": 4, "BorderColor": "#000", "CornerRadii": 8 } },
            { "Selector": ".fade", "Set": { "Background": "#f00", "Opacity": 0.5 } },
            {
              "Selector": ".button",
              "Set": { "Background": "#808080" },
              "Transitions": {
                "Background": { "Duration": 0.1, "Easing": "EaseOut" },
                "RenderScale": { "Duration": 0.1, "Easing": "EaseOut" }
              }
            },
            { "Selector": ".button:hover", "Set": { "Background": "#00ff00", "RenderScale": 1.2 } }
          ]
        }
        """;

    private readonly UIRoot _root = new();
    private UIElement? _button;
    private int _frame;
    private int _hoveredAt;
    private double _hoveredTime;
    private int _checks;
    private int _failures;

    public override void Start(FrameContext context)
    {
        if (!context.GameArguments.TryGetValue("test", out string? test) || test != "styles")
        {
            Enabled = false;
            return;
        }
        AnchorPanel panel = new();
        UIElement card = Box(panel, "card", new Vector2(40, 40), new Vector2(200, 100));
        Box(panel, "fade", new Vector2(300, 40), new Vector2(200, 100));
        UIElement overridden = Box(panel, "card", new Vector2(40, 200), new Vector2(200, 100));
        overridden.Background = new Vector4(1, 0, 0, 1); // code beats the theme
        _button = Box(panel, "button", new Vector2(600, 40), new Vector2(100, 100));
        _root.Theme = Theme.Parse(ThemeJson);
        _root.Content = panel;
        _ = card;
        RenderSchedule.Default.InsertAfter("Tonemap", "UI", _root);
    }

    private static UIElement Box(AnchorPanel panel, string className, Vector2 position, Vector2 size)
    {
        UIElement box = new() { Width = size.X, Height = size.Y };
        box.Classes.Add(className);
        AnchorPanel.SetAnchors(box, Anchors.At(Vector2.Zero, position));
        panel.Add(box);
        return box;
    }

    public override void Run(FrameContext context)
    {
        ++_frame;
        if (_frame == 3)
        {
            _button!.State = UIState.Hover;
            _hoveredAt = _frame;
            _hoveredTime = _root.Now;
            return;
        }
        // Wait out the transition, then a frame more for it to be drawn.
        if (_hoveredAt == 0 || _root.IsAnimating || _root.Now - _hoveredTime < 0.15 || _frame < _hoveredAt + 3)
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
        void At(int x, int y, (int, int, int) expected, string what)
        {
            (int r, int g, int b) = Pixel(x, y);
            (int er, int eg, int eb) = expected;
            Check(Math.Abs(r - er) <= 3 && Math.Abs(g - eg) <= 3 && Math.Abs(b - eb) <= 3, what, $"({x}, {y}) is {Pixel(x, y)}");
        }

        At(140, 90, (0x33, 0x66, 0xcc), "a class's background");
        At(42, 90, (0, 0, 0), "its border");
        At(20, 90, (255, 255, 255), "the panel's background from its type");
        At(400, 90, (255, 188, 188), "half opacity blends in linear light");
        At(140, 250, (255, 0, 0), "a value set in code beats the theme");
        At(650, 90, (0, 255, 0), "a hovered button transitioned to green");
        At(595, 90, (0, 255, 0), "and grew past its bounds");
        At(585, 90, (255, 255, 255), "by a fifth, not more");

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
        Console.WriteLine(_failures == 0
            ? $"StyleChecks: all {_checks} checks passed"
            : $"StyleChecks: {_failures} of {_checks} checks failed");
        context.QuitRequested = true;
        Enabled = false;
    }

    private void Check(bool passed, string what, string detail = "")
    {
        ++_checks;
        if (!passed)
        {
            ++_failures;
            Console.WriteLine($"StyleChecks: FAILED: {what} {detail}");
        }
    }
}
