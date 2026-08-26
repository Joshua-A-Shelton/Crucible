#include <gtest/gtest.h>
#include <crucible/Crucible.h>
#ifdef CRUCIBLE_RENDERING_MODULE
TEST(RenderingModule, Window)
{
    auto window = CRUCIBLE_API_createWindow("Window Test",800,600,crucible::rendering::WindowDecorationMode::BORDERED,crucible::rendering::WindowTransparency::PRE_MULTIPLIED);
    CRUCIBLE_API_showWindow(window);
    std::this_thread::sleep_for(std::chrono::seconds(5));
    CRUCIBLE_API_destroyWindow(window);
}
#endif