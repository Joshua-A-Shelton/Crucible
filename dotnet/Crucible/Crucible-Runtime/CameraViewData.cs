using System.Runtime.InteropServices;

namespace Crucible;

[StructLayout(LayoutKind.Sequential)]
public struct CameraViewData
{
    [Expose("Field Of View")]
    public float FieldOfView = 90;

    [Expose("Near Plane")]
    public float NearPlane = .001f;

    [Expose("Far Plane")] 
    public float FarPlane = 500;

    [Expose("Aspect Ratio")]
    public float AspectRatio = 16.0f/9.0f;
    public CameraViewData(){}
}