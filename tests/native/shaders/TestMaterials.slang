// A game's material shader, as the native GPU tests see one (ADR 0052): a
// material with a custom shading model, compiled like the Shader importer
// compiles (shaderfax --materials).
import crucible.material;

// A shading model that uses every hook: it counts the lights it sees and
// keeps the brightest, then colors the pixel by those alone (ambient adds a
// set amount), so a test can tell each hook ran.
public struct CountingShading : IShadingModel
{
    public struct Data
    {
        public float3 perLight;
    }

    public struct Accumulator
    {
        public float3 albedo;
        public uint lights;
        public float brightest;
        public float ambient;
        public float3 perLight;
    }

    public static Accumulator begin(SurfaceData s, Data d, SurfaceInput input)
    {
        Accumulator a;
        a.albedo = s.albedo;
        a.lights = 0;
        a.brightest = 0.0;
        a.ambient = 0.0;
        a.perLight = d.perLight;
        return a;
    }

    public static void light(inout Accumulator a, LightSample light)
    {
        a.lights += 1;
        a.brightest = max(a.brightest, max(light.color.r, max(light.color.g, light.color.b)));
    }

    public static void ambient(inout Accumulator a, AmbientSample ambient)
    {
        a.ambient = 0.25;
    }

    public static float4 finish(Accumulator a, SurfaceData s, Data d, SurfaceInput input)
    {
        // One temporary sun of brightness 1: albedo * (1 * perLight + 0.25).
        return float4(a.albedo * (float(a.lights) * a.brightest * a.perLight + a.ambient), s.alpha);
    }
}

public struct Flat : IMaterial
{
    public float4 color = float4(0, 1, 0, 1);
    public float strength = 0.5;
    public TextureSlot unusedTexture;
    public typealias Shading = CountingShading;

    public SurfaceData surface(SurfaceInput input)
    {
        SurfaceData s = SurfaceData.defaults(input);
        s.albedo = color.rgb;
        s.alpha = color.a;
        return s;
    }

    public CountingShading.Data shading(SurfaceInput input)
    {
        CountingShading.Data d;
        d.perLight = float3(strength, strength, strength);
        return d;
    }
}
