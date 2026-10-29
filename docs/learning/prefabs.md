# Prefabs

A learning note for M4 step 8 (ADR 0045): reusable groups of entities, and
how an instance remembers what it changed.

## The problem prefabs solve

A level has forty doors. Copying one door forty times works until the door
needs a fix: then there are forty copies to fix. A **prefab** is the door
described once; the level holds forty **instances**, each pointing at the
prefab and recording only what makes it different (this one is locked,
that one is painted red). Fix the prefab, and all forty get the fix.

The whole design follows from one question: what exactly does an instance
store, so that it keeps its own changes and still receives the prefab's?

## Storing the difference, field by field

Crucible stores the difference per **field**, not per component. A door's
`Health { Value: 10, Max: 10 }` damaged to 3 is saved as
`{ "Value": 3 }`. If the prefab later raises `Max` to 20, the instance
gets 20, because it never said anything about `Max`. With whole-component
overrides the instance would have saved `Max: 10` too, and quietly kept the
old value forever: the classic stale-override bug.

Computing the difference doesn't need special code per component. Each
component already has a generated JSON writer, so saving writes the
prefab's value and the instance's value to JSON and compares them member
by member: objects recursively, everything else (numbers, strings, arrays)
whole. Loading does the reverse: write the value the prefab produced, merge
the stored fields into it, and read it back. Arrays are compared whole
because "the third element changed" stops meaning anything once someone
inserts an element in the prefab.

## Naming entities inside an instance

An override has to say *which* entity it changes, and something outside
the instance (a camera following the door's handle) has to be able to
point at an entity inside it. With forty instances of one prefab there are
forty handles, all with the same ID inside the prefab file.

Crucible derives each one's ID instead of storing it:

```
handle in the level = derive(this door's instance ID, handle's ID in the prefab)
```

`derive` is a hash (SplitMix64's mixing function), so different instances
give different IDs, the same instance always gives the same ID, and
nothing needs to be written down. Nesting composes: a door inside a house
inside a level is `derive(house, derive(door, handle))`. The instance's own
root simply takes the instance's ID.

The cost is that these IDs are permanent. Change `derive` and every
override in every saved level points at nothing, so the function is written
into the ADR and never changes.

## References: each file in its own ID space

Inside the door prefab, the handle refers to the frame by the frame's ID
in the door file. Inside the level, the same frame has a derived ID. So
each file is read with its own map from IDs to entities (a **frame**):
the door's components are read with the door's frame, the level's
overrides with the level's frame. An override can mix the two, keeping
some fields and changing others. That's why loading writes the current
value out in the level's IDs before merging. After that, both halves of
the merge speak the same language.

## Outermost-level overrides

A house prefab contains door instances, and may override them ("the back
door is boarded up"). A level places a house, and may want the front door
red. Where does that second override live?

Full nesting (Unity) lets the level reach into the house's door instance
as its own layer, so a value can come from four places: the door prefab,
the house's override, the level's override, and the level's override of
the house's override. Crucible keeps the rule from ADR 0021: each file
only overrides the instances it contains directly. The level's front-door
change is stored on the *house* instance, naming the door entity by its
derived ID. Resolution is then just "load the door, apply the house's
changes, apply the level's changes", one file at a time.

## Saving by rebuilding

How does saving know what an instance changed? It rebuilds the prefab in a
private scratch world under the very same derived IDs, then walks both side
by side: a component only in the live world was added, one only in the
scratch world was removed, one in both is diffed field by field, and a
child whose ID the prefab doesn't have was added. Some changes can't be
expressed this way, such as deleting or moving one of the prefab's own
entities, so saving refuses them instead of silently losing them.

## Models are prefabs too

An imported model already has a node hierarchy, so it is instantiated the
same way: a root, then one entity per node with a `LocalTransform`, and a
`MeshInstance` where the node has a mesh. A model node has no stored ID,
so its ID is a hash of its **name path** (`Body/Lid`). An artist who
re-exports with the nodes in a different order keeps every override.
Renaming a node, or two siblings sharing a name, are the cases this can't
follow.
