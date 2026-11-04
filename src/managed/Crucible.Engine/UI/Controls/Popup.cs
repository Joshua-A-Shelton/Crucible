using System.Drawing;
using System.Numerics;

namespace Crucible.UI;

/// <summary>
/// Content shown above a root's tree (ADR 0063): in the root's popup layer,
/// placed beside an <see cref="Anchor"/> (below it, flipping above when
/// there isn't room), at least as wide as the anchor. It's a focus scope:
/// opening it focuses its first focusable element, and closing gives focus
/// back. A press outside it, or Cancel inside it, closes it.
/// </summary>
public class Popup : UIElement
{
    private UIElement? _content;

    public Popup() => IsFocusScope = true;

    public UIElement? Content
    {
        get => _content;
        set
        {
            if (_content == value)
            {
                return;
            }
            if (_content is not null)
            {
                RemoveChild(_content);
            }
            _content = value;
            if (value is not null)
            {
                AddChild(value);
            }
        }
    }

    /// <summary>What it opens beside; without one, centered on the root.</summary>
    public UIElement? Anchor { get; set; }

    public bool IsOpen => Owner is not null;

    internal UIRoot? Owner { get; set; }

    /// <summary>After it closes, however it was closed.</summary>
    public event Action<Popup>? Closed;

    /// <summary>Shows it in the root's popup layer (the anchor's root, or <paramref name="root"/>).</summary>
    public void Open(UIRoot? root = null)
    {
        UIRoot target = root ?? Anchor?.Root ?? throw new InvalidOperationException(
            "A popup opens in a root: give it one, or an anchor in a root's tree");
        target.OpenPopup(this);
    }

    public void Close() => Owner?.ClosePopup(this);

    internal void RaiseClosed() => Closed?.Invoke(this);

    protected override void OnCancel(UIEvent e)
    {
        e.Handled = true;
        Close();
    }

    // Below the anchor, or above it if it fits there and not below; kept on the root.
    internal RectangleF Place(Vector2 rootSize)
    {
        Vector2 size = DesiredSize;
        if (Anchor is not { } anchor)
        {
            return new RectangleF((rootSize.X - size.X) * 0.5f, (rootSize.Y - size.Y) * 0.5f, size.X, size.Y);
        }
        RectangleF a = anchor.Bounds;
        size.X = MathF.Max(size.X, a.Width);
        float y = a.Bottom;
        if (y + size.Y > rootSize.Y && a.Top - size.Y >= 0)
        {
            y = a.Top - size.Y;
        }
        float x = Math.Clamp(a.Left, 0, MathF.Max(rootSize.X - size.X, 0));
        return new RectangleF(x, y, size.X, size.Y);
    }
}
