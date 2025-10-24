#ifndef CRUCIBLE_API_H
#define CRUCIBLE_API_H
#include "crucible/CrucibleCore.h"
#include <glm/glm.hpp>

#include <slag/core/Texture.h>

namespace crucible
{
    namespace scripting
    {
        extern "C"
        {
            CRUCIBLE_API void CRUCIBLE_NATIVE_Matrix4X4Multiply(const glm::mat4& a, const glm::mat4& b, glm::mat4& out);
            CRUCIBLE_API void CRUCIBLE_NATIVE_Matrix4X4FromPositionRotationScale(const glm::vec3& position, const glm::quat& rotation, const glm::vec3& scale, glm::mat4& out);

            CRUCIBLE_API void CRUCIBLE_NATIVE_QuaternionFromAngleAxis(float angle, const glm::vec3& axis, glm::quat& out);
            CRUCIBLE_API void CRUCIBLE_NATIVE_QuaternionFromEulerAngles(const glm::vec3& euler, glm::quat& out);
            CRUCIBLE_API void CRUCIBLE_NATIVE_QuaternionMultiply(const glm::quat& a, const glm::quat& b, glm::quat& out);
            CRUCIBLE_API void CRUCIBLE_NATIVE_QuaternionInverse(const glm::quat& of, glm::quat& out);
            CRUCIBLE_API float CRUCIBLE_NATIVE_QuaternionMagnitude(const glm::quat& quat);
            CRUCIBLE_API void CRUCIBLE_NATIVE_QuaternionNormalized(const glm::quat& quat, glm::quat& out);

            CRUCIBLE_API void CRUCIBLE_NATIVE_Vector4MatrixMultipy(const glm::vec4& vector, const glm::mat4& matrix, glm::vec4& out);
            CRUCIBLE_API float CRUCIBLE_NATIVE_Vector4DotProduct(const glm::vec4& a, const glm::vec4& b);
            CRUCIBLE_API float CRUCIBLE_NATIVE_Vector4Magnitude(const glm::vec4& vector);
            CRUCIBLE_API void CRUCIBLE_NATIVE_Vector4Normalized(const glm::vec4& vector, glm::vec4& out);

            CRUCIBLE_API void CRUCIBLE_NATIVE_Vector3MatrixMultipy(const glm::vec3& vector, const glm::mat4& matrix, glm::vec3& out);
            CRUCIBLE_API float CRUCIBLE_NATIVE_Vector3DotProduct(const glm::vec3& a, const glm::vec3& b);
            CRUCIBLE_API void CRUCIBLE_NATIVE_Vector3CrossProduct(const glm::vec3& a, const glm::vec3&b, glm::vec3& out);
            CRUCIBLE_API float CRUCIBLE_NATIVE_Vector3Magnitude(const glm::vec3& vector);
            CRUCIBLE_API void CRUCIBLE_NATIVE_Vector3Normalized(const glm::vec3& vector, glm::vec3& out);

            CRUCIBLE_API float CRUCIBLE_NATIVE_Vector2DotProduct(const glm::vec2& a, const glm::vec2& b);
            CRUCIBLE_API float CRUCIBLE_NATIVE_Vector2Magnitude(const glm::vec2& vector);
            CRUCIBLE_API void CRUCIBLE_NATIVE_Vector2Normalized(const glm::vec2& vector, glm::vec2& out);

            CRUCIBLE_API void CRUCIBLE_NATIVE_TextureCreate2D(slag::Pixels::Format format, uint32_t width, uint32_t height, uint32_t mips, slag::Texture::SampleCount sampleCount, slag::Texture** outTexture);
            CRUCIBLE_API void CRUCIBLE_NATIVE_TextureDestroy(slag::Texture* texture);
            CRUCIBLE_API uint32_t CRUCIBLE_NATIVE_TextureGetWidth(slag::Texture* texture);
            CRUCIBLE_API uint32_t CRUCIBLE_NATIVE_TextureGetHeight(slag::Texture* texture);
            CRUCIBLE_API uint32_t CRUCIBLE_NATIVE_TextureGetDepth(slag::Texture* texture);
            CRUCIBLE_API uint32_t CRUCIBLE_NATIVE_TextureGetArraySize(slag::Texture* texture);
            CRUCIBLE_API uint32_t CRUCIBLE_NATIVE_TextureGetMipCount(slag::Texture* texture);
            CRUCIBLE_API slag::Pixels::Format CRUCIBLE_NATIVE_TextureGetFormat(slag::Texture* texture);
            CRUCIBLE_API slag::Texture::SampleCount CRUCIBLE_NATIVE_TextureGetSampleCount(slag::Texture* texture);
        }
    } // scripting
} // slag

#endif //CRUCIBLE_API_H
