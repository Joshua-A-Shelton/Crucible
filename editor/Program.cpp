#include <crucible/Engine.h>

int main()
{
    crucible::Engine::initialize();
    crucible::Window mainWindow(500,500,2, true,"MainWindow");
    mainWindow.open();
    crucible::Engine::cleanup();
}