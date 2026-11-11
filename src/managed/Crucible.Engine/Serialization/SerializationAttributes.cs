namespace Crucible.Serialization;

/// <summary>
/// Opts a type into source-generated serialization (ADR 0021, 0038): its
/// public fields and public get/set properties are saved, in declaration
/// order. On a non-public member of such a type, saves that member too.
/// Names are placeholders (docs/naming.md).
/// </summary>
/// <remarks>
/// The type must be <c>partial</c> (the generator adds its reader and
/// writer to it), with a parameterless constructor. The generator reports
/// anything it can't save as a compile error.
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Field | AttributeTargets.Property,
    Inherited = false)]
public sealed class SerializedAttribute : Attribute
{
    /// <summary>
    /// The type's data version, 1 by default. Above 1, the type declares
    /// <c>static void Migrate(JsonObject data, int fromVersion)</c>, which
    /// upgrades older data before it is read.
    /// </summary>
    public int Version { get; set; } = 1;
}

/// <summary>Leaves a public member out of a <see cref="SerializedAttribute"/> type's data.</summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, Inherited = false)]
public sealed class NotSerializedAttribute : Attribute;

/// <summary>
/// A member's earlier name: data saved under it is still read, and is
/// written under the new name from then on (ADR 0021).
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = true, Inherited = false)]
public sealed class FormerlyNamedAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}

/// <summary>
/// Editor hint (ADR 0069): a number is kept between <paramref name="min"/> and
/// <paramref name="max"/> when edited in the inspector. Saved data isn't
/// checked.
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, Inherited = false)]
public sealed class RangeAttribute(float min, float max) : Attribute
{
    public float Min { get; } = min;

    public float Max { get; } = max;
}

/// <summary>Editor hint (ADR 0069): text shown when the pointer rests on the member in the inspector.</summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, Inherited = false)]
public sealed class TooltipAttribute(string text) : Attribute
{
    public string Text { get; } = text;
}

/// <summary>
/// Editor hint (ADR 0089): an asset field (or a list of them) accepts only
/// these kinds, by their imported files' extensions (".crmesh", ".crclip",
/// ".crprefab"); its picker lists only them.
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, Inherited = false)]
public sealed class AssetKindAttribute(params string[] extensions) : Attribute
{
    public IReadOnlyList<string> Extensions { get; } = extensions;
}

/// <summary>Editor hint (ADR 0089): an asset field accepts only models with a skeleton.</summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, Inherited = false)]
public sealed class SkinnedAttribute : Attribute;

/// <summary>
/// Editor hint (ADR 0089): an entity field accepts only entities with these
/// components, and not its own entity; its picker lists only them.
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, Inherited = false)]
public sealed class EntityWithAttribute(params Type[] components) : Attribute
{
    public IReadOnlyList<Type> Components { get; } = components;
}

/// <summary>
/// Editor hint (ADR 0089): an int field is the index of a mesh of the model
/// in another field of the same type (named), picked by the mesh's name.
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, Inherited = false)]
public sealed class MeshOfAttribute(string field) : Attribute
{
    public string Field { get; } = field;
}

/// <summary>
/// Editor hint (ADR 0087): the name the inspector shows for the member, in
/// place of its own (which stays its JSON key).
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, Inherited = false)]
public sealed class LabelAttribute(string text) : Attribute
{
    public string Text { get; } = text;
}

/// <summary>Editor hint (ADR 0069): a heading shown above the member, starting a group.</summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, Inherited = false)]
public sealed class HeaderAttribute(string text) : Attribute
{
    public string Text { get; } = text;
}

/// <summary>
/// Editor hint (ADR 0069): a <c>Vector3</c> (RGB) or <c>Vector4</c> (RGBA) is a
/// color, shown as a swatch with a picker. Components are linear, 0 to 1
/// (above 1 for HDR).
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, Inherited = false)]
public sealed class ColorAttribute : Attribute;

/// <summary>
/// Editor hint (ADR 0075): a <c>uint</c> is a layer mask, one bit per layer,
/// shown by the project's layer names (<c>PhysicsLayers</c>).
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, Inherited = false)]
public sealed class LayersAttribute : Attribute;
