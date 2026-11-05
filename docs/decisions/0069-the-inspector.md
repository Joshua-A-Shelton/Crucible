# 0069 — The inspector

**Status:** Accepted (2026-11-05); built. Names settled by the owner as
drafted, including those added while building (docs/naming.md).

## Context

M7 step 4b: the selection's components, shown and edited (ADR 0068 split
step 4 in two). The step's decision, made before 4a: the inspector's
information about fields comes from generated metadata, not reflection
(NativeAOT, ADR 0038). Undo already records a component's JSON before and
after (ADR 0068).

## Options

**Where the field information comes from:** a field table the serializer
generator writes into every `[Serialized]` type, with edits made to the
component's JSON; or generated typed getters and setters per field; or a
table only for types that opt in.

**Editor hints:** a range and a tooltip; also a color and a header; or
none yet.

**Collections and references:** lists editable, dictionaries shown,
asset and entity drop-downs; everything read-only; or everything
editable.

**Components:** adding and removing them, with or without marking a prefab
instance's overrides.

## Decision

1. **A field table and JSON.** Each `[Serialized]` type gets a generated
   static `SerializedFields`: a `SerializedField` per member (its JSON
   name, its `FieldKind`, what a collection holds, an enum's names, a
   nested type's table, and its hints). The inspector reads a component's
   JSON, changes one field, and writes it back.
2. **Hints:** `[Range(min, max)]`, `[Tooltip("…")]`, `[Color]` (on a
   Vector3 or Vector4), `[Header("…")]`.
3. **Lists editable** (items edited, added and removed at the end);
   dictionaries shown, not edited; `AssetId` and `Entity` as drop-downs.
4. **Add and remove components, and mark overrides** with Revert and
   Revert All.

## Details

- **The table:**
  - written by the serializer generator beside the reader and writer, so
    every saved type has one, in games too, and `IJsonSerialized<T>` asks
    for it (`static abstract SerializedFields`);
  - a nested type's table is read through a function when first asked
    for, so a type can hold itself;
  - a hint on the wrong kind (a color on a float, a range on a string, a
    minimum above its maximum) is a compile error (CRU0108);
  - C++ components' C# halves (from the binding generator) get tables
    too, without hints: hints from C++ annotations come later.
- **Editors by kind (`FieldEditor`):**
  - bool: a toggle;
  - numbers: a number field, scrubbed by dragging; with a range, a slider
    beside it, and both kept inside it;
  - string and Guid: a text field, set on Enter or leaving;
  - enum: a drop-down of its names;
  - Vector2, Vector3, Vector4: number fields sharing the row;
  - color: a `ColorField` swatch opening a `ColorPicker` (a
    saturation-value square, a hue strip, alpha for a Vector4, hex in
    sRGB, the linear components as numbers, which may go above 1);
  - Quaternion: three angles in degrees (pitch, yaw, roll, as
    `CreateFromYawPitchRoll` takes them). The angles shown stay until the
    rotation changes elsewhere, so editing one doesn't make the others
    jump to an equivalent set;
  - Matrix4x4: four rows of four numbers;
  - nullable: a toggle for "has a value", then the value's editor;
  - nested types and lists: foldouts; lists with + and − (at the end);
  - dictionaries: their entries as text;
  - `AssetId`: None or one of the project's assets, by path;
  - `Entity`: None or one of the scene's entities, by label.
- **Undo:** each finished edit is a component step. A drag (a number
  scrubbed, a slider, a color picked while the picker is open) is a group,
  so one step.
- **Live values:** every frame each section reads its component's JSON
  again and hands each editor its part; a field being typed into is left
  alone.
- **Sections (`ComponentSection`):** the transform first, then by name; a
  fold, the type's name, and a menu (Remove Component; Revert All when
  it differs from the prefab).
- **Add Component:** every saved component the entity lacks, with its
  type's defaults (a transform: identity).
- **Prefab overrides:**
  - for an entity of a prefab instance, the prefab is loaded into a
    scratch world as saving does (ADR 0045), and the entity found by the
    scene ID its instance derives;
  - a top-level field differing from the prefab's is marked
    `overridden`, its label in the accent color (the UI has one font face,
    so not bold); a component the prefab hasn't got is marked on its
    title;
  - Revert (right-click on the label) sets the field to the prefab's
    value; Revert All the whole component (or removes one the instance
    added).
  - Removing a component from an instance's part is allowed: overrides
    record removed components (ADR 0045), unlike removed entities.
- **Tooltips** show in the status bar while the pointer is over the
  field: the UI has no hover popups yet.

## Why

A table plus JSON keeps one way to change a component, shared with undo,
saving, and prefab overrides, and needs no reflection. Accessors would be
faster per edit but much more generated code, and undo would still need
the JSON. The owner chose color and header hints and override marks too.

## Not yet

- Hints for C++ components (an annotation the binding generator reads).
- Tooltip popups; bold text for overrides (one font face).
- Reordering list items; editing dictionaries.
- Several entities selected at once.
- Game components in the editor: the editor loads only its own assembly
  until play mode builds games (step 6).

## As built

- **Engine:** `SerializedField`, `FieldKind`, `IJsonSerialized<T>.SerializedFields`,
  the attributes `RangeAttribute`, `TooltipAttribute`, `HeaderAttribute`,
  `ColorAttribute`; the generator's `SerializedFields`; `ColorField`,
  `ColorPicker`; `Label` and `TextField` shaped again when attached (ADR
  0068's fix).
- **Editor:** `InspectorPanel`, `ComponentSection`, `FieldEditor`,
  `IconButton` (internal), `PrefabValues` (internal); the shell's
  `SetComponent`, `AddComponent`, `RemoveComponent`.
- **Tests:**
  - **`FieldTableTests`:** members in order with their kinds, items, enum
    names, nested tables, and hints; engine components' tables;
  - **`SerializerGeneratorTests`:** misplaced hints are errors;
  - **`ColorPickerTests`:** sRGB, HSV, and hex conversions; the square
    and strips under the pointer; hex typed; alpha only for a Vector4;
  - **`InspectorTests`:** shown names, angles round-tripped, new values
    by kind, edits at JSON paths;
  - **`launcher.jit.inspector`:** a house's sections, a typed number and a
    scrubbed one (each one step), 90° typed into a rotation, a part
    overridden then reverted, a camera added, its projection chosen,
    removed, and brought back; saved as `inspector.png`.
- **Tried by the owner on KWin (2026-11-05):** typed and scrubbed numbers
  (each one undo step), rotations in degrees, a house's part overridden
  and reverted, and components added, edited, and removed all work.
