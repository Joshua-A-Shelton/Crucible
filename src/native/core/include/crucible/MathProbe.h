#pragma once

// M3 test probe: exposes each math operation to C# so tests can compare it
// with System.Numerics (tests/managed: MathTests). Not engine API; C# code
// uses System.Numerics itself. Names are placeholders.

#include <crucible/Expose.h>
#include <crucible/Math.h>

namespace crucible {

struct CRUCIBLE_EXPOSE MathProbeInverse
{
    Matrix4x4 matrix;
    bool invertible;
};

CRUCIBLE_EXPOSE Vector3 mathProbeNormalize(Vector3 v);
CRUCIBLE_EXPOSE Vector3 mathProbeCross(Vector3 a, Vector3 b);
CRUCIBLE_EXPOSE float mathProbeDot(Vector4 a, Vector4 b);
CRUCIBLE_EXPOSE float mathProbeLength(Vector2 v);
CRUCIBLE_EXPOSE Vector3 mathProbeLerp(Vector3 a, Vector3 b, float t);

CRUCIBLE_EXPOSE Quaternion mathProbeFromAxisAngle(Vector3 axis, float angle);
CRUCIBLE_EXPOSE Quaternion mathProbeFromYawPitchRoll(float yaw, float pitch, float roll);
CRUCIBLE_EXPOSE Quaternion mathProbeMultiply(Quaternion a, Quaternion b);
CRUCIBLE_EXPOSE Quaternion mathProbeConcatenate(Quaternion first, Quaternion second);
CRUCIBLE_EXPOSE Quaternion mathProbeInverse(Quaternion q);
CRUCIBLE_EXPOSE Quaternion mathProbeSlerp(Quaternion a, Quaternion b, float t);
CRUCIBLE_EXPOSE Vector3 mathProbeRotate(Vector3 v, Quaternion q);

CRUCIBLE_EXPOSE Matrix4x4 mathProbeRotationX(float angle);
CRUCIBLE_EXPOSE Matrix4x4 mathProbeRotationY(float angle);
CRUCIBLE_EXPOSE Matrix4x4 mathProbeRotationZ(float angle);
CRUCIBLE_EXPOSE Matrix4x4 mathProbeRotation(Quaternion q);
CRUCIBLE_EXPOSE Matrix4x4 mathProbeTranslation(Vector3 offset);
CRUCIBLE_EXPOSE Matrix4x4 mathProbeScale(Vector3 factors);
CRUCIBLE_EXPOSE Matrix4x4 mathProbeMatrixMultiply(Matrix4x4 a, Matrix4x4 b);
CRUCIBLE_EXPOSE Matrix4x4 mathProbeTranspose(Matrix4x4 m);
CRUCIBLE_EXPOSE float mathProbeDeterminant(Matrix4x4 m);
CRUCIBLE_EXPOSE MathProbeInverse mathProbeInvert(Matrix4x4 m);
CRUCIBLE_EXPOSE Matrix4x4 mathProbePerspective(float fieldOfViewY, float aspectRatio, float nearPlane, float farPlane);
CRUCIBLE_EXPOSE Matrix4x4 mathProbeOrthographic(float width, float height, float nearPlane, float farPlane);
CRUCIBLE_EXPOSE Matrix4x4 mathProbeLookAt(Vector3 eye, Vector3 target, Vector3 up);
CRUCIBLE_EXPOSE Vector3 mathProbeTransformPoint(Vector3 p, Matrix4x4 m);
CRUCIBLE_EXPOSE Vector3 mathProbeTransformDirection(Vector3 v, Matrix4x4 m);
CRUCIBLE_EXPOSE Vector4 mathProbeTransform(Vector4 v, Matrix4x4 m);

} // namespace crucible
