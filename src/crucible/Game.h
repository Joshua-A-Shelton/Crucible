#ifndef CRUCIBLEEDITOR_GAME_H
#define CRUCIBLEEDITOR_GAME_H

#include <slag/SlagLib.h>
#include <SDL.h>
namespace crucible
{

    class Game
    {
    public:
        void run(const char* gameName, const char* iconPath);
    protected:
        bool running = true;
        slag::Swapchain* swapchain = nullptr;
        SDL_Window* window = nullptr;
        virtual void initialize(const char* gameName,const char* iconPath);
        virtual void processEvents();
        virtual void update(float deltaTime);
        virtual void render(slag::Frame* frame);
        virtual void cleanup();

    };

} // crucible

#endif //CRUCIBLEEDITOR_GAME_H
