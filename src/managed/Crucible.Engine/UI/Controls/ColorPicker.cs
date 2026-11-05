using System.Globalization;
using System.Numerics;

namespace Crucible.UI;

/// <summary>
/// A color's swatch (ADR 0069): a button filled with its color (alpha shown
/// over a checkerboard), which opens a <see cref="ColorPicker"/> below it.
/// The color is linear RGBA, as the renderer uses it; components may go
/// above 1 (HDR), shown clamped.
/// </summary>
public class ColorField : ButtonBase
{
    private readonly Swatch _swatch = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private Vector4 _color = Vector4.One;

    public ColorField()
    {
        Content = _swatch;
        Picker = new ColorPicker { Anchor = this };
        Picker.ColorChanged += picker => SetColor(picker.Color, fromPicker: true);
    }

    /// <summary>Linear RGBA.</summary>
    public Vector4 Color
    {
        get => _color;
        set => SetColor(value, fromPicker: false);
    }

    /// <summary>Whether alpha is edited (a Vector4) or kept at 1 (a Vector3).</summary>
    public bool HasAlpha
    {
        get => Picker.HasAlpha;
        set => Picker.HasAlpha = value;
    }

    /// <summary>The popup it opens.</summary>
    public ColorPicker Picker { get; }

    /// <summary>The color changed in the picker (or was set).</summary>
    public event Action<ColorField>? ColorChanged;

    private void SetColor(Vector4 color, bool fromPicker)
    {
        if (!HasAlpha)
        {
            color.W = 1;
        }
        if (color == _color)
        {
            return;
        }
        _color = color;
        _swatch.Color = color;
        if (!fromPicker)
        {
            Picker.Color = color;
        }
        ColorChanged?.Invoke(this);
    }

    protected override void OnClicked()
    {
        if (Picker.IsOpen)
        {
            Picker.Close();
            return;
        }
        if (Root is { } root)
        {
            Picker.Color = _color;
            Picker.Open(root);
        }
    }

    protected override void OnDetached(UIRoot root) => Picker.Close();

    // The swatch: the color over a checkerboard, so alpha shows.
    private sealed class Swatch : UIElement
    {
        private Vector4 _color = Vector4.One;

        public Swatch()
        {
            IsHitTestVisible = false;
            MinHeight = 16;
            MinWidth = 32;
        }

        public Vector4 Color
        {
            set
            {
                _color = value;
                InvalidateVisual();
            }
        }

        protected override void OnDraw(Canvas canvas)
        {
            Vector2 size = ActualSize;
            if (_color.W < 1)
            {
                ColorPicker.Checkerboard(canvas, Vector2.Zero, size);
            }
            canvas.FillRect(Vector2.Zero, size, ColorPicker.Display(_color), 3);
        }
    }
}

/// <summary>
/// Picks a color (ADR 0069): a square of saturation (across) and value
/// (down) for the hue chosen on the strip beside it, an alpha strip when
/// <see cref="HasAlpha"/>, the color as hex (sRGB, as images and web colors
/// are written), and its linear components as numbers, which may go above 1.
/// Dragging in the square and strips changes it as the pointer moves.
/// </summary>
public class ColorPicker : Popup
{
    private const float AreaSize = 160, StripWidth = 18;

    private readonly Area _area;
    private readonly Strip _hue, _alpha;
    private readonly TextField _hex = new() { MinWidth = 96 };
    private readonly NumberField[] _components = new NumberField[4];
    private readonly StackPanel _numbers = new() { Orientation = Orientation.Horizontal, Spacing = 4 };
    private Vector4 _color = Vector4.One;
    private float _h, _s, _v = 1; // of the sRGB color; kept so hue survives grays
    private bool _hasAlpha;
    private bool _updating;

    public ColorPicker()
    {
        _area = new Area(this);
        _hue = new Strip(this, hue: true);
        _alpha = new Strip(this, hue: false) { Visibility = Visibility.Collapsed };
        StackPanel squares = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
        squares.Add(_area);
        squares.Add(_hue);
        squares.Add(_alpha);
        _hex.Committed += _ => HexCommitted();
        string[] names = ["R", "G", "B", "A"];
        for (int i = 0; i < 4; ++i)
        {
            int component = i;
            NumberField field = new() { Step = 0.01, Minimum = 0, MinWidth = 56 };
            if (i == 3)
            {
                field.Maximum = 1;
                field.Visibility = Visibility.Collapsed;
            }
            field.ValueChanged += changed => ComponentChanged(component, (float)changed.Value);
            field.Classes.Add("color-" + names[i].ToLowerInvariant());
            _components[i] = field;
            _numbers.Add(field);
        }
        StackPanel hexRow = new() { Orientation = Orientation.Horizontal, Spacing = 6 };
        hexRow.Add(new Label("Hex") { VerticalAlignment = VerticalAlignment.Center });
        hexRow.Add(_hex);
        StackPanel body = new() { Spacing = 8 };
        body.Classes.Add("color-picker");
        body.Add(squares);
        body.Add(hexRow);
        body.Add(_numbers);
        Content = body;
        Show();
    }

    /// <summary>Linear RGBA.</summary>
    public Vector4 Color
    {
        get => _color;
        set
        {
            if (!_hasAlpha)
            {
                value.W = 1;
            }
            if (value == _color)
            {
                return;
            }
            _color = value;
            FromColor();
            Show();
        }
    }

    public bool HasAlpha
    {
        get => _hasAlpha;
        set
        {
            _hasAlpha = value;
            _alpha.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
            _components[3].Visibility = value ? Visibility.Visible : Visibility.Collapsed;
            if (!value && _color.W != 1)
            {
                Change(_color with { W = 1 });
            }
            Show();
        }
    }

    /// <summary>The hue, saturation, and value shown (0 to 1), of the color in sRGB.</summary>
    public Vector3 Hsv => new(_h, _s, _v);

    public event Action<ColorPicker>? ColorChanged;

    // The square and strips, from pointer positions 0 to 1 across their size.
    internal void PickArea(Vector2 at)
    {
        _s = Math.Clamp(at.X, 0, 1);
        _v = 1 - Math.Clamp(at.Y, 0, 1);
        ChangeFromHsv();
    }

    internal void PickHue(float at)
    {
        _h = Math.Clamp(at, 0, 1);
        ChangeFromHsv();
    }

    internal void PickAlpha(float at) => Change(_color with { W = 1 - Math.Clamp(at, 0, 1) });

    private void ChangeFromHsv()
    {
        Vector3 srgb = FromHsv(_h, _s, _v);
        Change(new Vector4(ToLinear(srgb.X), ToLinear(srgb.Y), ToLinear(srgb.Z), _color.W));
    }

    private void Change(Vector4 color)
    {
        if (!_hasAlpha)
        {
            color.W = 1;
        }
        if (color == _color)
        {
            return;
        }
        _color = color;
        Show();
        ColorChanged?.Invoke(this);
    }

    private void ComponentChanged(int component, float value)
    {
        if (_updating)
        {
            return;
        }
        Vector4 color = _color;
        color[component] = value;
        Change(color);
        FromColor();
        Show();
    }

    private void HexCommitted()
    {
        if (TryParseHex(_hex.Text, out Vector4 srgba))
        {
            Change(new Vector4(ToLinear(srgba.X), ToLinear(srgba.Y), ToLinear(srgba.Z), _hasAlpha ? srgba.W : 1));
            FromColor();
        }
        Show();
    }

    // HSV from the color, keeping the hue (and saturation) where the color doesn't say.
    private void FromColor()
    {
        Vector3 srgb = new(ToSrgb(_color.X), ToSrgb(_color.Y), ToSrgb(_color.Z));
        (float h, float s, float v) = ToHsv(Vector3.Clamp(srgb, Vector3.Zero, Vector3.One));
        if (v > 0 && s > 0)
        {
            _h = h;
        }
        if (v > 0)
        {
            _s = s;
        }
        _v = v;
    }

    // The fields and drawing follow the color.
    private void Show()
    {
        _updating = true;
        _hex.Text = Hex(_color, _hasAlpha);
        for (int i = 0; i < 4; ++i)
        {
            if (!_components[i].IsFocused)
            {
                _components[i].Value = _color[i];
            }
        }
        _updating = false;
        _area.InvalidateVisual();
        _hue.InvalidateVisual();
        _alpha.InvalidateVisual();
    }

    // -------------------------------------------------------------------
    // Color math: sRGB transfer, HSV, hex
    // -------------------------------------------------------------------

    public static float ToLinear(float srgb) =>
        srgb <= 0.04045f ? srgb / 12.92f : MathF.Pow((srgb + 0.055f) / 1.055f, 2.4f);

    public static float ToSrgb(float linear) =>
        linear <= 0.0031308f ? linear * 12.92f : 1.055f * MathF.Pow(linear, 1 / 2.4f) - 0.055f;

    // What the canvas draws for a linear color (its colors are sRGB), clamped.
    internal static Vector4 Display(Vector4 linear) => new(Math.Clamp(ToSrgb(linear.X), 0, 1),
        Math.Clamp(ToSrgb(linear.Y), 0, 1), Math.Clamp(ToSrgb(linear.Z), 0, 1), Math.Clamp(linear.W, 0, 1));

    internal static (float H, float S, float V) ToHsv(Vector3 rgb)
    {
        float max = MathF.Max(rgb.X, MathF.Max(rgb.Y, rgb.Z)), min = MathF.Min(rgb.X, MathF.Min(rgb.Y, rgb.Z));
        float delta = max - min;
        float h = 0;
        if (delta > 0)
        {
            h = max == rgb.X ? (rgb.Y - rgb.Z) / delta
                : max == rgb.Y ? 2 + (rgb.Z - rgb.X) / delta
                : 4 + (rgb.X - rgb.Y) / delta;
            h = (h / 6 + 1) % 1;
        }
        return (h, max > 0 ? delta / max : 0, max);
    }

    internal static Vector3 FromHsv(float h, float s, float v)
    {
        float sector = h * 6 % 6;
        float c = v * s, x = c * (1 - MathF.Abs(sector % 2 - 1)), m = v - c;
        Vector3 rgb = (int)sector switch
        {
            0 => new(c, x, 0),
            1 => new(x, c, 0),
            2 => new(0, c, x),
            3 => new(0, x, c),
            4 => new(x, 0, c),
            _ => new(c, 0, x),
        };
        return rgb + new Vector3(m);
    }

    /// <summary>"RRGGBB" (or "RRGGBBAA"), sRGB, clamped.</summary>
    public static string Hex(Vector4 linear, bool alpha)
    {
        Vector4 shown = Display(linear);
        static int Byte(float value) => (int)MathF.Round(value * 255);
        string hex = $"{Byte(shown.X):X2}{Byte(shown.Y):X2}{Byte(shown.Z):X2}";
        return alpha ? hex + $"{Byte(shown.W):X2}" : hex;
    }

    /// <summary>Reads "RRGGBB" or "RRGGBBAA" (a leading '#' allowed) as sRGB components 0 to 1.</summary>
    public static bool TryParseHex(string text, out Vector4 srgba)
    {
        srgba = Vector4.One;
        string hex = text.Trim().TrimStart('#');
        if (hex.Length is not (6 or 8) || !uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint value))
        {
            return false;
        }
        if (hex.Length == 6)
        {
            value = (value << 8) | 0xff;
        }
        srgba = new Vector4(value >> 24, (value >> 16) & 0xff, (value >> 8) & 0xff, value & 0xff) / 255f;
        return true;
    }

    internal static void Checkerboard(Canvas canvas, Vector2 at, Vector2 size)
    {
        const float Cell = 6;
        canvas.FillRect(at, size, new Vector4(0.8f, 0.8f, 0.8f, 1), 3);
        for (float y = 0; y < size.Y; y += Cell)
        {
            for (float x = ((int)(y / Cell) % 2) * Cell; x < size.X; x += Cell * 2)
            {
                canvas.FillRect(at + new Vector2(x, y), Vector2.Min(new Vector2(Cell), size - new Vector2(x, y)),
                    new Vector4(0.55f, 0.55f, 0.55f, 1));
            }
        }
    }

    // Pointer positions over an element as 0 to 1 across it.
    private static Vector2 Fraction(UIElement element, Vector2 position) =>
        new((position.X - element.Bounds.X) / MathF.Max(element.Bounds.Width, 1),
            (position.Y - element.Bounds.Y) / MathF.Max(element.Bounds.Height, 1));

    // The saturation-value square: white to the hue across, black down.
    private sealed class Area(ColorPicker picker) : UIElement
    {
        private bool _dragging;

        protected override Vector2 MeasureCore(Vector2 available) => new(AreaSize);

        protected override void OnPointerDown(PointerEvent e)
        {
            if (e.Button == PointerButton.Left)
            {
                e.Handled = true;
                _dragging = true;
                picker.PickArea(Fraction(this, e.Position));
            }
        }

        protected override void OnPointerMove(PointerEvent e)
        {
            if (_dragging)
            {
                e.Handled = true;
                picker.PickArea(Fraction(this, e.Position));
            }
        }

        protected override void OnPointerUp(PointerEvent e) => _dragging = false;

        protected override void OnDraw(Canvas canvas)
        {
            Vector2 size = ActualSize;
            Vector3 hue = FromHsv(picker._h, 1, 1);
            canvas.FillRect(Vector2.Zero, size,
                Gradient.Linear(Vector2.Zero, new Vector2(size.X, 0), Vector4.One, new Vector4(hue, 1)));
            canvas.FillRect(Vector2.Zero, size,
                Gradient.Linear(Vector2.Zero, new Vector2(0, size.Y), Vector4.Zero, new Vector4(0, 0, 0, 1)));
            Vector2 mark = new(picker._s * size.X, (1 - picker._v) * size.Y);
            canvas.StrokeRect(mark - new Vector2(5), new Vector2(10), new Vector4(1), 1.5f, new Vector4(5));
            canvas.StrokeRect(mark - new Vector2(6), new Vector2(12), new Vector4(0, 0, 0, 0.6f), 1, new Vector4(6));
        }
    }

    // The hue strip (red at the top, round the wheel), or the alpha strip (opaque at the top).
    private sealed class Strip(ColorPicker picker, bool hue) : UIElement
    {
        private bool _dragging;

        protected override Vector2 MeasureCore(Vector2 available) => new(StripWidth, AreaSize);

        protected override void OnPointerDown(PointerEvent e)
        {
            if (e.Button == PointerButton.Left)
            {
                e.Handled = true;
                _dragging = true;
                Pick(e.Position);
            }
        }

        protected override void OnPointerMove(PointerEvent e)
        {
            if (_dragging)
            {
                e.Handled = true;
                Pick(e.Position);
            }
        }

        protected override void OnPointerUp(PointerEvent e) => _dragging = false;

        private void Pick(Vector2 position)
        {
            float at = Fraction(this, position).Y;
            if (hue)
            {
                picker.PickHue(at);
            }
            else
            {
                picker.PickAlpha(at);
            }
        }

        protected override void OnDraw(Canvas canvas)
        {
            Vector2 size = ActualSize;
            float at;
            if (hue)
            {
                float segment = size.Y / 6;
                for (int i = 0; i < 6; ++i)
                {
                    canvas.FillRect(new Vector2(0, i * segment), new Vector2(size.X, segment + 0.5f),
                        Gradient.Linear(Vector2.Zero, new Vector2(0, segment), new Vector4(FromHsv(i / 6f, 1, 1), 1),
                            new Vector4(FromHsv((i + 1) / 6f, 1, 1), 1)));
                }
                at = picker._h * size.Y;
            }
            else
            {
                Checkerboard(canvas, Vector2.Zero, size);
                Vector4 shown = Display(picker._color);
                canvas.FillRect(Vector2.Zero, size,
                    Gradient.Linear(Vector2.Zero, new Vector2(0, size.Y), shown with { W = 1 }, shown with { W = 0 }));
                at = (1 - picker._color.W) * size.Y;
            }
            canvas.StrokeRect(new Vector2(-2, at - 3), new Vector2(size.X + 4, 6), new Vector4(1), 1.5f, new Vector4(2));
        }
    }
}
