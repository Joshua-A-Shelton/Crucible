# 0099 — Loading from archives

**Status:** Accepted (2026-11-17); M10 step 2a built, awaiting the owner's
try by hand. 2b and 2c follow. The design's names were settled before
building; names marked * were added while building and are placeholders
until the try.

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
  follow the index as `PackScene`* records (ID, first, count), sorted by
  ID, then the IDs themselves; the header's reserved word became
  `sceneCount`*.
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
  The archive stays open for the run (`ProjectStartup.OpenedArchive`*).
  A standalone run needs nothing else from the project.
- **The launcher** passes `--scene`'s value as it is when it is an asset ID,
  and makes paths absolute otherwise.

## Why

A C++ reader works the same in JIT and AOT builds, runs on the native job
system 2b needs, and serves a native module as well as C#. Recording each
scene's list at cook time means a loader knows a scene's whole need
without parsing it or walking references at run time.

## Known limitations

- One archive at a time; patches and DLC (more archives over one another)
  come later.
- Reads are plain file reads of whole entries; memory mapping, and reading
  sections rather than whole files, could come with streaming large
  assets.

## As built (2a)

- Core: crucible/ArchiveReader.h and ArchiveReader.cpp (`ArchiveReader`:
  `open`, `close`*, `read`, `contains`*, `startScene`, `sceneAssets`,
  `count`*; `installArchiveSource`; for C#, `openArchive`*,
  `closeArchive`*, `archiveStartScene`*, `archiveAssetCount`*,
  `archiveSceneAssets`*, `installArchive`*); crucible/PackFile.h version
  2 (`PackScene`*, `PackHeader::sceneCount`*); `AssetSource::threadSafe`.
- C#: `Archive` (`Open`, `StartScene`, `SceneAssets`, `Count`*,
  `Install`*, `Dispose`); `ArchiveWriter.SetSceneAssets`*,
  `ReadSceneAssets`*, `Version` 2; the Cooker's scene lists;
  `ProjectStartup` reading `archive=` (`OpenedArchive`*).
- Launcher: `--archive`, `--scene` by ID, Game.crpak beside it.
- Editor: `Shell.RunCooked`* (for the checks); LevelChecks' archive mode
  (`check-archive=`*).
- Tests: CookerTests' "The archive reader finds what the cooker packed"*,
  "Each scene's list has only what it reaches"*, "A damaged or old
  archive doesn't open"*; CTest samples.walker-archive: the Level scene
  run from the Cache, then cooked and run from Game.crpak with the Cache's
  imported files moved aside; both runs hit the post 116.457 ms into its
  window and arrive at 34.900 s.
