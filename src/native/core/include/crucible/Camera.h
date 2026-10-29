#pragma once

// The camera component (ADR 0050): what a view of the world looks like. Its
// position and orientation are its entity's WorldTransform (looking along
// +Z, Y up); scale is ignored. Shared with C# as Crucible.Render.Camera.

#include <crucible/Expose.h>

#include <cstdint>

namespace crucible::render {

enum class CRUCIBLE_EXPOSE CameraProjection : uint32_t
{
    Perspective,
    Orthographic,
};

// Depth is reverse-Z (1 at the near plane); a perspective camera has no far
// plane. The aspect ratio comes from the surface drawn to.
struct CRUCIBLE_EXPOSE Camera
{
    CameraProjection projection;
    float fieldOfViewY;       // perspective: the full vertical angle, in radians
    float orthographicHeight; // orthographic: the full height of the view, in meters
    float nearPlane;          // meters from the camera
    float farPlane;           // orthographic only
    // The renderer draws from the camera with the highest priority; among
    // equals, the one with the lowest entity index.
    int32_t priority;
};

// Registers the component. Registering again is harmless.
CRUCIBLE_EXPOSE_INTERNAL void registerCamera();

} // namespace crucible::render
