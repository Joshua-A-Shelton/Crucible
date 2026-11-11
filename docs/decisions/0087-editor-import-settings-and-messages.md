# 0087 — Editor import settings and messages

**Status:** Accepted (2026-11-11); editor usability step 1 built. Names
settled by the owner as drafted (docs/naming.md); names added while
building are marked *.

## Context

Trying M9 step 2 by hand showed the editor leading the owner the wrong way:
a model's Collision setting lived only in its `.crmeta` sidecar, with no
way to see or change it in the editor, and the core's warning that a
collider had no cooked data went only to the terminal. Testing things in
the editor meant editing sidecars by hand and watching stderr.

## Decision

Decided by the owner (2026-11-11):

1. **One message sink in the core** (`crucible/Log.h`): every core message
   goes through `log::info`, `log::warning`, or `log::error`, not `fprintf`.
   Each is printed as before ("crucible: ...", information to stdout,
   warnings and errors to stderr), handed to a native sink if one is set,
   and kept for C#. The editor shows them in its Log, warnings and errors
   marked and coloured. All core messages, not only physics'.
2. **A selected asset's import settings in the inspector**, under "Import
   Settings", with a field editor for each of its importer's settings.
   Edits are held until **Apply**, which saves the sidecar and imports the
   asset again, or **Revert**, which drops them. Edits aren't undo steps.
3. **A Mesh or Convex Collider whose model lacks the cooked data says so**
   under its fields ("Walker.gltf has no Convex collision data"), with a
   button that sets the model's Collision and imports it again.

## How it works

- **The core's ring.** Messages are kept in a fixed ring of 256, each cut
  at 1023 bytes, behind a mutex held only to copy one in or out. They are
  rare and never on a hot path, and a fixed ring allocates nothing, so it
  works anywhere (out of memory included). The oldest makes way when it's
  full. The sink is called outside the lock, on the writer's thread.
- **C#.** `takeMessages`, `takenLevel`, and `takenText` are exposed
  internally; `Crucible.Log.Api.Pump`* takes them and raises
  `Received`* for each, oldest first. One owner pumps: the editor, at the
  start of its frame (`EditorSystem`). The core has already printed them,
  so the editor only adds them to the Log, and an error to the status bar.
  The generated bindings make `Crucible.Log` a namespace, so the event is on
  its `Api`, as other modules' calls are.
- **What stays a plain print.** The out-of-memory message (it must not
  depend on anything) and the Wayland drag debugging macro.
- **Settings in the database.** `Importer.SettingsFields`* is its settings
  type's generated field table. `AssetDatabase.SettingsOf`* returns an
  asset's settings as JSON, every field, from the canonical settings;
  `SettingsFieldsOf`* their fields; `SetSettings`* canonicalizes new ones
  (throwing if the importer can't read them), updates the entry, and writes
  the sidecar. Sub-assets and Crucible's own files (materials, scenes) have
  no settings.
- **The inspector.** `Shell.InspectedAsset`* is set by choosing an asset in
  the Assets panel, which clears the entity selection; selecting an entity
  clears it. `FieldEditor` edits through an `IFieldOwner`* (a component's
  section, or the new `ImportSettingsSection`*), so the settings get the
  same editors as components. A field that differs from the applied
  settings is marked, with Revert in its right-click menu.
  `Shell.ApplyImportSettings`* saves and then refreshes the assets, so
  dependants import too and everything reimported is reloaded.
- **The collider's notice.** `ColliderNotice`* reads the model's imported
  file for a `COLM` or `COLH` entry of nonzero size for the mesh index,
  again only when the model, shape, or mesh changes or the assets do. A
  model cooks one kind for all its meshes, so cooking the other kind says
  "(replaces Mesh)" on its button.

## Known limitations

- Collision is one kind per model; a model used by both Mesh and Convex
  colliders can't satisfy both (from ADR 0086).
- Edits not yet applied are dropped when another asset or an entity is
  selected.

## As built

- crucible/Log.h, Log.cpp; the core's prints moved to it.
- C#: `Crucible.Log.Api.Pump`*, `Received`*; `Importer.SettingsFields`*;
  `AssetDatabase.SettingsOf`*, `SettingsFieldsOf`*, `SetSettings`*;
  `Shell.InspectedAsset`*, `ApplyImportSettings`*; `IFieldOwner`*,
  `ImportSettingsSection`*, `InspectorPanel.ImportSettings`*,
  `ColliderNotice`*; `LogPanel.Add`'s style; the styles `log-warning`* and
  `log-error`*.
- Tests: `LogTests` (native), `ImportSettingsChecks`* (CTest's
  `launcher.jit.import-settings`, on a copy of the Walker's project).
