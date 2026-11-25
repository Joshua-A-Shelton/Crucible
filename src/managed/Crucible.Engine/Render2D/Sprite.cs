using System.Numerics;
using Crucible.Serialization;

namespace Crucible.Render2D;

/// <summary>
/// A picture drawn at its entity's Transform2D (ADR 0104). Declared in C++
/// (crucible/Sprite.h); this half adds saving and the values it starts with:
/// a centered pivot and a white tint.
/// </summary>
[System.ComponentModel.Category("2D"), System.ComponentModel.Description("Draws a texture, or a region of one, on the 2D plane.")]
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
[System.ComponentModel.Category("2D"), System.ComponentModel.Description("Renders the 2D view at a low resolution for crisp pixel art.")]
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

/// <summary>
/// Frame animation (ADR 0107): plays a sprite clip on its entity's
/// <see cref="Sprite"/>, or lets a state machine choose. Declared in C++
/// (crucible/Sprite.h); this half adds saving and the speed it starts with.
/// Its machine's parameters are set, and its events read, with the calls
/// 3D characters use (<see cref="Animation.Animator.SetFloat"/>,
/// <see cref="Animation.Animator.Events"/>, ...).
/// </summary>
[System.ComponentModel.Category("2D"), System.ComponentModel.Description("Plays a sprite sheet's animation clips.")]
[Serialized]
public partial struct SpriteAnimator
{
    public SpriteAnimator()
    {
        Speed = 1;
    }
}
