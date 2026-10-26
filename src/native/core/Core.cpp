#include <crucible/Core.h>

#include <slag/Instance.h>

namespace crucible {

bool hasRenderBackend()
{
    // Checks Slag's compiled-in backend registry; no GPU is needed.
    return slag::isBackendAvailable(slag::Backend::Vulkan) || slag::isBackendAvailable(slag::Backend::D3D12);
}

} // namespace crucible
