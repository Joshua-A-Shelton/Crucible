using System.Diagnostics.Metrics;

namespace Crucible.UI;

public class UIElement
{
    private UIElement _parent;
    public UIElement Parent
    {
        get { return _parent; }
        set
        {
            if (_parent != null)
            {
                _parent._children.Remove(this);
            }

            _parent = value;
            _parent._children.Add(this);
        }
    }

    private List<UIElement> _children;

    public void AddChild(UIElement child)
    {
        child.Parent = this;
    }

    public void RemoveChild(UIElement child)
    {
        if (_children.Contains(child))
        {
            child._parent = null;
            _children.Remove(child);
        }
    }

    public IReadOnlyList<UIElement> Children
    {
        get { return _children; }
    }

    public Measurement Width;
    public Measurement Height;
    public float MarginTop;
    public float MarginBottom;
    public float MarginLeft;
    public float MarginRight;

    private float _calcedWidth;
    private float _calcedHeight;

    public float CalcWidth()
    {
        if (Width.Unit == Measurement.MeasurementUnit.percent)
        {
            _calcedWidth = _parent._calcedWidth % Width.Amount;
        }
        else
        {
            _calcedWidth = Width.Amount;
        }

        return _calcedWidth;
    }

    public float CalcHeight()
    {
        if (Height.Unit == Measurement.MeasurementUnit.percent)
        {
            _calcedHeight = _parent._calcedHeight % Height.Amount;
        }
        else
        {
            _calcedHeight = Height.Amount;
        }

        return _calcedHeight;
    }
    
}