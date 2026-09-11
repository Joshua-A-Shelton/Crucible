#ifndef CRUCIBLE_WINDOW_H
#define CRUCIBLE_WINDOW_H
#include <slag/Slag.h>
#include <SDL3/SDL.h>
#undef OPAQUE
#undef TRANSPARENT
namespace crucible
{
    namespace rendering
    {
        inline const char* OWNING_CRUCIBLE_WINDOW_OBJECT_ID = "CRUCIBLE_WINDOW_REFERENCE";
        enum class CRUCIBLE_WINDOW_PRESENT_MODE
        {
            TEAR = 0,
            DOUBLE_BUFFER = 1,
            TRIPLE_BUFFER = 2
        };
        enum class WindowDecorationMode
        {
            BORDERED,
            BORDERLESS
        };
        enum class WindowTransparency
        {
            OPAQUE,
            PRE_MULTIPLIED,
        };
        class Window
        {
        public:

            Window(const std::string& name, uint32_t width, uint32_t height, Window* parentWindow=nullptr, WindowDecorationMode decorationMode = WindowDecorationMode::BORDERED, WindowTransparency transparency = WindowTransparency::PRE_MULTIPLIED);
            ~Window();
            Window(const Window&) = delete;
            Window& operator=(const Window&) = delete;
            Window(Window&&) = delete;
            Window& operator=(Window&&) =delete;

            void show() const;
            void attemptClose();
            void rebuildRenderTargets(uint32_t width, uint32_t height);

            Window* parent() const;
            slag::SwapChain* swapChain() const;


            static void updateWindowPresentMode(CRUCIBLE_WINDOW_PRESENT_MODE newPresentMode);
            static CRUCIBLE_WINDOW_PRESENT_MODE getWindowPresentMode();
            static std::vector<Window*> getOpenedWindows();

        private:

            SDL_Window* _window=nullptr;
            slag::SwapChain* _swapChain=nullptr;
            slag::Texture* _renderBuffer=nullptr;
            slag::Texture* _depthBuffer=nullptr;
        };
    }// rendering

} // crucible

#endif //CRUCIBLE_WINDOW_H