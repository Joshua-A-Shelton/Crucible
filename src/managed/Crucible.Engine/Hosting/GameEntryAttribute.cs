using System.Diagnostics.CodeAnalysis;

namespace Crucible.Hosting;

/// <summary>
/// Names a game assembly's entry type, so the loader finds it with one
/// attribute lookup instead of scanning the assembly (ADR 0011).
/// </summary>
/// <remarks>
/// The annotation tells the trimmer the entry type's constructor must be kept,
/// which is what lets the loader create it without an AOT warning.
/// </remarks>
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class GameEntryAttribute(
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] Type entryType)
    : Attribute
{
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
    public Type EntryType { get; } = entryType;
}
