using Crucible.Serialization;

namespace Crucible.Audio;

// The audio contract's components (ADR 0075), declared in C++
// (crucible/Audio.h); these halves add saving.

/// <summary>A sound at its entity's position, or everywhere unless spatial.</summary>
[Serialized]
public partial struct AudioSource;
