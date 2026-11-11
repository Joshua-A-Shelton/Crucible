# 0089 — Pickers that offer only valid choices

**Status:** Accepted (2026-11-11); editor usability step 3 built, then
tried by hand by the owner. Names, including those added while building,
settled by the owner as drafted (docs/naming.md).

## Context

The inspector's drop-downs (ADR 0069) listed every asset in the project
for any asset field, every entity for any entity field, and a mesh index
was a bare number. A Collider's model could be set to a sound, an
Animator's clip to a scene, and the lists were built once, so new assets
didn't appear until the selection changed.

## Decision

Decided by the owner (2026-11-11):

1. **Asset fields name the kinds they accept by file extension**, the
   imported file's (".crmesh", ".crclip"), which already names each kind
   (ADR 0021): `CRUCIBLE_HINT_ASSET(".crmesh")` in C++, `[AssetKind(".crmesh",
   ".crprefab")]` in C#. A game's own importers' kinds work the same.
2. **Animator.Skeleton lists only models with a skeleton**
   (`CRUCIBLE_HINT_ASSET_SKINNED`, `[Skinned]`).
3. **Entity fields name the components their entity needs**, leaving out
   their own entity: Joint.Other lists other entities with a Collider
   (`CRUCIBLE_HINT_ENTITY_WITH(Collider)`, `[EntityWith(typeof(Collider))]`).
   Fields without a hint list every entity, as before.
4. **A mesh index is a drop-down of its model's meshes by name**, "Body
   (0)" (`CRUCIBLE_HINT_MESH_OF(model)`, `[MeshOf(nameof(Model))]`):
   disabled, "Choose a model first", until there's a model; an index past
   its meshes shows as "Mesh 3 (missing)".
5. **Lists are read again each time a drop-down opens.**

## How it works

- **Hints.** The binding generator reads the C++ annotations and writes the
  C# attributes onto the mirrors (`EntityWith`'s component resolved to its
  mirror type); the serializer generator writes them into the field table:
  `SerializedField.AssetKinds`, `NeedsSkeleton`, `EntityComponents` (full
  type names), `MeshOf`. A list's items pick as the list does (a model's
  Materials). `[Skinned]` alone means models.
- **The database.** `AssetDatabase.KindOf(id)` is the imported file's
  extension: the importer's output, a sub-asset's recorded one, or a file
  read as it is (a scene, a prefab) by its own. `HasSkeleton(id)` and
  `MeshNamesOf(id)`* read a model's imported file (its SKIN, MSHS, and NAME
  sections), cached by the file's path, so a reimport is read again.
- **The inspector.** `ComboBox.Opening`* lets a field rebuild its choices
  just before its list opens. The value a field holds stays listed even if
  it no longer qualifies (an asset of the wrong kind set by hand), so
  nothing set is hidden. The mesh drop-down reads its model from the field
  beside it (`IFieldOwner.ValueAt`*), and refreshes when that changes.
- **Marked fields.** MeshInstance.Model and Collider.Mesh (models),
  MeshInstance.Mesh and Collider.MeshIndex (mesh of), Animator.Skeleton
  (skinned), Animator.Clip and LayerDesc.clip (clips),
  Animator.StateMachine (state machines), AudioSource.Clip (sounds),
  Joint.Other (with a Collider), PrefabInstance.Prefab (prefabs and models),
  MeshImportSettings.Materials (materials), and the Walker's
  Footsteps.Sound (sounds).

## As built

- crucible/Expose.h (`CRUCIBLE_HINT_ASSET`, `CRUCIBLE_HINT_ASSET_SKINNED`,
  `CRUCIBLE_HINT_ENTITY_WITH`, `CRUCIBLE_HINT_MESH_OF`); BindGen's
  `FieldHints`; the serializer generator's `HintModel`.
- C#: `AssetKindAttribute`, `SkinnedAttribute`, `EntityWithAttribute`,
  `MeshOfAttribute`; `SerializedField.AssetKinds`, `NeedsSkeleton`,
  `EntityComponents`, `MeshOf`; `AssetDatabase.KindOf`, `HasSkeleton`,
  `MeshNamesOf`*; `ComboBox.Opening`*; `IFieldOwner.Entity`*, `ValueAt`*.
- Tests: `FieldTableTests` (`PickerHintsComeFromTheAttributes`,
  `EngineComponentsCarryPickerHintsFromTheirHeaders`), `PickerChecks`
  (CTest's `launcher.jit.pickers`: each drop-down opened by a click on a
  copy of the Walker's project with the Diorama's house added).
