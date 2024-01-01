#ifndef CRUCIBLEEDITOR_CAMERAVIEWDATA_H
#define CRUCIBLEEDITOR_CAMERAVIEWDATA_H
#include <glm/glm.hpp>
namespace crucible
{
    struct CameraViewData
    {
    public:
        float fieldOfView = 90;
        float nearPlane = 0.001;
        float farPlane = 500;
        float aspectRatio = 16.0f/9.0f;
        glm::mat4 projectionMatrix();
    };
}
#endif //CRUCIBLEEDITOR_CAMERAVIEWDATA_H
