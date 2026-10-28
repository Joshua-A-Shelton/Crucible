namespace Crucible.Loop;

/// <summary>
/// A C++ system from the native registry (ADR 0036), sorted with the C#
/// systems and called through its function pointer with the loop's
/// <see cref="SystemContext"/>.
/// </summary>
internal sealed unsafe class NativeSystem(string name, ulong function, nint userData) : GameSystem
{
    private readonly delegate* unmanaged<SystemContext*, nint, void> _function =
        (delegate* unmanaged<SystemContext*, nint, void>)function;

    public override string Name => name;

    // C++ never throws (ADR 0030), so nothing reaches the loop's catch.
    public override void Run(FrameContext context) => _function(context.NativeContext, userData);

    // Every registered C++ system, as builder entries.
    internal static List<ScheduleBuilder.Entry> Registered()
    {
        List<ScheduleBuilder.Entry> entries = [];
        uint count = Api.NativeSystemCount();
        for (uint i = 0; i < count; ++i)
        {
            entries.Add(new ScheduleBuilder.Entry(
                Api.NativeSystemPhase(i),
                new NativeSystem(Api.NativeSystemName(i), Api.NativeSystemFunction(i), Api.NativeSystemUserData(i)),
                Constraints(i, before: true),
                Constraints(i, before: false)));
        }
        return entries;
    }

    private static string[] Constraints(uint index, bool before)
    {
        string[] names = new string[Api.NativeSystemConstraintCount(index, before)];
        for (uint i = 0; i < names.Length; ++i)
        {
            names[i] = Api.NativeSystemConstraint(index, before, i);
        }
        return names;
    }
}
