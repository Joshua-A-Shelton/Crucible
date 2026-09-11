#include <crucible/Crucible.h>

#include "crucible/update/API_Update.h"

int main()
{
    crucible::CrucibleInitParams initParams{};
    initParams.scriptingDLLPath = "Example.dll";
    if (crucible::Crucible::initialize(initParams) == crucible::CrucibleInitializationResult::SUCCESS)
    {
        crucible::update::run();
        crucible::Crucible::cleanup();
    }
    return 0;
}
