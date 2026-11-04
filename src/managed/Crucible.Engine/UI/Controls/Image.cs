using System.Numerics;
using Crucible.Render;

namespace Crucible.UI;

/// <summary>How an image fills the space it's given.</summary>
public enum Stretch
{
    None,          // its own size (a texel per logical unit), centered
    Fill,          // the whole space, distorted if need be
    Uniform,       // as large as fits, keeping its shape
    UniformToFill, // covering the space, keeping its shape, cropped
}

/// <summary>
/// A texture (ADR 0063), tinted, stretched by <see cref="Stretch"/>, or as
/// a nine-slice when <see cref="SliceInsets"/> are set: the corners keep
/// their size and the edges and middle stretch.
/// </summary>
public class Image : UIElement
{
    private TextureHandle _texture;
    private Stretch _stretch = Stretch.Uniform;
    private Thickness _sliceInsets, _sourceSlice;
    private Vector4 _tint = Vector4.One;

    public TextureHandle Texture
    {
        get => _texture;
        set
        {
            _texture = value;
            InvalidateMeasure();
        }
    }

    public Stretch Stretch
    {
        get => _stretch;
        set
        {
            _stretch = value;
            InvalidateMeasure();
        }
    }

    /// <summary>A nine-slice's borders where it's drawn, in logical units; none draws it whole.</summary>
    public Thickness SliceInsets
    {
        get => _sliceInsets;
        set
        {
            _sliceInsets = value;
            InvalidateVisual();
        }
    }

    /// <summary>The same borders in the texture, in texels; none uses <see cref="SliceInsets"/>.</summary>
    public Thickness SourceSlice
    {
        get => _sourceSlice;
        set
        {
            _sourceSlice = value;
            InvalidateVisual();
        }
    }

    /// <summary>Multiplies the texture (sRGB, straight alpha).</summary>
    public Vector4 Tint
    {
        get => _tint;
        set
        {
            _tint = value;
            InvalidateVisual();
        }
    }

    /// <summary>The texture's size in texels, a logical unit each; zero without one.</summary>
    public Vector2 NaturalSize
    {
        get
        {
            TextureSize size = Render.Api.TextureSize(_texture);
            return new Vector2(size.Width, size.Height);
        }
    }

    protected override Vector2 MeasureCore(Vector2 available) => Fit(_stretch, NaturalSize, available).Size;

    protected override void OnDraw(Canvas canvas)
    {
        Vector2 natural = NaturalSize;
        if (natural.X <= 0 || natural.Y <= 0)
        {
            return;
        }
        Vector2 space = ActualSize;
        if (_sliceInsets != default)
        {
            Thickness source = _sourceSlice == default ? _sliceInsets : _sourceSlice;
            canvas.DrawNineSlice(_texture, Vector2.Zero, space,
                new Vector4(_sliceInsets.Left, _sliceInsets.Top, _sliceInsets.Right, _sliceInsets.Bottom),
                new Vector4(source.Left / natural.X, source.Top / natural.Y, source.Right / natural.X,
                    source.Bottom / natural.Y), _tint);
            return;
        }
        (Vector2 size, Vector2 offset) = Fit(_stretch, natural, space);
        bool crop = size.X > space.X + 0.01f || size.Y > space.Y + 0.01f;
        if (crop)
        {
            canvas.PushClip(Vector2.Zero, space);
        }
        canvas.DrawImage(_texture, offset, size, new Vector4(0, 0, 1, 1), _tint, CornerRadii);
        if (crop)
        {
            canvas.PopClip();
        }
    }

    /// <summary>The size an image of <paramref name="natural"/> size takes in <paramref name="space"/>, and where, centered.</summary>
    public static (Vector2 Size, Vector2 Offset) Fit(Stretch stretch, Vector2 natural, Vector2 space)
    {
        if (natural.X <= 0 || natural.Y <= 0)
        {
            return (Vector2.Zero, Vector2.Zero);
        }
        bool bounded = float.IsFinite(space.X) && float.IsFinite(space.Y);
        Vector2 size = stretch switch
        {
            Stretch.Fill when bounded => space,
            Stretch.Uniform when float.IsFinite(space.X) || float.IsFinite(space.Y) =>
                natural * MathF.Min(Ratio(space.X, natural.X), Ratio(space.Y, natural.Y)),
            Stretch.UniformToFill when bounded => natural * MathF.Max(space.X / natural.X, space.Y / natural.Y),
            _ => natural,
        };
        Vector2 offset = bounded ? (space - size) * 0.5f : Vector2.Zero;
        return (size, offset);

        static float Ratio(float space, float natural) => float.IsFinite(space) ? space / natural : float.PositiveInfinity;
    }
}
