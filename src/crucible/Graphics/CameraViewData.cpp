#include "CameraViewData.h"
#include <glm/gtc/matrix_transform.hpp>
namespace crucible
{

    glm::mat4 CameraViewData::projectionMatrix()
    {
        return glm::perspective(fieldOfView,aspectRatio,nearPlane,farPlane);
    }
}