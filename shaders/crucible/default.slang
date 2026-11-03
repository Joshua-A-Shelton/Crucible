// The engine's built-in materials (ADR 0052): what a model's generated
// materials use, filled from its imported glTF/FBX material. DefaultToon
// and DefaultPbr share DefaultMaterial's parameters and surface, and differ
// only in their shading model.
import crucible.shared;
import crucible.material;

// glTF's metallic-roughness material. Textures multiply their factors. The
// alpha cutoff is the record's (MaterialInfo), passed per draw.
[GpuShared]
public struct DefaultMaterial
{
    public float4 baseColor; // linear
    public float3 emissive;
    public float metallic;
    public float roughness;
    public float normalScale;
    public float occlusionStrength;
    public uint baseColorTexture;
    public uint metallicRoughnessTexture; // green: roughness, blue: metallic
    public uint normalTexture;
    public uint occlusionTexture; // red
    public uint emissiveTexture;
};

SurfaceData defaultSurface(DefaultMaterial m, SurfaceInput input)
{
    SurfaceData s = SurfaceData.defaults(input);
    const float4 base = m.baseColor * input.color * sampleTexture(m.baseColorTexture, input.uv0, float4(1.0, 1.0, 1.0, 1.0));
    s.albedo = base.rgb;
    s.alpha = base.a;
    const float4 metallicRoughness = sampleTexture(m.metallicRoughnessTexture, input.uv0, float4(1.0, 1.0, 1.0, 1.0));
    s.roughness = m.roughness * metallicRoughness.g;
    s.metallic = m.metallic * metallicRoughness.b;
    if (m.normalTexture != 0)
    {
        float3 n = sampleTexture(m.normalTexture, input.uv0, float4(0.5, 0.5, 1.0, 1.0)).xyz * 2.0 - 1.0;
        n.xy *= m.normalScale;
        s.normal = input.toWorld(normalize(n));
    }
    const float occlusion = sampleTexture(m.occlusionTexture, input.uv0, float4(1.0, 1.0, 1.0, 1.0)).r;
    s.occlusion = 1.0 + m.occlusionStrength * (occlusion - 1.0);
    s.emissive = m.emissive * sampleTexture(m.emissiveTexture, input.uv0, float4(1.0, 1.0, 1.0, 1.0)).rgb;
    return s;
}

public struct DefaultToon : IMaterial
{
    public DefaultMaterial parameters;
    public typealias Shading = ToonShading;

    public SurfaceData surface(SurfaceInput input)
    {
        return defaultSurface(parameters, input);
    }

    public ToonData shading(SurfaceInput input)
    {
        return ToonData.defaults();
    }
}

public struct DefaultPbr : IMaterial
{
    public DefaultMaterial parameters;
    public typealias Shading = PbrShading;

    public SurfaceData surface(SurfaceInput input)
    {
        return defaultSurface(parameters, input);
    }

    public PbrData shading(SurfaceInput input)
    {
        return PbrData.defaults();
    }
}

[shader("vertex")]
MaterialVertex materialVertex(uint vertex : SV_VertexID, uint instance : SV_InstanceID,
                              uint firstInstance : SV_StartInstanceLocation)
{
    return materialVertexMain(vertex, firstInstance + instance);
}

[shader("fragment")]
float4 toonPixel(MaterialVertex v, bool frontFace : SV_IsFrontFace) : SV_Target
{
    return materialForward<DefaultToon>(v, frontFace);
}

[shader("fragment")]
float4 pbrPixel(MaterialVertex v, bool frontFace : SV_IsFrontFace) : SV_Target
{
    return materialForward<DefaultPbr>(v, frontFace);
}

// Alpha-tested materials' depth: the two share a surface, so one entry
// serves both. DepthPrepass with alpha-to-coverage, Shadows clipped.
[shader("fragment")]
float4 coveragePixel(MaterialVertex v, bool frontFace : SV_IsFrontFace) : SV_Target
{
    return materialCoverage<DefaultToon>(v, frontFace);
}

[shader("fragment")]
void maskPixel(MaterialVertex v, bool frontFace : SV_IsFrontFace)
{
    materialMask<DefaultToon>(v, frontFace);
}

// Blended materials (ADR 0055).
[shader("fragment")]
float4 toonTransparentPixel(MaterialVertex v, bool frontFace : SV_IsFrontFace) : SV_Target
{
    return materialTransparent<DefaultToon>(v, frontFace);
}

[shader("fragment")]
float4 pbrTransparentPixel(MaterialVertex v, bool frontFace : SV_IsFrontFace) : SV_Target
{
    return materialTransparent<DefaultPbr>(v, frontFace);
}

// Outlines (ADR 0055).
[shader("vertex")]
MaterialVertex outlineVertex(uint vertex : SV_VertexID, uint instance : SV_InstanceID,
                             uint firstInstance : SV_StartInstanceLocation)
{
    return outlineVertexMain(vertex, firstInstance + instance);
}

[shader("fragment")]
float4 toonOutlinePixel(MaterialVertex v, bool frontFace : SV_IsFrontFace) : SV_Target
{
    return materialOutline<DefaultToon>(v, frontFace);
}

[shader("fragment")]
float4 pbrOutlinePixel(MaterialVertex v, bool frontFace : SV_IsFrontFace) : SV_Target
{
    return materialOutline<DefaultPbr>(v, frontFace);
}
