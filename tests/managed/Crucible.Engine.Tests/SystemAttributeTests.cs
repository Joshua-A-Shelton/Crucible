using System.Reflection;
using Crucible.Loop;

namespace Crucible.Tests;

// Systems registered through [System] and the source generator (ADR 0036).
// Under NativeAOT too: the generated registration is plain code.

[System(Phases.Input, After = [typeof(InputSystem)])]
public sealed class ReadInputSystem : GameSystem
{
    public override void Run(FrameContext context)
    {
    }
}

[System(Phases.Update, After = [typeof(GeneratedSystems.Plan)], BeforeNames = ["test.HandWritten"])]
public sealed class MoveSystem : GameSystem
{
    public override void Run(FrameContext context)
    {
    }
}

public static class GeneratedSystems
{
    // Nested, so its name has a '+', as Type.FullName writes it.
    [System(Phases.Update)]
    internal sealed class Plan : GameSystem
    {
        public override void Run(FrameContext context)
        {
        }
    }
}

[NotInParallel("schedule")]
public class SystemAttributeTests
{
    private sealed class HandWritten : GameSystem
    {
        public override string Name => "test.HandWritten";

        public override void Run(FrameContext context)
        {
        }
    }

    [Test]
    public async Task GeneratedRegistrationAddsMarkedSystemsInOrder()
    {
        ScheduleBuilder builder = ScheduleBuilder.CreateDefault();
        builder.IncludeNativeSystems = false;
        builder.AddSystem(Phases.Update, new HandWritten());
        new Crucible.Generated.SystemRegistration().Register(builder);
        Schedule schedule = builder.Build();

        string Order(string phase) =>
            string.Join(" ", schedule.Phases.Single(candidate => candidate.Name == phase).Systems.Select(system => system.Name));
        await Assert.That(Order(Phases.Input)).IsEqualTo("Crucible.Loop.InputSystem Crucible.Tests.ReadInputSystem");
        await Assert.That(Order(Phases.Update)).IsEqualTo(
            "Crucible.Tests.GeneratedSystems+Plan Crucible.Tests.MoveSystem test.HandWritten");
    }

    [Test]
    public async Task AssemblyAttributeNamesTheRegistration()
    {
        // What the game loader looks up in JIT and editor builds (ADR 0011).
        SystemRegistrationAttribute? attribute =
            typeof(SystemAttributeTests).Assembly.GetCustomAttribute<SystemRegistrationAttribute>();
        await Assert.That(attribute?.RegistrationType).IsEqualTo(typeof(Crucible.Generated.SystemRegistration));
    }
}
