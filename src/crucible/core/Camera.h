#ifndef CRUCIBLE_CAMERA_H
#define CRUCIBLE_CAMERA_H
#include <glm/glm.hpp>
namespace crucible
{
    namespace core
    {
        class Camera
        {
        public:
            Camera(float near, float far, float fov, float width, float height, bool isPerspective);
            Camera(const Camera& camera);
            Camera& operator=(const Camera& camera);
            Camera(Camera&& camera);
            Camera& operator=(Camera&& camera);
            ~Camera()=default;

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

        private:
            void copy(const Camera& camera);
            void recalcMatrix();
            float _nearPlane;
            float _farPlane;
            float _fieldOfView;
            float _width;
            float _height;
            bool _isPerspective;
            glm::mat4 _projectionMatrix;
        };
    } // core
} // crucible

#endif //CRUCIBLE_CAMERA_H
