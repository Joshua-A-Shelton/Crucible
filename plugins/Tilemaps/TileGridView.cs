using System.Numerics;
using Crucible.Assets.Importers;
using Crucible.Editor;
using Crucible.Render;
using Crucible.UI;

namespace Crucible.Tilemaps.Editor;

/// <summary>
/// A tileset's texture (ADR 0108, 0112) for the Palette and New Tileset: zoomed by whole
/// numbers with square pixels (the wheel zooms, the middle or right button
/// pans), its tile grid drawn, a rectangle of tiles selected by dragging.
/// With <see cref="Drawing"/> set, a left click gives the texel instead (the
/// Palette's Custom shape).
/// </summary>
internal sealed class TileGridView : UIElement
{
    private static readonly int[] _zooms = [1, 2, 3, 4, 6, 8, 12, 16];
    private static readonly Vector4 _grid = new(1, 1, 1, 0.25f);

    private Vector2 _pan;
    private bool _fit = true;
    private Vector2 _last;
    private bool _panning;
    private (int X, int Y)? _dragFrom;
    private bool _dragged;

    public TileGridView()
    {
        IsFocusable = true;
        ClipToBounds = true;
    }

    public TextureHandle Texture { get; set; }

    public TilesetData? Tileset { get; set; }

    /// <summary>The selected tiles, by column and row, corners inclusive; null for none.</summary>
    public (int X0, int Y0, int X1, int Y1)? Selection { get; set; }

    /// <summary>The tile the selection was started on (a drag's first tile, ADR 0124): the brush is held there.</summary>
    public (int X, int Y)? SelectionFrom { get; private set; }

    /// <summary>A left click gives its texel to <see cref="TexelClicked"/> instead of selecting.</summary>
    public bool Drawing { get; set; }

    public event Action<TileGridView>? SelectionChanged;

    public event Action<Vector2>? TexelClicked;

    /// <summary>A tile pressed and let go without dragging to another: by column and row.</summary>
    public event Action<(int X, int Y)>? TileClicked;

    /// <summary>Drawn over the texture: the canvas, and a texel's place in the view.</summary>
    public Action<Canvas, Func<Vector2, Vector2>>? Overlay { get; set; }

    /// <summary>Screen units a texel.</summary>
    public int Zoom { get; private set; } = 2;

    public (int Width, int Height) TextureSize
    {
        get
        {
            TextureSize size = Texture.Generation != 0 ? Render.Api.TextureSize(Texture) : default;
            return ((int)size.Width, (int)size.Height);
        }
    }

    /// <summary>Tiles across and down; zero until the texture loads.</summary>
    public (int Columns, int Rows) Grid
    {
        get
        {
            (int width, int height) = TextureSize;
            return Tileset is { } tileset && width > 0 ? tileset.Grid(width, height) : (0, 0);
        }
    }

    public void FitNextDraw() => _fit = true;

    private Vector2 Origin(Vector2 size)
    {
        (int width, int height) = TextureSize;
        Vector2 at = (size - new Vector2(width, height) * Zoom) * 0.5f + _pan;
        return new Vector2(MathF.Round(at.X), MathF.Round(at.Y));
    }

    private Vector2 TexelAt(PointerEvent e) => (e.Position - new Vector2(Bounds.X, Bounds.Y) - Origin(ActualSize)) / Zoom;

    // The tile under a texel, by column and row; null off the grid.
    private (int X, int Y)? TileAt(Vector2 texel)
    {
        if (Tileset is not { } tileset)
        {
            return null;
        }
        (int columns, int rows) = Grid;
        int x = (int)MathF.Floor((texel.X - tileset.Margin) / (tileset.TileWidth + tileset.Spacing));
        int y = (int)MathF.Floor((texel.Y - tileset.Margin) / (tileset.TileHeight + tileset.Spacing));
        return x >= 0 && y >= 0 && x < columns && y < rows ? (x, y) : null;
    }

    protected override void OnDraw(Canvas canvas)
    {
        Vector2 size = ActualSize;
        Theme theme = Root!.Theme!; // the editor's (ADR 0129)
        canvas.FillRect(Vector2.Zero, size, theme.Color("field"));
        (int width, int height) = TextureSize;
        if (width <= 0)
        {
            return;
        }
        if (_fit)
        {
            _fit = false;
            _pan = Vector2.Zero;
            Zoom = _zooms.LastOrDefault(z => width * z <= size.X - 16 && height * z <= size.Y - 16, 1);
        }
        Vector2 origin = Origin(size), extent = new Vector2(width, height) * Zoom;
        const float cell = 8;
        canvas.PushClip(origin, extent);
        for (float y = 0; y < extent.Y; y += cell)
        {
            for (float x = ((int)(y / cell) % 2) * cell; x < extent.X; x += cell * 2)
            {
                canvas.FillRect(origin + new Vector2(x, y), new Vector2(cell), theme.Color("panel"));
            }
        }
        canvas.PopClip();
        canvas.DrawImage(Texture, origin, extent, new Vector4(0, 0, 1, 1), Vector4.One, default, pixelated: true);
        if (Tileset is { } tileset)
        {
            (int columns, int rows) = Grid;
            Vector2 tile = new(tileset.TileWidth * Zoom, tileset.TileHeight * Zoom);
            for (int y = 0; y < rows; ++y)
            {
                for (int x = 0; x < columns; ++x)
                {
                    (int px, int py) = tileset.PixelOf(y * columns + x, columns);
                    canvas.StrokeRect(origin + new Vector2(px, py) * Zoom, tile, _grid, 1);
                }
            }
            if (Selection is { } s)
            {
                (int ax, int ay) = tileset.PixelOf(s.Y0 * Math.Max(columns, 1) + s.X0, Math.Max(columns, 1));
                (int bx, int by) = tileset.PixelOf(s.Y1 * Math.Max(columns, 1) + s.X1, Math.Max(columns, 1));
                Vector2 from = origin + new Vector2(ax, ay) * Zoom;
                Vector2 to = origin + new Vector2(bx, by) * Zoom + tile;
                canvas.StrokeRect(from, to - from, theme.Color("accent"), 2);
            }
        }
        Overlay?.Invoke(canvas, texel => origin + texel * Zoom);
    }

    protected override void OnPointerDown(PointerEvent e)
    {
        Focus();
        e.Handled = true;
        _last = e.Position;
        if (e.Button is PointerButton.Middle or PointerButton.Right)
        {
            _panning = true;
            return;
        }
        if (e.Button != PointerButton.Left)
        {
            return;
        }
        Vector2 texel = TexelAt(e);
        if (Drawing)
        {
            TexelClicked?.Invoke(texel);
            InvalidateVisual();
            return;
        }
        if (TileAt(texel) is { } tile)
        {
            _dragFrom = tile;
            _dragged = false;
            Select(tile, tile);
        }
    }

    protected override void OnPointerMove(PointerEvent e)
    {
        if (_panning)
        {
            _pan += e.Position - _last;
            _last = e.Position;
            InvalidateVisual();
            e.Handled = true;
        }
        else if (_dragFrom is { } from && TileAt(TexelAt(e)) is { } to)
        {
            _dragged |= to != from;
            Select(from, to);
            e.Handled = true;
        }
    }

    protected override void OnPointerUp(PointerEvent e)
    {
        if (_dragFrom is { } tile && !_dragged && e.Button == PointerButton.Left)
        {
            TileClicked?.Invoke(tile);
        }
        _panning = false;
        _dragFrom = null;
    }

    /// <summary>A click on a tile, as the pointer gives it (the checks): selected, then clicked.</summary>
    public void Click(int x, int y)
    {
        Select((x, y), (x, y));
        TileClicked?.Invoke((x, y));
    }

    private void Select((int X, int Y) a, (int X, int Y) b)
    {
        (int X0, int Y0, int X1, int Y1) selection = (Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
        SelectionFrom = a;
        if (Selection != selection)
        {
            Selection = selection;
            SelectionChanged?.Invoke(this);
        }
        InvalidateVisual();
    }

    protected override void OnPointerWheel(PointerEvent e)
    {
        int at = Array.IndexOf(_zooms, Zoom);
        int next = Math.Clamp(at + (e.Wheel.Y > 0 ? 1 : e.Wheel.Y < 0 ? -1 : 0), 0, _zooms.Length - 1);
        if (next != at)
        {
            Vector2 local = e.Position - new Vector2(Bounds.X, Bounds.Y);
            Vector2 texel = (local - Origin(ActualSize)) / Zoom;
            Zoom = _zooms[next];
            _pan += local - (Origin(ActualSize) + texel * Zoom);
            InvalidateVisual();
        }
        e.Handled = true;
    }
}
