#ifndef CRUCIBLE_GAME_H
#define CRUCIBLE_GAME_H
#include <filesystem>
#include <queue>
#include <string>

#include "CrucibleCore.h"
#include <slag/Slag.h>
#include <SDL3/SDL.h>

namespace crucible
{
    class Node;
}

namespace crucible
{
    class CRUCIBLE_API Game
    {
    private:
        static inline Game* _instance = nullptr;
    public:
        Game(const char* gameName);
        ~Game();
        Game(const Game&) = delete;
        Game& operator=(const Game&) = delete;
        Game(Game&& from)=delete;
        Game& operator=(Game&& from)=delete;
        static Game* instance();

        void run();

        void queueForDeletion(slag::Texture* texture);
        void queueForDeletion(slag::Buffer* buffer);
        void queueForDeletion(crucible::Node* node);

        void signalQuit();
    private:
        slag::SwapChain* _swapChain = nullptr;
        SDL_Window* _window = nullptr;
        bool _isRunning = true;
        std::queue<crucible::Node*> _nodeDeletionQueue;
        void handleEvent(SDL_Event& e);
        void update(double deltaTime);
        void draw(slag::CommandBuffer* commandBuffer, slag::DescriptorPool* descriptorPool);
        void close();
        void resize();
        void minimize();

    };
} // crucible

#endif //CRUCIBLE_GAME_H