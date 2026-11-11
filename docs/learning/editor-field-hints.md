# Editor field hints: telling the inspector what a field means

A learning note for editor usability step 3 (ADR 0089).

## The problem with "it's an asset ID"

To the computer, a Collider's model and an AudioSource's clip are the same
thing: a 16-byte asset ID. So the inspector, which builds its editors from
a field's type, offered the same list for both: every asset in the project.
Picking a sound for a collider's model isn't an error the type system can
catch; it's just wrong.

The fix is a little metadata on the field, saying what it means: "this ID is
a model". Editors call these hints (Unity has attributes like `[Range]`,
Unreal has `meta=(AllowedClasses=...)`). The value saved is unchanged; only
the editor uses them.

## One hint, two languages

Crucible's components come from two places: C++ structs (Collider,
Animator), mirrored into C# by the binding generator, and C# types a game
writes. Hints work the same in both:

    CRUCIBLE_HINT_ASSET(".crmesh") assets::AssetId mesh;    // C++
    [AssetKind(".crmesh")] public AssetId Model;            // C#

In C++ the macro is an annotation that only the binding generator sees
(normal compilers see nothing). The generator writes it onto the C# mirror
as the attribute, and from there the same code reads both: the serializer
generator puts it into each type's field table, which the inspector reads.

## Kinds by extension

An asset's kind is the extension of its imported file: a model's is
.crmesh, a clip's .crclip, a sound's .crsound. Using the extension as the
name means there's no separate list of kinds to keep up to date: a game's
own importer brings its own kind with it.

## Lists that depend on other fields

A mesh index means nothing without its model: index 2 of which model? So
its hint names the field holding the model, and the drop-down reads that
field to list the model's meshes by name. That's also why the lists are
built when a drop-down opens rather than once: what's valid can change as
you edit.
