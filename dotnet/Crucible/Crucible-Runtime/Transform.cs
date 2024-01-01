namespace Crucible;

[Component]
public struct Transform3D
{
    private bool _needsUpdate = true;
    private Vector3 _position;
    [Expose]
    public Vector3 Position
    {
        get { return _position; }
        set 
        { 
            _position = value;
            _needsUpdate = true;
        }
    }

    private Quaternion _rotation;
    [Expose]
    public Quaternion Rotation
    {
        get { return _rotation;}
        set
        {
            _rotation = value;
            _needsUpdate = true;
        }
    }

    private Vector3 _scale;
    [Expose]
    public Vector3 Scale
    {
        get { return _scale; }
        set
        {
            _scale = value;
            _needsUpdate = true;
        }
    }

    private Matrix4x4 _transformMatrix;

    public Matrix4x4 Matrix
    {
        get
        {
            if (_needsUpdate)
            {
                _transformMatrix = Matrix4x4.FromTRS(_position, _rotation, _scale);
                _needsUpdate = false;
            }

            return _transformMatrix;
        }
    }

    public Transform3D()
    {
        _position = new Vector3();
        _rotation = new Quaternion();
        _scale = new Vector3(1, 1, 1);
        _transformMatrix = new Matrix4x4();
        _needsUpdate = false;
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