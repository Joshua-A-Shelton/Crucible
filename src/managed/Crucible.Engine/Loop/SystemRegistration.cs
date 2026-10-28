using System.Diagnostics.CodeAnalysis;

namespace Crucible.Loop;

/// <summary>
/// An assembly's systems, added to a schedule (ADR 0036). Crucible's source
/// generator implements it for every assembly with
/// <see cref="SystemAttribute"/> classes; hand-written implementations work
/// too. Names are placeholders (docs/naming.md).
/// </summary>
public interface ISystemRegistration
{
    void Register(ScheduleBuilder builder);
}

/// <summary>
/// Names an assembly's <see cref="ISystemRegistration"/>, so the game loader
/// finds it with one attribute lookup (ADR 0011). The source generator writes
/// it; AOT builds call the registration directly instead.
/// </summary>
/// <remarks>
/// The annotation keeps the type's constructor through trimming, as on
/// <see cref="Hosting.GameEntryAttribute"/>.
/// </remarks>
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class SystemRegistrationAttribute(
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] Type registrationType)
    : Attribute
{
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
    public Type RegistrationType { get; } = registrationType;
}
