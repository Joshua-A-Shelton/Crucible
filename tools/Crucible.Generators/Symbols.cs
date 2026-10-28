using Microsoft.CodeAnalysis;

namespace Crucible.Generators;

// Checks every registration generator shares.
internal static class Symbols
{
    public static bool IsGeneric(INamedTypeSymbol type)
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
    public static bool IsReachable(Accessibility accessibility) =>
        accessibility is Accessibility.Public or Accessibility.Internal or Accessibility.ProtectedOrInternal;

    public static bool IsReachableType(INamedTypeSymbol type)
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

    // Type.FullName, with '+' between nested types: what GameSystem.Name
    // defaults to and what components are registered under.
    public static string RuntimeName(INamedTypeSymbol type)
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
