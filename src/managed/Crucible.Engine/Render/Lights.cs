using System.Numerics;
using Crucible.Serialization;

namespace Crucible.Render;

// The light components (ADR 0053), declared in C++ (crucible/Lights.h);
// these halves add saving, and the values a component starts with when it's
// added (ADR 0077's by-hand run: all zeros is a light that lights nothing).

/// <summary>A light from far away, shining along its entity's +Z: the sun.</summary>
[Serialized]
public partial struct DirectionalLight
{
    public DirectionalLight()
    {
        Color = new Vector3(1, 0.95f, 0.88f);
        Intensity = 1;
        CastsShadows = true;
        ShadowDistance = 50;
    }
}

/// <summary>A light at its entity's position, fading to nothing at its range.</summary>
[Serialized]
public partial struct PointLight
{
    public PointLight()
    {
        Color = Vector3.One;
        Intensity = 5;
        Range = 5;
    }
}

/// <summary>A cone of light along its entity's +Z, fading to nothing at its range.</summary>
[Serialized]
public partial struct SpotLight
{
    public SpotLight()
    {
        Color = Vector3.One;
        Intensity = 10;
        Range = 10;
        InnerConeAngle = 0.4f;
        OuterConeAngle = 0.6f;
    }
}

/// <summary>Light that isn't from a light: sky above, ground below. One per world.</summary>
[Serialized]
public partial struct AmbientLight
{
    public AmbientLight()
    {
        SkyColor = new Vector3(0.35f, 0.4f, 0.55f);
        GroundColor = new Vector3(0.22f, 0.2f, 0.16f);
        Intensity = 0.75f;
    }
}

/// <summary>The sky drawn behind everything (ADR 0055): a gradient and the sun's disk. One per world.</summary>
[Serialized]
public partial struct Sky
{
    public Sky()
    {
        ZenithColor = new Vector3(0.18f, 0.32f, 0.7f);
        HorizonColor = new Vector3(0.95f, 0.75f, 0.55f);
        GroundColor = new Vector3(0.3f, 0.26f, 0.22f);
        Intensity = 1;
        SunSize = 0.04f;
        SunIntensity = 6;
    }
}
