#include <crucible/Crucible.h>
#include <crucible/Game.h>
#include <crucible/core/scenes/Node.h>
int main(int argc, char** argv)
{
    if(crucible::Crucible::initialize(crucible::Crucible::VULKAN,true))
    {
        crucible::Game game("Test Game","crucible.png");
        crucible::core::Node* node = crucible::core::Node::newNode(nullptr);
        crucible::core::Node* node2 = crucible::core::Node::newNode(node);
        delete node2;
        delete node;
        game.run();
    }
    crucible::Crucible::cleanup();

    return 0;
}