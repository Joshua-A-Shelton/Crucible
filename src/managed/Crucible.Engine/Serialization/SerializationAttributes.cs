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
