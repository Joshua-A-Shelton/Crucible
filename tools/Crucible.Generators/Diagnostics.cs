using Microsoft.CodeAnalysis;

namespace Crucible.Generators;

// The generator's compile errors (ADR 0036). IDs and wording are placeholders
// (docs/naming.md).
internal static class Diagnostics
{
    private const string Category = "Crucible.Systems";

    public static readonly DiagnosticDescriptor NotASystem = Error("CRU0001",
        "[System] on a class that isn't a system",
        "'{0}' has [System] but doesn't derive from Crucible.Loop.GameSystem");

    public static readonly DiagnosticDescriptor NotConcrete = Error("CRU0002",
        "[System] on an abstract or generic class",
        "'{0}' can't be registered: a [System] class must be concrete and not generic");

    public static readonly DiagnosticDescriptor NoConstructor = Error("CRU0003",
        "[System] class without a parameterless constructor",
        "'{0}' needs a public or internal parameterless constructor");

    public static readonly DiagnosticDescriptor NotAccessible = Error("CRU0004",
        "[System] class the generated registration can't reach",
        "'{0}' can't be created by the generated registration: it, and every class it is nested in, must be " +
        "public or internal, and not file-local");

    public static readonly DiagnosticDescriptor Renamed = Error("CRU0005",
        "[System] class that overrides Name",
        "'{0}' overrides Name: a [System] class keeps its full type name, so typeof constraints naming it " +
        "stay right; register it with ScheduleBuilder.AddSystem to rename it");

    public static readonly DiagnosticDescriptor BadPhase = Error("CRU0006",
        "Invalid [System] phase",
        "'{0}': {1}");

    public static readonly DiagnosticDescriptor BadConstraint = Error("CRU0007",
        "Invalid [System] constraint",
        "'{0}': {1}");

    // Serialization (ADR 0038).
    private const string SerializationCategory = "Crucible.Serialization";

    public static readonly DiagnosticDescriptor NotPartial = Error("CRU0101",
        "[Serialized] type that isn't partial",
        "'{0}' must be partial, and so must every type it is nested in: the generator adds its reader and writer",
        SerializationCategory);

    public static readonly DiagnosticDescriptor UnsupportedType = Error("CRU0102",
        "[Serialized] on a type that can't be serialized",
        "'{0}' can't be serialized: {1}",
        SerializationCategory);

    public static readonly DiagnosticDescriptor NoParameterlessConstructor = Error("CRU0103",
        "[Serialized] class without a parameterless constructor",
        "'{0}' needs a parameterless constructor (any accessibility): reading creates it, then sets its members",
        SerializationCategory);

    public static readonly DiagnosticDescriptor UnsupportedMember = Error("CRU0104",
        "Member type that can't be serialized",
        "'{0}.{1}' has type '{2}', which can't be serialized; mark it [NotSerialized] to leave it out",
        SerializationCategory);

    public static readonly DiagnosticDescriptor NotWritable = Error("CRU0105",
        "Serialized member that can't be read back",
        "'{0}.{1}' {2}, so it can't be read back; mark it [NotSerialized] to leave it out",
        SerializationCategory);

    public static readonly DiagnosticDescriptor DuplicateName = Error("CRU0106",
        "Two serialized members share a name",
        "'{0}': the name '{1}' is used by more than one member (former names included)",
        SerializationCategory);

    public static readonly DiagnosticDescriptor BadVersion = Error("CRU0107",
        "Invalid serialized version",
        "'{0}': {1}",
        SerializationCategory);

    public static readonly DiagnosticDescriptor MisplacedAttribute = Error("CRU0108",
        "Serialization attribute where it has no effect",
        "'{0}': {1}",
        SerializationCategory);

    // Components and services (ADR 0041).
    private const string RegistrationCategory = "Crucible.Registration";

    public static readonly DiagnosticDescriptor BadComponent = Error("CRU0201",
        "[Component] on a type that can't be a component",
        "'{0}' can't be a component: {1}",
        RegistrationCategory);

    public static readonly DiagnosticDescriptor BadService = Error("CRU0202",
        "[Service] on a type that can't be a service",
        "'{0}' can't be a service: {1}",
        RegistrationCategory);

    private static DiagnosticDescriptor Error(string id, string title, string message, string category = Category) =>
        new(id, title, message, category, DiagnosticSeverity.Error, isEnabledByDefault: true);
}
