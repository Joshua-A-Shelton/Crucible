#define SDL_MAIN_HANDLED
#include <crucible/Engine.h>

int main(int argc, char** args)
{
    crucible::Engine::initialize();
    crucible::Engine::cleanup();
}