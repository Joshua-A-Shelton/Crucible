# 0041 — Scenes, component registration, and services

**Status:** Accepted (2026-10-28). Names are placeholders (docs/naming.md).

## Context

M4 step 4 saves a world as a scene (ADR 0021: authored JSON) and loads it
back. That needs every saved component type's serializer at hand (ADR
0038), entity references inside components (ADR 0033) written in a form
that survives reloading, and the hierarchy. ADR 0035 registered component
types with explicit generic calls, a generator to come later. ADR 0012 left
open how plain C# services (game rules, bookkeeping, flow) are registered,
found, and saved: M4 decision M.

## Options

**Entity IDs in a file:** a random 64-bit ID per entity, kept across saves;
or 1..N renumbered on every save.

**Hierarchy:** children nested inside their parent, in order; or one flat
list where each entity names its parent and position.

**Component registration:** a `[Component]` attribute and a generator that
emits each assembly's registration (like `[System]`, ADR 0036), hooking up
saving for `[Serialized]` components; or keeping explicit `Register<T>()`
and adding `RegisterSaved<T>()`.

**Services:** `[Service]` objects created by generated registration and
found by type; a dependency-injection container; or singleton components
(Bevy's resources).

## Decision

1. **Entities keep a random 64-bit scene ID** across saves, in a small
   component. References and diffs stay put when other entities are added,
   and two people adding entities on different branches can't collide.
2. **Children are nested** inside their parent, in order: the file reads
   like the editor's tree.
3. **`[Component]` and a generator register component types,** per
   assembly, with storage as an option (sparse; managed for classes and
   structs holding references; tags for empty structs). A component that is
   also `[Serialized]` is saved in scenes; others (caches, per-frame state)
   aren't. `Components.Register<T>()` stays underneath, as the builder does
   for systems.
4. **Services are `[Service]` classes** with a parameterless constructor,
   created by generated registration and found by type through the frame
   context (`context.Services.Get<Inventory>()`), through a per-type index
   rather than a dictionary. `[Serialized]` services are saved together in
   one JSON document of game state, which save games will build on.

## Why

Stable IDs make scene files diff and merge like code, the reason authored
files are text (ADR 0021). Nesting matches how people read scenes.
Generated registration removes a list games would otherwise keep by hand,
and gives the editor the list of component types (its "add component"
menu). Services by type cover what game code needs (one inventory, one
quest log) without a container's machinery or a new dependency, and keep
game logic in plain C# (ADR 0012).

Given up, for now: several instances of one service type, constructor
injection, and saving services inside scenes (they are game state, not
level content).

## As built (M4 step 4a: registration)

Step 4 is split: 4a registers components and services, 4b saves scenes.

- **`[Component]`** (`Crucible.Ecs`), with `Sparse` as its one option. The
  generator picks the storage: managed for a class or a struct that isn't
  unmanaged, a tag for an unmanaged struct without instance fields, sparse
  when asked, chunks otherwise. It emits `Components.Register…<T>()` per
  type, sorted by name, and `SavedComponents.Add…<T>()` for each that is
  also `[Serialized]`, in `Crucible.Generated.ComponentRegistration`
  (`IComponentRegistration`), named by an assembly attribute.
- **`SavedComponents`** keeps, per component ID and name, how to write a
  value from an entity and read one onto it: blittable through
  `World.Read`/`Set`, tags as `{}`, managed through the pool (a managed
  component added but never set is written as null). Its entries hold game
  types, so unloading a game drops them (`SavedComponents.Forget`); a test
  confirms a game still unloads, and fails without it.
- **`[Service]`** (`Crucible.Loop`): the generator emits
  `services.Add(new T())` per class, and `services.AddSaved<T>()` for
  `[Serialized]` ones, in `Crucible.Generated.ServiceRegistration`.
- **`Services`:** `Add`, `Get<T>` (throws if missing), `TryGet<T>`, found
  through a static per-type index into an array, so a lookup allocates
  nothing. `Save` writes the saved services as one JSON object keyed by type
  name in ordinal order; `Load` replaces them with new instances and returns
  the names it didn't know. `FrameContext.Services` holds the game's.
- **Loading:** `GameLoader` finds all three registrations;
  `LoadedGame.RegisterComponents`, `RegisterServices`, and
  `RegisterSystems`. The host registers components, then services, then
  systems.
- **Compile errors:** `CRU0201` for a component that is generic, abstract,
  static, a ref struct, unreachable from generated code, or sparse while
  managed or a tag; `CRU0202` for a service that is generic, abstract,
  static, unreachable, or without a public or internal parameterless
  constructor.
- The `[System]` generator's symbol checks moved to a shared `Symbols`
  class.
