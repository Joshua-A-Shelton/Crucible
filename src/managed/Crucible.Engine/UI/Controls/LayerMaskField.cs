using System.Drawing;
using System.Numerics;

namespace Crucible.UI;

/// <summary>
/// A layer mask (ADR 0075): 32 bits, one per layer, shown by the names of
/// the layers that are on ("Default, Enemies"), "Nothing", or "Everything".
/// Activated, it opens a list below it: Nothing, Everything, then each named
/// layer with a tick when it's on. Activating one sets or toggles it and
/// leaves the list open, so several can be changed; Cancel or a press
/// outside closes it.
/// </summary>
public class LayerMaskField : ButtonBase
{
    private const uint Everything = 0xFFFFFFFFu;

    private readonly DockPanel _row = new();
    private readonly Label _value = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly ComboBoxArrow _arrow = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly Popup _popup = new();
    private readonly ListBox _list = new();
    private IReadOnlyList<string> _names = [];
    private int[] _shown = [0]; // the layers the list offers, after Nothing and Everything
    private uint _mask;

    public LayerMaskField()
    {
        DockPanel.SetDock(_arrow, Dock.Right);
        _row.Add(_arrow);
        _row.Add(_value);
        Content = _row;
        _popup.Anchor = this;
        _popup.Content = _list;
        _list.Classes.Add("dropdown");
        _list.CreateItem = () => new LayerMaskRow();
        _list.BindItem = (element, index) =>
        {
            LayerMaskRow row = (LayerMaskRow)element;
            row.Text = index switch
            {
                0 => "Nothing",
                1 => "Everything",
                _ => NameOf(_shown[index - 2]),
            };
            row.IsChecked = index switch
            {
                0 => _mask == 0,
                1 => _mask == Everything,
                _ => (_mask & (1u << _shown[index - 2])) != 0,
            };
        };
        _list.ItemActivated += (_, index) => Mask = index switch
        {
            0 => 0,
            1 => Everything,
            _ => _mask ^ (1u << _shown[index - 2]),
        };
        Rebuild();
    }

    /// <summary>
    /// The layers' names, by layer (up to 32). Named layers are offered;
    /// layer 0 always is, as "Default" when it has no name.
    /// </summary>
    public IReadOnlyList<string> LayerNames
    {
        get => _names;
        set
        {
            _names = value ?? [];
            Rebuild();
        }
    }

    public uint Mask
    {
        get => _mask;
        set
        {
            if (value == _mask)
            {
                return;
            }
            _mask = value;
            Show();
            _list.ItemsChanged();
            MaskChanged?.Invoke(this);
        }
    }

    public event Action<LayerMaskField>? MaskChanged;

    public bool IsOpen => _popup.IsOpen;

    public ListBox List => _list;

    /// <summary>A layer's name: its own, "Default" for an unnamed layer 0, else "Layer N".</summary>
    public string NameOf(int layer) =>
        layer < _names.Count && _names[layer].Length > 0 ? _names[layer] : layer == 0 ? "Default" : $"Layer {layer}";

    protected override void OnClicked()
    {
        if (_popup.IsOpen)
        {
            _popup.Close();
            return;
        }
        if (Root is not { } root)
        {
            return;
        }
        _popup.Open(root);
        _list.FocusItem(0);
    }

    protected override void OnDetached(UIRoot root) => _popup.Close();

    private void Rebuild()
    {
        _shown = [.. Enumerable.Range(0, 32).Where(layer => layer == 0 || (layer < _names.Count && _names[layer].Length > 0))];
        _list.ItemCount = 2 + _shown.Length;
        _list.ItemsChanged();
        Show();
    }

    // The layers that are on, by name; a layer that's on but unnamed by its number.
    private void Show() => _value.Text = _mask switch
    {
        0 => "Nothing",
        Everything => "Everything",
        _ => string.Join(", ", Enumerable.Range(0, 32).Where(layer => (_mask & (1u << layer)) != 0).Select(NameOf)),
    };
}

/// <summary>A row of a <see cref="LayerMaskField"/>'s list: a tick when it's on, and its name.</summary>
public sealed class LayerMaskRow : UIElement
{
    private const float CheckColumn = 22;

    private readonly Label _label = new() { VerticalAlignment = VerticalAlignment.Center };
    private bool _isChecked;

    public LayerMaskRow() => AddChild(_label);

    public string Text
    {
        get => _label.Text;
        set => _label.Text = value;
    }

    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (_isChecked != value)
            {
                _isChecked = value;
                InvalidateVisual();
            }
        }
    }

    protected override Vector2 MeasureCore(Vector2 available)
    {
        _label.Measure(new Vector2(float.PositiveInfinity));
        return new Vector2(CheckColumn + _label.DesiredSize.X, _label.DesiredSize.Y);
    }

    protected override void ArrangeCore(RectangleF content) =>
        _label.Arrange(new RectangleF(content.X + CheckColumn, content.Y, MathF.Max(content.Width - CheckColumn, 0),
            content.Height));

    protected override void OnDraw(Canvas canvas)
    {
        if (!_isChecked)
        {
            return;
        }
        // A tick in the check column, as menus draw theirs.
        float s = MathF.Min(CheckColumn, ActualSize.Y) * 0.5f;
        Vector2 c = new(CheckColumn * 0.4f, ActualSize.Y * 0.5f);
        canvas.DrawPolyline([c + new Vector2(-s * 0.5f, 0), c + new Vector2(-s * 0.15f, s * 0.35f), c + new Vector2(s * 0.55f, -s * 0.4f)],
            MathF.Max(s * 0.18f, 1.5f), Foreground);
    }
}
