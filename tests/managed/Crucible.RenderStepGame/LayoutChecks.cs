using System.Numerics;
using Crucible.Loop;
using Crucible.Render;
using Crucible.UI;
using Png = Crucible.Samples.Diorama.Png;

namespace Crucible.RenderStepGame;

// Run headless by CTest (launcher.jit.layout) with --game-arg test=layout
// [--game-arg capture=<file.png>]: a UIRoot at a DPI scale of 1.25 (the
// 1280x720 surface is 1024x576 logical units) holding every panel kind:
//   DockPanel: a top bar (40 high), a sidebar (200 wide) with a StackPanel
//   of custom-drawn pills, and an AnchorPanel filling the rest with a 2x2
//   Grid inset by 20 and a badge pinned near its top right, turned 10°.
[System(Phases.RenderPrepare)]
public sealed class LayoutChecks : GameSystem
{
    private const int Width = 1280, Height = 720;
    private readonly UIRoot _root = new() { Scale = 1.25f };
    private int _frame;
    private int _checks;
    private int _failures;

    // A custom-draw element: a pill in its whole size.
    private sealed class Pill(Vector4 color) : UIElement
    {
        protected override void OnDraw(Canvas canvas) =>
            canvas.FillRect(Vector2.Zero, ActualSize, color, ActualSize.Y * 0.5f);
    }

    public override void Start(FrameContext context)
    {
        if (!context.GameArguments.TryGetValue("test", out string? test) || test != "layout")
        {
            Enabled = false;
            return;
        }
        DockPanel dock = new() { Background = new Vector4(1, 1, 1, 1) };
        UIElement bar = new() { Height = 40, Background = new Vector4(0.2f, 0.2f, 0.2f, 1) };
        DockPanel.SetDock(bar, Dock.Top);
        StackPanel sidebar = new()
        {
            Width = 200,
            Spacing = 10,
            Padding = new Thickness(10),
            Background = new Vector4(0.8f, 0.8f, 0.8f, 1),
        };
        DockPanel.SetDock(sidebar, Dock.Left);
        foreach (Vector4 color in new[] { new Vector4(1, 0, 0, 1), new Vector4(0, 0.5f, 0, 1), new Vector4(0, 0, 1, 1) })
        {
            sidebar.Add(new Pill(color) { Height = 50 });
        }
        AnchorPanel area = new();
        Grid grid = new() { ColumnSpacing = 10, RowSpacing = 10 };
        grid.Columns.AddRange([GridLength.Star(), GridLength.Star()]);
        grid.Rows.AddRange([GridLength.Star(), GridLength.Star()]);
        Vector4[] cells = [new(1, 0, 0, 1), new(0, 1, 0, 1), new(0, 0, 1, 1), new(1, 1, 0, 1)];
        for (int i = 0; i < 4; ++i)
        {
            UIElement cell = new() { Background = cells[i] };
            Grid.SetColumn(cell, i % 2);
            Grid.SetRow(cell, i / 2);
            grid.Add(cell);
        }
        AnchorPanel.SetAnchors(grid, Anchors.Fill.Inset(20));
        area.Add(grid);
        UIElement badge = new()
        {
            Width = 80,
            Height = 30,
            Background = new Vector4(1, 0, 1, 1),
            CornerRadii = new Vector4(6),
            RenderTransform = Matrix3x2.CreateRotation(MathF.PI / 18),
        };
        AnchorPanel.SetAnchors(badge, Anchors.At(new Vector2(1, 0), new Vector2(-30, 30)));
        area.Add(badge);
        dock.Add(bar);
        dock.Add(sidebar);
        dock.Add(area);
        _root.Content = dock;
        RenderSchedule.InsertAfter("Tonemap", "UI", _root);
    }

    public override void Run(FrameContext context)
    {
        if (++_frame < 5)
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
            Check(Math.Abs(r - er) <= 3 && Math.Abs(g - eg) <= 3 && Math.Abs(b - eb) <= 3, what,
                $"({x}, {y}) is {Pixel(x, y)}");
        }

        Check(_root.Size == new Vector2(1024, 576), "the root is the surface in logical units", _root.Size.ToString());
        At(640, 25, (51, 51, 51), "the top bar"); // sRGB 0.2 in, sRGB 0.2 out
        At(20, 400, (204, 204, 204), "the sidebar's background");
        At(125, 94, (255, 0, 0), "the first pill");
        At(125, 169, (0, 128, 0), "the second pill");
        At(125, 244, (0, 0, 255), "the third pill");
        At(500, 225, (255, 0, 0), "the grid's top left cell");
        At(1000, 225, (0, 255, 0), "its top right cell");
        At(500, 600, (0, 0, 255), "its bottom left cell");
        At(1000, 600, (255, 255, 0), "its bottom right cell");
        At(1192, 106, (255, 0, 255), "the badge, over the grid");
        // Across the gap between two cells: snapped edges, so every pixel is
        // one cell's, or the background, never a blend (a misplaced edge
        // would be about half and half; rounding leaves a level or two).
        static bool Near((int R, int G, int B) p, (int R, int G, int B) q) =>
            Math.Abs(p.R - q.R) <= 3 && Math.Abs(p.G - q.G) <= 3 && Math.Abs(p.B - q.B) <= 3;
        bool crisp = true;
        for (int x = 740; x < 790; ++x)
        {
            (int R, int G, int B) p = Pixel(x, 225);
            crisp &= Near(p, (255, 0, 0)) || Near(p, (0, 255, 0)) || Near(p, (255, 255, 255));
        }
        Check(crisp, "cell edges are whole pixels");

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
        RenderSchedule.Remove("UI");
        Console.WriteLine(_failures == 0
            ? $"LayoutChecks: all {_checks} checks passed"
            : $"LayoutChecks: {_failures} of {_checks} checks failed");
        context.QuitRequested = true;
        Enabled = false;
    }

    private void Check(bool passed, string what, string detail = "")
    {
        ++_checks;
        if (!passed)
        {
            ++_failures;
            Console.WriteLine($"LayoutChecks: FAILED: {what} {detail}");
        }
    }
}
