using System.Numerics;
using Crucible.UI;
using Rig = Crucible.Tests.TreeViewTests.Rig;
using T = Crucible.Tests.TreeViewTests;

namespace Crucible.Tests;

// UI drags (ADR 0071): targets told as the pointer enters, moves, and
// leaves; a drop only where it was accepted; Escape cancels; a preview
// follows the pointer; a host carries a drag in from another window.
[NotInParallel("text")]
public class DragDropTests
{
    private const int Escape = 41;

    private sealed class Target : UIElement
    {
        public readonly List<string> Log = [];
        public bool Accepts = true;

        public Target(string name)
        {
            Name = name;
            AllowDrop = true;
            Width = 100;
            Height = 40;
        }

        public string Name { get; }

        protected override void OnDragEnter(DragEvent e) => Log.Add($"enter {e.Payload}");

        protected override void OnDragOver(DragEvent e)
        {
            Log.Add("over");
            e.Accepted = Accepts;
        }

        protected override void OnDragLeave(DragEvent e) => Log.Add("leave");

        protected override void OnDrop(DragEvent e) => Log.Add($"drop {e.Payload}");
    }

    private static Vector2 Center(UIElement element) =>
        new(element.Bounds.X + element.Bounds.Width / 2, element.Bounds.Y + element.Bounds.Height / 2);

    [Test]
    public async Task DragsVisitTargetsAndDropWhereAccepted()
    {
        using Rig rig = new();
        Button source = rig.Add(new Button("Source"));
        Target first = rig.Add(new Target("first")), second = rig.Add(new Target("second"));
        Label preview = new("dragging");
        rig.Send(T.Motion(Center(source)), T.Button(Center(source), true));
        rig.Root.BeginDrag("apple", preview);
        await Assert.That(rig.Root.IsDragging).IsTrue();
        await Assert.That(source.State.HasFlag(UIState.Pressed)).IsFalse(); // let go of the pointer

        rig.Send(T.Motion(Center(first)), T.Motion(Center(second)));
        await Assert.That(string.Join(",", first.Log)).IsEqualTo("enter apple,over,leave");
        // The preview follows the pointer, beside it.
        await Assert.That(preview.Root).IsSameReferenceAs(rig.Root);
        await Assert.That(preview.Bounds.X).IsGreaterThan(Center(second).X);
        rig.Send(T.Button(Center(second), false));
        // The release checks the target once more, then drops.
        await Assert.That(string.Join(",", second.Log)).IsEqualTo("enter apple,over,over,drop apple");
        await Assert.That(rig.Root.IsDragging).IsFalse();
        await Assert.That(preview.Root).IsNull();

        // Not accepted: no drop, just leave.
        first.Log.Clear();
        first.Accepts = false;
        rig.Root.BeginDrag("pear");
        rig.Send(T.Motion(Center(first)), T.Button(Center(first), false));
        await Assert.That(string.Join(",", first.Log)).IsEqualTo("enter pear,over,over,leave");

        // Escape cancels: the target is left, nothing dropped.
        second.Log.Clear();
        rig.Root.BeginDrag("plum");
        rig.Send(T.Motion(Center(second)), T.Key(Escape));
        await Assert.That(rig.Root.IsDragging).IsFalse();
        await Assert.That(string.Join(",", second.Log)).IsEqualTo("enter plum,over,leave");
    }

    [Test]
    public async Task HostsCarryDragsInFromOtherWindows()
    {
        using Rig rig = new();
        Target target = rig.Add(new Target("target"));
        List<Vector2> outside = [];
        rig.Root.DragOutside = (_, at) => outside.Add(at);
        rig.Send(T.Motion(Center(target)));
        rig.Root.BeginDrag("cherry");
        rig.Send(T.Motion(new Vector2(-20, 10)));
        await Assert.That(outside.Count).IsEqualTo(1);
        rig.Root.CancelDrag();
        target.Log.Clear();

        // Another window's drag, carried here by its host.
        object payload = "carried";
        rig.Root.ContinueDrag(payload, null, Center(target));
        await Assert.That(rig.Root.DragPayload).IsSameReferenceAs(payload);
        await Assert.That(rig.Root.DropAt(Center(target))).IsTrue();
        await Assert.That(string.Join(",", target.Log)).IsEqualTo("enter carried,over,over,drop carried");
    }
}
