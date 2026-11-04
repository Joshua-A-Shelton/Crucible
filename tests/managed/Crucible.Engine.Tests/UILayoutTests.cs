using System.Drawing;
using System.Numerics;
using Crucible.UI;

namespace Crucible.Tests;

// The element tree's layout and hit testing (ADR 0060), under JIT and
// NativeAOT; launcher.jit.layout draws a tree on the GPU.
public class UILayoutTests
{
    // Content of a fixed size, counting its measures.
    private sealed class Box(float width, float height) : UIElement
    {
        public int Measures;

        public Vector2 Content { get; set; } = new(width, height);

        protected override Vector2 MeasureCore(Vector2 available)
        {
            ++Measures;
            return Content;
        }
    }

    private static UIRoot Root(UIElement content, float width = 400, float height = 300, float scale = 1)
    {
        UIRoot root = new() { Scale = scale, Content = content };
        root.UpdateLayout(new Vector2(width, height) * scale);
        return root;
    }

    [Test]
    public async Task AlignmentMarginAndPaddingPlaceAnElement()
    {
        Box box = new(50, 20)
        {
            HorizontalAlignment = HorizontalAlignment.End,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10),
            Padding = new Thickness(5, 0, 5, 0),
        };
        Root(box);
        await Assert.That(box.DesiredSize).IsEqualTo(new Vector2(80, 40));
        await Assert.That(box.Bounds).IsEqualTo(new RectangleF(330, 140, 60, 20));

        // Stretch fills the slot less the margin; Min and Max still hold, a
        // minimum even past the space there is (as in WPF).
        Box stretched = new(50, 20) { Margin = new Thickness(10), MaxWidth = 100, MinHeight = 300 };
        Root(stretched);
        await Assert.That(stretched.Bounds).IsEqualTo(new RectangleF(10, 10, 100, 300));
        Box fixedSize = new(50, 20) { Width = 30, Height = 40, HorizontalAlignment = HorizontalAlignment.Start };
        Root(fixedSize);
        await Assert.That(fixedSize.Bounds.Size).IsEqualTo(new SizeF(30, 40));
    }

    [Test]
    public async Task StackPanelsStackWithSpacingAndSkipCollapsedChildren()
    {
        Box a = new(30, 10), b = new(50, 20), gone = new(99, 99) { Visibility = Visibility.Collapsed }, c = new(40, 30);
        StackPanel stack = new() { Spacing = 5, HorizontalAlignment = HorizontalAlignment.Start, VerticalAlignment = VerticalAlignment.Start };
        stack.Add(a);
        stack.Add(b);
        stack.Add(gone);
        stack.Add(c);
        UIRoot root = Root(stack);
        await Assert.That(stack.DesiredSize).IsEqualTo(new Vector2(50, 70));
        await Assert.That(b.Bounds).IsEqualTo(new RectangleF(0, 15, 50, 20));
        await Assert.That(c.Bounds.Y).IsEqualTo(40f);

        stack.Orientation = Orientation.Horizontal;
        root.UpdateLayout(new Vector2(400, 300));
        await Assert.That(stack.DesiredSize).IsEqualTo(new Vector2(130, 30));
        await Assert.That(c.Bounds.X).IsEqualTo(90f);
    }

    [Test]
    public async Task GridsSizeFixedThenAutoThenStarTracks()
    {
        Grid grid = new() { ColumnSpacing = 10 };
        grid.Columns.AddRange([GridLength.Pixels(50), GridLength.Auto, GridLength.Star(), GridLength.Star(3)]);
        grid.Rows.AddRange([GridLength.Auto, GridLength.Star()]);
        Box auto = new(70, 25), star = new(10, 10), wide = new(0, 0), spanned = new(500, 5);
        Grid.SetColumn(auto, 1);
        Grid.SetColumn(star, 3);
        Grid.SetRow(wide, 1);
        Grid.SetColumnSpan(wide, 4);
        Grid.SetColumn(spanned, 1);
        Grid.SetColumnSpan(spanned, 2); // spans a star track: doesn't size the auto one
        Grid.SetRow(spanned, 1);
        grid.Add(auto);
        grid.Add(star);
        grid.Add(wide);
        grid.Add(spanned);
        Root(grid, 430, 200);
        // 430 - 50 - 70 - 3 gaps of 10 = 280 left: 70 and 210.
        await Assert.That(auto.Bounds).IsEqualTo(new RectangleF(60, 0, 70, 25));
        await Assert.That(star.Bounds).IsEqualTo(new RectangleF(220, 0, 210, 25));
        await Assert.That(wide.Bounds).IsEqualTo(new RectangleF(0, 25, 430, 175));
        await Assert.That(spanned.Bounds.Width).IsEqualTo(150f);
    }

    [Test]
    public async Task DockPanelsDockInTurnAndTheLastFills()
    {
        Box top = new(0, 30), left = new(40, 0), right = new(60, 0), fill = new(0, 0);
        DockPanel dock = new();
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(left, Dock.Left);
        DockPanel.SetDock(right, Dock.Right);
        dock.Add(top);
        dock.Add(left);
        dock.Add(right);
        dock.Add(fill);
        Root(dock);
        await Assert.That(top.Bounds).IsEqualTo(new RectangleF(0, 0, 400, 30));
        await Assert.That(left.Bounds).IsEqualTo(new RectangleF(0, 30, 40, 270));
        await Assert.That(right.Bounds).IsEqualTo(new RectangleF(340, 30, 60, 270));
        await Assert.That(fill.Bounds).IsEqualTo(new RectangleF(40, 30, 300, 270));
    }

    [Test]
    public async Task AnchorsStretchOrPinChildren()
    {
        AnchorPanel panel = new();
        Box corner = new(50, 20), center = new(40, 40), band = new(0, 10), inset = new(0, 0);
        AnchorPanel.SetAnchors(corner, Anchors.At(new Vector2(1, 0), new Vector2(-10, 10)));
        AnchorPanel.SetAnchors(center, Anchors.Center);
        // Stretched across, pinned to the bottom at its desired height.
        AnchorPanel.SetAnchors(band, new Anchors(new Vector2(0, 1), new Vector2(1, 1), Vector2.Zero, Vector2.Zero));
        AnchorPanel.SetAnchors(inset, Anchors.Fill.Inset(20));
        panel.Add(corner);
        panel.Add(center);
        panel.Add(band);
        panel.Add(inset);
        Root(panel);
        await Assert.That(corner.Bounds).IsEqualTo(new RectangleF(340, 10, 50, 20));
        await Assert.That(center.Bounds).IsEqualTo(new RectangleF(180, 130, 40, 40));
        await Assert.That(band.Bounds).IsEqualTo(new RectangleF(0, 290, 400, 10));
        await Assert.That(inset.Bounds).IsEqualTo(new RectangleF(20, 20, 360, 260));
    }

    [Test]
    public async Task EdgesSnapToWholePhysicalPixelsAndNeighborsShareThem()
    {
        foreach (float scale in new[] { 1.25f, 1.5f })
        {
            Grid grid = new();
            grid.Columns.AddRange([GridLength.Star(), GridLength.Star(), GridLength.Star()]);
            Box[] cells = [new(0, 0), new(0, 0), new(0, 0)];
            for (int i = 0; i < cells.Length; ++i)
            {
                Grid.SetColumn(cells[i], i);
                grid.Add(cells[i]);
            }
            Root(grid, 333.3f, 100, scale);
            for (int i = 0; i < cells.Length; ++i)
            {
                RectangleF b = cells[i].Bounds;
                foreach (float edge in new[] { b.Left, b.Right, b.Top, b.Bottom })
                {
                    float physical = edge * scale;
                    await Assert.That(MathF.Abs(physical - MathF.Round(physical))).IsLessThan(1e-3f);
                }
                if (i > 0)
                {
                    await Assert.That(b.Left).IsEqualTo(cells[i - 1].Bounds.Right);
                }
            }
        }
    }

    [Test]
    public async Task OnlyChangedElementsAreMeasuredAgain()
    {
        Box a = new(10, 10), b = new(10, 10);
        StackPanel stack = new();
        stack.Add(a);
        stack.Add(b);
        UIRoot root = Root(stack);
        await Assert.That(a.Measures).IsEqualTo(1);
        root.UpdateLayout(new Vector2(400, 300)); // nothing changed
        await Assert.That(a.Measures + b.Measures).IsEqualTo(2);

        b.Content = new Vector2(10, 50);
        b.InvalidateMeasure();
        root.UpdateLayout(new Vector2(400, 300));
        await Assert.That(a.Measures).IsEqualTo(1); // same space offered: not again
        await Assert.That(b.Measures).IsEqualTo(2);
        await Assert.That(stack.DesiredSize.Y).IsEqualTo(60f);

        root.Scale = 2; // everything again
        root.UpdateLayout(new Vector2(800, 600));
        await Assert.That(a.Measures).IsEqualTo(2);
    }

    [Test]
    public async Task HitTestingFindsTheTopmostVisibleElement()
    {
        AnchorPanel panel = new();
        Box back = new(100, 100), front = new(50, 50), hidden = new(400, 300) { Visibility = Visibility.Hidden };
        Box ghost = new(400, 300) { IsHitTestVisible = false };
        AnchorPanel.SetAnchors(back, Anchors.TopLeft);
        AnchorPanel.SetAnchors(front, Anchors.TopLeft);
        panel.Add(back);
        panel.Add(front);
        panel.Add(hidden);
        panel.Add(ghost);
        UIRoot root = Root(panel);
        await Assert.That(root.HitTest(new Vector2(25, 25))).IsSameReferenceAs(front);
        await Assert.That(root.HitTest(new Vector2(75, 75))).IsSameReferenceAs(back);
        await Assert.That(root.HitTest(new Vector2(300, 200))).IsSameReferenceAs(panel);

        // A render transform moves hits, not layout: front, scaled 2x about its center.
        front.RenderTransform = Matrix3x2.CreateScale(2);
        await Assert.That(front.Bounds).IsEqualTo(new RectangleF(0, 0, 50, 50));
        await Assert.That(root.HitTest(new Vector2(70, 70))).IsSameReferenceAs(front);

        // Clipping hides children outside the bounds from hits.
        back.ClipToBounds = true;
        StackPanel clipped = new() { Width = 20, Height = 20, ClipToBounds = true, HorizontalAlignment = HorizontalAlignment.Start, VerticalAlignment = VerticalAlignment.Start };
        Box overflowing = new(80, 80);
        clipped.Add(overflowing);
        AnchorPanel.SetAnchors(clipped, Anchors.At(new Vector2(0, 1), new Vector2(0, 0)));
        panel.Add(clipped);
        root.UpdateLayout(new Vector2(400, 300));
        await Assert.That(root.HitTest(new Vector2(10, 290))).IsSameReferenceAs(overflowing);
        await Assert.That(root.HitTest(new Vector2(50, 250))).IsSameReferenceAs(panel);
    }

    [Test]
    public async Task ElementsBelongToOneParent()
    {
        StackPanel a = new(), b = new();
        Box box = new(1, 1);
        a.Add(box);
        await Assert.That(Assert.Throws<InvalidOperationException>(() => b.Add(box)).Message).Contains("parent");
        await Assert.That(Assert.Throws<InvalidOperationException>(() => a.Add(a)).Message).Contains("itself");
        a.Remove(box);
        b.Add(box);
        await Assert.That(box.Parent).IsSameReferenceAs(b);
    }

    [Test]
    public async Task AStarColumnsCellGrowsItsAutoRow()
    {
        // A form: captions in a fixed column, controls in a star column, auto rows.
        Grid grid = new() { HorizontalAlignment = HorizontalAlignment.Start, VerticalAlignment = VerticalAlignment.Start };
        grid.Columns.AddRange([GridLength.Pixels(100), GridLength.Star()]);
        grid.Rows.AddRange([GridLength.Auto, GridLength.Auto]);
        Box caption = new(50, 20), tall = new(80, 60), next = new(80, 10);
        Grid.SetColumn(tall, 1);
        Grid.SetRow(next, 1);
        Grid.SetColumn(next, 1);
        grid.Add(caption);
        grid.Add(tall);
        grid.Add(next);
        Root(grid);
        await Assert.That(tall.Bounds.Height).IsEqualTo(60f);
        await Assert.That(next.Bounds.Y).IsEqualTo(60f);
        await Assert.That(grid.DesiredSize.Y).IsEqualTo(70f);
    }
}
