# 0099 — Loading from archives

**Status:** Accepted (2026-11-17); M10 step 2a built, then tried by hand
by the owner; 2b built, then tried by hand by the owner; 2c built,
then tried by hand by the owner. The design's names were settled before
building; those added while building 2a, 2b, and 2c were settled by the
owner as drafted (docs/naming.md). A package's archive moved from beside
the launcher into its Data folder in ADR 0100.

## Context

ADR 0098 cooks a game's assets into one archive, Game.crpak, which nothing
read yet: a standalone run read the project's Cache (AssetCache) and its
scene by path. ADR 0021 §7 asks for loading on background-priority jobs
(ADR 0014). Today every loader (the renderer, audio, physics, animation)
reads an asset's bytes synchronously on the main thread, through the one
asset source (ADR 0076), the first time something needs it.

## Decision

Decided by the owner (2026-11-17), in three commits, each tried by hand:

1. **2a, reading archives.** A C++ reader in the core
   (crucible/ArchiveReader.h): `ArchiveReader` (`open`, `read`,
   `startScene`, `sceneAssets`) and `installArchiveSource`, which makes it
   the asset source; C# wraps it as `Archive` (`Open`, `StartScene`,
   `SceneAssets`). The cooker records each archived scene's asset list
   (pack file version 2). The launcher's `--archive <file>` (game argument
   `archive=`) reads a game's assets from it and starts the scene its
   header names, or the one `--scene` names by ID; with neither
   `--project` nor `--archive`, a game runs from Game.crpak beside the
   launcher if there is one. `AssetSource` gains `threadSafe`: the
   archive's is; the editor's database and C# sources aren't. Loading is
   still synchronous.
2. **2b, asynchronous creation.** Acquiring a mesh, texture, material,
   sound, collision shape, or clip returns at once; a Background job reads,
   decompresses, parses, and creates it (GPU uploads recorded on the worker
   and submitted to the copy queue under one lock). The system
   `"Assets.Publish"` publishes finished loads on the main thread each
   frame; handles answer `isReady`. A source without `threadSafe` is read
   on the main thread, the rest on jobs. Until ready, things wait, then
   appear: an instance is drawn once its mesh, materials, and textures all
   are; a sound starts when ready; a body is created once its shape is; an
   Animator holds the bind pose.
3. **2c, loading scenes.** C# `Scene.LoadAsync(world, sceneId)` returns a
   `SceneLoadRequest` (`Progress`, `IsDone`, `Result`): the scene's listed
   assets are acquired, and once all are ready its entities are added in
   one frame. Replacing a scene is the game's to do.

## 2a in detail

- **Scene lists.** The cooker keeps what each packed asset names (a scene's
  or prefab's asset IDs, an import's recorded references) and, for each
  packed scene, follows them: everything reached, other scenes and what
  only they reach left out (a scene naming another doesn't load it). They
  follow the index as `PackScene` records (ID, first, count), sorted by
  ID, then the IDs themselves; the header's reserved word became
  `sceneCount`.
- **The reader** opens the file once and keeps its index and scene lists in
  memory (48 bytes an asset). It checks the kind, the version
  (Unsupported for another: cook again), every entry's bounds, and the
  sort order; an asset is found by binary search and read at its offset
  with positional reads (`pread`; on Windows, `ReadFile` with an offset),
  so threads never share a file position: the source is thread-safe, as
  2b needs.
- **Startup.** Given `archive=`, ProjectStartup opens the archive, installs
  it (replacing any source: the archive was asked for), and loads the
  start scene, or `scene=`'s ID, read through the source like any asset.
  The archive stays open for the run (`ProjectStartup.OpenedArchive`).
  A standalone run needs nothing else from the project.
- **The launcher** passes `--scene`'s value as it is when it is an asset ID,
  and makes paths absolute otherwise.

## 2b in detail

Decided by the owner while building (2026-11-17), changing the design
above where marked:

- **Uploads on the graphics queue, not the copy queue** (a change). Slag's
  copy queue can't change texture layouts and needs textures in `Common`
  (Slag ADR 0049), but textures are created `Undefined`: each would still
  need a graphics-queue transition before its copy and another after. So a
  worker records the whole upload, its barriers as before, and submits it
  to the graphics queue under one lock (`queueLock`), which the frame's
  submits, presents, and waits take too. Uploads signal their own timeline
  semaphore; a load is published once its value is reached.
- **The Animator's skeleton and state machine load on jobs too** (an
  addition). An Animator holds still (its time doesn't move) until its
  skeleton, state machine, and every clip it, its layers, or its machine
  plays are loaded; a machine's clips start loading as soon as it's read.
  `play` and `setLayer` accept a clip that's still loading.
- **Startup waits** until 2c. Each system registers a load starter
  (`registerLoadStarter`), which starts loads for what the world's
  components name without simulating; `startLoads` runs them all, and
  `waitForLoads` returns once none is pending. ProjectStartup and the
  editor's Open Scene call both after loading a scene, so it starts whole
  and physics starts with its level; runs stay deterministic.

The machinery (crucible/AssetLoads.h): `startLoad` takes a `LoadTask`
(the asset, a `prepare` run on a Background job with its bytes, an
optional `finished` for work outside the job, such as an upload, and a
`publish` on the main thread). `publishLoads` is "Assets.Publish"
(BeginFrame); `pendingLoads` counts what's in flight. Without job workers
a load runs to the end inside startLoad, so the engine's tests and anything
else without workers see loading as synchronous, failures included.

Per system:

- **Renderer.** Acquiring makes a placeholder in a new slot and starts the
  load; a model's job parses it, fills its buffer, and submits the upload,
  and its publish allocates its descriptor slot and makes its materials (on
  the main thread, with the tables). A texture's slot is taken when it's
  acquired, as materials write it into their parameters; it's written once
  the texture is published (`textureSlot`; `textureDescriptor` gives 0
  until then). A model draws only once it and its materials' textures are
  all ready (`isMeshReady`, `isTextureReady`, `isMaterialReady`; C#
  `IsReady`). A material's file (a few hundred bytes) is still read when
  it's acquired; its textures load on jobs. Released while loading, a
  load's result is dropped when it ends. A failure is logged when the load
  ends and the handle never becomes ready; hot reload starts a failed one
  again.
- **Audio.** A play asked for while its sound loads takes its voice at
  once and is sent when the sound is published (late, if its time has
  passed); an AudioSource that plays on start starts once it's loaded.
- **Physics.** A Mesh or Convex collider (3D) or a Chain collider (2D)
  gets its body once its data is loaded; a body that has a shape keeps it
  until new data is.

## 2c in detail

Decided by the owner while building (2026-11-17), changing the design
above where marked:

- **A practice run, not the archive's list** (a change). The archive's
  list gives each asset's kind only, and one model file feeds the
  renderer, physics, and animation, so acquiring it by kind would need a
  preload hook per system and kind, and it would work only from an
  archive. Instead `Scene.LoadAsync` reads the scene into a practice world
  that never runs, and 2b's load starters go over it each frame
  (`SceneLoadSystem`, BeginFrame, after "Assets.Publish"), starting
  loads. Each starter now also returns how many of the world's components
  still wait (`LoadStarter` returns a count, `startLoads` the sum): a
  MeshInstance until its model can draw, a Mesh, Convex, or Chain collider
  until its data is loaded, an AudioSource that plays on start until its
  sound is, an Animator until its skeleton, state machine, and clips are;
  a failed load waits no longer. At 0 the practice world is dropped and the
  scene is read again into the real world, all in one frame: everything it
  needs is there, so nothing appears late. `Progress` is how much of the
  most that ever waited has loaded, and never goes back. The scene list
  stays in the archive for later uses (streaming in a cooked order).
- **Startup and Open Scene still wait** before the first frame (the
  owner's choice): the first scene opens whole and runs stay
  deterministic; LoadAsync is for a game's next scene.
- **Assets only game components name** (the Walker's footstep sound) load
  when the game first uses them (the owner's choice); a game can acquire
  them before calling LoadAsync.
- **Scenes by ID everywhere** (an addition). A scene was opened by path
  except from an archive; LoadAsync reads it by ID through the asset
  source, so the editor's source serves scenes as saved, and AssetCache
  finds them by the ID inside each file, as it finds prefabs by their
  sidecars.
- A request moves on with its world's schedule (the editor's world, or
  play mode's); one still loading when that loop stops is dropped.
  `LoadAsync` throws NotFound if the source has no such scene.

## Why

A C++ reader works the same in JIT and AOT builds, runs on the native job
system 2b needs, and serves a native module as well as C#. Recording each
scene's list at cook time means a loader knows a scene's whole need
without parsing it or walking references at run time.

## Known limitations

- Pipelines (a material's shader) are still made on the main thread, when
  the material is acquired.
- Fonts and hot reloads stay synchronous.
- LoadAsync reads the scene file and its prefabs on the main thread when
  it's called (they're small), and reads the scene twice: into the
  practice world, then the real one.
- Progress counts components, not bytes: a scene of one big model moves
  from 0 to 1 in one step.

- One archive at a time; patches and DLC (more archives over one another)
  come later.
- Reads are plain file reads of whole entries; memory mapping, and reading
  sections rather than whole files, could come with streaming large
  assets.

## As built (2c)

- Core: `LoadStarter` returns how many of the world's components still
  wait, `startLoads` the sum; each starter counts (DrawList.cpp,
  Box3dPhysics.cpp, Box2dPhysics.cpp, MixerService.cpp, Animation.cpp).
- C#: `Scene.LoadAsync(world, sceneId, prefabs)`; `SceneLoadRequest`
  (`Progress`, `IsDone`, `Result`) and `SceneLoadSystem` in
  Scenes/SceneLoadRequest.cs, the system in the default frame;
  `AssetCache` finding scenes; the editor's source serving scenes.
- Tests: AsyncLoadTests' starters' counts (a collider, an AudioSource, an
  Animator); C# `SceneLoadAsyncTests` ("The scene's entities appear
  together once moved on", "A scene the source doesn't have is not
  found"); ProjectStartupTests' "The cache reads imported files, prefabs,
  and scenes"; CTest launcher.jit.scene-loading (`SceneLoadingChecks`,
  `check-scene-loading=`): the Walker's Level loaded into the editor's
  world over 5 frames, its 12 entities at the top coming in together, with
  nothing it needs still loading.

## As built (2b)

- Core: crucible/AssetLoads.h and AssetLoads.cpp; RenderAssets.cpp's
  prepare and finish halves (`PreparedMesh`, `PreparedTexture`,
  `submitUpload`, the uploads semaphore), `isMeshReady`, `isTextureReady`,
  `isMaterialReady`, `textureSlot`, `isMeshLoading`, `queueLock`; load
  starters in DrawList.cpp, Box3dPhysics.cpp, Box2dPhysics.cpp,
  MixerService.cpp, and Animation.cpp.
- C#: `MeshHandle.IsReady`, `TextureHandle.IsReady`,
  `MaterialHandle.IsReady` (RenderHandles.cs); "Assets.Publish"
  registered with the other modules; ProjectStartup, Open Scene, and the
  end of a run waiting for loads.
- Tests: AsyncLoadTests.cpp ("A collider's body is made once its
  collision data has loaded", "A sound asked for while it loads starts
  once it has", "An Animator holds still until its skeleton and clip have
  loaded", "A source that isn't thread-safe is read on the main thread"),
  with AsyncLoadTestSupport.h's `GatedSource`; RenderAssetsTests' "Models
  and textures load on jobs and are ready once published on every
  backend".

## As built (2a)

- Core: crucible/ArchiveReader.h and ArchiveReader.cpp (`ArchiveReader`:
  `open`, `close`, `read`, `contains`, `startScene`, `sceneAssets`,
  `count`; `installArchiveSource`; for C#, `openArchive`,
  `closeArchive`, `archiveStartScene`, `archiveAssetCount`,
  `archiveSceneAssets`, `installArchive`); crucible/PackFile.h version
  2 (`PackScene`, `PackHeader::sceneCount`); `AssetSource::threadSafe`.
- C#: `Archive` (`Open`, `StartScene`, `SceneAssets`, `Count`,
  `Install`, `Dispose`); `ArchiveWriter.SetSceneAssets`,
  `ReadSceneAssets`, `Version` 2; the Cooker's scene lists;
  `ProjectStartup` reading `archive=` (`OpenedArchive`).
- Launcher: `--archive`, `--scene` by ID, Game.crpak beside it.
- Editor: `Shell.RunCooked` (for the checks); LevelChecks' archive mode
  (`check-archive=`).
- Tests: CookerTests' "The archive reader finds what the cooker packed",
  "Each scene's list has only what it reaches", "A damaged or old
  archive doesn't open"; CTest samples.walker-archive: the Level scene
  run from the Cache, then cooked and run from Game.crpak with the Cache's
  imported files moved aside; both runs hit the post 116.457 ms into its
  window and arrive at 34.900 s.
