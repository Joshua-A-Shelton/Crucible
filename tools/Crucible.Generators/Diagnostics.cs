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

    private static DiagnosticDescriptor Error(string id, string title, string message) =>
        new(id, title, message, Category, DiagnosticSeverity.Error, isEnabledByDefault: true);
}
