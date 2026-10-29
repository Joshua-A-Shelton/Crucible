// Crucible's shader library: what every shader may import (ADR 0047).

// Marks a struct whose layout C++ and C# mirror: shaderfax generates
// both, and refuses a struct laid out differently as a constant buffer member
// and a structured buffer element, or on Vulkan and D3D12.
[__AttributeUsage(_AttributeTargets.Struct)]
public struct GpuSharedAttribute
{
};

// The view a frame is drawn from (ADR 0050), filled by the renderer from the
// camera every frame. Row vectors, as in C++: a world position p lands at
// mul(float4(p, 1), viewProjection). Depth is reverse-Z, with no far plane
// for a perspective camera.
[GpuShared]
public struct ViewConstants
{
    public float4x4 view;
    public float4x4 projection;
    public float4x4 viewProjection;
    public float4x4 inverseView;
    public float4x4 inverseProjection;
    public float4x4 inverseViewProjection;
    public float3 cameraPosition; // world space
    public float time;            // seconds since the renderer started
    public float2 viewportSize;   // pixels
};

// A world matrix as shaders read it (ADR 0051): its first three columns, so
// a world position is (dot(p, x), dot(p, y), dot(p, z)) with p = float4(position, 1).
[GpuShared]
public struct InstanceTransform
{
    public float4 x;
    public float4 y;
    public float4 z;
};
