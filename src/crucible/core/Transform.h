#ifndef CRUCIBLE_TRANSFORM_H
#define CRUCIBLE_TRANSFORM_H
#include <glm/glm.hpp>
#include <glm/gtc/quaternion.hpp>
namespace crucible
{
    namespace core
    {

        class Transform
        {
        public:
            Transform();
            Transform(const glm::vec3& position, const glm::quat& rotation, float scale);
            Transform(const glm::vec3& position, const glm::vec3& rotation, float scale);
            Transform(const glm::vec3& position, const glm::quat& rotation, const glm::vec3& scale);
            Transform(const glm::vec3& position, const glm::vec3& rotation, const glm::vec3& scale);
            ~Transform();
            Transform(const Transform& from);
            Transform& operator=(const Transform& from);
            Transform(Transform&& from);
            Transform& operator=(Transform&& from);

            void setPosition(float x, float y, float z);
            void setPosition(const glm::vec3& position);

            void setRotation(const glm::quat& rotation);
            void setRotation(const glm::vec3& euler);

            void setScale(float scale);
            void setScale(float x, float y, float z);
            void setScale(const glm::vec3& scale);

            void translate(float x, float y, float z);
            void translate(const glm::vec3& by);

            void rotate(const glm::quat& by);
            void rotate(float pitch, float yaw, float roll);
            void rotate(const glm::vec3& euler);

            void scale(float by);
            void scale(float x, float y, float z);
            void scale(const glm::vec3& by);

            glm::vec3 position()const;
            glm::quat rotation()const;
            glm::vec3 rotationEuler()const;
            glm::vec3 scale()const;
            glm::mat4 matrix();

        private:
            void copy(const Transform& from);
            void move(const Transform& from);
            void updateMatrix();
            glm::vec3 _position;
            glm::vec3 _scale;
            glm::quat _rotation;
            glm::mat4 _matrix;
            bool _needsUpdate = true;

        };

    } // core
} // crucible

#endif //CRUCIBLE_TRANSFORM_H
