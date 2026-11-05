// The editor's Grid and SelectionOutline steps (ADR 0067).
import crucible.shared;

ConstantBuffer<ViewConstants> view : register(b1);

struct OverlayConstants
{
    uint ids;   // SelectionOutline: the Pick step's EntityIds (Texture2D<uint>)
    uint count; // how many entities are outlined
    uint pad0;
    uint pad1;
    float4 color;        // the outline's, linear
    uint4 selected[4];   // their indices plus one (16)
};
[[vk::push_constant]] ConstantBuffer<OverlayConstants> overlay;

// One triangle covering the screen.
[shader("vertex")]
float4 overlayVertex(uint vertex : SV_VertexID) : SV_Position
{
    const float2 corner = float2((vertex << 1) & 2, vertex & 2);
    return float4(corner * float2(2.0, -2.0) + float2(-1.0, 1.0), 0.0, 1.0);
}

struct GridOutput
{
    float4 color : SV_Target;
    float depth : SV_Depth;
};

// How close a pixel is to a line of a grid of `cell` meters, from 1 on the
// line to 0 a pixel away (antialiased by the coordinate's screen rate).
float gridLine(float2 coordinate, float cell)
{
    const float2 scaled = coordinate / cell;
    const float2 rate = max(fwidth(scaled), 1e-6);
    const float2 distance = abs(frac(scaled - 0.5) - 0.5) / rate;
    return 1.0 - min(min(distance.x, distance.y), 1.0);
}

// The ground plane (y = 0) under the view ray, depth-tested against the
// scene through the depth it writes out: 1 m lines, 10 m lines brighter,
// the X axis red and the Z axis blue, fading with distance and where lines
// would crowd closer than a few pixels.
[shader("fragment")]
GridOutput gridPixel(float4 position : SV_Position)
{
    const float2 ndc = position.xy / view.viewportSize * float2(2.0, -2.0) + float2(-1.0, 1.0);
    const float4 near = mul(float4(ndc, 1.0, 1.0), view.inverseViewProjection);
    const bool orthographic = view.projection[3][3] == 1.0;
    const float3 nearPoint = near.xyz / near.w;
    const float3 origin = orthographic ? nearPoint : view.cameraPosition;
    const float3 direction = orthographic ? normalize(view.inverseView[2].xyz) : normalize(nearPoint - view.cameraPosition);
    if (abs(direction.y) < 1e-6)
    {
        discard;
    }
    const float t = -origin.y / direction.y;
    if (t <= 0.0)
    {
        discard;
    }
    const float3 ground = origin + direction * t;
    const float4 clip = mul(float4(ground, 1.0), view.viewProjection);

    const float2 coordinate = ground.xz;
    const float2 rate = fwidth(coordinate);
    const float crowding = saturate(1.5 - max(rate.x, rate.y) * 4.0); // 1 m lines fade before they'd blur
    const float minor = gridLine(coordinate, 1.0) * crowding;
    const float major = gridLine(coordinate, 10.0);
    const float xAxis = 1.0 - min(abs(ground.z) / max(rate.y, 1e-6), 1.0);
    const float zAxis = 1.0 - min(abs(ground.x) / max(rate.x, 1e-6), 1.0);

    float3 color = float3(0.5, 0.5, 0.5);
    float alpha = max(minor * 0.25, major * 0.5);
    if (xAxis > 0.0 || zAxis > 0.0)
    {
        color = xAxis > zAxis ? float3(0.8, 0.15, 0.15) : float3(0.15, 0.3, 0.9);
        alpha = max(alpha, max(xAxis, zAxis) * 0.8);
    }
    const float fade = saturate(1.0 - length(ground - view.cameraPosition) / 120.0);
    alpha *= fade;
    if (alpha <= 0.002)
    {
        discard;
    }
    GridOutput output;
    output.color = float4(color, alpha);
    output.depth = clip.z / clip.w;
    return output;
}

bool isSelected(uint id)
{
    for (uint i = 0; i < overlay.count; ++i)
    {
        if (overlay.selected[i / 4][i % 4] == id)
        {
            return true;
        }
    }
    return false;
}

// The selection's outline: pixels within two of an edge between the
// selection's pixels and anything else's, drawn on the backbuffer.
[shader("fragment")]
float4 outlinePixel(float4 position : SV_Position) : SV_Target
{
    Texture2D<uint> ids = ResourceDescriptorHeap[overlay.ids];
    uint width = 0, height = 0;
    ids.GetDimensions(width, height);
    const int2 pixel = int2(position.xy);
    const bool inside = isSelected(ids.Load(int3(pixel, 0)));
    bool edge = false;
    for (int y = -2; y <= 2 && !edge; ++y)
    {
        for (int x = -2; x <= 2 && !edge; ++x)
        {
            const int2 other = clamp(pixel + int2(x, y), int2(0, 0), int2(width - 1, height - 1));
            edge = isSelected(ids.Load(int3(other, 0))) != inside;
        }
    }
    if (!edge)
    {
        discard;
    }
    return overlay.color;
}
