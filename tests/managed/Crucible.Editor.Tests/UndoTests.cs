using Crucible.Ecs;

namespace Crucible.Editor.Tests;

// The undo history (ADR 0068): order, groups, merging, and knowing what's saved.
public class UndoTests
{
    // Adds to a counter; undoing takes it away.
    private sealed class Add(string title, List<int> log, int amount) : UndoStep(title)
    {
        public override void Undo(World world) => log.Add(-amount);

        public override void Redo(World world) => log.Add(amount);
    }

    [Test]
    public async Task StepsUndoNewestFirstAndANewStepClearsRedo()
    {
        using World world = new();
        UndoHistory history = new();
        List<int> log = [];
        history.Do(new Add("One", log, 1), world);
        history.Do(new Add("Two", log, 2), world);
        await Assert.That(history.NextUndo!.Title).IsEqualTo("Two");
        await Assert.That(history.Undo(world)).IsTrue();
        await Assert.That(history.Undo(world)).IsTrue();
        await Assert.That(history.Undo(world)).IsFalse();
        await Assert.That(history.Redo(world)).IsTrue();
        await Assert.That(string.Join(",", log)).IsEqualTo("1,2,-2,-1,1");
        await Assert.That(history.RedoCount).IsEqualTo(1);
        history.Push(new Add("Three", log, 3));
        await Assert.That(history.CanRedo).IsFalse();
        await Assert.That(history.UndoCount).IsEqualTo(2);
    }

    [Test]
    public async Task GroupsUndoAsOneAndNest()
    {
        using World world = new();
        UndoHistory history = new();
        List<int> log = [];
        history.BeginGroup("Drag");
        history.Push(new Add("a", log, 1));
        history.BeginGroup("Inner");
        history.Push(new Add("b", log, 2));
        history.EndGroup();
        await Assert.That(history.CanUndo).IsFalse(); // not while a group is open
        history.Push(new Add("c", log, 3));
        history.EndGroup();
        await Assert.That(history.UndoCount).IsEqualTo(1);
        await Assert.That(history.NextUndo!.Title).IsEqualTo("Drag");
        history.Undo(world);
        await Assert.That(string.Join(",", log)).IsEqualTo("-3,-2,-1");
        // An empty group records nothing.
        history.BeginGroup("Nothing");
        history.EndGroup();
        await Assert.That(history.UndoCount).IsEqualTo(0);
        await Assert.That(() => history.EndGroup()).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task ItKnowsWhetherTheSceneDiffersFromTheSavedOne()
    {
        using World world = new();
        UndoHistory history = new();
        List<int> log = [];
        await Assert.That(history.IsDirty).IsFalse();
        history.Push(new Add("One", log, 1));
        await Assert.That(history.IsDirty).IsTrue();
        history.Undo(world);
        await Assert.That(history.IsDirty).IsFalse(); // back where it was saved
        history.Redo(world);
        history.MarkSaved();
        await Assert.That(history.IsDirty).IsFalse();
        history.Undo(world);
        await Assert.That(history.IsDirty).IsTrue();
        history.Redo(world);
        await Assert.That(history.IsDirty).IsFalse();
        // Undone past the save, then a new step: the saved state is gone for good.
        history.Undo(world);
        history.Push(new Add("Other", log, 5));
        history.Undo(world);
        await Assert.That(history.IsDirty).IsTrue();
        // An open group with something in it is a change.
        history.MarkSaved();
        history.BeginGroup("Drag");
        await Assert.That(history.IsDirty).IsFalse();
        history.Push(new Add("a", log, 1));
        await Assert.That(history.IsDirty).IsTrue();
        history.EndGroup();
        history.Clear();
        await Assert.That(history.IsDirty).IsFalse();
        await Assert.That(history.CanUndo).IsFalse();
    }

    [Test]
    public async Task OnlyTheNewestStepsAreKept()
    {
        using World world = new();
        UndoHistory history = new();
        List<int> log = [];
        for (int i = 0; i < UndoHistory.Limit + 10; ++i)
        {
            history.Push(new Add($"{i}", log, 1));
        }
        await Assert.That(history.UndoCount).IsEqualTo(UndoHistory.Limit);
        while (history.Undo(world))
        {
        }
        // Saved with nothing done, and the oldest steps dropped: still dirty after undoing all.
        await Assert.That(history.IsDirty).IsTrue();
    }
    // ADR 0117: a reloaded plugin's steps go, and with them every step only
    // reachable through them; the scene's saved state stays right.
    private sealed class Plugin(string title) : UndoStep(title)
    {
        public override void Undo(World world)
        {
        }

        public override void Redo(World world)
        {
        }
    }

    [Test]
    public async Task DroppingAPluginsStepsCutsTheHistoryBehindThem()
    {
        using World world = new();
        UndoHistory history = new();
        List<int> log = [];
        history.Push(new Add("One", log, 1));
        history.BeginGroup("Grouped");
        history.Push(new Plugin("Inside"));
        history.EndGroup();
        history.Push(new Add("Two", log, 2));
        history.Push(new Add("Three", log, 3));
        history.Undo(world); // Three to redo
        (int dropped, string? title) = history.DropSteps(step => step is Plugin);
        await Assert.That(dropped).IsEqualTo(2);
        await Assert.That(title).IsEqualTo("Grouped");
        await Assert.That(history.UndoCount).IsEqualTo(1);
        await Assert.That(history.NextUndo!.Title).IsEqualTo("Two");
        await Assert.That(history.NextRedo!.Title).IsEqualTo("Three");
        await Assert.That(history.DropSteps(step => step is Plugin).Dropped).IsEqualTo(0);

        // A plugin step to redo: it and what would be redone after it go.
        history.Push(new Plugin("Redo Me"));
        history.Push(new Add("Four", log, 4));
        history.Undo(world);
        history.Undo(world);
        await Assert.That(history.DropSteps(step => step is Plugin).Dropped).IsEqualTo(2);
        await Assert.That(history.CanRedo).IsFalse();
    }

    [Test]
    public async Task SavedAtADroppedStepIsStillSavedWithNothingAfterIt()
    {
        using World world = new();
        UndoHistory history = new();
        List<int> log = [];
        history.Push(new Add("One", log, 1));
        history.Push(new Plugin("Plugin"));
        history.MarkSaved();
        history.DropSteps(step => step is Plugin);
        await Assert.That(history.IsDirty).IsFalse();

        history.Push(new Add("Two", log, 2));
        history.Push(new Plugin("Plugin"));
        history.MarkSaved();
        history.Undo(world);
        history.Undo(world); // back past the save
        history.DropSteps(step => step is Plugin);
        await Assert.That(history.IsDirty).IsTrue();
    }
}
