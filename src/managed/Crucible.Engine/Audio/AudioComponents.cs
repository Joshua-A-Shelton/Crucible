using Crucible.Serialization;

namespace Crucible.Audio;

// The audio contract's components (ADR 0075), declared in C++
// (crucible/Audio.h); this half adds saving and the defaults a source starts
// with when it's added (all zeros would be silent).

/// <summary>A sound at its entity's position, or everywhere unless spatial.</summary>
[System.ComponentModel.Category("Audio"), System.ComponentModel.Description("Plays a sound, in 3D at the entity or flat.")]
[Serialized]
[Crucible.Scenes.ComponentIcon("volume-2")]
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

/// <summary>Where sounds are heard from (a tag): usually on the camera.</summary>
[System.ComponentModel.Category("Audio"), System.ComponentModel.Description("Where sounds are heard from: usually on the camera.")]
[Crucible.Scenes.ComponentIcon("ear")]
public partial struct AudioListener;
