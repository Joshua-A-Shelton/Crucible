// Exports the D3D12 Agility SDK's version and folder from the executable
// (Slag ADR 0010). Windows loads Slag's pinned D3D12 runtime from the D3D12\
// folder next to the executable only when the executable itself exports
// these; a library can't. Added by crucible_use_d3d12_agility().

#include <slag/native/D3D12Agility.h>

SLAG_D3D12_AGILITY_SDK_EXPORTS(CRUCIBLE_AGILITY_SDK_VERSION, ".\\D3D12\\")
