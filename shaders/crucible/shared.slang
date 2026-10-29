// Crucible's shader library: what every shader may import (ADR 0047).

// Marks a struct whose layout C++ and C# mirror: crucible_shaderc generates
// both, and refuses a struct laid out differently as a constant buffer member
// and a structured buffer element, or on Vulkan and D3D12.
[__AttributeUsage(_AttributeTargets.Struct)]
public struct GpuSharedAttribute
{
};
