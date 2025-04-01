#include "Camera.h"

#include <glm/ext/matrix_clip_space.hpp>
#include "scenes/World.h"

namespace crucible
{
    namespace core
    {
        Camera::Camera(float near, float far, float fov, float width, float height, bool isPerspective, uint8_t renderOrder, uint32_t textureWidth, uint32_t textureHeight, slag::Pixels::Format colorFormat, uint8_t multiSamplePixels)
        {
            _nearPlane = near;
            _farPlane = far;
            _fieldOfView = fov;
            _width = width;
            _height = height;
            _isPerspective = isPerspective;
            _renderOrder = renderOrder;
            recalcMatrix();
            _colorTarget = slag::Texture::newTexture(colorFormat,slag::Texture::TEXTURE_2D,textureWidth,textureHeight,1,1,multiSamplePixels,slag::TextureUsageFlags::RENDER_TARGET_ATTACHMENT | slag::TextureUsageFlags::SAMPLED_IMAGE);
            _depthTarget = slag::Texture::newTexture(slag::Pixels::D32_FLOAT,slag::Texture::TEXTURE_2D,textureWidth,textureHeight,1,1,multiSamplePixels,slag::TextureUsageFlags::DEPTH_STENCIL_ATTACHMENT);
            _allCameras.emplace_back(this);

        }

        Camera::~Camera()
        {
            delete _colorTarget;
            delete _depthTarget;
            _allCameras.erase(std::find(_allCameras.begin(), _allCameras.end(), this));
        }


        Camera::Camera(Camera&& camera)
        {
            move(camera);
        }

        Camera& Camera::operator=(Camera&& camera)
        {
            move(camera);
            return *this;
        }

        void Camera::move(Camera& camera)
        {
            _nearPlane = camera._nearPlane;
            _farPlane = camera._farPlane;
            _fieldOfView = camera._fieldOfView;
            _width = camera._width;
            _height = camera._height;
            _isPerspective = camera._isPerspective;
            _projectionMatrix = camera._projectionMatrix;
            _renderOrder = camera._renderOrder;
            std::swap(_colorTarget,camera._colorTarget);
            std::swap(_depthTarget,camera._depthTarget);
            _allCameras.erase(std::find(_allCameras.begin(), _allCameras.end(), &camera));
            _allCameras.emplace_back(this);
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

        uint8_t Camera::renderOrder() const
        {
            return _renderOrder;
        }

        void Camera::renderOrder(uint8_t value)
        {
            _renderOrder = value;
        }

        slag::Texture* Camera::colorTarget() const
        {
            return _colorTarget;
        }

        slag::Texture* Camera::depthTarget() const
        {
            return _depthTarget;
        }

        const std::vector<core::Camera*>& Camera::cameras()
        {
            return _allCameras;
        }

        bool Camera::compare(core::Camera* a, core::Camera* b)
        {
            return a->_renderOrder < b->_renderOrder;
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