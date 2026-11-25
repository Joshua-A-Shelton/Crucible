using System.Numerics;
using Crucible.Serialization;

namespace Crucible.Render2D;

/// <summary>
/// One layer of tiles from a tilemap asset, drawn at its entity's
/// Transform2D (ADR 0108, 0112): <c>Layer</c> names it by its id. Declared
/// in C++ (crucible/Tilemap.h); this half adds saving and the tint it starts
/// with: white. A Collider2D of shape Tilemap on the same entity collides
/// with that layer's tiles' shapes.
/// </summary>
[System.ComponentModel.Category("2D"), System.ComponentModel.Description("Draws one layer of a tilemap, and can collide with its tiles' shapes.")]
[Serialized]
public partial struct Tilemap
{
    public Tilemap()
    {
        Tint = Vector4.One;
    }
}

/// <summary>
/// A map whose layers are the entity's children (ADR 0112), each child's
/// Tilemap drawing one layer of <c>Map</c>. The editor's map mode edits
/// them as one; nothing at run time reads it. Declared in C++
/// (crucible/Tilemap.h); this half adds saving.
/// </summary>
[System.ComponentModel.Category("2D"), System.ComponentModel.Description("Makes the entity a map: its children draw the map's layers.")]
[Serialized]
public partial struct TilemapLayers;
