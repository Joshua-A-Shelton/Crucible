# Crucible

A game engine built on [Slag](https://github.com/Joshua-A-Shelton/Slag).
Currently in the specification phase; see `CLAUDE.md` and `docs/`.

## The engine review

A walk-through of the engine, system by system, with the owner (before the
first game), and what it decided to change:

- [The core](docs/review/01-core.md): C++ and C#, the ECS, the frame and
  its systems, jobs.
- [The platform layer](docs/review/02-platform.md): windows and input,
  latency, math and coordinates, errors, memory, and logging.
- [Assets and scenes](docs/review/03-assets-and-scenes.md): the asset
  database, importers and the cache, serialization, scenes and prefabs,
  cooking and loading.
- [The renderer](docs/review/04-renderer.md): steps on the render graph,
  meshes and materials, lights and shadows, and the finishing.
- [The UI library](docs/review/05-ui.md): elements and layout, drawing,
  text, styling, input, and controls.
- [Animation and audio](docs/review/06-animation-and-audio.md): clips and
  the pose runtime, state machines, events, skinning, and the mixer.
- [Physics](docs/review/07-physics.md): 3D on Box3D, 2D on Box2D,
  colliders, joints, queries, characters, and hitboxes.
- [The 2D path](docs/review/08-2d.md): sprites and sorting, the pixel
  camera, frame animation, tilemaps and map mode, and 2D lighting.
- [The editor](docs/review/09-editor.md): the shell and viewport, undo,
  the inspector, assets and imports, play, and the extensions API.
- [Findings](docs/review/findings.md): what the review decided to change.

## Learning notes

Longer explanations of decisions, written to be reread:

- [Storage types vs. SIMD types](docs/learning/storage-and-simd-types.md)
  (ADR 0024): why the math library has two kinds of vector types, and
  which to use.
- [Measuring input latency](docs/learning/measuring-latency.md)
- [SIMD and data layout](docs/learning/simd-and-data-layout.md)
- [Parallel speedup and memory](docs/learning/parallel-speedup-and-memory.md)
  (ADR 0037): what the M3 demonstration's timings say.
- [Source generators](docs/learning/source-generators.md) (ADR 0036)
- [Generated serializers](docs/learning/generated-serializers.md) (ADR 0038)
- [Compression and checksums](docs/learning/compression-and-checksums.md)
  (ADR 0039): zstd and LZ4, and CRC32C.
- [The asset database](docs/learning/asset-database.md) (ADR 0040)
- [Scenes](docs/learning/scenes.md) (ADR 0041)
- [Texture import](docs/learning/texture-import.md) (ADR 0042)
- [Mesh import](docs/learning/mesh-import.md) (ADR 0043): mirroring into
  left-handed space.
- [FBX import](docs/learning/fbx-import.md) (ADR 0044)
- [Prefabs](docs/learning/prefabs.md) (ADR 0045)
- [The shader toolchain](docs/learning/shader-toolchain.md) (ADR 0047):
  DXIL and SPIR-V, and buffer layout rules.
- [The render graph and the render schedule](docs/learning/render-graph.md)
  (ADR 0048): what a render graph works out, and how steps share
  resources.
- [GPU assets](docs/learning/gpu-assets.md) (ADR 0049): uploads, resizable
  BAR, generational handles, and freeing what the GPU may still use.
- [Transforms and cameras](docs/learning/transforms-and-cameras.md)
  (ADR 0050): local and world matrices, view and projection, and why
  reverse-Z.
- [Drawing meshes](docs/learning/drawing-meshes.md) (ADR 0051): culling,
  instancing, vertex pulling, the depth prepass, the MSAA resolve, and
  measuring "only what changed".
- [Materials](docs/learning/materials.md) (ADR 0052): materials, shading
  models, and the lighting loop; generics, toon bands, and the material
  buffer.
- [Lighting](docs/learning/lighting.md) (ADR 0053): clusters, why lights
  have a range, CPU culling, and hemisphere ambient.
- [Shadows](docs/learning/shadows.md) (ADR 0054): cascades, snapping
  against shimmer, acne and bias, the tent filter, and the cached atlas.
- [Sky, transparency, and antialiasing in the shader](docs/learning/sky-and-transparency.md)
  (ADR 0055): drawing the sky last, sorting against order-independent
  transparency (with measurements), premultiplied alpha, specular AA, toon
  band AA, and alpha-to-coverage.
- [Finishing the image](docs/learning/finishing-the-image.md) (ADR 0055):
  exposure and tonemappers, resolving after tonemapping, bloom, grading,
  and inverted-hull against edge-detected outlines.
- [Comparing renders across graphics APIs](docs/learning/comparing-renders.md)
  (ADR 0056): why D3D12 and Vulkan differ, tolerances, difference images,
  and measuring before keeping an optimization.
- [Exact-size glyphs and distance fields](docs/learning/text-rendering.md)
  (ADR 0020): hinting, MSDF, and why the UI uses both.
- [Shaping and line breaking](docs/learning/shaping-and-line-breaking.md)
  (ADR 0057): glyphs and clusters, font units, and where Latin lines break.
- [Batched commands](docs/learning/batched-commands.md) (ADR 0058): C# render
  steps writing records that native code plays back, checking them once,
  and write-combined upload memory.
- [Drawing 2D with distance fields](docs/learning/drawing-2d.md) (ADR 0059):
  one quad per shape, antialiasing from the distance, blending in linear
  light, and placing exact glyphs.
- [UI layout](docs/learning/ui-layout.md) (ADR 0060): measure and arrange,
  star sizing, dirty flags, snapping edges to pixels, and anchors.
- [Styling and animation](docs/learning/styling-and-animation.md) (ADR 0061):
  value layers, specificity, transitions and tweens, easing curves, and
  real time.
- [UI input](docs/learning/ui-input.md) (ADR 0062): bubbling, capture,
  focus and scopes, spatial navigation, and stick hysteresis.
- [Controls](docs/learning/controls.md) (ADR 0063): controls made of parts,
  the popup layer, and virtualized lists with measured rows.
- [Testing a UI headless](docs/learning/testing-a-ui.md) (ADR 0064):
  scripted journeys, a testable meaning of "crisp", making sure a check can
  fail, and why captures belong in the loop.
- [Text editing](docs/learning/text-editing.md) (ADR 0065): keys versus
  typed text, carets between characters, anchors and goal columns, undo in
  groups, and scrubbing numbers.
- [Docking and windows](docs/learning/docking-and-windows.md) (ADR 0066):
  layouts as trees, drop targets, why Wayland drags windows differently,
  a Wayland connection beside SDL's, pixels versus points, and drawing
  your own window frame.
- [Viewports and picking](docs/learning/viewports-and-picking.md) (ADR 0067):
  a viewport as an image, an editor camera outside the scene, picking by
  drawing IDs, and handles a fixed size on screen.
- [Undo and hierarchies](docs/learning/undo-and-hierarchies.md) (ADR 0068):
  three ways to undo, recording edits after they're made, gestures as one
  step, scene IDs that survive deletion, and a tree drawn as a list.
- [Inspectors and metadata](docs/learning/inspectors-and-metadata.md) (ADR 0069):
  field tables instead of reflection, editing through JSON, hints as
  attributes, rotations as angles, linear and sRGB color, and overrides.
- [Drag and drop, and file watching](docs/learning/drag-drop-and-file-watching.md)
  (ADR 0071): a drag as a mode, crossing windows, a live placement preview
  from picked depth, and debouncing a folder's changes.
- [Play mode and reloading](docs/learning/play-mode-and-reloading.md) (ADR 0072):
  building a game the editor doesn't know, load contexts and unloading,
  play as a snapshot, a schedule inside a schedule, input focus, and
  reloading GPU assets while frames still use them.
- [Running standalone](docs/learning/running-standalone.md) (ADR 0073): one
  game in two hosts, the asset cache as the contract between editor and
  game, defaults over boilerplate, and reading a child process's output.
- [Native modules](docs/learning/native-modules.md) (ADR 0074): exporting a
  C++ API from a shared library, a variable per library, why there's no
  ABI promise, and why loading is easy but unloading isn't.
- [Subsystem contracts](docs/learning/subsystem-contracts.md) (ADR 0075,
  0076): where to draw a replaceable part's line, null defaults, order
  without ordering, gameplay systems, and the seams that already existed.
- [End-to-end checks](docs/learning/end-to-end-checks.md) (ADR 0077): testing
  a session rather than a feature, starting from nothing, repeatable
  captures, and checks that don't change what they measure.
- [Skinning and clips](docs/learning/skinning-and-clips.md) (ADR 0078): the
  skinning formula, weights as bytes, resampled clips and smallest-three
  rotations, testing against a reference pose, and sub-assets.
- [Poses and blending](docs/learning/poses-and-blending.md) (ADR 0079): local
  and model space, sampling with nlerp, blending as one operation, structure
  of arrays, and debug lines.
- [Compute skinning](docs/learning/compute-skinning.md) (ADR 0080): deforming
  once and drawing many times, the palette, keeping the formats, culling by
  bone reach, and barriers from a render graph.
- [State machines and events](docs/learning/state-machines-and-events.md)
  (ADR 0081): rules as data, kinds of parameter, exit times, blend spaces
  in phase, and events timestamped where they really happened.
- [Root motion and IK](docs/learning/root-motion-and-ik.md) (ADR 0081):
  moving the character by its animation, the law of cosines for two-bone
  IK, poles, look-at, and weights.
- [Mixing audio](docs/learning/mixing-audio.md) (ADR 0082): the audio
  thread's deadline, lock-free rings, resampling, constant-power panning,
  a low-pass, ducking, Freeverb, and sample-exact scheduling.
- [Audio codecs](docs/learning/audio-codecs.md) (ADR 0083): PCM, IMA ADPCM's
  adaptive steps, Vorbis's perceptual coding, and streaming ahead through a
  ring.
- [Frame-rate independence](docs/learning/frame-rate-independence.md) (ADR
  0084): carrying exact times from animation to the speaker, and the state
  machine transition a two-rate test caught.
- [Fixed-step physics](docs/learning/fixed-step-physics.md) (ADR 0085): why
  physics steps at a fixed rate, interpolating between ticks, reconciling
  the ECS with Box3D, and moving dynamic, kinematic, and static bodies.
- [Collision meshes](docs/learning/collision-meshes.md) (ADR 0086): triangle
  meshes and convex hulls, cooking bounding-volume trees at import, building
  them at run time, and which way a triangle faces.
- [Engine messages](docs/learning/engine-messages.md) (ADR 0087): one sink
  for the core's warnings, a fixed ring that never allocates, pulling them
  into C# each frame, and holding import settings until Apply.
- [Pausing a game](docs/learning/pausing-a-game.md) (ADR 0088): stopping the
  simulation but not the drawing, resuming without a jump, stepping one
  fixed tick, and holding the mixer's sounds in place.
- [Editor field hints](docs/learning/editor-field-hints.md) (ADR 0089):
  metadata that tells the inspector what a field means, hints in C++ and
  C#, asset kinds by extension, and lists that depend on other fields.
- [Physics queries](docs/learning/physics-queries.md) (ADR 0090): rays,
  shape casts, and overlaps, filtering them, why they see the last tick,
  and rays that start inside something.
- [Character controllers](docs/learning/character-controllers.md) (ADR
  0091): collide and slide, riding over steps, slopes that are walls, who
  makes a character fall, and meeting rigid bodies.
- [Joints](docs/learning/joints.md) (ADR 0092): the five kinds, frames,
  where angles start, joints to the world, and why jointed bodies don't
  collide.
- [2D physics](docs/learning/physics-2d.md) (ADR 0093): why 2D physics is
  its own engine, a 2D transform that becomes the same world matrix,
  angles, convex polygons, (ADR 0094) whose movement a joint's limits
  measure and how a character is kept from seeing itself, and (ADR 0095)
  chains: ghost collisions and which side is solid.
- [Swept hitboxes](docs/learning/swept-hitboxes.md) (ADR 0096): why a hit
  checked once a frame depends on the frame rate, sweeping instead, curved
  paths as screws, activations, owners, and bone attachments.
- [Walking a level](docs/learning/walking-a-level.md) (ADR 0097): a level
  as one mesh, steering on top of root motion, where exact moments come
  from, comparing two runs, and game time without waiting for it (ADR
  0105).
- [Cooking](docs/learning/cooking.md) (ADR 0098): source, cached, and
  cooked data, walking what a game uses, and an archive laid out for
  reading.
- [Loading from archives](docs/learning/loading-from-archives.md) (ADR
  0099): positional reads for many threads, loading on jobs and
  publishing on the main thread, loading the next scene while the game
  runs, knowing a scene's needs ahead, and checking a new asset source
  changes nothing.
- [Shipping a game](docs/learning/shipping-a-game.md) (ADR 0100, 0101):
  what a player gets, bringing the .NET runtime along, ReadyToRun, one
  executable with NativeAOT and what it keeps, making the folder safely,
  checking a package away from the build, checking it plays the same
  (ADR 0102), and logs in a shipped game (ADR 0103).
- [Sprites](docs/learning/sprites.md) (ADR 0104): a sprite as a rectangle
  of a texture, drawing thousands in one call, painter's order and floors,
  pixels per unit, and keeping pixel art's colors exact.
- [Pixel-perfect](docs/learning/pixel-perfect.md) (ADR 0106): the virtual
  resolution and mixels, fitting the window exactly or sharply, and
  layering pixel art over and under 3D.
- [Frame animation](docs/learning/frame-animation.md) (ADR 0107): sprite
  sheets and slicing, clips with a duration per frame, baking on import,
  events on frames, state machines without blending, and seeing pixels
  zoomed in.
- [Tilemaps](docs/learning/tilemaps.md) (ADR 0108): tiles and tilesets,
  painting and filling, drawing a level as built quads, colliding with
  merged outlines instead of tile boxes, and why maps are baked.
- [Map editing](docs/learning/map-editing.md) (ADR 0112): map mode as a
  workspace, layers as entities, why a map has one tile size, stamps and
  picking up, and collision set in the palette.
- [2D lighting](docs/learning/2d-lighting.md) (ADR 0113): multiplying the
  art by light, a light buffer at the virtual resolution, linear light,
  falloff and bands.
- [Floors and bridges](docs/learning/floors-and-bridges.md) (ADR 0114):
  walking under a bridge and over it top-down, sorting and physics layers
  switching together.
- [Dropping files](docs/learning/dropping-files.md) (ADR 0115): how a
  file dragged from a file manager reaches a program on Windows, X11, and
  Wayland, and guessing whether an image is pixel art.
- [Editor assemblies](docs/learning/editor-assemblies.md) (ADR 0116):
  separate assemblies against conditional compilation, load contexts and
  hot reload, and how an unload leaks.
- [Editor plugins](docs/learning/editor-plugins.md) (ADR 0117): a facade
  instead of the editor's insides, an entry point instead of attributes,
  and keeping a panel's state across a reload without keeping old code.
- [Plugins](docs/learning/plugins.md) (ADR 0118): source while you write
  it and compiled when you share it, binary compatibility and semantic
  versioning, ordering by dependencies, and finding plugins nobody names.
- [Workspaces](docs/learning/workspaces.md) (ADR 0119): modes and
  documents, sharing panels, several views of one scene, keys and focus,
  pointing at things that may go away, and debug builds' lifetimes.
- [Opening editors](docs/learning/opening-editors.md) (ADR 0120):
  associations, what a kind is, several editors for one kind, and opening
  something through what uses it.
- [Viewport tools](docs/learning/viewport-tools.md) (ADR 0121): one tool
  at a time, who gets the input and pointer capture, drawing on top, and
  rays, planes, and picking under the pointer.
- [Scene changes](docs/learning/scene-changes.md) (ADR 0122): before and
  after events, holding an operation for a question, one undo step for
  several edits, and not reacting to your own.
- [Official plugins](docs/learning/official-plugins.md) (ADR 0123):
  dogfooding an API, swappable parts, shipping plugins beside the editor,
  loading them once, and tests that see more than the code they test.
- [Icons and tooltips](docs/learning/icons-and-tooltips.md) (ADR 0124):
  icon fonts, when a tooltip shows and hides, and where a brush is held.
- [Preview worlds](docs/learning/preview-worlds.md) (ADR 0125): several
  worlds, systems and clocks, scrubbing, and change tracking.
- [Custom inspectors](docs/learning/custom-inspectors.md) (ADR 0126):
  generated fields first, drawers at three levels, mixing with the
  defaults, and typed values over saved data.
- [Choosing components](docs/learning/choosing-components.md) (ADR 0127):
  when menus stop working, search first, groups for browsing, and reusing
  the platform's attributes.
- [A game's own editor](docs/learning/a-games-own-editor.md) (ADR 0128):
  a tour of the pieces, by way of the Walker's character editor.
- [Up-to-date builds](docs/learning/up-to-date-builds.md) (ADR 0110): file
  times versus content hashes, what makes a build stamp trustworthy, and
  why copies need one.
