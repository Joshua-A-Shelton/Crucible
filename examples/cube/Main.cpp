#include <crucible/Game.h>

#include <crucible/Crucible.h>
using namespace crucible;
int main()
{
    if (Crucible::initialize(Crucible::VULKAN,true))
    {
        Game game("Cube Example","","plugins/CubeExample.dll");
        game.run();
    }
    Crucible::cleanup();
    return 0;
}