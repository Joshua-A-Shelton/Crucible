#pragma once

// The Diorama's native module (ADR 0074): a C++ component and system, to
// show a project's own C++ beside its C# (Code/).

#include <crucible/Expose.h>

#include <cstdint>

namespace diorama {

// Moves its entity up and down around where it was placed: the lamp posts'
// lights bobbing.
struct CRUCIBLE_EXPOSE Bob
{
    CRUCIBLE_HINT_RANGE(0, 1) float height; // how far above and below, in meters
    CRUCIBLE_HINT_RANGE(0, 4) float speed;  // bobs per second
};

// How many entities the Bob system moved last frame: C# calling into the
// module, through the bindings in the game assembly.
CRUCIBLE_EXPOSE uint32_t bobbedLastFrame();

} // namespace diorama
