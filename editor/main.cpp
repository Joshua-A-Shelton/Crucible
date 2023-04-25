#define SDL_MAIN_HANDLED
#include "CrucibleLib.h"
#include "Window.h"

int main()
{

    if(crucible::CrucibleLib::init())
    {
        crucible::Window window(800,500,"Crucible");
        window.show();
    }
    crucible::CrucibleLib::cleanup();
}
