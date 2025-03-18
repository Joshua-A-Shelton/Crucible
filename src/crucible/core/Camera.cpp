#include "Camera.h"

#include <glm/ext/matrix_clip_space.hpp>

namespace crucible
{
    namespace core
    {
        Camera::Camera(float near, float far, float fov, float width, float height, bool isPerspective)
        {
            _nearPlane = near;
            _farPlane = far;
            _fieldOfView = fov;
            _width = width;
            _height = height;
            _isPerspective = isPerspective;
            recalcMatrix();

        }

        Camera::Camera(const Camera& camera)
        {
            copy(camera);
        }

        Camera& Camera::operator=(const Camera& camera)
        {
            copy(camera);
            return *this;
        }

        Camera::Camera(Camera&& camera)
        {
            copy(camera);
        }

        Camera& Camera::operator=(Camera&& camera)
        {
            copy(camera);
            return *this;
        }

        void Camera::copy(const Camera& camera)
        {
            _nearPlane = camera._nearPlane;
            _farPlane = camera._farPlane;
            _fieldOfView = camera._fieldOfView;
            _width = camera._width;
            _height = camera._height;
            _isPerspective = camera._isPerspective;
            _projectionMatrix = camera._projectionMatrix;
        }

        float Camera::nearPlane() const
        {
            return _nearPlane;
        }

        void Camera::nearPlane(float value)
        {
            _nearPlane = value;
            recalcMatrix();
        }

        float Camera::farPlane() const
        {
            return _farPlane;
        }

        void Camera::farPlane(float value)
        {
            _farPlane = value;
            recalcMatrix();
        }

        float Camera::fov() const
        {
            return _fieldOfView;
        }

        void Camera::fov(float value)
        {
            _fieldOfView = value;
            recalcMatrix();
        }

        float Camera::width() const
        {
            return _width;
        }

        void Camera::width(float value)
        {
            _width = value;
            recalcMatrix();
        }

        float Camera::height() const
        {
            return _height;
        }

        void Camera::height(float value)
        {
            _height = value;
            recalcMatrix();
        }

        bool Camera::isPerspective() const
        {
            return _isPerspective;
        }

        void Camera::isPerspective(bool value)
        {
            _isPerspective = value;
            recalcMatrix();
        }


        void Camera::recalcMatrix()
        {
            if (_isPerspective)
            {
                _projectionMatrix = glm::perspective(_fieldOfView,_width/_height,_nearPlane,_farPlane);
            }
            else
            {
                _projectionMatrix = glm::ortho(-_width/2,_width/2,-_height/2,_height/2,_nearPlane,_farPlane);
            }
        }
    } // core
} // crucible