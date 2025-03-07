#include "Transform.h"
#include <crucible/core/scenes/Node.h>
#include <crucible/core/scenes/World.h>
#include <cstring>
#include <stack>

namespace crucible
{
    namespace core
    {
        Transform::Transform()
        {
            _position = glm::vec3(0,0,0);
            _rotation = glm::quat(1,0,0,0);
            _scale = glm::vec3(1,1,1);
        }

        Transform::Transform(const glm::vec3& position, const glm::quat& rotation, float scale)
        {
            _position = position;
            _rotation = rotation;
            _scale = glm::vec3(scale,scale,scale);
        }

        Transform::Transform(const glm::vec3& position, const glm::vec3& rotation, float scale)
        {
            _position = position;
            _rotation = glm::quat(rotation);
            _scale = glm::vec3(scale,scale,scale);
        }

        Transform::Transform(const glm::vec3& position, const glm::quat& rotation, const glm::vec3& scale)
        {
            _position = position;
            _rotation = rotation;
            _scale = scale;
        }

        Transform::Transform(const glm::vec3& position, const glm::vec3& rotation, const glm::vec3& scale)
        {
            _position = position;
            _rotation = glm::quat(rotation);
            _scale = scale;
        }

        Transform::~Transform()
        {
            int i=0;
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
            copy(from);
        }

        Transform& Transform::operator=(Transform&& from)
        {
            copy(from);
            return *this;
        }

        void Transform::copy(const Transform& from)
        {
            memcpy(this,&from,sizeof(Transform));
        }

        void Transform::setPosition(float x, float y, float z)
        {
            _position.x = x;
            _position.y = y;
            _position.z = z;
        }

        void Transform::setPosition(const glm::vec3& position)
        {
            _position = position;
        }

        void Transform::setRotation(const glm::quat& rotation)
        {
            _rotation = rotation;
        }

        void Transform::setRotation(const glm::vec3& euler)
        {
            _rotation = glm::quat(euler);
        }

        void Transform::setScale(float scale)
        {
            _scale.x = scale;
            _scale.y = scale;
            _scale.z = scale;
        }

        void Transform::setScale(float x, float y, float z)
        {
            _scale.x = x;
            _scale.y = y;
            _scale.z = z;
        }

        void Transform::setScale(const glm::vec3& scale)
        {
            _scale = scale;
        }

        void Transform::translate(float x, float y, float z)
        {
            _position.x+=x;
            _position.y+=y;
            _position.z+=z;
        }

        void Transform::translate(const glm::vec3& by)
        {
            _position+=by;
        }

        void Transform::rotate(const glm::quat& by)
        {
            _rotation*=by;
        }

        void Transform::rotate(float pitch, float yaw, float roll)
        {
            _rotation*=glm::quat(glm::vec3(pitch,yaw,roll));
        }

        void Transform::rotate(const glm::vec3& euler)
        {
            _rotation*=glm::quat(euler);
        }

        void Transform::rotate(float angle, const glm::vec3& axis)
        {
            _rotation = glm::rotate(_rotation,angle,axis);
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
        }

        void Transform::scale(const glm::vec3& by)
        {
            _scale.x*=by.x;
            _scale.y*=by.y;
            _scale.z*=by.z;
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
            auto translation = glm::translate(glm::mat4(1),_position);
            auto rotation = glm::mat4(_rotation);
            auto scale = glm::scale(glm::mat4(1),_scale);
            return translation*rotation*scale;
        }

        Transform Transform::toGlobal(Node* relativeTo) const
        {
            return *this + cumulativeFrom(relativeTo);
        }

        Transform Transform::cumulativeFrom(Node* node)
        {
            std::stack<Transform> hierarchy;
            Node* currentNode = node;
            auto type = World::RegisterOrRetrieveType("Crucible.Core.Transform",sizeof(Transform), alignof(Transform));
            Transform* nodeTransform = nullptr;
            while(currentNode)
            {
                nodeTransform = (Transform*)node->entity().get(type);
                if(nodeTransform)
                {
                    hierarchy.push(*nodeTransform);
                }
                currentNode = currentNode->getParent();
            }

            if(hierarchy.empty())
            {
                return Transform();
            }
            Transform returnTransform = hierarchy.top();
            hierarchy.pop();
            while(!hierarchy.empty())
            {
                returnTransform = hierarchy.top() + returnTransform;
                hierarchy.pop();
            }
            return returnTransform;
        }

        Transform Transform::operator+(const Transform& with) const
        {
            //this version works for applying the scale in the parents space, but is not reversible, as it creates shear
            //glm::vec3 scale = glm::inverse(_rotation) * (with._scale * (_scale*_rotation));
            //this version applies scale along the local space
            glm::vec3 scale = with._scale*_scale;
            glm::quat rotation =  _rotation * with._rotation;
            glm::vec3 position = ((with._scale * _position) * with._rotation) + with._position;
            return Transform(position,normalize(rotation),scale);
        }

        Transform Transform::operator-(const Transform& by) const
        {
            auto inv = glm::inverse(by._rotation);
            glm::quat rotation = inv*_rotation;
            //this version does not work with non-uniform scale, as shear is introduced
            //glm::vec3 scale = inv * (_scale / (by._scale*by._rotation));
            //this version applies scale along the local space
            glm::vec3 scale = _scale / by._scale;
            glm::vec3 position = _position-((scale*by._position)*rotation);
            return Transform(position,rotation,scale);
        }

        Transform Transform::fastConcat(const Transform& with) const
        {
            glm::vec scale = _scale * with._scale;
            glm::quat rotation =  _rotation * with._rotation;
            glm::vec3 position = ((with._scale * _position) * with._rotation) + with._position;
            return Transform(position,rotation,scale);
        }
    } // core
} // crucible