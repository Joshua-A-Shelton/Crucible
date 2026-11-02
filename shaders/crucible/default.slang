// The engine's built-in materials (ADR 0052): what a model's generated
// materials use, filled from its imported glTF/FBX material. DefaultToon
// and DefaultPbr share DefaultMaterial's parameters and surface, and differ
// only in their shading model.
import crucible.shared;
import crucible.material;

// glTF's metallic-roughness material. Textures multiply their factors;
// alphaCutoff is 0 for opaque materials.
[GpuShared]
public struct DefaultMaterial
{
    public float4 baseColor; // linear
    public float3 emissive;
    public float metallic;
    public float roughness;
    public float alphaCutoff;
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

// Opaque, and alpha-tested in Opaque too: what the prepass clipped must not
// be drawn over what's behind it.
float4 opaque<M : IMaterial>(MaterialVertex v, bool frontFace, float cutoff)
{
    const float4 color = shadeMaterial<M>(v, frontFace);
    if (color.a < cutoff)
    {
        discard;
    }
    return float4(color.rgb, 1.0);
}

[shader("fragment")]
float4 toonPixel(MaterialVertex v, bool frontFace : SV_IsFrontFace) : SV_Target
{
    return opaque<DefaultToon>(v, frontFace, loadMaterial<DefaultToon>().parameters.alphaCutoff);
}

[shader("fragment")]
float4 pbrPixel(MaterialVertex v, bool frontFace : SV_IsFrontFace) : SV_Target
{
    return opaque<DefaultPbr>(v, frontFace, loadMaterial<DefaultPbr>().parameters.alphaCutoff);
}

// DepthPrepass for alpha-tested materials: the two share a surface, so one
// entry serves both.
[shader("fragment")]
void maskPixel(MaterialVertex v, bool frontFace : SV_IsFrontFace)
{
    clipMaterial<DefaultToon>(v, frontFace, loadMaterial<DefaultToon>().parameters.alphaCutoff);
}
