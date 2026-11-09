using Crucible.Ecs;

namespace Crucible.Editor.Tests;

// Play mode's undo (ADR 0072): the history saved at Play comes back at
// Stop, without what was recorded while playing. Play itself runs in
// launcher.jit.play.
public class PlayModeTests
{
    private sealed class Step(string title) : UndoStep(title)
    {
        public override void Undo(World world)
        {
        }

        public override void Redo(World world)
        {
        }
    }

    [Test]
    public async Task StopPutsTheHistoryBackAsItWasAtPlay()
    {
        using World world = new();
        UndoHistory history = new();
        history.Push(new Step("Before"));
        history.Push(new Step("Undone"));
        history.Undo(world);
        history.MarkSaved();
        UndoHistory.Saved atPlay = history.Save();

        history.Push(new Step("While playing"));
        history.Push(new Step("And more"));
        await Assert.That(history.UndoCount).IsEqualTo(3);
        await Assert.That(history.CanRedo).IsFalse();

        history.Restore(atPlay);
        await Assert.That(history.UndoCount).IsEqualTo(1);
        await Assert.That(history.NextUndo!.Title).IsEqualTo("Before");
        await Assert.That(history.NextRedo!.Title).IsEqualTo("Undone");
        await Assert.That(history.IsDirty).IsFalse();
    }
}
