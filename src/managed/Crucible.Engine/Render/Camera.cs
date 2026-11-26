using Crucible.Serialization;

namespace Crucible.Render;

/// <summary>
/// A view of the world (ADR 0050), from its entity's world transform.
/// Declared in C++ (crucible/Camera.h); this half adds saving, and the
/// values it starts with when it's added (a field of view of 0 shows nothing).
/// </summary>
[System.ComponentModel.Category("Rendering"), System.ComponentModel.Description("What the game is seen through: perspective or orthographic.")]
[Serialized]
[Crucible.Scenes.ComponentIcon("video")]
public partial struct Camera
{
    public Camera()
    {
        Projection = CameraProjection.Perspective;
        FieldOfViewY = 0.8f;
        OrthographicHeight = 10;
        NearPlane = 0.1f;
        FarPlane = 1000;
    }
}

/// <summary>
/// How a camera's image is finished (ADR 0055): exposure, bloom,
/// tonemapping, grading; on the camera's entity. Every field is neutral at
/// zero. Declared in C++ (crucible/PostProcess.h); this half adds saving.
/// </summary>
[System.ComponentModel.Category("Rendering"), System.ComponentModel.Description("How a camera's image is finished: exposure, bloom, tonemapping, grading.")]
[Serialized]
[Crucible.Scenes.ComponentIcon("sparkles")]
public partial struct PostProcess;
