using System.Drawing;
using System.Numerics;

namespace Crucible.UI;

// Tooltips (ADR 0124): an element's ToolTip text, shown near the pointer
// once it has rested on the element for ToolTipDelay, in a layer of its own
// above the popups. It takes no input and no focus: a press, a key, the
// wheel, or the pointer leaving the element hides it, and it doesn't come
// back on the same element until the pointer has left it.
public sealed partial class UIRoot
{
    // Below and right of the pointer, clear of its arrow.
    private static readonly Vector2 _toolTipOffset = new(12, 20);

    private UIElement? _toolTipTarget;
    private double _toolTipSince;
    private bool _toolTipSuppressed;
    private ToolTipView? _toolTip;

    /// <summary>How long the pointer rests on an element before its tooltip shows, in seconds.</summary>
    public double ToolTipDelay { get; set; } = 0.5;

    /// <summary>The tooltip shown now (its text), or null; for tests.</summary>
    public string? ShownToolTip => _toolTip?.Text;

    // After hover moves: the nearest element under the pointer with a tooltip.
    private void TrackToolTip(UIElement? hit)
    {
        UIElement? target = hit;
        while (target is not null && target.ToolTip.Length == 0)
        {
            target = target.Parent;
        }
        if (target == _toolTipTarget)
        {
            return;
        }
        HideToolTip();
        _toolTipTarget = target;
        _toolTipSince = Now;
        _toolTipSuppressed = false;
    }

    // A press, a key, or the wheel: hidden until the pointer leaves the element.
    private void DismissToolTip()
    {
        HideToolTip();
        _toolTipSuppressed = true;
    }

    private void HideToolTip()
    {
        if (_toolTip is { } shown)
        {
            _toolTip = null;
            Detached(shown);
            shown.Attach(null);
            InvalidateLayout();
        }
    }

    // Each frame (UpdateStyles): shown once the pointer has rested long
    // enough, its text following the element's, gone with the element.
    private void TickToolTip(double now)
    {
        if (_toolTipTarget is { } target && (target.Root != this || target.ToolTip.Length == 0))
        {
            HideToolTip();
            _toolTipTarget = null;
            return;
        }
        if (_toolTipTarget is null || _toolTipSuppressed || _dragPayload is not null)
        {
            return;
        }
        if (_toolTip is { } shown)
        {
            if (shown.Text != _toolTipTarget.ToolTip)
            {
                shown.Text = _toolTipTarget.ToolTip;
                InvalidateLayout();
            }
            return;
        }
        if (now - _toolTipSince < ToolTipDelay)
        {
            return;
        }
        ToolTipView view = new(_toolTipTarget.ToolTip, _pointer + _toolTipOffset);
        _toolTip = view;
        view.Attach(this);
        MarkAll(view);
        InvalidateLayout();
    }

    // Waiting to show one: the host keeps drawing frames until it does.
    private bool ToolTipPending => _toolTipTarget is not null && !_toolTipSuppressed && _toolTip is null;

    private void LayoutToolTip()
    {
        if (_toolTip is { } view)
        {
            view.Measure(_size);
            Vector2 size = view.DesiredSize;
            float x = Math.Clamp(view.At.X, 0, MathF.Max(_size.X - size.X, 0));
            float y = view.At.Y + size.Y > _size.Y ? MathF.Max(view.At.Y - _toolTipOffset.Y * 2 - size.Y, 0) : view.At.Y;
            view.Arrange(new RectangleF(x, y, size.X, size.Y));
        }
    }

    private void DrawToolTip() => _toolTip?.Draw(_canvas);

    // The tooltip itself: its text in a box styled by the theme's "tooltip"
    // class. It's never hit: it isn't in the layers hit testing looks at.
    private sealed class ToolTipView : UIElement
    {
        private readonly Label _label;

        public ToolTipView(string text, Vector2 at)
        {
            At = at;
            Classes.Add("tooltip");
            _label = new Label(text);
            _label.Classes.Add("tooltip-text");
            AddChild(_label);
        }

        public Vector2 At { get; }

        public string Text
        {
            get => _label.Text;
            set => _label.Text = value;
        }
    }
}
