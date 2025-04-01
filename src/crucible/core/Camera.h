#ifndef CRUCIBLE_CAMERA_H
#define CRUCIBLE_CAMERA_H
#include <vector>
#include <glm/glm.hpp>

#include "slag/Texture.h"

namespace crucible
{
    namespace core
    {
        class Camera
        {
        public:
            Camera(float near, float far, float fov, float width, float height, bool isPerspective, uint8_t renderOrder, uint32_t textureWidth, uint32_t textureHeight, slag::Pixels::Format colorFormat, uint8_t multiSamplePixels);
            ~Camera();
            Camera(const Camera& camera)=delete;
            Camera& operator=(const Camera& camera)=delete;
            Camera(Camera&& camera);
            Camera& operator=(Camera&& camera);

            float nearPlane() const;
            void nearPlane(float value);
            float farPlane() const;
            void farPlane(float value);
            float fov() const;
            void fov(float value);
            float width() const;
            void width(float value);
            float height() const;
            void height(float value);
            bool isPerspective() const;
            void isPerspective(bool value);
            uint8_t renderOrder() const;
            void renderOrder(uint8_t value);
            slag::Texture* colorTarget() const;
            slag::Texture* depthTarget() const;
            static const std::vector<core::Camera*>& cameras();
            static bool compare(core::Camera* a, core::Camera* b);

        private:
            void move(Camera& camera);
            void recalcMatrix();
            float _nearPlane;
            float _farPlane;
            float _fieldOfView;
            float _width;
            float _height;
            bool _isPerspective;
            uint8_t _renderOrder;
            glm::mat4 _projectionMatrix;
            slag::Texture* _colorTarget=nullptr;
            slag::Texture* _depthTarget=nullptr;
            inline static std::vector<core::Camera*> _allCameras;
        };
    } // core
} // crucible

#endif //CRUCIBLE_CAMERA_H
