using System.Numerics;
using System.Text;
using Crucible.Binary;
using Crucible.Platform;
using Crucible.Text;
using Crucible.UI;

namespace Crucible.Tests;

// The tree view and dialogs (ADR 0068), under JIT and NativeAOT, driven by
// synthetic input with the default theme.
[NotInParallel("text")]
public class TreeViewTests
{
    private const int Return = 40, Escape = 41, Right = 79, Left = 80, Down = 81;

    private sealed class Rig : IDisposable
    {
        public readonly UIRoot Root = new();
        public readonly StackPanel Panel = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
        private readonly FontHandle _font = LoadNotoSans();

        public Rig()
        {
            Root.DefaultFont = _font;
            Root.Content = Panel;
        }

        public T Add<T>(T element) where T : UIElement
        {
            Panel.Add(element);
            Layout();
            return element;
        }

        public void Layout() => Root.UpdateLayout(new Vector2(400, 300));

        public void Send(params InputEvent[] events)
        {
            Root.ProcessInput(events);
            Layout();
        }

        public void Type(string typed)
        {
            byte[] utf8 = Encoding.UTF8.GetBytes(typed);
            Root.ProcessInput([new InputEvent { Type = InputEventType.TextInput, Code = 0, Value = utf8.Length }], utf8);
            Layout();
        }

        public void Dispose()
        {
            Root.Content = null;
            Fonts.Unload(_font);
        }

        private static FontHandle LoadNotoSans()
        {
            byte[] font = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fonts", "NotoSans-Regular.ttf"));
            string path = Path.Combine(Path.GetTempPath(), "crucible-tree-" + Guid.NewGuid().ToString("N") + ".crfont");
            try
            {
                using ImportedFileWriter writer = new(Tag.Of("FONT"), 1);
                writer.Add(Tag.Of("DATA"), 1, font).Write(path, Compression.None);
                return Fonts.Load(File.ReadAllBytes(path));
            }
            finally
            {
                File.Delete(path);
            }
        }
    }

    private static InputEvent Motion(Vector2 at) => new() { Type = InputEventType.MouseMotion, X = at.X, Y = at.Y };

    private static InputEvent Button(Vector2 at, bool down, int button = 1) =>
        new() { Type = down ? InputEventType.MouseButtonDown : InputEventType.MouseButtonUp, X = at.X, Y = at.Y, Code = button };

    private static InputEvent Key(int scancode) => new() { Type = InputEventType.KeyDown, Code = scancode };

    // 1 and 2 at the top; 1 holds 10 and 11, and 10 holds 100.
    private static readonly Dictionary<ulong, ulong[]> _nodes = new()
    {
        [0] = [1, 2],
        [1] = [10, 11],
        [10] = [100],
    };

    private static TreeView Sample() => new()
    {
        Height = 240,
        ChildrenOf = node => _nodes.GetValueOrDefault(node ?? 0, []),
        BindRow = row => row.Text = $"Node {row.Node}",
    };

    private static string Rows(TreeView tree) =>
        string.Join(" ", Enumerable.Range(0, tree.RowCount).Select(i => $"{tree.RowNode(i)}@{tree.RowDepth(i)}"));

    private static Vector2 RowPoint(TreeView tree, ulong node, float fraction)
    {
        TreeRow row = tree.RowOf(node)!;
        UIElement item = row.Parent!; // the list's row around it
        return new Vector2(item.Bounds.X + item.Bounds.Width / 2, item.Bounds.Y + item.Bounds.Height * fraction);
    }

    [Test]
    public async Task TreesShowExpandedNodesAndNavigateWithTheArrows()
    {
        using Rig rig = new();
        TreeView tree = rig.Add(Sample());
        tree.Refresh();
        rig.Layout();
        await Assert.That(Rows(tree)).IsEqualTo("1@0 2@0");
        tree.SetExpanded(1, true);
        rig.Layout();
        await Assert.That(Rows(tree)).IsEqualTo("1@0 10@1 11@1 2@0");
        await Assert.That(tree.RowOf(10)!.Text).IsEqualTo("Node 10");
        await Assert.That(tree.RowOf(10)!.Depth).IsEqualTo(1);

        // Focus selects; Right expands, then goes in; Left goes out, then collapses.
        tree.FocusNode(1);
        await Assert.That(tree.SelectedNode).IsEqualTo(1UL);
        rig.Send(Key(Down));
        await Assert.That(tree.SelectedNode).IsEqualTo(10UL);
        rig.Send(Key(Right));
        await Assert.That(tree.IsExpanded(10)).IsTrue();
        rig.Send(Key(Right));
        await Assert.That(tree.SelectedNode).IsEqualTo(100UL);
        rig.Send(Key(Left));
        await Assert.That(tree.SelectedNode).IsEqualTo(10UL);
        rig.Send(Key(Left));
        await Assert.That(tree.IsExpanded(10)).IsFalse();
        await Assert.That(Rows(tree)).IsEqualTo("1@0 10@1 11@1 2@0");

        // A refresh keeps what's expanded and selected.
        int changes = 0;
        tree.SelectionChanged += _ => ++changes;
        tree.Refresh();
        rig.Layout();
        await Assert.That(Rows(tree)).IsEqualTo("1@0 10@1 11@1 2@0");
        await Assert.That(tree.SelectedNode).IsEqualTo(10UL);
        await Assert.That(changes).IsEqualTo(0);

        // A press on an expander toggles it.
        TreeExpander expander = tree.RowOf(1)!.Children.OfType<StackPanel>().Single().Children.OfType<TreeExpander>().Single();
        await Assert.That(expander.Classes.Contains("open")).IsTrue();
        Vector2 at = new(expander.Bounds.X + expander.Bounds.Width / 2, expander.Bounds.Y + expander.Bounds.Height / 2);
        rig.Send(Motion(at), Button(at, true), Button(at, false));
        await Assert.That(Rows(tree)).IsEqualTo("1@0 2@0");
    }

    [Test]
    public async Task RowsScrolledAwayAndBackStillDrawTheirText()
    {
        using Rig rig = new();
        // Many rows with the same text: a reused row often shows it again.
        ulong[] many = [.. Enumerable.Range(1, 200).Select(i => (ulong)i)];
        TreeView tree = rig.Add(new TreeView
        {
            Height = 240,
            ChildrenOf = node => node is null ? many : [],
            BindRow = row => row.Text = row.Node % 3 == 0 ? "House" : "Tree",
        });
        tree.Refresh();
        rig.Layout();
        for (int pass = 0; pass < 2; ++pass)
        {
            tree.ScrollView.ScrollTo(Orientation.Vertical, pass == 0 ? float.MaxValue : 0);
            rig.Layout();
            rig.Layout();
        }
        string blank = string.Join(" ", many.Where(node => tree.RowOf(node) is { } row && row.Label.Layout is null));
        await Assert.That(blank).IsEqualTo("");
    }

    [Test]
    public async Task RowsDragOntoOneAnotherButNotIntoThemselves()
    {
        using Rig rig = new();
        TreeView tree = rig.Add(Sample());
        tree.CanDrag = true;
        tree.SetExpanded(1, true);
        rig.Layout();
        List<TreeDrop> drops = [];
        tree.Dropped += (_, drop) => drops.Add(drop);

        // 2 onto the middle of 11: inside it.
        Vector2 from = RowPoint(tree, 2, 0.5f), to = RowPoint(tree, 11, 0.5f);
        rig.Send(Motion(from), Button(from, true), Motion(from + new Vector2(0, -10)), Motion(to), Button(to, false));
        // Onto the top quarter of 10: before it; the bottom quarter of 2: after it.
        Vector2 before = RowPoint(tree, 10, 0.1f), after = RowPoint(tree, 2, 0.9f);
        from = RowPoint(tree, 11, 0.5f);
        rig.Send(Motion(from), Button(from, true), Motion(before), Button(before, false));
        rig.Send(Motion(from), Button(from, true), Motion(after), Button(after, false));
        await Assert.That(string.Join(" ", drops.Select(d => $"{d.Node}>{d.Target}:{d.Position}")))
            .IsEqualTo("2>11:Inside 11>10:Before 11>2:After");

        // 1 onto its own child: nothing.
        from = RowPoint(tree, 1, 0.5f);
        to = RowPoint(tree, 10, 0.5f);
        rig.Send(Motion(from), Button(from, true), Motion(to), Button(to, false));
        await Assert.That(drops.Count).IsEqualTo(3);
        // A press without moving selects, and doesn't drop.
        rig.Send(Motion(to), Button(to, true), Button(to, false));
        await Assert.That(drops.Count).IsEqualTo(3);
        await Assert.That(tree.SelectedNode).IsEqualTo(10UL);

        // A right-click selects the row and asks for a menu.
        ulong? asked = 0;
        tree.ContextRequested += (_, node, _) => asked = node;
        Vector2 two = RowPoint(tree, 2, 0.5f);
        rig.Send(Motion(two), Button(two, true, 3), Button(two, false, 3));
        await Assert.That(asked).IsEqualTo(2UL);
        await Assert.That(tree.SelectedNode).IsEqualTo(2UL);
    }

    [Test]
    public async Task RowsRenameInlineAndEscapeKeepsTheOldText()
    {
        using Rig rig = new();
        TreeView tree = rig.Add(Sample());
        tree.Refresh();
        rig.Layout();
        List<string> renamed = [];
        tree.Renamed += (_, node, text) => renamed.Add($"{node}={text}");

        await Assert.That(tree.BeginRename(2)).IsTrue();
        rig.Layout();
        await Assert.That(tree.RowOf(2)!.IsRenaming).IsTrue();
        await Assert.That(rig.Root.FocusedElement is TextField).IsTrue();
        rig.Type("Village"); // replaces the selected text
        rig.Send(Key(Return));
        await Assert.That(tree.RowOf(2)!.IsRenaming).IsFalse();
        await Assert.That(string.Join(",", renamed)).IsEqualTo("2=Village");
        await Assert.That(rig.Root.FocusedElement is ListItem { Index: 1 }).IsTrue();

        tree.BeginRename(1);
        rig.Type("Gone");
        rig.Send(Key(Escape));
        await Assert.That(tree.RowOf(1)!.IsRenaming).IsFalse();
        await Assert.That(renamed.Count).IsEqualTo(1);
    }

    [Test]
    public async Task DialogsAreModalAndAnswerWithAButtonOrCancel()
    {
        using Rig rig = new();
        Button behind = rig.Add(new Button("Behind"));
        int clicks = 0;
        behind.Clicked += _ => ++clicks;
        List<int> answers = [];
        Dialog dialog = new("Save changes?", "Save", "Don't Save", "Cancel");
        dialog.Chosen += (_, choice) => answers.Add(choice);
        dialog.Open(rig.Root);
        rig.Layout();
        await Assert.That(dialog.IsModal).IsTrue();
        await Assert.That(rig.Root.FocusedElement is Button { Text: "Save" }).IsTrue();

        // A press outside is used up: the dialog stays, the button behind doesn't click.
        Vector2 at = new(behind.Bounds.X + 4, behind.Bounds.Y + 4);
        rig.Send(Motion(at), Button(at, true), Button(at, false));
        await Assert.That(dialog.IsOpen).IsTrue();
        await Assert.That(clicks).IsEqualTo(0);

        rig.Send(Key(Escape));
        await Assert.That(dialog.IsOpen).IsFalse();
        await Assert.That(string.Join(",", answers)).IsEqualTo("-1");

        Dialog again = new("Save changes?", "Save", "Don't Save");
        again.Chosen += (_, choice) => answers.Add(choice);
        again.Open(rig.Root);
        rig.Layout();
        rig.Send(Key(Right)); // to Don't Save
        rig.Send(Key(Return));
        await Assert.That(again.IsOpen).IsFalse();
        await Assert.That(string.Join(",", answers)).IsEqualTo("-1,1");
    }
}
