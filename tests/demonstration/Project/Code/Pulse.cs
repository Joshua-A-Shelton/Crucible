using Crucible.Ecs;
using Crucible.Serialization;

namespace Demo;

// Pulse is declared in C++ (Native/Pulse.h, ADR 0074); the binding
// generator writes its fields, and this half registers and saves it.
[Component, Serialized]
public partial struct Pulse;
