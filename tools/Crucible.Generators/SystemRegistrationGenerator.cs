using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Crucible.Generators;

/// <summary>
/// Turns an assembly's <c>[System]</c> classes into one generated
/// <c>ISystemRegistration</c> with an <c>AddSystem</c> call per class, and the
/// assembly attribute the game loader finds it by (ADR 0011, 0036). Misuse is
/// a compile error.
/// </summary>
/// <remarks>
/// An incremental generator: Roslyn caches each stage's output and reruns a
/// stage only when its input changed, which is what keeps an IDE responsive.
/// That needs every stage's output to compare by value, so the per-class
/// stage reduces symbols to strings (<see cref="SystemModel"/>) at once.
/// </remarks>
[Generator(LanguageNames.CSharp)]
public sealed class SystemRegistrationGenerator : IIncrementalGenerator
{
    private const string AttributeName = "Crucible.Loop.SystemAttribute";
    private const string GameSystemName = "Crucible.Loop.GameSystem";
    private const string FixedTick = "FixedTick";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Roslyn's fast path: only classes carrying the attribute reach the
        // transform, found through its index of attribute names.
        IncrementalValuesProvider<SystemModel> systems = context.SyntaxProvider.ForAttributeWithMetadataName(
            AttributeName,
            static (node, _) => node is ClassDeclarationSyntax,
            static (attributeContext, _) => Describe(attributeContext));
        context.RegisterSourceOutput(systems.Collect(), static (output, all) => Emit(output, all));
    }

    // One class: its names, phase, and constraints as strings, or what's wrong
    // with it.
    private static SystemModel Describe(GeneratorAttributeSyntaxContext context)
    {
        INamedTypeSymbol type = (INamedTypeSymbol)context.TargetSymbol;
        AttributeData attribute = context.Attributes[0];
        string display = type.ToDisplayString();
        LocationInfo? classLocation = LocationInfo.From(((ClassDeclarationSyntax)context.TargetNode).Identifier.GetLocation());
        LocationInfo? attributeLocation =
            LocationInfo.From(attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation()) ?? classLocation;
        List<DiagnosticInfo> diagnostics = [];

        void Report(DiagnosticDescriptor descriptor, LocationInfo? location, params string[] arguments) =>
            diagnostics.Add(new DiagnosticInfo(descriptor, location, new EquatableArray<string>(arguments)));

        INamedTypeSymbol? gameSystem = context.SemanticModel.Compilation.GetTypeByMetadataName(GameSystemName);
        if (gameSystem is null || !DerivesFrom(type, gameSystem))
        {
            Report(Diagnostics.NotASystem, classLocation, display);
        }
        else
        {
            if (type.IsAbstract || IsGeneric(type))
            {
                Report(Diagnostics.NotConcrete, classLocation, display);
            }
            else if (!type.InstanceConstructors.Any(constructor =>
                         constructor.Parameters.Length == 0 && IsReachable(constructor.DeclaredAccessibility)))
            {
                Report(Diagnostics.NoConstructor, classLocation, display);
            }
            if (!IsReachableType(type))
            {
                Report(Diagnostics.NotAccessible, classLocation, display);
            }
            if (OverridesName(type, gameSystem))
            {
                Report(Diagnostics.Renamed, classLocation, display);
            }
        }

        string? phase = attribute.ConstructorArguments.Length == 1 ? attribute.ConstructorArguments[0].Value as string : null;
        if (string.IsNullOrWhiteSpace(phase))
        {
            Report(Diagnostics.BadPhase, attributeLocation, display, "the phase is empty");
        }
        else if (phase == FixedTick)
        {
            Report(Diagnostics.BadPhase, attributeLocation, display,
                "systems go in the fixed-tick group's sub-phases (PrePhysics, Physics, PostPhysics), not in FixedTick itself");
        }

        List<string> before = [];
        List<string> after = [];
        foreach (KeyValuePair<string, TypedConstant> argument in attribute.NamedArguments)
        {
            List<string> target = argument.Key.StartsWith("Before", StringComparison.Ordinal) ? before : after;
            bool byType = argument.Key is "Before" or "After";
            if (argument.Value.Kind != TypedConstantKind.Array)
            {
                continue;
            }
            foreach (TypedConstant element in argument.Value.Values)
            {
                if (byType)
                {
                    if (element.Value is not INamedTypeSymbol constraint)
                    {
                        Report(Diagnostics.BadConstraint, attributeLocation, display, $"{argument.Key} holds a null type");
                    }
                    else if (constraint.TypeKind == TypeKind.Error)
                    {
                        // The compiler already reports the missing type.
                    }
                    else if (ConstraintProblem(constraint, gameSystem) is { } problem)
                    {
                        Report(Diagnostics.BadConstraint, attributeLocation, display,
                            $"{argument.Key} names {constraint.ToDisplayString()}, which {problem}");
                    }
                    else
                    {
                        target.Add(RuntimeName(constraint));
                    }
                }
                else if (element.Value is string { Length: > 0 } name)
                {
                    target.Add(name);
                }
                else
                {
                    Report(Diagnostics.BadConstraint, attributeLocation, display, $"{argument.Key} holds an empty name");
                }
            }
        }

        return new SystemModel(RuntimeName(type), type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            phase ?? "", new EquatableArray<string>([.. before]), new EquatableArray<string>([.. after]),
            new EquatableArray<DiagnosticInfo>([.. diagnostics]));
    }

    // Every class at once: diagnostics, then the registration, if any class
    // is valid. Sorted by name, so the file is the same on every build.
    private static void Emit(SourceProductionContext output, IReadOnlyList<SystemModel> systems)
    {
        foreach (SystemModel system in systems)
        {
            foreach (DiagnosticInfo diagnostic in system.Diagnostics)
            {
                output.ReportDiagnostic(diagnostic.ToDiagnostic());
            }
        }
        SystemModel[] valid = [.. systems.Where(system => system.Diagnostics.Length == 0)
            .OrderBy(system => system.Name, StringComparer.Ordinal)];
        if (valid.Length == 0)
        {
            return;
        }

        StringBuilder source = new();
        source.Append("""
            // <auto-generated/>
            // Crucible's system registration (ADR 0036): one AddSystem call per [System] class.
            #nullable enable

            [assembly: global::Crucible.Loop.SystemRegistration(typeof(global::Crucible.Generated.SystemRegistration))]

            namespace Crucible.Generated
            {
                [global::System.CodeDom.Compiler.GeneratedCode("Crucible.Generators", "1.0")]
                internal sealed class SystemRegistration : global::Crucible.Loop.ISystemRegistration
                {
                    public void Register(global::Crucible.Loop.ScheduleBuilder builder)
                    {

            """);
        foreach (SystemModel system in valid)
        {
            source.Append("            builder.AddSystem(").Append(Literal(system.Phase)).Append(", new ")
                .Append(system.TypeExpression).Append("(),\n                before: ").Append(Names(system.Before))
                .Append(", after: ").Append(Names(system.After)).Append(");\n");
        }
        source.Append("""
                    }
                }
            }

            """);
        output.AddSource("SystemRegistration.g.cs", source.ToString());
    }

    private static string Names(EquatableArray<string> names) =>
        names.Length == 0 ? "null" : $"new string[] {{ {string.Join(", ", names.Select(Literal))} }}";

    private static string Literal(string value) => SymbolDisplay.FormatLiteral(value, quote: true);

    // Why a typeof constraint can't name this type, or null if it can.
    private static string? ConstraintProblem(INamedTypeSymbol constraint, INamedTypeSymbol? gameSystem)
    {
        if (gameSystem is null || !DerivesFrom(constraint, gameSystem))
        {
            return "doesn't derive from Crucible.Loop.GameSystem";
        }
        if (constraint.IsAbstract || IsGeneric(constraint))
        {
            return "is abstract or generic, so it is never in a schedule";
        }
        if (OverridesName(constraint, gameSystem))
        {
            return "overrides Name; name it with BeforeNames or AfterNames instead";
        }
        return null;
    }

    private static bool DerivesFrom(INamedTypeSymbol type, INamedTypeSymbol baseType)
    {
        for (INamedTypeSymbol? current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, baseType))
            {
                return true;
            }
        }
        return false;
    }

    // Whether the class, or a base class below GameSystem, overrides Name.
    private static bool OverridesName(INamedTypeSymbol type, INamedTypeSymbol gameSystem)
    {
        for (INamedTypeSymbol? current = type;
             current is not null && !SymbolEqualityComparer.Default.Equals(current, gameSystem);
             current = current.BaseType)
        {
            if (current.GetMembers("Name").OfType<IPropertySymbol>().Any(property => property.IsOverride))
            {
                return true;
            }
        }
        return false;
    }

    private static bool IsGeneric(INamedTypeSymbol type)
    {
        for (INamedTypeSymbol? current = type; current is not null; current = current.ContainingType)
        {
            if (current.Arity > 0)
            {
                return true;
            }
        }
        return false;
    }

    // The generated class lives in the same assembly, so internal is enough.
    private static bool IsReachable(Accessibility accessibility) =>
        accessibility is Accessibility.Public or Accessibility.Internal or Accessibility.ProtectedOrInternal;

    private static bool IsReachableType(INamedTypeSymbol type)
    {
        for (INamedTypeSymbol? current = type; current is not null; current = current.ContainingType)
        {
            if (!IsReachable(current.DeclaredAccessibility) || current.IsFileLocal)
            {
                return false;
            }
        }
        return true;
    }

    // The name GameSystem.Name defaults to: Type.FullName, with '+' between
    // nested types.
    private static string RuntimeName(INamedTypeSymbol type)
    {
        if (type.ContainingType is not null)
        {
            return RuntimeName(type.ContainingType) + "+" + type.MetadataName;
        }
        return type.ContainingNamespace.IsGlobalNamespace
            ? type.MetadataName
            : type.ContainingNamespace.ToDisplayString() + "." + type.MetadataName;
    }
}

// One [System] class, reduced to strings so the incremental cache can compare it.
internal sealed record SystemModel(
    string Name,
    string TypeExpression,
    string Phase,
    EquatableArray<string> Before,
    EquatableArray<string> After,
    EquatableArray<DiagnosticInfo> Diagnostics);
