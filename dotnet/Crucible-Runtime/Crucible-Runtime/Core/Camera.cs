using System.Runtime.InteropServices;
using Crucible.Core.Attributes;

namespace Crucible.Core;
[Component]
[StructLayout(LayoutKind.Sequential)]
public unsafe struct Camera
{

    public Camera(float near, float far, float fov, float width, float height, bool isPerspective)
    {
        _nearPlane = near;
        _farPlane = far;
        _fieldOfView = fov;
        _width = width;
        _height = height;
        //set via property, and we'll automatically calculate the matrix
        IsPerspective = isPerspective;
    }
    private float _nearPlane;
    private float _farPlane;
    private float _fieldOfView;
    private float _width;
    private float _height;
    private bool _isPerspective;
    private Matrix _projectionMatrix;
    
#pragma warning disable 0649
    private static delegate* unmanaged<ref Camera,  float, void> _cameraSetNearPlane_ptr;
    private static delegate* unmanaged<ref Camera,  float, void> _cameraSetFarPlane_ptr;
    private static delegate* unmanaged<ref Camera,  float, void> _cameraSetFOV_ptr;
    private static delegate* unmanaged<ref Camera,  float, void> _cameraSetWidth_ptr;
    private static delegate* unmanaged<ref Camera,  float, void> _cameraSetHeight_ptr;
    private static delegate* unmanaged<ref Camera,  bool, void> _cameraSetPerspective_ptr;
#pragma warning restore 0649

    [Expose("Near Plane")]
    public float NearPlane
    {
        get{return _nearPlane;}
        set{_cameraSetNearPlane_ptr(ref this,value);}
    }
    [Expose("Far Plane")]
    public float FarPlane
    {
        get{return _farPlane;}
        set{_cameraSetFarPlane_ptr(ref this,value);}
    }
    [Expose("Field Of View")]
    public float FieldOfView
    {
        get{return _fieldOfView;}
        set{_cameraSetFOV_ptr(ref this,value);}
    }
    [Expose("Width")]
    public float Width
    {
        get{return _width;}
        set{_cameraSetWidth_ptr(ref this,value);}
    }
    [Expose("Height")]
    public float Height
    {
        get{return _height;}
        set{_cameraSetHeight_ptr(ref this,value);}
    }
    [Expose("Perspective")]
    public bool IsPerspective
    {
        get{return _isPerspective;}
        set{_cameraSetPerspective_ptr(ref this,value);}
    }
}