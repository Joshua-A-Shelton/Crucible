#include "Transform.h"

namespace crucible
{
    namespace core
    {
        Transform::Transform()
        {
            _position = glm::vec3(0,0,0);
            _rotation = glm::quat(1,0,0,0);
            _scale = glm::vec3(1,1,1);
            _matrix = glm::mat4(1);
            _needsUpdate=false;
        }

        Transform::Transform(const glm::vec3& position, const glm::quat& rotation, float scale)
        {
            _position = position;
            _rotation = rotation;
            _scale = glm::vec3(scale,scale,scale);
            updateMatrix();
        }

        Transform::Transform(const glm::vec3& position, const glm::vec3& rotation, float scale)
        {
            _position = position;
            _rotation = glm::quat(rotation);
            _scale = glm::vec3(scale,scale,scale);
            updateMatrix();
        }

        Transform::Transform(const glm::vec3& position, const glm::quat& rotation, const glm::vec3& scale)
        {
            _position = position;
            _rotation = rotation;
            _scale = scale;
            updateMatrix();
        }

        Transform::Transform(const glm::vec3& position, const glm::vec3& rotation, const glm::vec3& scale)
        {
            _position = position;
            _rotation = glm::quat(rotation);
            _scale = scale;
            updateMatrix();
        }

        Transform::~Transform()
        {

        }

        Transform::Transform(const Transform& from)
        {
            copy(from);
        }

        Transform& Transform::operator=(const Transform& from)
        {
            copy(from);
            return *this;
        }

        Transform::Transform(Transform&& from)
        {
            move(from);
        }

        Transform& Transform::operator=(Transform&& from)
        {
            move(from);
            return *this;
        }

        void Transform::copy(const Transform& from)
        {
            _position = from._position;
            _rotation = from._rotation;
            _scale = from._scale;
            _matrix = from._matrix;
            _needsUpdate = from._needsUpdate;
        }

        void Transform::move(const Transform& from)
        {
            _position = from._position;
            _rotation = from._rotation;
            _scale = from._scale;
            _matrix = from._matrix;
            _needsUpdate = from._needsUpdate;
        }

        void Transform::updateMatrix()
        {
            if(_needsUpdate)
            {
                auto translation = glm::translate(glm::mat4(1),_position);
                auto rotation = glm::mat4(_rotation);
                auto scale = glm::scale(glm::mat4(1),_scale);
                _matrix = translation*rotation*scale;
                _needsUpdate = false;
            }
        }

        void Transform::setPosition(float x, float y, float z)
        {
            _position.x = x;
            _position.y = y;
            _position.z = x;
            _needsUpdate = true;
        }

        void Transform::setPosition(const glm::vec3& position)
        {
            _position = position;
            _needsUpdate = true;
        }

        void Transform::setRotation(const glm::quat& rotation)
        {
            _rotation = rotation;
            _needsUpdate = true;
        }

        void Transform::setRotation(const glm::vec3& euler)
        {
            _rotation = glm::quat(euler);
            _needsUpdate = true;
        }

        void Transform::setScale(float scale)
        {
            _scale.x = scale;
            _scale.y = scale;
            _scale.z = scale;
            _needsUpdate = true;
        }

        void Transform::setScale(float x, float y, float z)
        {
            _scale.x = x;
            _scale.y = y;
            _scale.z = z;
            _needsUpdate = true;
        }

        void Transform::setScale(const glm::vec3& scale)
        {
            _scale = scale;
            _needsUpdate = true;
        }

        void Transform::translate(float x, float y, float z)
        {
            _position.x+=x;
            _position.y+=y;
            _position.z+=z;
            _needsUpdate = true;
        }

        void Transform::translate(const glm::vec3& by)
        {
            _position+=by;
            _needsUpdate=true;
        }

        void Transform::rotate(const glm::quat& by)
        {
            _rotation*=by;
            _needsUpdate = true;
        }

        void Transform::rotate(float pitch, float yaw, float roll)
        {
            _rotation*=glm::quat(glm::vec3(pitch,yaw,roll));
            _needsUpdate = true;
        }

        void Transform::rotate(const glm::vec3& euler)
        {
            _rotation*=glm::quat(euler);
            _needsUpdate = true;
        }

        void Transform::scale(float by)
        {
            _scale*=by;
        }

        void Transform::scale(float x, float y, float z)
        {
            _scale.x*=x;
            _scale.y*=y;
            _scale.z*=z;
            _needsUpdate = true;
        }

        void Transform::scale(const glm::vec3& by)
        {
            _scale.x*=by.x;
            _scale.y*=by.y;
            _scale.z*=by.z;
            _needsUpdate = true;
        }

        glm::vec3 Transform::position() const
        {
            return _position;
        }

        glm::quat Transform::rotation() const
        {
            return _rotation;
        }

        glm::vec3 Transform::rotationEuler() const
        {
            return glm::eulerAngles(_rotation);
        }

        glm::vec3 Transform::scale() const
        {
            return _scale;
        }

        glm::mat4 Transform::matrix()
        {
            updateMatrix();
            return _matrix;
        }
    } // core
} // crucible