#pragma once

// The imported animation clip, .crclip (ADR 0078): written by the mesh and
// FBX importers, one per animation, as a sub-asset of the model file it came
// from; read by the pose runtime (M8 step 2).
//
// A CLIP file (kind tag("CLIP"), version 1):
//   INFO  ClipInfo
//   TRCK  ClipTrack, one per joint the clip moves; it finds the joint by name
//   ROTS  uint16 x4 per sample: a rotation, smallest three (see below)
//   POSS  uint16 x3 per sample: a translation, within its track's range
//   SCLS  uint16 x3 per sample: a scale, within its track's range
//   NAME  UTF-8 names, referenced by offset and length
//   EVNT  ClipEvent, by time (optional; from the model's sidecar, ADR 0081)
//   WNDW  ClipWindow, by start (optional; likewise)
// Every track is resampled at the clip's sample rate: sample i at time
// i / sampleRate, the last at or just past the duration; a channel that
// never changes is stored as one sample (ClipTrack::constant). Times are seconds; transforms are relative to
// the joint's parent, in Crucible's space (mirrored on X like the model).
//
// Smallest three: the rotation's largest component (in magnitude) is
// dropped, after negating the quaternion if it was negative; the other three,
// in x, y, z, w order, are stored from [-1/√2, 1/√2] to [0, 65535]; the
// fourth uint16 is the dropped component's index (0 to 3).

#include <crucible/BinaryFile.h>
#include <crucible/Expose.h>

#include <cstdint>

namespace crucible::assets {

inline constexpr uint32_t clipFileKind = binary::tag("CLIP");
inline constexpr uint32_t clipFileVersion = 1;

struct CRUCIBLE_EXPOSE ClipInfo
{
    float duration;   // seconds
    float sampleRate; // samples per second
    uint32_t sampleCount;
    uint32_t trackCount;
};

// Bits of ClipTrack::constant: the channels stored as one sample.
enum class CRUCIBLE_EXPOSE ClipChannel : uint32_t
{
    Rotation = 1,
    Translation = 2,
    Scale = 4,
};

struct CRUCIBLE_EXPOSE ClipTrack
{
    uint32_t nameOffset; // into NAME: the joint's name
    uint32_t nameLength;
    uint32_t constant;          // ClipChannel bits
    uint32_t rotationOffset;    // into ROTS, in samples
    uint32_t translationOffset; // into POSS, in samples
    uint32_t scaleOffset;       // into SCLS, in samples
    float translationMin[3];    // the range POSS's values span
    float translationMax[3];
    float scaleMin[3];
    float scaleMax[3];
};

// A named moment on the clip's timeline (a footstep). The name is its hash,
// animation::eventName, as the runtime reports it.
struct CRUCIBLE_EXPOSE ClipEvent
{
    float time; // seconds, 0 to the duration
    uint32_t name;
};

// A named range on the clip's timeline (a parry window): open from start
// until end.
struct CRUCIBLE_EXPOSE ClipWindow
{
    float start; // seconds
    float end;
    uint32_t name;
};

} // namespace crucible::assets
