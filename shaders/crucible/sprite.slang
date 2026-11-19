// The Sprites step (ADR 0104): every sprite in one instanced draw over the
// finished image, six vertices each, in the order the CPU sorted them; each
// reads its own texture from the heap. Texels are sampled point-clamped and
// written as they are (times the tint), so colors are exactly the texture's.
// The editor's Pick step draws them again, writing their entities.
import crucible.shared;

ConstantBuffer<ViewConstants> view : register(b1);

static const uint pointClampSampler = 3;

// Sprites.cpp's SpriteInstance.
struct SpriteInstance
{
    float2 axisX;    // world-space direction and length of a local meter along x
    float2 axisY;    // and along y
    float3 origin;   // world position of the entity
    float pad0;
    float2 localMin; // the quad in local meters, pivot applied
    float2 localMax;
    float2 uvMin;    // the region's top-left texture coordinate (swapped when flipped)
    float2 uvMax;    // and its bottom-right
    float4 tint;
    uint texture;    // Texture2D's heap slot
    uint entity;     // entity index + 1, for picking
    uint pad1;
    uint pad2;
};

struct SpriteConstants
{
    uint sprites; // StructuredBuffer<SpriteInstance>
    uint pad0;
    uint pad1;
    uint pad2;
};
[[vk::push_constant]] ConstantBuffer<SpriteConstants> constants;

struct SpriteVaryings
{
    float4 position : SV_Position;
    float2 uv : TEXCOORD0;
    nointerpolation uint instance : INSTANCE;
};

[shader("vertex")]
SpriteVaryings spriteVertex(uint vertex : SV_VertexID, uint instance : SV_InstanceID)
{
    StructuredBuffer<SpriteInstance> sprites = ResourceDescriptorHeap[constants.sprites];
    const SpriteInstance sprite = sprites[instance];
    // Two triangles: bottom-left, bottom-right, top-left, top-left,
    // bottom-right, top-right.
    const uint corners[6] = { 0, 1, 2, 2, 1, 3 };
    const float2 corner = float2(corners[vertex] & 1, corners[vertex] >> 1);
    const float2 local = lerp(sprite.localMin, sprite.localMax, corner);
    const float2 world = sprite.origin.xy + local.x * sprite.axisX + local.y * sprite.axisY;
    SpriteVaryings output;
    output.position = mul(float4(world, sprite.origin.z, 1.0), view.viewProjection);
    // Texture rows run down, local y up.
    output.uv = float2(lerp(sprite.uvMin.x, sprite.uvMax.x, corner.x), lerp(sprite.uvMax.y, sprite.uvMin.y, corner.y));
    output.instance = instance;
    return output;
}

float4 spriteColor(SpriteVaryings input, out SpriteInstance sprite)
{
    StructuredBuffer<SpriteInstance> sprites = ResourceDescriptorHeap[constants.sprites];
    sprite = sprites[input.instance];
    Texture2D<float4> texture = ResourceDescriptorHeap[NonUniformResourceIndex(sprite.texture)];
    SamplerState s = SamplerDescriptorHeap[pointClampSampler];
    return texture.Sample(s, input.uv) * sprite.tint;
}

[shader("fragment")]
float4 spritePixel(SpriteVaryings input) : SV_Target
{
    SpriteInstance sprite;
    return spriteColor(input, sprite);
}

// The editor's picks: the entity where the sprite shows (not where it's
// clear), and the depth there.
struct SpritePickOutput
{
    uint entity : SV_Target0;
    float depth : SV_Target1;
};

[shader("fragment")]
SpritePickOutput spritePickPixel(SpriteVaryings input)
{
    SpriteInstance sprite;
    if (spriteColor(input, sprite).a < 0.5)
    {
        discard;
    }
    SpritePickOutput output;
    output.entity = sprite.entity;
    output.depth = input.position.z;
    return output;
}
