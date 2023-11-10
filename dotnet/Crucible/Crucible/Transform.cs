namespace Crucible;

[Component]
public struct Transform3D
{
    private bool _needsUpdate = true;
    private Vector3 _position;
    public Vector3 Position
    {
        get { return _position; }
        set 
        { 
            _position = value;
            _needsUpdate = true;
        }
    }

    public void Translate(float x, float y, float z)
    {
        _position.Translate(x,y,z);
        _needsUpdate = true;
    }

    public void Translate(Vector3 translation)
    {
        _position.Translate(translation);
        _needsUpdate = true;
    }
}