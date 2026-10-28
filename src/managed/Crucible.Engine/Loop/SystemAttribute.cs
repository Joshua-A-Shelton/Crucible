namespace Crucible.Loop;

/// <summary>
/// Registers a <see cref="GameSystem"/> in a phase (ADR 0036). Crucible's
/// source generator turns every marked class in an assembly into a
/// <see cref="ScheduleBuilder.AddSystem"/> call, so registration needs no
/// reflection and works the same under JIT, hot reload, and NativeAOT. Names
/// are placeholders (docs/naming.md).
/// </summary>
/// <remarks>
/// <para>
/// The class must derive from <see cref="GameSystem"/>, be concrete and not
/// generic, have a public parameterless constructor, and keep the default
/// <see cref="GameSystem.Name"/> (its full type name), so that a
/// <c>typeof</c> constraint naming it is always right. The generator reports
/// anything else as a compile error.
/// </para>
/// <para>
/// Constraints order systems within the phase: <see cref="Before"/> and
/// <see cref="After"/> by type, checked when compiling;
/// <see cref="BeforeNames"/> and <see cref="AfterNames"/> by name, for C++
/// systems and systems that rename themselves, checked when the schedule is
/// built.
/// </para>
/// </remarks>
/// <param name="phase">The phase, usually one of <see cref="Phases"/>.</param>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class SystemAttribute(string phase) : Attribute
{
    public string Phase { get; } = phase;

    /// <summary>Systems this one runs before.</summary>
    public Type[] Before { get; set; } = [];

    /// <summary>Systems this one runs after.</summary>
    public Type[] After { get; set; } = [];

    /// <summary>Systems this one runs before, by name.</summary>
    public string[] BeforeNames { get; set; } = [];

    /// <summary>Systems this one runs after, by name.</summary>
    public string[] AfterNames { get; set; } = [];
}
