using System.Numerics;
using Crucible.Serialization;

namespace Crucible.Render2D;

/// <summary>
/// A picture drawn at its entity's Transform2D (ADR 0104). Declared in C++
/// (crucible/Sprite.h); this half adds saving and the values it starts with:
/// a centered pivot and a white tint.
/// </summary>
[Serialized]
public partial struct Sprite
{
    public Sprite()
    {
        Pivot = new Vector2(0.5f, 0.5f);
        Tint = Vector4.One;
    }
}

/// <summary>The part of a texture a sprite shows, in pixels from its top-left (ADR 0104).</summary>
[Serialized]
public partial struct SpriteRegion;

/// <summary>
/// The 2D camera (ADR 0106): sprites drawn at its virtual resolution and
/// scaled up to the window. Declared in C++ (crucible/Sprite.h); this half
/// adds saving and the values it starts with: 320x180 at 16 pixels a meter,
/// fitted sharply.
/// </summary>
[Serialized]
public partial struct PixelCamera
{
    public PixelCamera()
    {
        Width = 320;
        Height = 180;
        PixelsPerUnit = 16;
        Fit = PixelFit.Sharp;
    }
}
