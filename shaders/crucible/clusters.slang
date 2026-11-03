// LightClustering's compute shader (ADR 0053): for each cluster of the view
// (16 x 9 screen tiles x 24 depth slices), the visible point and spot lights
// whose range reaches it. A thread per cluster tests each light's sphere
// against the cluster's box in view space.
import crucible.shared;

ConstantBuffer<ViewConstants> view : register(b1);

struct ClusterConstants
{
    uint ranges; // RWStructuredBuffer<uint>: each cluster's light count
    uint lists;  // RWStructuredBuffer<uint>: maxLightsPerCluster indices per cluster
};
[[vk::push_constant]] ConstantBuffer<ClusterConstants> constants;

static const uint countX = 16;
static const uint countY = 9;
static const uint countZ = 24;
static const uint maxLightsPerCluster = 64;

// A view-space point at a depth: x = ndc * w(z) / P00 works for perspective
// (w = z) and orthographic (w = 1) projections alike.
float3 atDepth(float2 ndc, float z)
{
    const float w = z * view.projection[2][3] + view.projection[3][3];
    return float3(ndc.x * w / view.projection[0][0], ndc.y * w / view.projection[1][1], z);
}

[shader("compute")]
[numthreads(64, 1, 1)]
void buildClusters(uint3 thread : SV_DispatchThreadID)
{
    const uint cluster = thread.x;
    if (cluster >= countX * countY * countZ)
    {
        return;
    }
    const uint x = cluster % countX;
    const uint y = (cluster / countX) % countY;
    const uint z = cluster / (countX * countY);
    // The slice's depths, from floor(log(z) * scale + bias) = slice.
    const float nearZ = exp((float(z) - view.clusterSliceBias) / view.clusterSliceScale);
    const float farZ = exp((float(z) + 1.0 - view.clusterSliceBias) / view.clusterSliceScale);
    // The tile in normalized device coordinates; pixel rows go down.
    const float2 ndcMin = float2(float(x) / countX * 2.0 - 1.0, 1.0 - float(y + 1) / countY * 2.0);
    const float2 ndcMax = float2(float(x + 1) / countX * 2.0 - 1.0, 1.0 - float(y) / countY * 2.0);
    float3 boxMin = atDepth(ndcMin, nearZ);
    float3 boxMax = boxMin;
    for (uint corner = 0; corner < 8; ++corner)
    {
        const float2 ndc = float2((corner & 1) != 0 ? ndcMax.x : ndcMin.x, (corner & 2) != 0 ? ndcMax.y : ndcMin.y);
        const float3 p = atDepth(ndc, (corner & 4) != 0 ? farZ : nearZ);
        boxMin = min(boxMin, p);
        boxMax = max(boxMax, p);
    }

    StructuredBuffer<LightData> lights = ResourceDescriptorHeap[view.lights];
    RWStructuredBuffer<uint> ranges = ResourceDescriptorHeap[constants.ranges];
    RWStructuredBuffer<uint> lists = ResourceDescriptorHeap[constants.lists];
    uint count = 0;
    for (uint i = 0; i < view.lightCount && count < maxLightsPerCluster; ++i)
    {
        const uint index = view.directionalLightCount + i;
        const LightData light = lights[index];
        const float3 center = mul(float4(light.position, 1.0), view.view).xyz;
        const float3 offset = center - clamp(center, boxMin, boxMax);
        if (dot(offset, offset) <= light.range * light.range)
        {
            lists[cluster * maxLightsPerCluster + count] = index;
            ++count;
        }
    }
    ranges[cluster] = count;
}
