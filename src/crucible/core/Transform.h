#ifndef CRUCIBLE_TRANSFORM_H
#define CRUCIBLE_TRANSFORM_H
#include <glm/glm.hpp>
#include <glm/gtc/quaternion.hpp>
namespace crucible
{
    namespace core
    {
        ///Represents a position, rotation, and scale of an object in 3d space
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

            /**
             * Directly set transform's local position
             * @param x
             * @param y
             * @param z
             */
            void setPosition(float x, float y, float z);
            /**
             * Directly set transform's local position
             * @param position
             */
            void setPosition(const glm::vec3& position);
            /**
             * Directly set transform's local rotation via quaternion
             * @param rotation
             */
            void setRotation(const glm::quat& rotation);
            /**
             * Directly set transform's local rotation via euler angles
             * @param euler
             */
            void setRotation(const glm::vec3& euler);
            /**
             * Directly set transform's local scale
             * @param scale
             */
            void setScale(float scale);
            /**
             * Directly set transform's local scale
             * @param x
             * @param y
             * @param z
             */
            void setScale(float x, float y, float z);
            /**
             * Directly set transform's local scale
             * @param scale
             */
            void setScale(const glm::vec3& scale);
            /**
             * Translate transform along local axes
             * @param x
             * @param y
             * @param z
             */
            void translate(float x, float y, float z);
            /**
             * Translate transform along local axes
             * @param by
             */
            void translate(const glm::vec3& by);
            /**
             * Rotate transform along local axes
             * @param by
             */
            void rotate(const glm::quat& by);
            /**
             * Rotate transform along local pitch, yaw, and roll
             * @param pitch
             * @param yaw
             * @param roll
             */
            void rotate(float pitch, float yaw, float roll);
            /**
             * Rotate transform along local pitch, yaw, and roll
             * @param euler
             */
            void rotate(const glm::vec3& euler);
            /**
             * Locally scale by amount
             * @param by
             */
            void scale(float by);
            /**
             * Locally scale by amount
             * @param x
             * @param y
             * @param z
             */
            void scale(float x, float y, float z);
            /**
             * Locally scale by amount
             * @param by
             */
            void scale(const glm::vec3& by);
            /**
             * Transform's local position
             * @return
             */
            glm::vec3 position()const;
            /**
             * Transform's local rotation
             * @return
             */
            glm::quat rotation()const;
            /**
             * Transform's local rotation in Euler angles
             * @return
             */
            glm::vec3 rotationEuler()const;
            /**
             * Transform's local scale
             * @return
             */
            glm::vec3 scale()const;
            /**
             * Matrix that represents the local transform
             * @return
             */
            glm::mat4 matrix();
            ///update the matrix based on components, called automatically when retrieving matrix, but this can be used in the scripting side
            void updateMatrix();

        private:
            inline static glm::mat4 (*getMatrix)(Transform& transform)= nullptr;


            void copy(const Transform& from);
            void move(const Transform& from);

            glm::vec3 _position;
            glm::vec3 _scale;
            glm::quat _rotation;
            glm::mat4 _matrix;
            bool _needsUpdate = true;

        };

    } // core
} // crucible

#endif //CRUCIBLE_TRANSFORM_H
