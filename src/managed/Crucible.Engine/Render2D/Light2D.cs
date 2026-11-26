using System.Numerics;
using Crucible.Serialization;

namespace Crucible.Render2D;

/// <summary>
/// A circle of 2D light (ADR 0113), added into the light buffer that lit
/// sprites and tiles are multiplied by. Declared in C++ (crucible/Light2D.h);
/// this half adds saving and what a new one starts with: white light,
/// intensity 1, a range of 3 m, a smooth fade.
/// </summary>
[System.ComponentModel.Category("2D/Lights"), System.ComponentModel.Description("Light from a point on the 2D plane.")]
[Serialized]
[Crucible.Scenes.ComponentIcon("lightbulb")]
public partial struct PointLight2D
{
    public PointLight2D()
    {
        Color = Vector3.One;
        Intensity = 1;
        Range = 3;
    }
}

/// <summary>
/// A cone of 2D light shining along its entity's local down (ADR 0113).
/// Declared in C++ (crucible/Light2D.h); this half adds saving and what a
/// new one starts with: as a point light's, in a cone of 60°, full inside 40°.
/// </summary>
[System.ComponentModel.Category("2D/Lights"), System.ComponentModel.Description("A cone of light on the 2D plane.")]
[Serialized]
[Crucible.Scenes.ComponentIcon("flashlight")]
public partial struct SpotLight2D
{
    public SpotLight2D()
    {
        Color = Vector3.One;
        Intensity = 1;
        Range = 3;
        InnerConeAngle = MathF.PI * 40 / 180;
        OuterConeAngle = MathF.PI / 3;
    }
}

/// <summary>
/// The light everything 2D and lit starts from (ADR 0113); one per world.
/// Declared in C++ (crucible/Light2D.h); this half adds saving and what a
/// new one starts with: a dim blue night, so lights show.
/// </summary>
[System.ComponentModel.Category("2D/Lights"), System.ComponentModel.Description("Light over the whole 2D scene.")]
[Serialized]
[Crucible.Scenes.ComponentIcon("sun-dim")]
public partial struct AmbientLight2D
{
    public AmbientLight2D()
    {
        Color = new Vector3(0.35f, 0.4f, 0.6f);
        Intensity = 1;
    }
}
