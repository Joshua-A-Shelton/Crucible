#ifndef CRUCIBLEEDITOR_GAME_H
#define CRUCIBLEEDITOR_GAME_H

#include <slag/SlagLib.h>
#include <SDL.h>
#include "Graphics/RenderRegistry.h"

namespace crucible
{

    class Game
    {
    public:
        void run(const char* gameName, const char* iconPath);
        slag::Swapchain* swapchain();
        SDL_Window* window();
    protected:
        bool running = true;
        RenderRegistry renderRegistry;

        virtual void initialize(const char* gameName,const char* iconPath);
        virtual void setUpSwapchain(slag::SwapchainBuilder& builder);
        virtual void processEvents();
        virtual void update(float deltaTime);
        virtual void render(slag::Frame* frame);
        virtual void cleanup();
        void handleResize();
    private:
        void gameLoop();
        void buildSwapchain(const char* gameName);
        void destroySwapchain();
        slag::Swapchain* _swapchain = nullptr;
        SDL_Window* _window = nullptr;

    };

} // crucible

#endif //CRUCIBLEEDITOR_GAME_H
