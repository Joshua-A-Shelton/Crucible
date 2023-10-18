#define SDL_MAIN_HANDLED
#include <crucible/Engine.h>

int main()
{
    crucible::Engine::initialize();
    crucible::Engine::cleanup();
}