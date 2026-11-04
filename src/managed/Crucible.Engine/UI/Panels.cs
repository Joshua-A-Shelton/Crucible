using System.Drawing;
using System.Numerics;

namespace Crucible.UI;

public enum Orientation
{
    Vertical,
    Horizontal,
}

/// <summary>Children one after another, with <see cref="Spacing"/> between them (ADR 0060).</summary>
public sealed class StackPanel : Panel
{
    private Orientation _orientation;
    private float _spacing;

    public Orientation Orientation
    {
        get => _orientation;
        set
        {
            _orientation = value;
            InvalidateMeasure();
        }
    }

    public float Spacing
    {
        get => _spacing;
        set
        {
            _spacing = value;
            InvalidateMeasure();
        }
    }

    protected override Vector2 MeasureCore(Vector2 available)
    {
        bool vertical = _orientation == Orientation.Vertical;
        // Unbounded along the stack: each child takes what it wants.
        Vector2 offered = vertical ? new Vector2(available.X, float.PositiveInfinity)
            : new Vector2(float.PositiveInfinity, available.Y);
        float along = 0, across = 0;
        int counted = 0;
        foreach (UIElement child in Children)
        {
            child.Measure(offered);
            if (child.Visibility == Visibility.Collapsed)
            {
                continue;
            }
            along += vertical ? child.DesiredSize.Y : child.DesiredSize.X;
            across = MathF.Max(across, vertical ? child.DesiredSize.X : child.DesiredSize.Y);
            ++counted;
        }
        along += _spacing * MathF.Max(counted - 1, 0);
        return vertical ? new Vector2(across, along) : new Vector2(along, across);
    }

    protected override void ArrangeCore(RectangleF content)
    {
        bool vertical = _orientation == Orientation.Vertical;
        float at = vertical ? content.Y : content.X;
        foreach (UIElement child in Children)
        {
            if (child.Visibility == Visibility.Collapsed)
            {
                child.Arrange(new RectangleF(content.X, content.Y, 0, 0));
                continue;
            }
            if (vertical)
            {
                child.Arrange(new RectangleF(content.X, at, content.Width, child.DesiredSize.Y));
                at += child.DesiredSize.Y + _spacing;
            }
            else
            {
                child.Arrange(new RectangleF(at, content.Y, child.DesiredSize.X, content.Height));
                at += child.DesiredSize.X + _spacing;
            }
        }
    }
}

public enum GridUnit
{
    Auto,   // the largest child's (children spanning one track)
    Pixels, // logical units
    Star,   // a share of what's left
}

/// <summary>A grid row's height or column's width.</summary>
public readonly record struct GridLength(float Value, GridUnit Unit)
{
    public static GridLength Auto => new(0, GridUnit.Auto);

    public static GridLength Pixels(float value) => new(value, GridUnit.Pixels);

    public static GridLength Star(float weight = 1) => new(weight, GridUnit.Star);
}

/// <summary>
/// Rows and columns sized automatically, in logical units, or as shares of
/// what's left (ADR 0060). Children say where they go with
/// <see cref="SetRow"/>, <see cref="SetColumn"/> and the spans. With no
/// rows (or columns), there's one star track.
/// </summary>
public sealed class Grid : Panel
{
    private float[] _columnSizes = [];
    private float[] _rowSizes = [];

    public List<GridLength> Columns { get; } = [];

    public List<GridLength> Rows { get; } = [];

    public float ColumnSpacing { get; set; }

    public float RowSpacing { get; set; }

    public static void SetRow(UIElement element, int row)
    {
        element.GridRow = row;
        element.Parent?.InvalidateMeasure();
    }

    public static void SetColumn(UIElement element, int column)
    {
        element.GridColumn = column;
        element.Parent?.InvalidateMeasure();
    }

    public static void SetRowSpan(UIElement element, int span)
    {
        element.GridRowSpan = Math.Max(span, 1);
        element.Parent?.InvalidateMeasure();
    }

    public static void SetColumnSpan(UIElement element, int span)
    {
        element.GridColumnSpan = Math.Max(span, 1);
        element.Parent?.InvalidateMeasure();
    }

    /// <summary>Call after editing <see cref="Columns"/>, <see cref="Rows"/> or the spacing.</summary>
    public void DefinitionsChanged() => InvalidateMeasure();

    protected override Vector2 MeasureCore(Vector2 available)
    {
        List<GridLength> columns = Tracks(Columns), rows = Tracks(Rows);
        _columnSizes = new float[columns.Count];
        _rowSizes = new float[rows.Count];
        // Fixed tracks first; auto tracks grow to their children, measured
        // with what fixed tracks give them and unbounded otherwise; star
        // tracks share what's left, then their children are measured in it.
        FixedSizes(columns, _columnSizes);
        FixedSizes(rows, _rowSizes);
        foreach (UIElement child in Children)
        {
            if (Spans(child, columns, rows, GridUnit.Star))
            {
                continue;
            }
            child.Measure(new Vector2(Offered(child.GridColumn, child.GridColumnSpan, columns, _columnSizes),
                Offered(child.GridRow, child.GridRowSpan, rows, _rowSizes)));
            Grow(child.GridColumn, child.GridColumnSpan, columns, _columnSizes, child.DesiredSize.X);
            Grow(child.GridRow, child.GridRowSpan, rows, _rowSizes, child.DesiredSize.Y);
        }
        ShareStars(columns, _columnSizes, available.X, ColumnSpacing);
        ShareStars(rows, _rowSizes, available.Y, RowSpacing);
        foreach (UIElement child in Children)
        {
            if (!Spans(child, columns, rows, GridUnit.Star))
            {
                continue;
            }
            // Its star tracks' shares; unbounded along an axis where it's in
            // an auto track (a star column's cell in an auto row).
            child.Measure(new Vector2(
                SpansUnit(child.GridColumn, child.GridColumnSpan, columns, GridUnit.Auto) ? float.PositiveInfinity
                    : Span(child.GridColumn, child.GridColumnSpan, _columnSizes, ColumnSpacing),
                SpansUnit(child.GridRow, child.GridRowSpan, rows, GridUnit.Auto) ? float.PositiveInfinity
                    : Span(child.GridRow, child.GridRowSpan, _rowSizes, RowSpacing)));
            // It grows the auto tracks it's in, and, unbounded, star tracks
            // size to their content like auto ones.
            Grow(child.GridColumn, child.GridColumnSpan, columns, _columnSizes, child.DesiredSize.X);
            Grow(child.GridRow, child.GridRowSpan, rows, _rowSizes, child.DesiredSize.Y);
            if (float.IsInfinity(available.X))
            {
                Grow(child.GridColumn, child.GridColumnSpan, columns, _columnSizes, child.DesiredSize.X, GridUnit.Star);
            }
            if (float.IsInfinity(available.Y))
            {
                Grow(child.GridRow, child.GridRowSpan, rows, _rowSizes, child.DesiredSize.Y, GridUnit.Star);
            }
        }
        // Auto tracks may have grown: share what's left again.
        ShareStars(columns, _columnSizes, available.X, ColumnSpacing);
        ShareStars(rows, _rowSizes, available.Y, RowSpacing);
        return new Vector2(Total(_columnSizes, ColumnSpacing), Total(_rowSizes, RowSpacing));
    }

    protected override void ArrangeCore(RectangleF content)
    {
        List<GridLength> columns = Tracks(Columns), rows = Tracks(Rows);
        if (_columnSizes.Length != columns.Count || _rowSizes.Length != rows.Count)
        {
            return; // definitions changed without DefinitionsChanged
        }
        float[] columnSizes = (float[])_columnSizes.Clone(), rowSizes = (float[])_rowSizes.Clone();
        ShareStars(columns, columnSizes, content.Width, ColumnSpacing);
        ShareStars(rows, rowSizes, content.Height, RowSpacing);
        foreach (UIElement child in Children)
        {
            int column = Math.Clamp(child.GridColumn, 0, columns.Count - 1), row = Math.Clamp(child.GridRow, 0, rows.Count - 1);
            float x = content.X + Span(0, column, columnSizes, ColumnSpacing) + (column > 0 ? ColumnSpacing : 0);
            float y = content.Y + Span(0, row, rowSizes, RowSpacing) + (row > 0 ? RowSpacing : 0);
            child.Arrange(new RectangleF(x, y, Span(column, child.GridColumnSpan, columnSizes, ColumnSpacing),
                Span(row, child.GridRowSpan, rowSizes, RowSpacing)));
        }
    }

    private static List<GridLength> Tracks(List<GridLength> defined) => defined.Count > 0 ? defined : [GridLength.Star()];

    private static void FixedSizes(List<GridLength> tracks, float[] sizes)
    {
        for (int i = 0; i < tracks.Count; ++i)
        {
            sizes[i] = tracks[i].Unit == GridUnit.Pixels ? tracks[i].Value : 0;
        }
    }

    private static bool SpansUnit(int first, int span, List<GridLength> tracks, GridUnit unit)
    {
        for (int i = Math.Max(first, 0); i < Math.Min(first + span, tracks.Count); ++i)
        {
            if (tracks[i].Unit == unit)
            {
                return true;
            }
        }
        return false;
    }

    private static bool Spans(UIElement child, List<GridLength> columns, List<GridLength> rows, GridUnit unit)
    {
        for (int i = Math.Max(child.GridColumn, 0); i < Math.Min(child.GridColumn + child.GridColumnSpan, columns.Count); ++i)
        {
            if (columns[i].Unit == unit)
            {
                return true;
            }
        }
        for (int i = Math.Max(child.GridRow, 0); i < Math.Min(child.GridRow + child.GridRowSpan, rows.Count); ++i)
        {
            if (rows[i].Unit == unit)
            {
                return true;
            }
        }
        return false;
    }

    // What a non-star child is offered: its tracks' sizes, unbounded if one is auto.
    private static float Offered(int first, int span, List<GridLength> tracks, float[] sizes)
    {
        float total = 0;
        for (int i = Math.Max(first, 0); i < Math.Min(first + span, tracks.Count); ++i)
        {
            if (tracks[i].Unit == GridUnit.Auto)
            {
                return float.PositiveInfinity;
            }
            total += sizes[i];
        }
        return total;
    }

    // A single-track child grows its track if the track has `unit` (auto by default).
    private static void Grow(int first, int span, List<GridLength> tracks, float[] sizes, float size,
        GridUnit unit = GridUnit.Auto)
    {
        int index = Math.Clamp(first, 0, tracks.Count - 1);
        if (span == 1 && tracks[index].Unit == unit)
        {
            sizes[index] = MathF.Max(sizes[index], size);
        }
    }

    private static void ShareStars(List<GridLength> tracks, float[] sizes, float available, float spacing)
    {
        if (float.IsInfinity(available))
        {
            return;
        }
        float weights = 0, used = spacing * MathF.Max(tracks.Count - 1, 0);
        for (int i = 0; i < tracks.Count; ++i)
        {
            if (tracks[i].Unit == GridUnit.Star)
            {
                weights += MathF.Max(tracks[i].Value, 0);
            }
            else
            {
                used += sizes[i];
            }
        }
        float left = MathF.Max(available - used, 0);
        for (int i = 0; i < tracks.Count; ++i)
        {
            if (tracks[i].Unit == GridUnit.Star)
            {
                sizes[i] = weights > 0 ? left * MathF.Max(tracks[i].Value, 0) / weights : 0;
            }
        }
    }

    private static float Span(int first, int span, float[] sizes, float spacing)
    {
        float total = 0;
        int count = 0;
        for (int i = Math.Max(first, 0); i < Math.Min(first + span, sizes.Length); ++i)
        {
            total += sizes[i];
            ++count;
        }
        return total + spacing * MathF.Max(count - 1, 0);
    }

    private static float Total(float[] sizes, float spacing) => Span(0, sizes.Length, sizes, spacing);
}

public enum Dock
{
    Left,
    Top,
    Right,
    Bottom,
}

/// <summary>
/// Children docked to a side in turn, each taking its desired size from
/// what's left; the last fills the rest (ADR 0060).
/// </summary>
public sealed class DockPanel : Panel
{
    private bool _lastChildFill = true;

    public bool LastChildFill
    {
        get => _lastChildFill;
        set
        {
            _lastChildFill = value;
            InvalidateArrange();
        }
    }

    public static void SetDock(UIElement element, Dock dock)
    {
        element.DockSide = dock;
        element.Parent?.InvalidateMeasure();
    }

    protected override Vector2 MeasureCore(Vector2 available)
    {
        // WPF's: the space left shrinks as children dock; the size needed
        // grows along each child's side and is at least each child across.
        Vector2 left = available, used = Vector2.Zero, needed = Vector2.Zero;
        foreach (UIElement child in Children)
        {
            child.Measure(Vector2.Max(left, Vector2.Zero));
            Vector2 size = child.DesiredSize;
            if (child.DockSide is Dock.Left or Dock.Right)
            {
                needed.Y = MathF.Max(needed.Y, used.Y + size.Y);
                used.X += size.X;
                left.X -= size.X;
            }
            else
            {
                needed.X = MathF.Max(needed.X, used.X + size.X);
                used.Y += size.Y;
                left.Y -= size.Y;
            }
        }
        return Vector2.Max(needed, used);
    }

    protected override void ArrangeCore(RectangleF content)
    {
        float x0 = content.Left, y0 = content.Top, x1 = content.Right, y1 = content.Bottom;
        for (int i = 0; i < Children.Count; ++i)
        {
            UIElement child = Children[i];
            float width = MathF.Max(x1 - x0, 0), height = MathF.Max(y1 - y0, 0);
            if (_lastChildFill && i == Children.Count - 1)
            {
                child.Arrange(new RectangleF(x0, y0, width, height));
                break;
            }
            Vector2 size = child.DesiredSize;
            switch (child.DockSide)
            {
                case Dock.Left:
                    child.Arrange(new RectangleF(x0, y0, MathF.Min(size.X, width), height));
                    x0 += MathF.Min(size.X, width);
                    break;
                case Dock.Right:
                    child.Arrange(new RectangleF(x1 - MathF.Min(size.X, width), y0, MathF.Min(size.X, width), height));
                    x1 -= MathF.Min(size.X, width);
                    break;
                case Dock.Top:
                    child.Arrange(new RectangleF(x0, y0, width, MathF.Min(size.Y, height)));
                    y0 += MathF.Min(size.Y, height);
                    break;
                case Dock.Bottom:
                    child.Arrange(new RectangleF(x0, y1 - MathF.Min(size.Y, height), width, MathF.Min(size.Y, height)));
                    y1 -= MathF.Min(size.Y, height);
                    break;
            }
        }
    }
}

/// <summary>
/// Where an <see cref="AnchorPanel"/> child goes (Godot's anchors): each edge
/// at a fraction of the panel (<see cref="Min"/> for left and top,
/// <see cref="Max"/> for right and bottom) plus an offset in logical units.
/// Along an axis whose anchors and offsets coincide, the child keeps its
/// desired size and the anchor is its pivot: anchored at 1, its right (or
/// bottom) edge sits there.
/// </summary>
public readonly record struct Anchors(Vector2 Min, Vector2 Max, Vector2 OffsetMin, Vector2 OffsetMax)
{
    public static Anchors Fill => new(Vector2.Zero, Vector2.One, Vector2.Zero, Vector2.Zero);

    public static Anchors TopLeft => At(new Vector2(0, 0));

    public static Anchors Top => At(new Vector2(0.5f, 0));

    public static Anchors TopRight => At(new Vector2(1, 0));

    public static Anchors Left => At(new Vector2(0, 0.5f));

    public static Anchors Center => At(new Vector2(0.5f, 0.5f));

    public static Anchors Right => At(new Vector2(1, 0.5f));

    public static Anchors BottomLeft => At(new Vector2(0, 1));

    public static Anchors Bottom => At(new Vector2(0.5f, 1));

    public static Anchors BottomRight => At(new Vector2(1, 1));

    /// <summary>Pinned at a point of the panel, at its desired size, moved by <paramref name="offset"/>.</summary>
    public static Anchors At(Vector2 point, Vector2 offset = default) => new(point, point, offset, offset);

    /// <summary>The same anchors with every edge moved inward by <paramref name="inset"/>: for stretched anchors, a margin.</summary>
    public Anchors Inset(float inset) =>
        this with { OffsetMin = OffsetMin + new Vector2(inset), OffsetMax = OffsetMax - new Vector2(inset) };
}

/// <summary>Children placed by their <see cref="Anchors"/>, on top of each other (ADR 0060).</summary>
public sealed class AnchorPanel : Panel
{
    public static void SetAnchors(UIElement element, Anchors anchors)
    {
        element.Anchors = anchors;
        element.Parent?.InvalidateArrange();
    }

    protected override Vector2 MeasureCore(Vector2 available)
    {
        Vector2 size = Vector2.Zero;
        foreach (UIElement child in Children)
        {
            Anchors a = child.Anchors;
            // Stretched axes are offered the panel's span; pinned ones are unbounded.
            Vector2 offered = new(
                Pinned(a, 0) ? float.PositiveInfinity : MathF.Max(available.X * (a.Max.X - a.Min.X) + a.OffsetMax.X - a.OffsetMin.X, 0),
                Pinned(a, 1) ? float.PositiveInfinity : MathF.Max(available.Y * (a.Max.Y - a.Min.Y) + a.OffsetMax.Y - a.OffsetMin.Y, 0));
            child.Measure(offered);
            size = Vector2.Max(size, child.DesiredSize + Vector2.Abs(a.OffsetMin));
        }
        return size;
    }

    protected override void ArrangeCore(RectangleF content)
    {
        foreach (UIElement child in Children)
        {
            Anchors a = child.Anchors;
            (float x, float width) = Axis(a, 0, content.X, content.Width, child.DesiredSize.X);
            (float y, float height) = Axis(a, 1, content.Y, content.Height, child.DesiredSize.Y);
            child.Arrange(new RectangleF(x, y, width, height));
        }
    }

    private static bool Pinned(Anchors a, int axis) =>
        Component(a.Min, axis) == Component(a.Max, axis) && Component(a.OffsetMin, axis) == Component(a.OffsetMax, axis);

    private static (float Start, float Size) Axis(Anchors a, int axis, float start, float length, float desired)
    {
        float min = Component(a.Min, axis), max = Component(a.Max, axis);
        float offsetMin = Component(a.OffsetMin, axis), offsetMax = Component(a.OffsetMax, axis);
        if (Pinned(a, axis))
        {
            float pivot = start + min * length + offsetMin;
            return (pivot - desired * min, desired);
        }
        float from = start + min * length + offsetMin, to = start + max * length + offsetMax;
        return (from, MathF.Max(to - from, 0));
    }

    private static float Component(Vector2 v, int axis) => axis == 0 ? v.X : v.Y;
}
