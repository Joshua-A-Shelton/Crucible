#ifndef CRUCIBLE_WINDOW_H
#define CRUCIBLE_WINDOW_H
#include <SDL3/SDL.h>
#include <slag/SlagLib.h>
namespace crucible
{
    namespace core
    {
        class Window
        {
        private:
            SDL_Window* _sdlHandle = nullptr;
            slag::Swapchain* _swapchain = nullptr;
        public:
            Window(const std::string& title, uint32_t width, uint32_t height, slag::Swapchain::PresentMode presentMode);
            ~Window();
            Window(const Window&)=delete;
            Window& operator=(const Window&)=delete;
            Window(Window&& from);
            Window& operator=(Window&& from);
            void run();
        private:
            void move(Window& from);
        };

    } // core
} // crucible

#endif //CRUCIBLE_WINDOW_H
