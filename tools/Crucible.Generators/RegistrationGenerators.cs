using System.Text;
using Microsoft.CodeAnalysis;

namespace Crucible.Generators;

/// <summary>
/// Emits each assembly's component registration from its <c>[Component]</c>
/// types (ADR 0041): one <c>Components.Register…</c> call per type, by its
/// storage, and a <c>SavedComponents.Add…</c> for each that is also
/// <c>[Serialized]</c>; plus the assembly attribute the game loader finds it
/// by.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class ComponentRegistrationGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValuesProvider<RegisteredType> types = context.SyntaxProvider.ForAttributeWithMetadataName(
            "Crucible.Ecs.ComponentAttribute",
            static (_, _) => true,
            static (attributeContext, _) => Describe(attributeContext));
        context.RegisterSourceOutput(types.Collect(), static (output, all) =>
            Registration.Emit(output, all, "Component", "global::Crucible.Ecs.IComponentRegistration",
                "global::Crucible.Ecs.ComponentRegistration", "public void Register()"));
    }

    private static RegisteredType Describe(GeneratorAttributeSyntaxContext context)
    {
        INamedTypeSymbol type = (INamedTypeSymbol)context.TargetSymbol;
        bool sparse = context.Attributes[0].NamedArguments
            .Any(argument => argument.Key == "Sparse" && argument.Value.Value is true);
        string? problem = Registration.TypeProblem(type);
        bool managed = !type.IsValueType || !type.IsUnmanagedType;
        bool tag = !managed && !type.GetMembers().OfType<IFieldSymbol>().Any(field => !field.IsStatic);
        if (problem is null && sparse && (managed || tag))
        {
            problem = managed
                ? "a managed component (a class, or a struct holding references) can't be sparse"
                : "a tag (an empty struct) can't be sparse";
        }
        string kind = managed ? "Managed" : tag ? "Tag" : sparse ? "Sparse" : "";
        bool saved = Registration.IsSerialized(type);
        string expression = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        List<string> calls = [$"global::Crucible.Ecs.Components.Register{kind}<{expression}>();"];
        if (saved)
        {
            string adder = managed ? "AddManaged" : tag ? "AddTag" : "Add";
            calls.Add($"global::Crucible.Ecs.SavedComponents.{adder}<{expression}>();");
        }
        return Registration.Model(type, problem, Diagnostics.BadComponent, calls);
    }
}

/// <summary>
/// Emits each assembly's service registration from its <c>[Service]</c>
/// classes (ADR 0041): one <c>services.Add(new T())</c> per class, and
/// <c>AddSaved</c> for each that is also <c>[Serialized]</c>.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class ServiceRegistrationGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValuesProvider<RegisteredType> types = context.SyntaxProvider.ForAttributeWithMetadataName(
            "Crucible.Loop.ServiceAttribute",
            static (_, _) => true,
            static (attributeContext, _) => Describe(attributeContext));
        context.RegisterSourceOutput(types.Collect(), static (output, all) =>
            Registration.Emit(output, all, "Service", "global::Crucible.Loop.IServiceRegistration",
                "global::Crucible.Loop.ServiceRegistration",
                "public void Register(global::Crucible.Loop.Services services)"));
    }

    private static RegisteredType Describe(GeneratorAttributeSyntaxContext context)
    {
        INamedTypeSymbol type = (INamedTypeSymbol)context.TargetSymbol;
        string? problem = Registration.TypeProblem(type);
        if (problem is null && !type.InstanceConstructors.Any(constructor =>
                constructor.Parameters.Length == 0 && Symbols.IsReachable(constructor.DeclaredAccessibility)))
        {
            problem = "it needs a public or internal parameterless constructor";
        }
        string expression = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        List<string> calls = [$"services.Add(new {expression}());"];
        if (Registration.IsSerialized(type))
        {
            calls.Add($"services.AddSaved<{expression}>();");
        }
        return Registration.Model(type, problem, Diagnostics.BadService, calls);
    }
}

// What the two generators share: the checks, the model, and the emitted file.
internal static class Registration
{
    public static string? TypeProblem(INamedTypeSymbol type) =>
        Symbols.IsGeneric(type) ? "generic types aren't supported"
        : type.IsAbstract || type.IsStatic ? "it is abstract or static"
        : type.IsRefLikeType ? "a ref struct can't be stored"
        : !Symbols.IsReachableType(type) ? "generated code can't reach it: it, and every type it is nested in, " +
                                           "must be public or internal, and not file-local"
        : null;

    public static bool IsSerialized(INamedTypeSymbol type) =>
        type.GetAttributes().Any(attribute =>
            attribute.AttributeClass?.ToDisplayString() == "Crucible.Serialization.SerializedAttribute");

    public static RegisteredType Model(INamedTypeSymbol type, string? problem, DiagnosticDescriptor descriptor,
        List<string> calls)
    {
        DiagnosticInfo[] diagnostics = problem is null
            ? []
            :
            [
                new DiagnosticInfo(descriptor, LocationInfo.From(type.Locations.FirstOrDefault()),
                    new EquatableArray<string>([type.ToDisplayString(), problem])),
            ];
        return new RegisteredType(Symbols.RuntimeName(type), new EquatableArray<string>([.. calls]),
            new EquatableArray<DiagnosticInfo>(diagnostics));
    }

    public static void Emit(SourceProductionContext output, IReadOnlyList<RegisteredType> types, string what,
        string interfaceName, string attributeName, string method)
    {
        foreach (DiagnosticInfo diagnostic in types.SelectMany(type => type.Diagnostics))
        {
            output.ReportDiagnostic(diagnostic.ToDiagnostic());
        }
        RegisteredType[] valid = [.. types.Where(type => type.Diagnostics.Length == 0)
            .OrderBy(type => type.Name, StringComparer.Ordinal)];
        if (valid.Length == 0)
        {
            return;
        }
        StringBuilder source = new();
        source.Append($$"""
            // <auto-generated/>
            // Crucible's {{what.ToLowerInvariant()}} registration (ADR 0041): every [{{what}}] type in this assembly.
            #nullable enable

            [assembly: {{attributeName}}(typeof(global::Crucible.Generated.{{what}}Registration))]

            namespace Crucible.Generated
            {
                [global::System.CodeDom.Compiler.GeneratedCode("Crucible.Generators", "1.0")]
                internal sealed class {{what}}Registration : {{interfaceName}}
                {
                    {{method}}
                    {

            """);
        foreach (string call in valid.SelectMany(type => type.Calls))
        {
            source.Append("            ").Append(call).Append('\n');
        }
        source.Append("""
                    }
                }
            }

            """);
        output.AddSource($"{what}Registration.g.cs", source.ToString());
    }
}

// One registered type, reduced to strings so the incremental cache can compare it.
internal sealed record RegisteredType(
    string Name,
    EquatableArray<string> Calls,
    EquatableArray<DiagnosticInfo> Diagnostics);
