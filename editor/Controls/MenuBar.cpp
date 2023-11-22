#include "MenuBar.h"
#include "imgui.h"

namespace crucible
{
    namespace controls
    {
        void MenuBar::show()
        {
            ImGui::BeginMainMenuBar();
            if(ImGui::MenuItem("File"))
            {

            }
            ImGui::EndMainMenuBar();
        }
    }
} // crucible