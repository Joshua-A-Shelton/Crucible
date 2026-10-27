#include <crucible/MathProbe.h>

namespace crucible {

Vector3 mathProbeNormalize(Vector3 v)
{
    return normalize(v);
}

Vector3 mathProbeCross(Vector3 a, Vector3 b)
{
    return cross(a, b);
}

float mathProbeDot(Vector4 a, Vector4 b)
{
    return dot(a, b);
}

float mathProbeLength(Vector2 v)
{
    return length(v);
}

Vector3 mathProbeLerp(Vector3 a, Vector3 b, float t)
{
    return lerp(a, b, t);
}

Quaternion mathProbeFromAxisAngle(Vector3 axis, float angle)
{
    return Quaternion::fromAxisAngle(axis, angle);
}

Quaternion mathProbeFromYawPitchRoll(float yaw, float pitch, float roll)
{
    return Quaternion::fromYawPitchRoll(yaw, pitch, roll);
}

Quaternion mathProbeMultiply(Quaternion a, Quaternion b)
{
    return a * b;
}

Quaternion mathProbeConcatenate(Quaternion first, Quaternion second)
{
    return concatenate(first, second);
}

Quaternion mathProbeInverse(Quaternion q)
{
    return inverse(q);
}

Quaternion mathProbeSlerp(Quaternion a, Quaternion b, float t)
{
    return slerp(a, b, t);
}

Vector3 mathProbeRotate(Vector3 v, Quaternion q)
{
    return rotate(v, q);
}

Matrix4x4 mathProbeRotationX(float angle)
{
    return Matrix4x4::rotationX(angle);
}

Matrix4x4 mathProbeRotationY(float angle)
{
    return Matrix4x4::rotationY(angle);
}

Matrix4x4 mathProbeRotationZ(float angle)
{
    return Matrix4x4::rotationZ(angle);
}

Matrix4x4 mathProbeRotation(Quaternion q)
{
    return Matrix4x4::rotation(q);
}

Matrix4x4 mathProbeTranslation(Vector3 offset)
{
    return Matrix4x4::translation(offset);
}

Matrix4x4 mathProbeScale(Vector3 factors)
{
    return Matrix4x4::scale(factors);
}

Matrix4x4 mathProbeMatrixMultiply(Matrix4x4 a, Matrix4x4 b)
{
    return a * b;
}

Matrix4x4 mathProbeTranspose(Matrix4x4 m)
{
    return transpose(m);
}

float mathProbeDeterminant(Matrix4x4 m)
{
    return determinant(m);
}

MathProbeInverse mathProbeInvert(Matrix4x4 m)
{
    MathProbeInverse result{};
    result.invertible = invert(m, result.matrix);
    return result;
}

Matrix4x4 mathProbePerspective(float fieldOfViewY, float aspectRatio, float nearPlane, float farPlane)
{
    return Matrix4x4::perspectiveFieldOfView(fieldOfViewY, aspectRatio, nearPlane, farPlane);
}

Matrix4x4 mathProbeOrthographic(float width, float height, float nearPlane, float farPlane)
{
    return Matrix4x4::orthographic(width, height, nearPlane, farPlane);
}

Matrix4x4 mathProbeLookAt(Vector3 eye, Vector3 target, Vector3 up)
{
    return Matrix4x4::lookAt(eye, target, up);
}

Vector3 mathProbeTransformPoint(Vector3 p, Matrix4x4 m)
{
    return transformPoint(p, m);
}

Vector3 mathProbeTransformDirection(Vector3 v, Matrix4x4 m)
{
    return transformDirection(v, m);
}

Vector4 mathProbeTransform(Vector4 v, Matrix4x4 m)
{
    return transform(v, m);
}

} // namespace crucible
