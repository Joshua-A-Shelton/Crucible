using Crucible.Loop;
using Crucible.Render;

namespace Crucible.Editor;

// While playing (ADR 0072), each phase of the editor's frame runs that
// phase's game systems from the game's own schedule: one system per phase,
// so the game's run where they would in the game's own loop.

/// <summary>Runs a phase's game systems while the editor plays.</summary>
public abstract class PlayPhaseSystem(string phase) : GameSystem
{
    public override void Run(FrameContext context) => Shell.Current?.RunPlayPhase(phase, context);
}

[System(Phases.BeginFrame)]
public sealed class PlayBeginFrame() : PlayPhaseSystem(Phases.BeginFrame);

// After the UI has had the input: the game sees what's left of it.
[System(Phases.Input, After = [typeof(Crucible.UI.UIInputSystem)])]
public sealed class PlayInput() : PlayPhaseSystem(Phases.Input);

[System(Phases.PrePhysics)]
public sealed class PlayPrePhysics() : PlayPhaseSystem(Phases.PrePhysics);

[System(Phases.Physics)]
public sealed class PlayPhysics() : PlayPhaseSystem(Phases.Physics);

[System(Phases.PostPhysics)]
public sealed class PlayPostPhysics() : PlayPhaseSystem(Phases.PostPhysics);

[System(Phases.Update)]
public sealed class PlayUpdate() : PlayPhaseSystem(Phases.Update);

[System(Phases.Animation)]
public sealed class PlayAnimation() : PlayPhaseSystem(Phases.Animation);

[System(Phases.LateUpdate)]
public sealed class PlayLateUpdate() : PlayPhaseSystem(Phases.LateUpdate);

[System(Phases.RenderPrepare)]
public sealed class PlayRenderPrepare() : PlayPhaseSystem(Phases.RenderPrepare);

[System(Phases.RenderSubmit, Before = [typeof(EditorViewportSystem), typeof(RenderSystem)])]
public sealed class PlayRenderSubmit() : PlayPhaseSystem(Phases.RenderSubmit);

[System(Phases.EndFrame)]
public sealed class PlayEndFrame() : PlayPhaseSystem(Phases.EndFrame);
