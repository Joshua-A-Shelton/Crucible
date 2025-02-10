#include <crucible/Crucible.h>
#include <crucible/Game.h>
int main(int argc, char** argv)
{
    if(crucible::Crucible::initialize(crucible::Crucible::VULKAN,true))
    {
        crucible::Game game("Test Game","crucible.png");
        game.run();
    }
    crucible::Crucible::cleanup();

    return 0;
}