using System.Drawing;
using System.Numerics;

namespace Crucible.UI;

// Drags (ADR 0071): something picked up in one element and dropped on
// another. The element that starts one gives a payload and, optionally, a
// preview drawn beside the pointer; from then the root routes the pointer to
// drop targets (elements with AllowDrop) instead of the pressed element.
// Leaving the root, its host can carry the drag to another window
// (DragOutside, ContinueDrag, DropAt).
public sealed partial class UIRoot
{
    private static readonly Vector2 PreviewOffset = new(14, 14);

    private object? _dragPayload;
    private UIElement? _dragPreview;
    private UIElement? _dragTarget;
    private bool _dragAccepted;
    private readonly DragEvent _dragEvent = new();

    /// <summary>Whether a drag is under way in this root.</summary>
    public bool IsDragging => _dragPayload is not null;

    /// <summary>What's being dragged here, or null.</summary>
    public object? DragPayload => _dragPayload;

    /// <summary>
    /// The pointer left the root during a drag, at that position (logical
    /// units): a host with several windows can carry the drag on.
    /// </summary>
    internal Action<UIRoot, Vector2>? DragOutside { get; set; }

    /// <summary>
    /// Starts a drag of <paramref name="payload"/>, from a pointer press held
    /// in this root; <paramref name="preview"/> (styled, not hit) follows the
    /// pointer. The pressed element lets go of the pointer: moves now go to
    /// drop targets, and releasing drops on the one that accepted.
    /// </summary>
    public void BeginDrag(object payload, UIElement? preview = null)
    {
        ArgumentNullException.ThrowIfNull(payload);
        CancelDrag();
        if (_capture is not null)
        {
            _capture.State &= ~UIState.Pressed;
            _capture = null;
        }
        _dragPayload = payload;
        SetPreview(preview);
        DragMovedTo(_pointer);
    }

    /// <summary>Ends a drag without dropping (Escape does this too).</summary>
    public void CancelDrag()
    {
        if (_dragPayload is null)
        {
            return;
        }
        SetTarget(null);
        _dragPayload = null;
        SetPreview(null);
    }

    /// <summary>For hosts: a drag from elsewhere (another window) is over this root at <paramref name="position"/>.</summary>
    internal void ContinueDrag(object payload, UIElement? preview, Vector2 position)
    {
        if (_dragPayload != payload)
        {
            CancelDrag();
            _dragPayload = payload;
            SetPreview(preview);
        }
        _pointer = position;
        DragMovedTo(position);
    }

    /// <summary>For hosts: the drag carried here was released at <paramref name="position"/>; false if nothing took it.</summary>
    internal bool DropAt(Vector2 position)
    {
        if (_dragPayload is null)
        {
            return false;
        }
        DragMovedTo(position);
        return FinishDrag(position);
    }

    // During a drag, a move finds the drop target under the pointer.
    private void DragMovedTo(Vector2 position)
    {
        if (_dragPreview is not null)
        {
            InvalidateLayout();
        }
        bool inside = position.X >= 0 && position.Y >= 0 && position.X < _size.X && position.Y < _size.Y;
        if (!inside)
        {
            SetTarget(null);
            DragOutside?.Invoke(this, position);
            return;
        }
        UIElement? target = HitEnabled(position);
        while (target is not null && !target.AllowDrop)
        {
            target = target.Parent;
        }
        SetTarget(target);
        if (_dragTarget is { } over)
        {
            DragEvent e = Drag(over, position);
            over.Raise(UIElement.EventKind.DragOver, e);
            _dragAccepted = e.Accepted;
        }
    }

    private bool FinishDrag(Vector2 position)
    {
        UIElement? target = _dragTarget;
        bool accepted = _dragAccepted;
        object? payload = _dragPayload;
        bool dropped = false;
        if (target is not null && accepted && payload is not null)
        {
            DragEvent e = Drag(target, position);
            e.Accepted = true;
            target.Raise(UIElement.EventKind.Drop, e);
            dropped = true;
            _dragTarget = null; // dropped on, not left
        }
        CancelDrag();
        return dropped;
    }

    private void SetTarget(UIElement? target)
    {
        if (target == _dragTarget)
        {
            return;
        }
        if (_dragTarget is { } left)
        {
            _dragTarget = null;
            left.Raise(UIElement.EventKind.DragLeave, Drag(left, _pointer));
        }
        _dragAccepted = false;
        _dragTarget = target;
        target?.Raise(UIElement.EventKind.DragEnter, Drag(target, _pointer));
    }

    private void SetPreview(UIElement? preview)
    {
        if (_dragPreview is { } old)
        {
            old.Attach(null);
        }
        _dragPreview = preview;
        if (preview is not null)
        {
            preview.IsHitTestVisible = false;
            preview.Attach(this);
            MarkAll(preview);
        }
        InvalidateLayout();
    }

    private DragEvent Drag(UIElement source, Vector2 position)
    {
        _dragEvent.Reset(source);
        _dragEvent.Payload = _dragPayload ?? "";
        _dragEvent.Position = position;
        _dragEvent.Accepted = false;
        return _dragEvent;
    }

    private void LayoutDragPreview()
    {
        if (_dragPreview is { } preview)
        {
            preview.Measure(_size);
            Vector2 at = _pointer + PreviewOffset;
            preview.Arrange(new RectangleF(at.X, at.Y, preview.DesiredSize.X, preview.DesiredSize.Y));
        }
    }

    private void DrawDragPreview() => _dragPreview?.Draw(_canvas);
}
