// The Sky step's shader (ADR 0055): a gradient from the horizon up to the
// zenith and down to the ground, and the sun's disk. Drawn at the far depth
// (0, reverse-Z), so only where nothing else was.

import crucible.shared;

ConstantBuffer<ViewConstants> view : register(b1);

// One triangle covering the screen, at the far depth.
[shader("vertex")]
float4 skyVertex(uint vertex : SV_VertexID) : SV_Position
{
    const float2 corner = float2((vertex << 1) & 2, vertex & 2);
    return float4(corner * float2(2.0, -2.0) + float2(-1.0, 1.0), 0.0, 1.0);
}

[shader("fragment")]
float4 skyPixel(float4 position : SV_Position) : SV_Target
{
    // The view ray through the pixel: from the camera to the pixel's point
    // on the near plane (orthographic views look straight ahead).
    const float2 ndc = position.xy / view.viewportSize * float2(2.0, -2.0) + float2(-1.0, 1.0);
    const float4 near = mul(float4(ndc, 1.0, 1.0), view.inverseViewProjection);
    const bool orthographic = view.projection[3][3] == 1.0;
    const float3 direction = orthographic ? normalize(view.inverseView[2].xyz)
                                          : normalize(near.xyz / near.w - view.cameraPosition);

    // Above the horizon toward the zenith, below it toward the ground, both
    // changing fastest near the horizon.
    const float up = direction.y;
    float3 color = up >= 0.0 ? lerp(view.skyHorizon, view.skyZenith, sqrt(saturate(up)))
                             : lerp(view.skyHorizon, view.skyGround, sqrt(saturate(-up * 4.0)));

    // The sun's disk, toward the first directional light, its edge
    // smoothed over about a pixel.
    if (view.skySunIntensity > 0.0 && view.directionalLightCount > 0)
    {
        StructuredBuffer<LightData> lights = ResourceDescriptorHeap[view.lights];
        const float cosine = dot(direction, lights[0].direction);
        const float edge = max(fwidth(cosine), 1e-6);
        const float disk = smoothstep(view.skySunCosine - edge, view.skySunCosine + edge, cosine);
        color += lights[0].color * view.skySunIntensity * disk;
    }
    return float4(color, 1.0);
}
