using System.Numerics;
using Crucible.Serialization;

namespace Crucible.Render2D;

/// <summary>
/// One layer of tiles from a tilemap asset, drawn at its entity's
/// Transform2D (ADR 0108). Declared in C++ (crucible/Tilemap.h); this half
/// adds saving and the tint it starts with: white. A Collider2D of shape
/// Tilemap on the same entity collides with its tiles' shapes.
/// </summary>
[Serialized]
public partial struct Tilemap
{
    public Tilemap()
    {
        Tint = Vector4.One;
    }
}
