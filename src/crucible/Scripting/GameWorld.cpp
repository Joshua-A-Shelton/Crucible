#include "GameWorld.h"
#include "ScriptingEngine.h"
namespace crucible
{
    void(*getMainCameraData)(CameraViewData* cameraView, glm::mat4* cameraPosition);

    void GameWorld::getManagedFunctions()
    {
        auto gameWorldType = ScriptingEngine::getManagedType("Crucible.GameWorld, Crucible-Runtime");
        getMainCameraData = gameWorldType.getFunction<void(*)(CameraViewData*,glm::mat4*)>("GetMainCamera");
    }

    glm::mat4 GameWorld::getMainCamera()
    {
        CameraViewData viewData;
        glm::mat4 position(1);
        getMainCameraData(&viewData,&position);
        glm::mat4 proj = viewData.projectionMatrix();
        return position * proj;
    }
} // crucible