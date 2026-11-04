using System.Numerics;
using System.Text;
using Crucible.Binary;
using Crucible.Platform;
using Crucible.Text;
using Crucible.UI;

namespace Crucible.Tests;

// Text fields (ADR 0065), under JIT and NativeAOT, driven by synthetic
// platform events: typed text arrives as TextInput events pointing into the
// frame's polled text, as the platform sends it. The platform isn't started
// here, so the clipboard is the UI's own.
[NotInParallel("text")]
public class TextFieldTests
{
    private const int A = 4, C = 6, V = 25, X = 27, Y = 28, Z = 29;
    private const int Return = 40, Escape = 41, Backspace = 42, Space = 44;
    private const int Home = 74, Delete = 76, End = 77, Right = 79, Left = 80, Down = 81, Up = 82;

    private sealed class Rig : IDisposable
    {
        public double Time = 10;
        public readonly UIRoot Root = new();
        public readonly StackPanel Panel = new() { Spacing = 10, HorizontalAlignment = HorizontalAlignment.Start };
        private readonly FontHandle _font = LoadNotoSans();

        public Rig(float scale = 1)
        {
            Root.Scale = scale;
            Root.DefaultFont = _font;
            Root.Content = Panel;
            Root.TestClock = () => Time;
        }

        public T Add<T>(T element) where T : UIElement
        {
            Panel.Add(element);
            Layout();
            return element;
        }

        public void Layout() => Root.UpdateLayout(new Vector2(500, 400) * Root.Scale);

        public void Send(params InputEvent[] events)
        {
            Root.ProcessInput(events);
            Layout();
        }

        // Typed text, one TextInput event per piece, as the platform polls it.
        public void Type(params string[] pieces)
        {
            List<byte> text = [];
            InputEvent[] events = new InputEvent[pieces.Length];
            for (int i = 0; i < pieces.Length; ++i)
            {
                byte[] utf8 = Encoding.UTF8.GetBytes(pieces[i]);
                events[i] = new InputEvent { Type = InputEventType.TextInput, Code = text.Count, Value = utf8.Length };
                text.AddRange(utf8);
            }
            Root.ProcessInput(events, text.ToArray());
            Layout();
        }

        // A press and release at a point in logical units; presses close in time count up.
        public void Click(Vector2 at, KeyModifiers modifiers = KeyModifiers.None)
        {
            Vector2 p = at * Root.Scale;
            Send(Motion(p), Button(p, true, modifiers), Button(p, false, modifiers));
        }

        public void Dispose()
        {
            Root.Content = null;
            Fonts.Unload(_font);
        }
    }

    private static FontHandle LoadNotoSans()
    {
        byte[] font = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fonts", "NotoSans-Regular.ttf"));
        string path = Path.Combine(Path.GetTempPath(), "crucible-textfield-" + Guid.NewGuid().ToString("N") + ".crfont");
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

    private static InputEvent Motion(Vector2 at) => new() { Type = InputEventType.MouseMotion, X = at.X, Y = at.Y };

    private static InputEvent Button(Vector2 at, bool down, KeyModifiers modifiers = KeyModifiers.None) => new()
    {
        Type = down ? InputEventType.MouseButtonDown : InputEventType.MouseButtonUp,
        X = at.X,
        Y = at.Y,
        Code = 1,
        Modifiers = (uint)modifiers,
    };

    private static InputEvent Key(int scancode, KeyModifiers modifiers = KeyModifiers.None) =>
        new() { Type = InputEventType.KeyDown, Code = scancode, Modifiers = (uint)modifiers };

    private static InputEvent Wheel(Vector2 at, float notches) =>
        new() { Type = InputEventType.MouseWheel, X = at.X, Y = at.Y, Dy = notches };

    // Where the caret before a character sits, in the root's logical units.
    private static Vector2 At(TextField field, int index)
    {
        TextCaret caret = field.Layout!.Caret(index);
        return new Vector2(field.Bounds.X + field.Padding.Left + caret.X - field.ScrollX,
            field.Bounds.Y + field.Padding.Top + caret.Top + caret.Height / 2);
    }

    private static Vector2 Center(UIElement element) =>
        new(element.Bounds.X + element.Bounds.Width / 2, element.Bounds.Y + element.Bounds.Height / 2);

    [Test]
    public async Task TypingEditsAtTheCaret()
    {
        using Rig rig = new();
        TextField field = rig.Add(new TextField());
        field.Focus();
        rig.Type("hel", "lo");
        await Assert.That(field.Text).IsEqualTo("hello");
        await Assert.That(field.CaretIndex).IsEqualTo(5);
        await Assert.That(rig.Root.HandledKeyboard).IsTrue();

        rig.Send(Key(Left), Key(Left));
        rig.Type("é"); // two UTF-8 bytes, one character
        await Assert.That(field.Text).IsEqualTo("helélo");
        rig.Send(Key(Backspace), Key(Delete));
        await Assert.That(field.Text).IsEqualTo("helo");
        rig.Send(Key(Home));
        rig.Type(">");
        rig.Send(Key(End));
        rig.Type("<");
        await Assert.That(field.Text).IsEqualTo(">helo<");

        // Shift extends a selection, typing replaces it.
        rig.Send(Key(Left), Key(Left, KeyModifiers.Shift), Key(Left, KeyModifiers.Shift));
        await Assert.That(field.SelectedText).IsEqualTo("lo");
        rig.Type("p");
        await Assert.That(field.Text).IsEqualTo(">hep<");
        // Left with a selection goes to its start.
        rig.Send(Key(Home, KeyModifiers.Shift), Key(Left));
        await Assert.That(field.CaretIndex).IsEqualTo(0);
        await Assert.That(field.SelectionLength).IsEqualTo(0);
    }

    [Test]
    public async Task SpaceIsTextNotActivation()
    {
        using Rig rig = new();
        TextField field = rig.Add(new TextField());
        int activations = 0;
        field.Activated += (_, _) => ++activations;
        field.Focus();
        rig.Send(Key(Space));
        rig.Type(" ");
        await Assert.That(activations).IsEqualTo(0);
        await Assert.That(field.Text).IsEqualTo(" ");
        await Assert.That(rig.Root.HandledKeyboard).IsTrue();
    }

    [Test]
    public async Task CtrlMovesAndDeletesByWords()
    {
        using Rig rig = new();
        TextField field = rig.Add(new TextField("one two, three"));
        field.Focus();
        rig.Send(Key(End), Key(Left, KeyModifiers.Ctrl));
        await Assert.That(field.CaretIndex).IsEqualTo(9); // before "three"
        rig.Send(Key(Left, KeyModifiers.Ctrl));
        await Assert.That(field.CaretIndex).IsEqualTo(7); // before ","
        rig.Send(Key(Home), Key(Right, KeyModifiers.Ctrl));
        await Assert.That(field.CaretIndex).IsEqualTo(3);
        rig.Send(Key(End), Key(Backspace, KeyModifiers.Ctrl));
        await Assert.That(field.Text).IsEqualTo("one two, ");
        rig.Send(Key(A, KeyModifiers.Ctrl));
        await Assert.That(field.SelectedText).IsEqualTo("one two, ");
    }

    [Test]
    public async Task UndoGroupsTypingAndRedoes()
    {
        using Rig rig = new();
        TextField field = rig.Add(new TextField("start"));
        field.Focus();
        rig.Send(Key(End));
        rig.Type(" ", "a", "b", "c");
        rig.Send(Key(Backspace), Key(Backspace));
        await Assert.That(field.Text).IsEqualTo("start a");
        rig.Send(Key(Z, KeyModifiers.Ctrl)); // both deletions
        await Assert.That(field.Text).IsEqualTo("start abc");
        rig.Send(Key(Z, KeyModifiers.Ctrl)); // all the typing
        await Assert.That(field.Text).IsEqualTo("start");
        await Assert.That(field.Undo()).IsFalse();
        rig.Send(Key(Y, KeyModifiers.Ctrl));
        await Assert.That(field.Text).IsEqualTo("start abc");
        rig.Send(Key(Z, KeyModifiers.Ctrl | KeyModifiers.Shift));
        await Assert.That(field.Text).IsEqualTo("start a");

        // Moving the caret ends a group.
        rig.Type("x");
        rig.Send(Key(Left), Key(Right));
        rig.Type("y");
        rig.Send(Key(Z, KeyModifiers.Ctrl));
        await Assert.That(field.Text).IsEqualTo("start ax");
        // A new edit clears what could be redone.
        rig.Type("z");
        await Assert.That(field.Redo()).IsFalse();
    }

    [Test]
    public async Task CopyCutAndPaste()
    {
        using Rig rig = new();
        TextField field = rig.Add(new TextField("copy me"));
        field.Focus();
        field.Select(0, 4);
        rig.Send(Key(C, KeyModifiers.Ctrl));
        await Assert.That(Clipboard.Text).IsEqualTo("copy");
        rig.Send(Key(End), Key(V, KeyModifiers.Ctrl));
        await Assert.That(field.Text).IsEqualTo("copy mecopy");
        field.Select(4, 7);
        rig.Send(Key(X, KeyModifiers.Ctrl));
        await Assert.That(field.Text).IsEqualTo("copycopy");
        await Assert.That(Clipboard.Text).IsEqualTo(" me");

        // One line takes line breaks as spaces.
        Clipboard.Text = "two\nlines";
        rig.Send(Key(V, KeyModifiers.Ctrl));
        await Assert.That(field.Text).IsEqualTo("copytwo linescopy");
        // Other shortcuts are left to the game.
        rig.Send(Key(19, KeyModifiers.Ctrl)); // Ctrl+P
        await Assert.That(rig.Root.HandledKeyboard).IsFalse();
    }

    [Test]
    public async Task EnterCommitsEscapeRevertsAndLeavingCommits()
    {
        using Rig rig = new();
        TextField field = rig.Add(new TextField("name"));
        Button other = rig.Add(new Button("Other"));
        int commits = 0, changes = 0;
        field.Committed += _ => ++commits;
        field.TextChanged += _ => ++changes;
        int cancels = 0;
        rig.Panel.Canceled += (_, _) => ++cancels;

        field.Focus();
        await Assert.That(field.SelectedText).IsEqualTo("name"); // focus selects it all
        rig.Type("x");
        rig.Send(Key(Escape));
        await Assert.That(field.Text).IsEqualTo("name");
        await Assert.That(cancels).IsEqualTo(0);
        rig.Send(Key(Escape)); // nothing to put back: Cancel goes on
        await Assert.That(cancels).IsEqualTo(1);
        await Assert.That(commits).IsEqualTo(0);

        rig.Send(Key(End));
        rig.Type("d");
        rig.Send(Key(Return));
        await Assert.That(commits).IsEqualTo(1);
        rig.Send(Key(Return)); // unchanged since: nothing to commit
        await Assert.That(commits).IsEqualTo(1);

        rig.Type("!");
        other.Focus();
        await Assert.That(commits).IsEqualTo(2);
        await Assert.That(field.Text).IsEqualTo("named!");
        await Assert.That(changes).IsEqualTo(4); // x, back, d, !

        field.Text = "set"; // by code: a change, not a commit
        await Assert.That(changes).IsEqualTo(5);
        await Assert.That(commits).IsEqualTo(2);
    }

    [Test]
    public async Task ClicksPlaceTheCaretAndSelectWordsAndAll()
    {
        using Rig rig = new();
        TextField field = rig.Add(new TextField("alpha beta gamma"));
        rig.Click(At(field, 8));
        await Assert.That(field.IsFocused).IsTrue();
        await Assert.That(field.CaretIndex).IsEqualTo(8);
        await Assert.That(field.SelectionLength).IsEqualTo(0);

        rig.Time += 0.1;
        rig.Click(At(field, 8)); // a double-click: the word
        await Assert.That(field.SelectedText).IsEqualTo("beta");
        rig.Time += 0.1;
        rig.Click(At(field, 8)); // a triple-click: everything
        await Assert.That(field.SelectedText).IsEqualTo("alpha beta gamma");

        rig.Time += 1; // too late to count on
        rig.Click(At(field, 2));
        await Assert.That(field.CaretIndex).IsEqualTo(2);
        rig.Click(At(field, 13), KeyModifiers.Shift);
        await Assert.That(field.SelectedText).IsEqualTo("pha beta ga");

        // Dragging selects.
        rig.Time += 1;
        Vector2 from = At(field, 6), to = At(field, 10);
        rig.Send(Motion(from), Button(from, true), Motion(to), Button(to, false));
        await Assert.That(field.SelectedText).IsEqualTo("beta");
    }

    [Test]
    public async Task OneLineScrollsToTheCaretByWholePixels()
    {
        using Rig rig = new(1.25f);
        TextField field = rig.Add(new TextField { Width = 200 });
        field.Focus();
        rig.Type("a long line of text that runs well past the edge");
        await Assert.That(field.ScrollX).IsGreaterThan(0f);
        float physical = field.ScrollX * 1.25f;
        await Assert.That(MathF.Abs(physical - MathF.Round(physical))).IsLessThan(0.001f);
        // The caret is in view.
        float caretX = field.Bounds.X + field.Padding.Left + field.Layout!.Caret(field.CaretIndex).X - field.ScrollX;
        await Assert.That(caretX).IsLessThanOrEqualTo(field.Bounds.Right - field.Padding.Right);
        rig.Send(Key(Home));
        await Assert.That(field.ScrollX).IsEqualTo(0f);
        // Up and down aren't one line's: focus moves on.
        Button below = rig.Add(new Button("Below"));
        rig.Send(Key(Down));
        await Assert.That(below.IsFocused).IsTrue();
    }

    [Test]
    public async Task MultilineWrapsAndMovesBetweenLines()
    {
        using Rig rig = new();
        TextField field = rig.Add(new TextField { Multiline = true, Width = 140 });
        field.Focus();
        rig.Type("first");
        rig.Send(Key(Return));
        rig.Type("second line that wraps around");
        await Assert.That(field.Text).IsEqualTo("first\nsecond line that wraps around");
        await Assert.That(field.Layout!.LineCount).IsGreaterThan(2);
        float oneLine = field.Layout.Line(0).Height;
        await Assert.That(field.DesiredSize.Y).IsGreaterThan(oneLine * 2.5f);

        rig.Send(Key(Up, KeyModifiers.Ctrl), Key(Home, KeyModifiers.Ctrl));
        await Assert.That(field.CaretIndex).IsEqualTo(0);
        rig.Send(Key(Right), Key(Right), Key(Down));
        await Assert.That(field.Layout.Caret(field.CaretIndex).Line).IsEqualTo(1u);
        await Assert.That(field.CaretIndex).IsBetween(7, 8); // under "fi|rst": "s|econd" or "se|cond"
        rig.Send(Key(End));
        await Assert.That(field.Layout.Caret(field.CaretIndex).Line).IsEqualTo(1u); // stays on its line
        rig.Send(Key(Up));
        await Assert.That(field.Layout.Caret(field.CaretIndex).Line).IsEqualTo(0u);
        // Ctrl+Enter commits instead.
        int commits = 0;
        field.Committed += _ => ++commits;
        rig.Send(Key(Return, KeyModifiers.Ctrl));
        await Assert.That(commits).IsEqualTo(1);
    }

    [Test]
    public async Task TheCaretBlinksWhileFocused()
    {
        using Rig rig = new();
        TextField field = rig.Add(new TextField("blink"));
        field.Focus();
        rig.Layout();
        await Assert.That(field.CaretShown).IsTrue();
        rig.Time += 0.6;
        rig.Layout();
        await Assert.That(field.CaretShown).IsFalse();
        rig.Send(Key(Left)); // moving shows it again
        await Assert.That(field.CaretShown).IsTrue();
        rig.Time += 1.05;
        rig.Layout();
        await Assert.That(field.CaretShown).IsTrue();
    }

    [Test]
    public async Task NumberFieldsCommitTypedNumbersAndStep()
    {
        using Rig rig = new();
        NumberField field = rig.Add(new NumberField { Minimum = 0, Maximum = 100, Step = 0.5, Value = 10 });
        int changes = 0, commits = 0;
        field.ValueChanged += _ => ++changes;
        field.Committed += _ => ++commits;
        await Assert.That(field.Text).IsEqualTo("10.0");

        field.Focus();
        rig.Type("42.25");
        await Assert.That(field.Value).IsEqualTo(10.0); // not yet
        rig.Send(Key(Return));
        await Assert.That(field.Value).IsEqualTo(42.25);
        await Assert.That(field.Text).IsEqualTo("42.25"); // more decimals than the step's when it has them
        await Assert.That(commits).IsEqualTo(1);

        field.SelectAll();
        rig.Type("lots");
        rig.Send(Key(Return));
        await Assert.That(field.Value).IsEqualTo(42.25);
        await Assert.That(field.Text).IsEqualTo("42.25");

        field.SelectAll();
        rig.Type("500");
        rig.Send(Key(Return));
        await Assert.That(field.Value).IsEqualTo(100.0);

        rig.Send(Key(Down), Key(Down, KeyModifiers.Shift));
        await Assert.That(field.Value).IsEqualTo(94.5);
        await Assert.That(changes).IsEqualTo(4);
        await Assert.That(commits).IsEqualTo(4);
        rig.Send(Wheel(Center(field), 1));
        await Assert.That(field.Value).IsEqualTo(95.0);

        // The wheel leaves an unfocused field alone.
        rig.Root.ClearFocus();
        rig.Send(Wheel(Center(field), 1));
        await Assert.That(field.Value).IsEqualTo(95.0);
    }

    [Test]
    public async Task APressThatFocusesANumberFieldScrubs()
    {
        using Rig rig = new();
        NumberField field = rig.Add(new NumberField { Value = 5 });
        int commits = 0;
        field.Committed += _ => ++commits;
        Vector2 at = Center(field);
        rig.Send(Motion(at), Button(at, true), Motion(at + new Vector2(20, 0)), Button(at + new Vector2(20, 0), false));
        await Assert.That(field.Value).IsEqualTo(10.0); // 4 units a step
        await Assert.That(commits).IsEqualTo(1);
        await Assert.That(field.IsFocused).IsTrue();

        // Focused already, a press places the caret instead.
        rig.Time += 1;
        rig.Send(Motion(at), Button(at, true), Motion(at + new Vector2(20, 0)), Button(at + new Vector2(20, 0), false));
        await Assert.That(field.Value).IsEqualTo(10.0);

        // A focusing press released without moving leaves it all selected, to type over.
        rig.Root.ClearFocus();
        rig.Layout();
        rig.Time += 1;
        rig.Click(at);
        await Assert.That(field.SelectedText).IsEqualTo("10");
        rig.Type("7");
        rig.Send(Key(Return));
        await Assert.That(field.Value).IsEqualTo(7.0);
    }
}
