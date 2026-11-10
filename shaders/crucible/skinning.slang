// The Skinning step (ADR 0080): one skinned instance's vertices, blended by
// their joints' palette matrices, into the frame's skinned vertices, stream
// by stream in the model's own formats, so draws read them as they would
// the model's.

struct SkinConstants
{
    uint model;    // the model buffer (ByteAddressBuffer)
    uint palettes; // StructuredBuffer<float4>: three rows (x, y, z, with translation in w) per joint
    uint output;   // the skinned vertices (RWByteAddressBuffer)
    uint paletteOffset;
    uint vertexCount;
    uint influences; // per vertex: 4 or 8
    uint attributes; // the mesh's MeshAttribute bits
    // The mesh's first vertex in each of the model's streams (bytes).
    uint positions;
    uint normals;
    uint tangents;
    uint outlineNormals;
    uint joints;  // uint16 per influence
    uint weights; // unorm8 per influence
    // Where this instance's vertices start in each skinned stream (bytes).
    uint outPositions;
    uint outNormals;
    uint outTangents;
    uint outOutlineNormals;
};
[[vk::push_constant]] ConstantBuffer<SkinConstants> skin;

static const uint normalsAttribute = 1;
static const uint tangentsAttribute = 2;
static const uint outlineNormalsAttribute = 32;

// The importer's octahedral encoding (ADR 0043), two 16-bit numbers.
float3 decodeOctahedral(uint packed)
{
    float2 e = float2(int(packed << 16) >> 16, int(packed) >> 16) / 32767.0;
    float3 v = float3(e, 1.0 - abs(e.x) - abs(e.y));
    if (v.z < 0.0)
    {
        const float2 signs = select(v.xy >= 0.0, float2(1.0, 1.0), float2(-1.0, -1.0));
        v.xy = (1.0 - abs(v.yx)) * signs;
    }
    return normalize(v);
}

uint encodeOctahedral(float3 n)
{
    n /= abs(n.x) + abs(n.y) + abs(n.z);
    float2 e = n.xy;
    if (n.z < 0.0)
    {
        const float2 signs = select(e >= 0.0, float2(1.0, 1.0), float2(-1.0, -1.0));
        e = (1.0 - abs(e.yx)) * signs;
    }
    const int2 q = int2(round(clamp(e, -1.0, 1.0) * 32767.0));
    return (uint(q.x) & 0xffff) | (uint(q.y) << 16);
}

uint loadByte(ByteAddressBuffer buffer, uint offset)
{
    return (buffer.Load(offset & ~3u) >> ((offset & 3u) * 8)) & 0xff;
}

uint loadShort(ByteAddressBuffer buffer, uint offset)
{
    return (buffer.Load(offset & ~3u) >> ((offset & 2u) * 8)) & 0xffff;
}

// A direction through the blended matrix's 3x3.
float3 transformDirection(float3 d, float4 x, float4 y, float4 z)
{
    return float3(dot(d, x.xyz), dot(d, y.xyz), dot(d, z.xyz));
}

[shader("compute")]
[numthreads(64, 1, 1)]
void skinVertices(uint3 id : SV_DispatchThreadID)
{
    const uint vertex = id.x;
    if (vertex >= skin.vertexCount)
    {
        return;
    }
    ByteAddressBuffer model = ResourceDescriptorHeap[skin.model];
    StructuredBuffer<float4> palettes = ResourceDescriptorHeap[skin.palettes];
    RWByteAddressBuffer output = ResourceDescriptorHeap[skin.output];

    // The weighted sum of its joints' matrices (weights sum to 255).
    float4 x = 0.0, y = 0.0, z = 0.0;
    for (uint k = 0; k < skin.influences; ++k)
    {
        const uint influence = vertex * skin.influences + k;
        const float weight = loadByte(model, skin.weights + influence) / 255.0;
        if (weight == 0.0)
        {
            continue;
        }
        const uint row = (skin.paletteOffset + loadShort(model, skin.joints + influence * 2)) * 3;
        x += palettes[row] * weight;
        y += palettes[row + 1] * weight;
        z += palettes[row + 2] * weight;
    }

    const float4 position = float4(asfloat(model.Load3(skin.positions + vertex * 12)), 1.0);
    output.Store3(skin.outPositions + vertex * 12, asuint(float3(dot(position, x), dot(position, y), dot(position, z))));
    if ((skin.attributes & normalsAttribute) != 0)
    {
        const float3 normal = decodeOctahedral(model.Load(skin.normals + vertex * 4));
        output.Store(skin.outNormals + vertex * 4, encodeOctahedral(normalize(transformDirection(normal, x, y, z))));
    }
    if ((skin.attributes & tangentsAttribute) != 0)
    {
        // The direction turns; the handedness (the second word) stays.
        const uint2 packed = model.Load2(skin.tangents + vertex * 8);
        const float3 tangent = decodeOctahedral(packed.x);
        output.Store2(skin.outTangents + vertex * 8,
                      uint2(encodeOctahedral(normalize(transformDirection(tangent, x, y, z))), packed.y));
    }
    if ((skin.attributes & outlineNormalsAttribute) != 0)
    {
        const float3 normal = decodeOctahedral(model.Load(skin.outlineNormals + vertex * 4));
        output.Store(skin.outOutlineNormals + vertex * 4,
                     encodeOctahedral(normalize(transformDirection(normal, x, y, z))));
    }
}
