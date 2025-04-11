using System.Runtime.InteropServices;
using Crucible.Core.Attributes;

namespace Crucible.Core;

public unsafe class Camera
{
    internal IntPtr _cameraPtr = IntPtr.Zero;
    public Camera(float near, float far, float fov, float width, float height, bool isPerspective, Texture.Format renderTargetFormat, UInt32 outputWidth, UInt32 outputHeight,Texture.MultiSamplePixels multiSamplePixels, byte renderOrder = 0)
    {
        _cameraInitialize_ptr(ref _cameraPtr, near, far, fov, width, height, isPerspective, renderOrder,outputWidth,outputHeight,renderTargetFormat,(byte)multiSamplePixels);
    }

    ~Camera()
    {
        _cameraCleanUp_ptr(_cameraPtr);
    }
    
#pragma warning disable 0649
    private static delegate* unmanaged<ref IntPtr, float, float, float, float, float, bool, byte, UInt32, UInt32, Texture.Format, byte, void> _cameraInitialize_ptr;
    private static delegate* unmanaged<IntPtr, void> _cameraCleanUp_ptr;
    private static delegate* unmanaged<IntPtr, float> _cameraGetNear_ptr;
    private static delegate* unmanaged<IntPtr, float, void> _cameraSetNear_ptr;
    private static delegate* unmanaged<IntPtr, float> _cameraGetFar_ptr;
    private static delegate* unmanaged<IntPtr, float, void> _cameraSetFar_ptr;
    private static delegate* unmanaged<IntPtr, float> _cameraGetFOV_ptr;
    private static delegate* unmanaged<IntPtr, float, void> _cameraSetFOV_ptr;
    private static delegate* unmanaged<IntPtr, float> _cameraGetHeight_ptr;
    private static delegate* unmanaged<IntPtr, float, void> _cameraSetHeight_ptr;
    private static delegate* unmanaged<IntPtr, float> _cameraGetWidth_ptr;
    private static delegate* unmanaged<IntPtr, float, void> _cameraSetWidth_ptr;
    private static delegate* unmanaged<IntPtr, bool> _cameraGetIsPerspective_ptr;
    private static delegate* unmanaged<IntPtr, bool, void> _cameraSetIsPerspective_ptr;
    private static delegate* unmanaged<IntPtr, byte> _cameraGetRenderOrder_ptr;
    private static delegate* unmanaged<IntPtr, byte, void> _cameraSetRenderOrder_ptr;
#pragma warning restore 0649

    private float NearPlane
    {
        get {return _cameraGetNear_ptr(_cameraPtr);}
        set {_cameraSetNear_ptr(_cameraPtr,value);}
    }

    private float FarPlane
    {
        get { return _cameraGetFar_ptr(_cameraPtr);}
        set { _cameraSetFar_ptr(_cameraPtr,value);}
    }

    private float FieldOfView
    {
        get { return _cameraGetFOV_ptr(_cameraPtr);}
        set { _cameraSetFOV_ptr(_cameraPtr,value);}
    }

    private float Width
    {
        get { return _cameraGetWidth_ptr(_cameraPtr);}
        set { _cameraSetWidth_ptr(_cameraPtr,value);}
    }

    private float Height
    {
        get { return _cameraGetHeight_ptr(_cameraPtr);}
        set { _cameraSetHeight_ptr(_cameraPtr,value);}
    }

    private bool IsPerspective
    {
        get { return _cameraGetIsPerspective_ptr(_cameraPtr); }
        set { _cameraSetIsPerspective_ptr(_cameraPtr,value);}
    }

    private byte RenderOrder
    {
        get { return _cameraGetRenderOrder_ptr(_cameraPtr); }
        set { _cameraSetRenderOrder_ptr(_cameraPtr,value);}
    }
}