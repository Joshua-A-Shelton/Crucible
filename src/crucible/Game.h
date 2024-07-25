#ifndef CRUCIBLEEDITOR_GAME_H
#define CRUCIBLEEDITOR_GAME_H

#include <slag/SlagLib.h>
#include <SDL.h>

namespace crucible
{

    class Game
    {
    public:
        Game(void* fromHandle = nullptr);
        void run(const char* gameName, const char* iconPath);
        void runFrame(float deltaTime);
        slag::Swapchain* swapchain();
        SDL_Window* window();
    protected:
        bool running = true;

        virtual void initialize(const char* gameName,const char* iconPath);
        virtual void setUpSwapchain(slag::SwapchainBuilder& builder);
        virtual void processEvents();
        virtual void update(float deltaTime);
        virtual void render(slag::Frame* frame);
        virtual void cleanup();
        void handleResize();
        //call before runFrame
        void setupResources(const char* gameName, const char* iconPath);
        //call after last case of runFrame
        void teardownResources();
    private:
        void gameLoop();
        void buildSwapchain(const char* gameName);
        void destroySwapchain();
        slag::Swapchain* _swapchain = nullptr;
        SDL_Window* _window = nullptr;
        void* _fromHandle = nullptr;

    };

} // crucible

#endif //CRUCIBLEEDITOR_GAME_H
