// 2D lights into the light buffer (ADR 0113), in a file of its own: each
// shader file has one push-constant block. The buffer starts at the
// ambient (its clear); each point or spot light is a square around it, in
// one instanced draw, adding its light: full at its center, nothing at its
// range, through a smooth fade or that many flat bands; a spot's only
// inside its cone. Drawn at the size of the image the sprites draw into,
// so light changes a pixel (a virtual pixel, through a pixel camera) at a
// time.

// Sprites.cpp's Light2DInstance.
struct Light2DInstance
{
    float2 position;
    float2 direction; // a spot's, unit length; zero for a point light
    float3 color;     // times its intensity
    float range;
    float cosInner;   // a spot's half angles' cosines
    float cosOuter;
    uint bands;       // 0: a smooth fade
    uint pad;
};

struct Light2DConstants
{
    uint lights; // StructuredBuffer<Light2DInstance>
    uint pad0;
    uint pad1;
    uint pad2;
    float4x4 viewProjection;
};
[[vk::push_constant]] ConstantBuffer<Light2DConstants> constants;

struct Light2DVaryings
{
    float4 position : SV_Position;
    float2 world : TEXCOORD0;
    nointerpolation uint instance : INSTANCE;
};

[shader("vertex")]
Light2DVaryings lightVertex(uint vertex : SV_VertexID, uint instance : SV_InstanceID)
{
    StructuredBuffer<Light2DInstance> lights = ResourceDescriptorHeap[constants.lights];
    const Light2DInstance light = lights[instance];
    const uint corners[6] = { 0, 1, 2, 2, 1, 3 };
    const float2 corner = float2(corners[vertex] & 1, corners[vertex] >> 1) * 2 - 1;
    Light2DVaryings output;
    output.world = light.position + corner * light.range;
    output.position = mul(float4(output.world, 0.0, 1.0), constants.viewProjection);
    output.instance = instance;
    return output;
}

[shader("fragment")]
float4 lightPixel(Light2DVaryings input) : SV_Target
{
    StructuredBuffer<Light2DInstance> lights = ResourceDescriptorHeap[constants.lights];
    const Light2DInstance light = lights[input.instance];
    const float2 toPixel = input.world - light.position;
    const float distance = length(toPixel) / light.range;
    float strength = saturate(1.0 - distance * distance);
    strength *= strength;
    if (any(light.direction != 0))
    {
        const float facing = distance > 0 ? dot(toPixel / length(toPixel), light.direction) : 1.0;
        strength *= light.cosInner > light.cosOuter ? smoothstep(light.cosOuter, light.cosInner, facing)
                                                    : (facing >= light.cosOuter ? 1.0 : 0.0);
    }
    if (light.bands > 0)
    {
        strength = ceil(strength * light.bands) / light.bands;
    }
    return float4(light.color * strength, 0.0);
}
