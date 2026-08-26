#ifndef CRUCIBLE_API_RENDERING_H
#define CRUCIBLE_API_RENDERING_H

#ifdef CRUCIBLE_RENDERING_MODULE
#include <cstdint>
#include <slag/Slag.h>
#include "core/Window.h"
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
    crucible::rendering::Window* CRUCIBLE_API_createWindow(const char* name, uint32_t width, uint32_t height, crucible::rendering::WindowDecorationMode decorationMode, crucible::rendering::WindowTransparency transparency);
    void CRUCIBLE_API_destroyWindow(crucible::rendering::Window* window);

    void CRUCIBLE_API_showWindow(crucible::rendering::Window* window);

    void CRUCIBLE_API_setWindowPresentMode(crucible::rendering::CRUCIBLE_WINDOW_PRESENT_MODE mode);
    crucible::rendering::CRUCIBLE_WINDOW_PRESENT_MODE CRUCIBLE_API_getWindowPresentMode();
}

#endif //CRUCIBLE_RENDERING_MODULE

#endif //CRUCIBLE_API_RENDERING_H