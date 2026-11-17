# 0098 — Cooking

**Status:** Accepted (2026-11-17); M10 step 1 built, awaiting the owner's
try by hand. The design's names were settled before building; names marked
* were added while building and are placeholders until the try.

## Context

ADR 0021 §7: a shipping build walks dependencies from the root scenes and
packs only reachable assets into archives. At run time every loader but
the scene reads assets by ID through one asset source (ADR 0076), which a
standalone run fills from the project's Cache (AssetCache). Imported files
are CRBN files (ADR 0039) that embed other assets' IDs: a material its
shader and textures, a model its textures and remapped materials, a state
machine its clips. The asset database recorded only the source files each
import read, not the IDs it wrote.

## Decision

Decided by the owner (2026-11-17):

1. **Importers record their references.** `ImportContext.Reference(id)`
   notes an asset ID the output names; the import record keeps them as
   `References` beside its source dependencies
   (`AssetDatabase.ReferencesOf`*). The material, mesh (and FBX), and
   state machine importers report theirs; their versions went up so
   cached imports record them (the FBX importer's also covers the bone
   rename, which missed it).
2. **Roots: scenes and an include list.** The project file's "Build"
   section (`BuildSettings`*): `Scenes`, by ID, the first the one the game
   starts with, and `Include`, paths in Assets/ of assets or folders always
   packed, for what game code loads by ID. Scenes are packed by their IDs.
3. **One archive, Crucible's own format** (crucible/PackFile.h*): a header
   (`PackHeader`*: kind PACK, version, entry count, index offset, file
   size, and the start scene), each entry's bytes on a 4 KiB boundary, and
   an index (`PackEntry`*: ID, offset, size, kind) sorted by the IDs' bytes
   for binary search. Imported files are stored whole, their sections
   recompressed at `Compression::Small` (zstd 19); scenes and prefabs as
   they are. More archives (patches, DLC) can come later; the header has a
   start scene of zero for one that isn't a game's own.
4. **The Build menu and the command line.** Build > Build Settings… opens
   the Build Settings window (a panel, see below); every change saves the
   project file at once (`Project.SaveBuild`*). Build > Cook
   imports what changed and cooks into Build/Cooked in the project
   (`Project.CookedFolder`*), logging the count and size, or every problem.
   Headless, the editor's `--cook <folder>` (the game argument `cook=`*)
   cooks there and quits. New projects' .gitignore keeps Build/ out, as the
   samples' do now.

## The Build Settings window

Redesigned after the owner's first try (2026-11-17), which found every
line looking alike, "Add Open Scene" needing the scene opened first, and
the include list's path field unclear. Decided by the owner:

- **Cards**, like the Inspector's components: Scenes, Always Include, and
  Cook, each a title over a muted hint. Rows show the name bright and its
  folder muted; the first scene's name is followed by a Start tag; Up,
  Down, and Remove are icon buttons.
- **Scenes** are added from a drop-down of the project's scenes not yet
  listed (`AddPicker`*, which keeps its label and reads its choices each
  time it opens), or by dropping one from the Assets panel.
- **Always Include** takes the same: a drop-down of the project's folders,
  then its assets, or a drop from the Assets panel, whose folders can now
  be dragged (`FolderDrag`*). No paths are typed. A folder's row says how
  many assets it covers; an entry that matches nothing says "not found".
- **Cook** says where it writes and how the last cook went (its count,
  size, and time, or how many problems, with the details in the Log).

`BuildSettingsChecks`* (CTest launcher.jit.build-settings*) drives it on a
copy of the Walker's project and saves build-settings.png.

## The walk

`Cooker` starts from the scenes and the include list (a folder means every
asset under it, with their sub-assets) and visits each asset once:

- **Scenes and prefabs** (read as they are) are packed as they are, and any
  JSON string in them that is a known asset's ID is followed. That covers
  every asset field and prefab instance without knowing the components:
  entity IDs aren't GUIDs, and a GUID that names no asset is ignored.
- **Imported assets** are imported if they aren't up to date, packed, and
  their recorded references followed. A sub-asset (a clip) is packed alone;
  its model isn't pulled in unless something names it.
- **Problems** (an ID that names no asset, an asset that fails to import or
  has no importer installed, an include path that matches nothing, no
  scenes) are all collected, and with any, nothing is written.

The Walker's build settings name its Level scene: a cook packs 8 assets
(about 60 KB): the scene, the level and Walker models, Level.crstates, the
Idle, Swing, and WalkForward clips, and the footstep sound; not the other
scenes or the Walk clip.

## Known limitations

- Cooking runs on the main thread; a big project's imports would stall the
  editor while it cooks.
- Include entries are paths, so renaming an included folder breaks them
  (scenes are named by ID and survive renames).
- The editor's importer list has no Material or FBX importer, so a scene
  reaching a .crmaterial or an .fbx fails the cook with "no importer
  installed"; until the editor imports them, nothing can use them anyway.
- Generated material types name their shader in game code (ADR 0052):
  include a shader that code uses but no material names.

## As built

- Crucible.Assets: `ImportContext.Reference`, `ImportRecord.References`,
  `AssetDatabase.ReferencesOf`*; `BuildSettings`*, `ProjectFile.Build`,
  `Project.Build`, `SaveBuild`*, `CookedFolder`*; `ArchiveWriter`
  (`Add`, `Write`, `StartScene`*, `Alignment`*, `ReadIndex`*,
  `ReadStartScene`*); `Cooker` (`Cook`, `ArchiveName`*), `CookResult`*
  (`ArchivePath`, `AssetCount`, `Size`, `Errors`, `Succeeded`).
- crucible/PackFile.h*: `PackHeader`*, `PackEntry`*, `packFileKind`*,
  `packFileVersion`*, `packAlignment`*; read at run time from step 2.
- The editor: Shell.Build.cs (`Build.Settings`*, `Build.Cook`*, `Cook`,
  `LastCook`*), BuildSettingsPanel.cs* (panel ID `BuildSettings`*,
  `AddPicker`*), `FolderDrag`*, the Build menu; the launcher's `--cook`.
- Tests: `CookerTests` (the Walker's Level cooks to just what it needs,
  sorted and aligned, the same sections; state machines record their
  clips; included folders; problems), MaterialImporterTests' "A material
  records its shader and textures for cooking"; CTest
  samples.walker-cook*; `BuildSettingsChecks`* (launcher.jit.build-settings*).
