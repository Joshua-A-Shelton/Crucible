#ifndef CRUCIBLE_WINDOW_H
#define CRUCIBLE_WINDOW_H
#include <slag/SlagLib.h>
#include <SDL.h>
#include <functional>

namespace crucible
{
    class Window
    {
    public:
        Window(uint32_t width, uint32_t height, uint32_t backBuffers, bool vysncEnabled, const char* name);
        ~Window();
        int width();
        int height();
        std::string name();
        void open();
        std::function<void(slag::Frame* frame, double delta)> update= nullptr;
    private:
        SDL_Window* _window = nullptr;
        slag::Swapchain* _swapchain = nullptr;
    };
} // crucible
#endif //CRUCIBLE_WINDOW_H