# Inspectors and metadata: describing fields without reflection

A learning note for M7 step 4b (ADR 0069).

## What an inspector needs to know

An inspector shows an object's fields and lets you edit them. To do that
for a type it has never seen, it needs to know, for each field: its name,
what kind of value it holds (a number, a vector, a list of something), and
any hints the author gave (a range, a color). That's **metadata**: data
about the type rather than about one value of it.

## Reflection, and why not here

C# can answer those questions at run time through reflection:
`typeof(Light).GetFields()` lists the fields, and each can be read and
written by name. Unity's and Godot's C# inspectors work this way. The
cost: reflection is slow, and ahead-of-time compilation (NativeAOT, which
Crucible's shipping games use) trims away the information reflection
needs unless you tell it to keep it.

Crucible already avoids reflection for saving: a source generator writes
a JSON reader and writer for every `[Serialized]` type while it compiles.
The generator already knows every field's name and kind, so it writes one
more thing: a **field table**, a static list describing each field.

## Editing through JSON

The table describes fields, but doesn't hold a way to read or write them
on a value. Instead the inspector edits the value's JSON: it reads the
component as JSON (the same JSON a scene file holds), changes one field
in it, and writes the whole thing back through the generated reader.

That sounds roundabout, but it means there's one way to change a
component, and undo, saving, and prefab overrides all already speak it.
An undo step is just the JSON before and after. A prefab override is the
JSON that differs. Editing a field deep inside a list inside a nested
type is setting the value at a path: `["Items", 1, "Count"]`.

## Hints are attributes the generator reads

`[Range(0, 10)]` on a field doesn't do anything at run time; it's read by
the generator while compiling and copied into the table. That's why a
hint in the wrong place (a color on a float) can be a compile error
rather than a surprise in the editor.

## A drag is one undo step

Dragging a number to scrub it, or a color around the picker's square,
changes the value every frame. Recording each change would make undo
step back one frame of the drag at a time. So the editor opens an undo
group when a drag starts and closes it when it ends; the history merges
the changes in between into one step.

## Rotations as angles

A rotation is stored as a quaternion: four numbers that are hard to read
and can't be typed sensibly. People think in angles: pitch, yaw, roll.
Converting a quaternion to angles has a catch: many sets of angles give
the same rotation (yaw 180°, pitch 0° is the same as yaw 0°, pitch 180°,
roll 180°). If the inspector converted every frame, editing one angle
could make the other two jump to an equivalent set. So it remembers the
angles it showed and keeps them until the rotation is changed by
something else, like a handle drag.

## Colors: linear and sRGB

The renderer works with linear color: numbers proportional to light
energy, so adding two lights adds their numbers. Screens and image files
use sRGB, which spends more of its values on darks, where eyes are more
sensitive. Hex colors (`#336699`) are sRGB. So the color picker stores
linear values (what the component holds) but shows and accepts hex in
sRGB. Linear 0.2159 is sRGB 0.502, which is `80` in hex. Linear values
above 1 are fine (an HDR light brighter than white); the swatch can only
show them clamped.

## Overrides: comparing with the prefab

An instance of a prefab saves only what it changes. To show which fields
those are, the inspector loads the prefab into a scratch world, the same
way saving does, finds the entity that corresponds to the selected one
(by the scene ID the instance derives for it), and compares each field's
JSON. Reverting a field is setting it back to the prefab's JSON.
