#ifndef CRUCIBLE_TRANSFORM_H
#define CRUCIBLE_TRANSFORM_H
#include <glm/glm.hpp>
#include <glm/gtc/quaternion.hpp>
namespace crucible
{
    namespace core
    {
        class Node;
        ///Represents a position, rotation, and scale of an object in 3d space
        class Transform
        {
        public:
            Transform();
            explicit Transform(const unsigned char* uncompressedBytes);
            Transform(const glm::vec3& position, const glm::quat& rotation, float scale);
            Transform(const glm::vec3& position, const glm::vec3& rotation, float scale);
            Transform(const glm::vec3& position, const glm::quat& rotation, const glm::vec3& scale);
            Transform(const glm::vec3& position, const glm::vec3& rotation, const glm::vec3& scale);

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
             * Rotate transform along axis by an angle
             * @param angle
             * @param axis
             */
            void rotate(float angle, const glm::vec3& axis);
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

            Transform inverse() const;

            Transform toGlobal(Node* relativeTo) const;

            static Transform cumulativeFrom(Node* node);

            Transform operator+(const Transform& with)const;
            Transform operator-(const Transform& from)const;
            Transform fastConcat(const Transform& with)const;

            bool operator==(const Transform& other)const;
            bool approximateEquals(const Transform& other, float epsilon=.001)const;

            Transform littleEndian();
            Transform bigEndian();


        private:
            inline static glm::mat4 (*getMatrix)(Transform& transform)= nullptr;

            inline void copy(const Transform& from);

            glm::vec3 _position;
            glm::quat _rotation;
            glm::vec3 _scale;

        };

    } // core
} // crucible

#endif //CRUCIBLE_TRANSFORM_H
