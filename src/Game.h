#ifndef CRUCIBLE_GAME_H
#define CRUCIBLE_GAME_H
#include "CrucibleCore.h"
#include <SDL3/SDL.h>
#include <slag/SlagLib.h>
#include <filesystem>

namespace crucible
{

    class CRUCIBLE_API Game
    {
    public:
        Game(const std::string& name, const std::filesystem::path& icon, SDL_Window* inWindow = nullptr);
        ~Game();
        Game(const Game&)=delete;
        Game& operator=(const Game&)=delete;
        Game(Game&& from);
        Game& operator=(Game&& from);

    protected:
        void move(Game& from);
        virtual void handleEvent(SDL_Event& event);
        virtual void update(double deltaTime);
        virtual void draw(slag::CommandBuffer* commandBuffer, slag::Texture* drawBuffer);

        SDL_Window* _window = nullptr;
        bool _windowManaged = false;
        SDL_Surface* _icon = nullptr;
        slag::Swapchain* _swapChain = nullptr;
        void resize();
        void minimize();
    public:
        void run();
        void close();
    private:
        bool _keepOpen = true;


    };

} // crucible

#endif //CRUCIBLE_GAME_H
