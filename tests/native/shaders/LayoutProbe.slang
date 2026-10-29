// The shader toolchain's GPU test (ADR 0047): fills a buffer of [GpuShared]
// structs, which C++ reads back through the generated mirror.
import crucible.shared;

[GpuShared]
struct Probe
{
    float3 position;
    float radius;
    float4x4 transform;
    float3 tintColor;
    float tintStrength;
    float2 uv;
    int index;
    uint flags;
};

struct FillConstants
{
    uint output;
    uint count;
};
[[vk::push_constant]] ConstantBuffer<FillConstants> constants;

[shader("compute")]
[numthreads(64, 1, 1)]
void fill(uint3 thread : SV_DispatchThreadID)
{
    if (thread.x >= constants.count)
    {
        return;
    }
    RWStructuredBuffer<Probe> output = ResourceDescriptorHeap[constants.output];
    float i = float(thread.x);
    Probe probe;
    probe.position = float3(i, i + 0.5, -i);
    probe.radius = i * 0.25;
    // Row by row: row-major storage keeps them in this order in memory.
    probe.transform = float4x4(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, i, 14, 15, 16);
    probe.tintColor = float3(0.25, 0.5, 0.75);
    probe.tintStrength = i;
    probe.uv = float2(0.125, i);
    probe.index = -int(thread.x);
    probe.flags = thread.x | 0x80000000u;
    output[thread.x] = probe;
}

// Packed as D3D12 packs it: b right after a, a 16-byte stride. Vulkan's own
// rules would put b at 16 (std430); -fvk-use-dx-layout and Slag's
// scalarBlockLayout make it match.
[GpuShared]
struct PackedPair
{
    float a;
    float3 b;
};

[shader("compute")]
[numthreads(64, 1, 1)]
void fillPacked(uint3 thread : SV_DispatchThreadID)
{
    if (thread.x >= constants.count)
    {
        return;
    }
    RWStructuredBuffer<PackedPair> output = ResourceDescriptorHeap[constants.output];
    PackedPair pair;
    pair.a = float(thread.x);
    pair.b = float3(1, 2, float(thread.x) * 2);
    output[thread.x] = pair;
}

