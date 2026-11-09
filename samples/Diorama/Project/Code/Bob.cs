using Crucible.Ecs;
using Crucible.Serialization;

namespace Diorama;

// Bob is declared in C++ (Native/Bob.h, ADR 0074); the binding generator
// writes its fields here, and this half registers and saves it.
[Component, Serialized]
public partial struct Bob;
