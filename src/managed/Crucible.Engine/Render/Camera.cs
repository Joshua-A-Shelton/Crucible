using Crucible.Serialization;

namespace Crucible.Render;

/// <summary>
/// A view of the world (ADR 0050), from its entity's world transform.
/// Declared in C++ (crucible/Camera.h); this half adds saving.
/// </summary>
[Serialized]
public partial struct Camera;
