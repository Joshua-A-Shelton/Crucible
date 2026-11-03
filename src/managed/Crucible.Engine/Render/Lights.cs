using Crucible.Serialization;

namespace Crucible.Render;

// The light components (ADR 0053), declared in C++ (crucible/Lights.h);
// these halves add saving.

/// <summary>A light from far away, shining along its entity's +Z: the sun.</summary>
[Serialized]
public partial struct DirectionalLight;

/// <summary>A light at its entity's position, fading to nothing at its range.</summary>
[Serialized]
public partial struct PointLight;

/// <summary>A cone of light along its entity's +Z, fading to nothing at its range.</summary>
[Serialized]
public partial struct SpotLight;

/// <summary>Light that isn't from a light: sky above, ground below. One per world.</summary>
[Serialized]
public partial struct AmbientLight;
