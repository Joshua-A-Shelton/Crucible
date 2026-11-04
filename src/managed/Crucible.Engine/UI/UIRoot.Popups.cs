using System.Drawing;
using System.Numerics;

namespace Crucible.UI;

// The popup layer (ADR 0063): popups laid out, drawn, and hit-tested above
// the content, newest on top.
public sealed partial class UIRoot
{
    private readonly List<Popup> _popups = [];
    private readonly Dictionary<Popup, UIElement?> _focusBefore = [];

    /// <summary>The open popups, oldest first.</summary>
    public IReadOnlyList<Popup> Popups => _popups;

    internal void OpenPopup(Popup popup)
    {
        if (popup.Owner == this)
        {
            return;
        }
        if (popup.Owner is not null || popup.Parent is not null || popup.Root is not null)
        {
            throw new InvalidOperationException("The popup is already in a tree");
        }
        _focusBefore[popup] = _focused;
        _popups.Add(popup);
        popup.Owner = this;
        popup.Attach(this);
        MarkAll(popup);
        InvalidateLayout();
        // Laid out now, so focus can go inside.
        UpdateLayout(_size * _scale);
        if (!MoveFocusInto(popup))
        {
            ClearFocus();
        }
    }

    internal void ClosePopup(Popup popup)
    {
        int at = _popups.IndexOf(popup);
        if (at < 0)
        {
            return;
        }
        // Popups opened from it close with it.
        for (int i = _popups.Count - 1; i > at; --i)
        {
            ClosePopup(_popups[i]);
        }
        _popups.Remove(popup);
        Detached(popup);
        popup.Attach(null);
        popup.Owner = null;
        InvalidateLayout();
        if (_focusBefore.Remove(popup, out UIElement? before) && before is not null && before.Root == this)
        {
            SetFocus(before);
        }
        popup.RaiseClosed();
    }

    // Lays out the open popups after the content.
    private void LayoutPopups()
    {
        foreach (Popup popup in _popups)
        {
            popup.Measure(_size);
            popup.Arrange(popup.Place(_size));
        }
    }

    private void DrawPopups()
    {
        foreach (Popup popup in _popups)
        {
            popup.Draw(_canvas);
        }
    }

    // The newest popup under the point first, then the content.
    private UIElement? HitLayers(Vector2 point)
    {
        for (int i = _popups.Count - 1; i >= 0; --i)
        {
            if (_popups[i].HitTest(point) is { } hit)
            {
                return hit;
            }
        }
        return _content?.HitTest(point);
    }

    // A press outside the newest popup closes it, and is used up doing so.
    private bool DismissPopups(Vector2 point)
    {
        if (_popups.Count == 0)
        {
            return false;
        }
        Popup top = _popups[^1];
        if (top.Bounds.Contains(point.X, point.Y))
        {
            return false;
        }
        ClosePopup(top);
        return true;
    }

    private bool MoveFocusInto(UIElement scope)
    {
        List<UIElement> candidates = [];
        Collect(scope, candidates);
        return candidates.Count > 0 && SetFocus(TabOrder(candidates)[0]);
    }
}
