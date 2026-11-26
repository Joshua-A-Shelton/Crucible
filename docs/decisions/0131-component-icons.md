# 0131 — Component icons

**Status:** Accepted (2026-11-25); step 3 of the editor's look
(milestones). Names are placeholders until the owner settles them
(docs/naming.md).

## Context

The Hierarchy was a column of names, the Inspector's sections were told
apart only by their titles, and Add Component was one long list in flat
groups; the owner's notes on ADR 0127 asked for groups inside groups and
for groups that look distinct from what's in them.

## Options

**A row's icon:** its main component's, then any component's; a prefab
icon for every instance. **Color:** muted, accent where selected; by
category. **Add Component's groups:** paths that fold, remembered; paths
always open. **The attribute:** `[ComponentIcon]`; `[Icon]` (beside the
UI's `Icon` control).

## Decision

Decided by the owner (2026-11-25):

1. **Components name a Lucide icon:** `[ComponentIcon("lightbulb")]` in
   C# (`Crucible.Scenes.ComponentIconAttribute`), and
   `CRUCIBLE_HINT_ICON("lightbulb")` on an exposed C++ struct, which the
   binding generator turns into the mirror's attribute. Every engine
   component has one.
2. **A Hierarchy row shows its main component's icon** (the same list that
   names unnamed entities), else that of another of its components that
   has one (by name), else a dot. A prefab instance shows a small package
   mark after its name; an instance whose root has nothing telling of its
   own (a model's) shows the package as its icon instead (found
   building: a house's meshes are inside it).
3. **Muted, accent where selected:** row icons muted, the selected row's
   and the Inspector's headers' in the accent (tokens `icon`,
   `icon-selected`).
4. **Categories are paths:** `[Category("Physics/2D")]` is a group inside
   Physics (the engine's "Physics 2D" became "Physics/2D", the 2D lights
   "2D/Lights"). Groups are small muted headings with a chevron and a
   rule, folded and opened by a click (or Enter), remembered in
   editor.json (`"CollapsedCategories"`). A search shows matches flat,
   each with its group.

## The engine's icons

| Components | Icon |
|---|---|
| Camera, MeshInstance, Sky, PostProcess | `video`, `box`, `cloud-sun`, `sparkles` |
| DirectionalLight; PointLight, PointLight2D; SpotLight, SpotLight2D; AmbientLight, AmbientLight2D | `sun`; `lightbulb`; `flashlight`; `sun-dim` |
| Sprite, SpriteAnimator, PixelCamera, Tilemap, TilemapLayers | `image`, `film`, `scan`, `grid-2x2`, `layers` |
| RigidBody, Collider, Joint, CharacterController, Hitbox (and their 2D twins) | `weight`, `shapes`, `link-2`, `footprints`, `swords` |
| Animator, BoneAttachment | `person-standing`, `paperclip` |
| AudioSource, AudioListener | `volume-2`, `ear` |
| EntityName, LocalTransform, Transform2D | `tag`, `move-3d`, `move` |
| The Characters plugin's Character; the Diorama's Bob | `user`; `arrow-up-down` |
| A transform alone; a prefab instance | `dot`; `package` |

## How it works

- **Tree rows** (UI library) gained `TreeRow.Icon` and `TreeRow.Mark`,
  icons shown only while they have a glyph; the Hierarchy fills them as
  it binds a row. `Shell.IconOf` picks the icon, `EditorIcons.OfComponent`
  reads the attribute (cached per type).
- **The Inspector's headers** show the component's icon between the fold
  and the name.
- **Add Component** builds rows from category paths, a heading the first
  time each path (and each of its parents) appears, skipping what's under
  a folded one; its rows are a heading or a component, each indented by
  depth.

**Added while building:** `Shell.PrefabIcon` ("package");
`ComponentPicker.Fold` and `ShownGroups` (for its check); the Diorama's
`Bob` names `arrow-up-down` in C++, checked by `launcher.jit.native`.

## Consequences

- A game's and a plugin's components get icons with one attribute, and
  groups of their own with a category path.
- Colors by category were set aside for now; the tokens make them a
  style change later.
