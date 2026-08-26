#include <crucible/Crucible.h>

int main()
{
    crucible::CrucibleInitParams initParams{};
    initParams.scriptingDLLPath = "NativeExample.dll";
    auto initResult = crucible::Crucible::initialize(initParams);
    if (initResult == crucible::CrucibleInitializationResult::SUCCESS)
    {
        crucible::Crucible::cleanup();
    }
}