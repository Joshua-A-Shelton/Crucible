#include "API.h"

#include <vector>
#include <glm/gtc/quaternion.hpp>

#include "crucible/Game.h"

namespace crucible
{
    namespace scripting
    {

        void CRUCIBLE_NATIVE_Matrix4X4Multiply(const glm::mat4& a, const glm::mat4& b, glm::mat4& out)
        {
            out = a*b;
        }

        void CRUCIBLE_NATIVE_Matrix4X4FromPositionRotationScale(const glm::vec3& position, const glm::quat& rotation, const glm::vec3& scale, glm::mat4& out)
        {
            auto translationMatrix = glm::translate(glm::mat4(1.0f), position);
            auto rotationMatrix = glm::mat4(rotation);
            auto scaleMatrix = glm::scale(glm::mat4(1.0f), scale);
            out = translationMatrix*rotationMatrix*scaleMatrix;
        }

        void CRUCIBLE_NATIVE_QuaternionFromAngleAxis(float angle, const glm::vec3& axis, glm::quat& out)
        {
            out = glm::quat(angle,axis);
        }

        void CRUCIBLE_NATIVE_QuaternionFromEulerAngles(const glm::vec3& eulerAngles, glm::quat& out)
        {
            out = glm::quat(eulerAngles);
        }

        void CRUCIBLE_NATIVE_QuaternionMultiply(const glm::quat& a, const glm::quat& b, glm::quat& out)
        {
            out = a*b;
        }

        void CRUCIBLE_NATIVE_QuaternionInverse(const glm::quat& of, glm::quat& out)
        {
            out = glm::inverse(of);
        }

        float CRUCIBLE_NATIVE_QuaternionMagnitude(const glm::quat& quat)
        {
            return glm::length(quat);
        }

        void CRUCIBLE_NATIVE_QuaternionNormalized(const glm::quat& quat, glm::quat& out)
        {
            out = glm::normalize(quat);
        }


        void CRUCIBLE_NATIVE_Vector4MatrixMultipy(const glm::vec4& vector, const glm::mat4& matrix, glm::vec4& out)
        {
            out = vector*matrix;
        }

        float CRUCIBLE_NATIVE_Vector4DotProduct(const glm::vec4& a, const glm::vec4& b)
        {
            return glm::dot(a,b);
        }

        float CRUCIBLE_NATIVE_Vector4Magnitude(const glm::vec4& vector)
        {
            return glm::length(vector);
        }

        void CRUCIBLE_NATIVE_Vector4Normalized(const glm::vec4& vector, glm::vec4& out)
        {
            out = glm::normalize(vector);
        }

        void CRUCIBLE_NATIVE_Vector3MatrixMultipy(const glm::vec3& vector, const glm::mat4& matrix, glm::vec3& out)
        {
            glm::vec4 vec(vector,1);
            out = vec * matrix;
        }

        float CRUCIBLE_NATIVE_Vector3DotProduct(const glm::vec3& a, const glm::vec3& b)
        {
            return glm::dot(a,b);
        }

        void CRUCIBLE_NATIVE_Vector3CrossProduct(const glm::vec3& a,const glm::vec3&b, glm::vec3& out)
        {
            out = glm::cross(a,b);
        }

        float CRUCIBLE_NATIVE_Vector3Magnitude(const glm::vec3& vector)
        {
            return glm::length(vector);
        }

        void CRUCIBLE_NATIVE_Vector3Normalized(const glm::vec3& vector, glm::vec3& out)
        {
            out = glm::normalize(vector);
        }

        float CRUCIBLE_NATIVE_Vector2DotProduct(const glm::vec2& a, const glm::vec2& b)
        {
            return glm::dot(a,b);
        }

        float CRUCIBLE_NATIVE_Vector2Magnitude(const glm::vec2& vector)
        {
            return glm::length(vector);
        }

        void CRUCIBLE_NATIVE_Vector2Normalized(const glm::vec2& vector, glm::vec2& out)
        {
            out = glm::normalize(vector);
        }

        void CRUCIBLE_NATIVE_TextureCreate2D(slag::Pixels::Format format, uint32_t width, uint32_t height,uint32_t mips, slag::Texture::SampleCount sampleCount, slag::Texture** outTexture)
        {
            auto aspects = slag::Pixels::aspectFlags(format);
            auto formatProperties = slag::Pixels::formatProperties(format);
            slag::Texture::UsageFlags usage = formatProperties.validUsageFlags;
            *outTexture = slag::Texture::newTexture(format,slag::Texture::Type::TEXTURE_2D,usage,width,height,1,mips,1,sampleCount);
        }

        void CRUCIBLE_NATIVE_TextureDestroy(slag::Texture* texture)
        {
            auto instance = Game::instance();
            if (instance)
            {
                instance->queueForDeletion(texture);
            }
            else
            {
                delete texture;
            }
        }

        uint32_t CRUCIBLE_NATIVE_TextureGetWidth(slag::Texture* texture)
        {
            return texture->width();
        }

        uint32_t CRUCIBLE_NATIVE_TextureGetHeight(slag::Texture* texture)
        {
            return texture->height();
        }

        uint32_t CRUCIBLE_NATIVE_TextureGetDepth(slag::Texture* texture)
        {
            return texture->depth();
        }

        uint32_t CRUCIBLE_NATIVE_TextureGetArraySize(slag::Texture* texture)
        {
            return texture->layers();
        }

        uint32_t CRUCIBLE_NATIVE_TextureGetMipCount(slag::Texture* texture)
        {
            return texture->mipLevels();
        }

        slag::Pixels::Format CRUCIBLE_NATIVE_TextureGetFormat(slag::Texture* texture)
        {
            return texture->format();
        }

        slag::Texture::SampleCount CRUCIBLE_NATIVE_TextureGetSampleCount(slag::Texture* texture)
        {
            return texture->sampleCount();
        }
    } // scripting
} // slag
