#ifndef CRUCIBLE_API_RENDERING_H
#define CRUCIBLE_API_RENDERING_H

#ifdef CRUCIBLE_RENDERING_MODULE
#include <cstdint>
#include <slag/Slag.h>
#include "core/Mesh.h"
#include "core/Window.h"

#include <crucible/core/CrucibleAPI.h>

namespace crucible
{
    enum class CrucibleInitializationResult;
}

namespace crucible
{
    namespace rendering
    {
        CrucibleInitializationResult initializeRenderingSubmodule();
        void cleanupRenderingSubmodule();
        slag::GraphicsCard* getGraphicsCard();
    }
}

extern "C"
{
//Window
    CRUCIBLE_API crucible::rendering::Window* CRUCIBLE_API_createWindow(const char* name, uint32_t width, uint32_t height, crucible::rendering::Window* parent, crucible::rendering::WindowDecorationMode decorationMode, crucible::rendering::WindowTransparency transparency);
    CRUCIBLE_API void CRUCIBLE_API_destroyWindow(crucible::rendering::Window* window);
    CRUCIBLE_API void CRUCIBLE_API_showWindow(crucible::rendering::Window* window);
    CRUCIBLE_API void CRUCIBLE_API_setWindowPresentMode(crucible::rendering::CRUCIBLE_WINDOW_PRESENT_MODE mode);
    CRUCIBLE_API crucible::rendering::CRUCIBLE_WINDOW_PRESENT_MODE CRUCIBLE_API_getWindowPresentMode();

//Textures
    slag::Texture* CRUCIBLE_API_createTexture1D(slag::PixelFormat format, slag::TextureUsageFlags usageFlags, uint32_t width, uint32_t mipLevels);
    slag::Texture* CRUCIBLE_API_createTexture2D(slag::PixelFormat format, slag::TextureUsageFlags usageFlags, uint32_t width, uint32_t height, uint32_t mipLevels);
    slag::Texture* CRUCIBLE_API_createTexture2DArray(slag::PixelFormat format, slag::TextureUsageFlags usageFlags, uint32_t width, uint32_t height, uint32_t mipLevels, uint32_t arrayLayers);
    void CRUCIBLE_API_destroyTexture(slag::Texture* texture);


//Meshes
    crucible::rendering::MeshLoadResult CRUCIBLE_API_createMeshFromFile(const unsigned char* data, crucible::rendering::VertexAttributeFlags cpuAccessibleAttributes, crucible::rendering::Mesh* outMesh);
    crucible::rendering::MeshLoadResult CRUCIBLE_API_createMeshFromVertexData(const crucible::rendering::VertexDataStreams& vertexStreams, crucible::rendering::VertexAttributeFlags cpuAccessibleAttributes, crucible::rendering::Mesh* outMesh);
    void CRUCIBLE_API_destroyMesh(crucible::rendering::Mesh* mesh);

}

#endif //CRUCIBLE_RENDERING_MODULE

#endif //CRUCIBLE_API_RENDERING_H