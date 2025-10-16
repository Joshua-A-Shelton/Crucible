//
// Created by josh on 10/15/25.
//

#include "Game.h"

#include "scripting/ScriptingEngine.h"

namespace crucible
{
    Game::Game()
    {
        if (_gameInitialized)
        {
            throw std::runtime_error("Tried to initialize more than one Game");
        }
        _gameInitialized = true;
        scripting::ScriptingEngine::gameManagerInitialize();
    }

    Game::~Game()
    {
        scripting::ScriptingEngine::gameManagerCleanUp();
        _gameInitialized = false;
    }

    Game::Game(Game&& from)
    {
        move(from);
    }

    Game& Game::operator=(Game&& from)
    {
        move(from);
        return *this;
    }

    void Game::move(Game& from)
    {

    }
} // crucible