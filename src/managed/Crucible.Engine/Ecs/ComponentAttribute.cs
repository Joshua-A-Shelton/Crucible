using System.Diagnostics.CodeAnalysis;

namespace Crucible.Ecs;

/// <summary>
/// Registers a type as a component (ADR 0041): Crucible's source generator
/// emits each assembly's registration, so games don't list their components
/// by hand. Its storage follows from the type: a class, or a struct holding
/// references, is managed (ADR 0035); an empty struct is a tag; other
/// structs are blittable, in chunks or, with <see cref="Sparse"/>, in a
/// sparse set. A component that is also <c>[Serialized]</c> is saved in
/// scenes. Names are placeholders (docs/naming.md).
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public sealed class ComponentAttribute : Attribute
{
    /// <summary>Keep it in a sparse set: for blittable components added and removed often (ADR 0023).</summary>
    public bool Sparse { get; set; }
}

/// <summary>An assembly's components, registered (ADR 0041); the generator implements it.</summary>
public interface IComponentRegistration
{
    void Register();
}

/// <summary>Names an assembly's <see cref="IComponentRegistration"/>, for the game loader (ADR 0011).</summary>
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class ComponentRegistrationAttribute(
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] Type registrationType)
    : Attribute
{
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
    public Type RegistrationType { get; } = registrationType;
}
