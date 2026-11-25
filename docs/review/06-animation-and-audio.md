# Review 6: animation and audio

How characters move and how the game sounds: skeletons, clips, the pose
runtime, state machines, events and windows, root motion and IK, skinning;
and the mixer, sounds, and their import. Sprite animation is page 8's.
Decisions: ADRs 0026, 0028, 0078, 0079, 0080, 0081, 0082, 0083, 0096,
0125.

## What it does

A character's model brings its skeleton and clips; an `Animator` on its
entity plays a clip or a state machine, blending and layering them into a
pose each frame, which the GPU uses to skin the mesh. Clips carry events
and windows at exact times (a footstep, a parry window), which gameplay
and audio use. The mixer plays sounds on its own real-time thread,
scheduled to the sample.

```
 Walker.gltf ─► import ─► skeleton (MESH) + clips (.crclip sub-assets)
                                │
 Animator (on the entity) ─► pose runtime: sample, crossfade, layers, state
                             machine, IK, root motion ─► native pose buffer
                                │                         │
                       events / windows (exact times)     Skinning step (compute,
                                │                         per view) ─► draw
                       gameplay, audio scheduling
 Audio: game ─► lock-free command queue ─► mixer on SDL's audio thread ─► device
```

## How it works

### Skeletons and clips (ADR 0026, 0078)

- **Clips are sub-assets** of the model they came from, each with its own
  ID kept by name in the model's sidecar, so a character can play clips
  from other files.
- **Bones aren't entities:** a model's prefab leaves them out, and the
  pose holds bone transforms in native buffers. Sixty entities per
  character would cost the hierarchy pass for data only animation reads.
- **Clips are resampled** at 30 per second and quantized (16-bit rotations
  and ranges), about a quarter of the size of float keys. **Tracks find
  bones by name,** so a clip plays on any skeleton with the same names.

### The pose runtime (ADR 0079, 0081, 0125)

- **`Animator`** names its skeleton and plays a clip or a state machine on
  the base, with crossfades and **up to eight layers** (override, masked by
  bone subtrees, or additive).
- **Wide math:** poses are structure-of-arrays blocks sampled and blended
  several bones at a time; tests compare every bone against a plain
  scalar version.
- **State machines** are hand-edited JSON (`.crstates`) imported to binary,
  driven from C# by parameters and triggers; **1D blend spaces** now, with
  room for 2D.
- **Events and windows** are set per clip in the model's import settings
  and baked into the clip: events at exact times, windows as time ranges
  (open and close as events, and a query for what's open).
- **Root motion** moves the entity through the physics character
  controller instead of letting the root bone travel.
- **IK** (two-bone, look-at) is set by calls each frame from gameplay.
- **Clips advance only while the game plays** (`Animation.Update` is a
  gameplay system); editing holds a character's first sample. Previews run
  their own clock, and scrubbing sets a time directly (ADR 0125).

### Skinning (ADR 0080)

- **Compute pre-skinning per view:** a Skinning step skins the characters
  a view (and its shadows) draws into a buffer; later passes treat them as
  static meshes. Bounds come from the posed bones grown by how far their
  vertices reach, so a raised arm is never culled.

### Audio (ADR 0028, 0082, 0083)

- **Crucible's own mixer** on SDL's audio device thread: buses with
  volume, low-pass, a reverb send, ducking, snapshots, 3D panning and
  distance attenuation, music crossfades on a beat or a bar. The game
  talks to it through a lock-free queue, so the audio thread never waits.
- **Sample-accurate scheduling:** every sound starts at its event's time
  plus a fixed delay (50 ms by default), so rhythm is exact; a sound that
  still arrives late starts at once and is counted.
- **Sounds:** WAV and FLAC import to PCM (short) or ADPCM (long); OGG stays
  Vorbis, decoded a little ahead of each voice on jobs from the whole file
  in memory.
- **Behind Crucible's audio API,** so a game can swap in FMOD or Wwise.

## Why it was built this way

- **Exact times everywhere** come from the action RPG: parry windows and
  hit timing must not depend on the frame rate, and sounds must land on
  the beat.
- **Own runtime** (ozz-animation as the reference): wide math for many
  characters, and the layering, IK, and root motion the games need, built
  to fit the ECS and jobs.
- **Compute skinning** keeps every later pass (depth, shadows, outlines)
  simple: they see static meshes.
- **A dedicated audio thread** because audio deadlines can't wait behind
  frame jobs.

## What I'd question

1. **50 ms on every sound.** The fixed schedule delay keeps rhythm exact,
   but every sound, a button's click or a parry's clang, is 50 ms late. For
   the action RPG's feel that may be noticeable; a shorter delay for sounds
   triggered by input (with the 50 ms kept for animation events), or a
   measured, adaptive one, would help.
2. **Audio latency is estimated, not measured.** The output latency comes
   from buffer sizes; nothing has measured it per platform, and Steam
   Deck's isn't known (open items).
3. **State machines are hand-edited JSON.** The graph editor was planned
   for the action RPG; with the editor extensions API done, it's now a
   workspace like any other, and the first game will want it if it uses
   state machines much.
4. **No animation in the editor's scene.** Characters hold their first
   pose while editing; seeing a character move needs Play or a preview.
   Previewing a clip on a scene's character (with the preview's clock) would
   help placing props and hitboxes.
5. **Not yet built:** 2D blend spaces, retargeting between skeletons with
   different bone names, better clip compression (ACL), streaming music
   from disk (it's loaded whole now). Each waits for a game to need it.
