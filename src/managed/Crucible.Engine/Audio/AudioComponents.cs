using Crucible.Serialization;

namespace Crucible.Audio;

// The audio contract's components (ADR 0075), declared in C++
// (crucible/Audio.h); this half adds saving and the defaults a source starts
// with when it's added (all zeros would be silent).

/// <summary>A sound at its entity's position, or everywhere unless spatial.</summary>
[Serialized]
public partial struct AudioSource
{
    public AudioSource()
    {
        Volume = 1;
        Pitch = 1;
        Spatial = true;
        MinDistance = 1;
        MaxDistance = 50;
        Rolloff = Rolloff.Inverse;
    }
}
