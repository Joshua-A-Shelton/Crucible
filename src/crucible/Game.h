#ifndef CRUCIBLE_GAME_H
#define CRUCIBLE_GAME_H
#include <filesystem>
#include <string>

#include "CrucibleCore.h"
#include <SDL3/SDL.h>

namespace crucible
{
    CRUCIBLE_API class Game
    {
    private:
        static inline bool _gameInitialized = false;
    public:
        Game();
        ~Game();
        Game(const Game&) = delete;
        Game& operator=(const Game&) = delete;
        Game(Game&& from);
        Game& operator=(Game&& from);
    private:
        void move(Game& from);

    };
} // crucible

#endif //CRUCIBLE_GAME_H