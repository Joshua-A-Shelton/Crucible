#pragma once

#include <crucible/Export.h>
#include <crucible/Expose.h>

namespace crucible {

// Placeholder until real engine API exists: whether a Slag render backend was
// compiled in. Proves the core links against Slag and is callable from C#.
CRUCIBLE_EXPORT CRUCIBLE_EXPOSE bool hasRenderBackend();

} // namespace crucible
