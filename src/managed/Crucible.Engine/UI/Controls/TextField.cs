using System.Drawing;
using System.Globalization;
using System.Numerics;
using Crucible.Platform;
using Crucible.Text;

namespace Crucible.UI;

/// <summary>
/// Editable text (ADR 0065): one line that scrolls sideways to follow the
/// caret, or <see cref="Multiline"/> text that wraps. Typing, the caret and
/// selection keys, the mouse (click, drag, double-click a word, triple-click
/// all), the clipboard, and its own undo, which groups a run of typing into
/// one step. <see cref="TextChanged"/> fires on every edit,
/// <see cref="Committed"/> when the edit is done: Enter (one line only) or
/// losing focus. Escape puts back the text it had when focused.
/// </summary>
public class TextField : UIElement
{
    public static readonly StyledProperty<Vector4> CaretColorProperty = StyledProperty<Vector4>.Register<TextField>(
        "CaretColor", new Vector4(1), Invalidates.Visual, StyleValues.ReadColor, StyleValues.LerpColor);
    public static readonly StyledProperty<Vector4> SelectionColorProperty = StyledProperty<Vector4>.Register<TextField>(
        "SelectionColor", new Vector4(0.3f, 0.55f, 0.95f, 0.45f), Invalidates.Visual, StyleValues.ReadColor,
        StyleValues.LerpColor);

    // SDL scancodes (SDL_scancode.h): by position, so shortcuts sit where
    // QWERTY has them on any layout.
    private const int ScancodeA = 4, ScancodeC = 6, ScancodeV = 25, ScancodeX = 27, ScancodeY = 28, ScancodeZ = 29;
    private const int ScancodeReturn = 40, ScancodeEscape = 41, ScancodeBackspace = 42, ScancodeTab = 43;
    private const int ScancodeHome = 74, ScancodeDelete = 76, ScancodeEnd = 77;
    private const int ScancodeRight = 79, ScancodeLeft = 80, ScancodeDown = 81, ScancodeUp = 82, ScancodeKeypadEnter = 88;
    private const int UndoLimit = 100;
    private const double BlinkPeriod = 1.0; // shown for the first half

    private readonly ShapedText _shaped = new();
    private string _text = "";
    private bool _multiline;
    private int _caret;
    private int _anchor; // the selection's other end; equal to the caret for none
    private float _goalX = float.NaN; // where up and down aim, kept across short lines
    private float _scrollX;
    private bool _revealCaret;
    private bool _selecting; // a drag from a press
    private string _revert = ""; // what Escape puts back and Committed compares with
    private readonly List<Snapshot> _undo = [];
    private readonly List<Snapshot> _redo = [];
    private EditKind _lastEdit;
    private double _blinkStart;
    private bool _caretShown;

    private readonly record struct Snapshot(string Text, int Caret, int Anchor);

    private enum EditKind
    {
        None,
        Typing,
        Deleting,
        Other,
    }

    public TextField()
    {
        IsFocusable = true;
        ClipToBounds = true;
    }

    public TextField(string text) : this() => SetTextCore(text);

    /// <summary>
    /// The text. Setting it puts the caret at the end, starts a new undo
    /// history, and raises <see cref="TextChanged"/> if it changed.
    /// </summary>
    public string Text
    {
        get => _text;
        set
        {
            string text = Clean(value ?? "");
            _undo.Clear();
            _redo.Clear();
            _lastEdit = EditKind.None;
            _revert = text;
            if (text != _text)
            {
                Replace(text, text.Length, text.Length);
            }
            else
            {
                Select(_text.Length, _text.Length);
            }
        }
    }

    /// <summary>Several lines, wrapped to its width: Enter starts a new line, and up and down move between lines.</summary>
    public bool Multiline
    {
        get => _multiline;
        set
        {
            if (_multiline != value)
            {
                _multiline = value;
                _scrollX = 0;
                if (!value)
                {
                    string text = Clean(_text);
                    if (text != _text)
                    {
                        Replace(text, Math.Min(_caret, text.Length), Math.Min(_anchor, text.Length));
                    }
                }
                InvalidateMeasure();
            }
        }
    }

    public Vector4 CaretColor { get => GetValue(CaretColorProperty); set => SetValue(CaretColorProperty, value); }

    public Vector4 SelectionColor { get => GetValue(SelectionColorProperty); set => SetValue(SelectionColorProperty, value); }

    /// <summary>The caret's index in <see cref="Text"/>.</summary>
    public int CaretIndex => _caret;

    /// <summary>Where the selection starts (the lower end).</summary>
    public int SelectionStart => Math.Min(_caret, _anchor);

    public int SelectionLength => Math.Abs(_caret - _anchor);

    public string SelectedText => _text.Substring(SelectionStart, SelectionLength);

    /// <summary>The text as last shaped; null without a font.</summary>
    public TextLayout? Layout => _shaped.Layout;

    // Tests: the caret's blink, and how far one line is scrolled.
    internal bool CaretShown => _caretShown;

    internal float ScrollX => _scrollX;

    /// <summary>The text changed: typed, deleted, pasted, undone, or set.</summary>
    public event Action<TextField>? TextChanged;

    /// <summary>An edit is done (Enter, or focus leaving) and the text differs from before it.</summary>
    public event Action<TextField>? Committed;

    /// <summary>Selects from <paramref name="anchor"/> to <paramref name="caret"/> (indices into the text, clamped).</summary>
    public void Select(int anchor, int caret)
    {
        _lastEdit = EditKind.None;
        SelectCore(anchor, caret);
    }

    private void SelectCore(int anchor, int caret)
    {
        _anchor = Math.Clamp(anchor, 0, _text.Length);
        _caret = Math.Clamp(caret, 0, _text.Length);
        _goalX = float.NaN;
        CaretMoved();
    }

    public void SelectAll() => Select(0, _text.Length);

    /// <summary>Puts the text as it was before the last edit (or group of typing); false if there's none.</summary>
    public bool Undo() => Restore(_undo, _redo);

    public bool Redo() => Restore(_redo, _undo);

    protected internal override bool AcceptsTextInput => true;

    // -----------------------------------------------------------------------
    // Layout and drawing
    // -----------------------------------------------------------------------

    protected override Vector2 MeasureCore(Vector2 available)
    {
        float wrapWidth = _multiline && float.IsFinite(available.X) ? MathF.Max(available.X - CaretWidth, 1) : 0;
        TextLayout? layout = _shaped.Shape(this, GlyphRendering.Exact, _text, wrapWidth);
        if (layout is null)
        {
            return Vector2.Zero;
        }
        Vector2 size = layout.Size + new Vector2(CaretWidth, 0);
        return new Vector2(float.IsFinite(available.X) ? MathF.Min(size.X, available.X) : size.X, size.Y);
    }

    protected override void ArrangeCore(RectangleF content)
    {
        base.ArrangeCore(content);
        ClampScroll(content.Width);
        if (_revealCaret)
        {
            _revealCaret = false;
            Reveal(content);
        }
    }

    protected override void OnDraw(Canvas canvas)
    {
        if (_shaped.Layout is not { } layout)
        {
            return;
        }
        Thickness padding = Padding;
        Vector2 origin = new(padding.Left - _scrollX, padding.Top);
        canvas.PushClip(new Vector2(padding.Left, padding.Top), Vector2.Max(ActualSize - padding.Total, Vector2.Zero));
        if (IsFocused && _caret != _anchor)
        {
            DrawSelection(canvas, layout, origin);
        }
        canvas.DrawText(layout, origin, Foreground);
        if (IsFocused && _caretShown)
        {
            TextCaret caret = layout.Caret(_caret);
            float scale = Root?.Scale ?? 1;
            float x = MathF.Round((origin.X + caret.X) * scale) / scale;
            canvas.FillRect(new Vector2(x, origin.Y + caret.Top), new Vector2(CaretWidth, caret.Height), CaretColor);
        }
        canvas.PopClip();
    }

    protected override void OnDetached(UIRoot root) => _shaped.Detach();

    // One physical pixel at 1x, whole pixels above.
    private float CaretWidth
    {
        get
        {
            float scale = Root?.Scale ?? 1;
            return MathF.Max(MathF.Round(scale), 1) / scale;
        }
    }

    private void DrawSelection(Canvas canvas, TextLayout layout, Vector2 origin)
    {
        int start = SelectionStart, end = start + SelectionLength;
        for (int i = 0; i < layout.LineCount; ++i)
        {
            TextLine line = layout.Line(i);
            int from = Math.Max(start, (int)line.Start), to = Math.Min(end, (int)line.End);
            if (from > to || (from == to && !(end > line.End && from == (int)line.End)))
            {
                continue;
            }
            float x0 = layout.Caret(from).X;
            float x1 = to == (int)line.End ? MathF.Max(line.Width, x0) : layout.Caret(to).X;
            if (end > line.End && to == (int)line.End)
            {
                x1 += FontSize * 0.3f; // the line break, selected
            }
            canvas.FillRect(origin + new Vector2(x0, line.Top), new Vector2(x1 - x0, line.Height), SelectionColor);
        }
    }

    // Keeps the caret inside the box: one line scrolls sideways (by whole
    // physical pixels, so the text stays crisp), several lines ask the
    // scroll views around it.
    private void Reveal(RectangleF content)
    {
        if (_shaped.Layout is not { } layout)
        {
            return;
        }
        TextCaret caret = layout.Caret(_caret);
        if (!_multiline)
        {
            float scroll = _scrollX;
            if (caret.X - scroll > content.Width - CaretWidth)
            {
                scroll = caret.X - content.Width + CaretWidth;
            }
            if (caret.X < scroll)
            {
                scroll = caret.X;
            }
            float scale = Root?.Scale ?? 1;
            _scrollX = MathF.Round(scroll * scale) / scale;
            ClampScroll(content.Width);
            return;
        }
        RectangleF target = new(content.X + caret.X, content.Y + caret.Top, CaretWidth, caret.Height);
        for (UIElement? at = Parent; at is not null; at = at.Parent)
        {
            if (at is ScrollView view)
            {
                view.ScrollIntoView(target);
            }
        }
    }

    private void ClampScroll(float width)
    {
        float textWidth = _shaped.Layout?.Size.X ?? 0;
        _scrollX = _multiline ? 0 : Math.Clamp(_scrollX, 0, MathF.Max(textWidth + CaretWidth - width, 0));
    }

    // -----------------------------------------------------------------------
    // Focus, blinking, committing
    // -----------------------------------------------------------------------

    protected override void OnGotFocus(UIEvent e)
    {
        _revert = _text;
        _lastEdit = EditKind.None;
        // Focus from the keyboard or a gamepad selects everything, ready to
        // type over; a click then puts the caret where it landed.
        SelectAll();
    }

    protected override void OnLostFocus(UIEvent e)
    {
        _selecting = false;
        Commit();
        InvalidateVisual();
    }

    protected internal override void OnFocusedTick(double now)
    {
        bool shown = (now - _blinkStart) % BlinkPeriod < BlinkPeriod * 0.5;
        if (shown != _caretShown)
        {
            _caretShown = shown;
            InvalidateVisual();
        }
    }

    /// <summary>Ends the edit: raises <see cref="Committed"/> if the text changed since it began.</summary>
    protected virtual void Commit()
    {
        if (IsEdited)
        {
            MarkCommitted();
        }
        _lastEdit = EditKind.None;
    }

    /// <summary>The text differs from when the edit began.</summary>
    protected bool IsEdited => _text != _revert;

    // The edit is done as the text now stands: raises Committed.
    private protected void MarkCommitted()
    {
        _revert = _text;
        _lastEdit = EditKind.None;
        Committed?.Invoke(this);
    }

    // -----------------------------------------------------------------------
    // Typing and keys
    // -----------------------------------------------------------------------

    protected override void OnTextInput(TextInputEvent e)
    {
        e.Handled = true;
        Insert(e.Text, EditKind.Typing);
    }

    protected override void OnKeyDown(KeyEvent e)
    {
        bool ctrl = (e.Modifiers & KeyModifiers.Ctrl) != 0, shift = (e.Modifiers & KeyModifiers.Shift) != 0;
        switch (e.Scancode)
        {
            case ScancodeLeft:
            case ScancodeRight:
                {
                    int direction = e.Scancode == ScancodeLeft ? -1 : 1;
                    int to = ctrl ? WordEdge(_caret, direction)
                        : !shift && _caret != _anchor ? (direction < 0 ? SelectionStart : SelectionStart + SelectionLength)
                        : Step(_caret, direction);
                    MoveCaret(to, shift);
                    break;
                }
            case ScancodeHome:
                MoveCaret(ctrl || !_multiline ? 0 : LineEdge(-1), shift);
                break;
            case ScancodeEnd:
                MoveCaret(ctrl || !_multiline ? _text.Length : LineEdge(1), shift);
                break;
            case ScancodeUp:
            case ScancodeDown:
                if (!_multiline)
                {
                    return; // unhandled: focus moves on
                }
                MoveVertically(e.Scancode == ScancodeUp ? -1 : 1, shift);
                break;
            case ScancodeBackspace:
            case ScancodeDelete:
                {
                    int direction = e.Scancode == ScancodeBackspace ? -1 : 1;
                    if (_caret == _anchor)
                    {
                        int to = ctrl ? WordEdge(_caret, direction) : Step(_caret, direction);
                        SelectCore(_caret, to); // keeps a run of deleting going
                    }
                    Insert("", EditKind.Deleting);
                    break;
                }
            case ScancodeReturn:
            case ScancodeKeypadEnter:
                if (_multiline && !ctrl)
                {
                    Insert("\n", EditKind.Other);
                }
                else
                {
                    Commit();
                }
                break;
            case ScancodeEscape:
                if (_text == _revert)
                {
                    return; // nothing to put back: Cancel goes on (closing a dialog)
                }
                Edit(_revert, _revert.Length, _revert.Length, EditKind.Other);
                break;
            case ScancodeTab:
                return;
            case ScancodeA when ctrl:
                SelectAll();
                break;
            case ScancodeC when ctrl:
            case ScancodeX when ctrl:
                if (_caret != _anchor)
                {
                    Clipboard.Text = SelectedText;
                    if (e.Scancode == ScancodeX)
                    {
                        Insert("", EditKind.Other);
                    }
                }
                break;
            case ScancodeV when ctrl:
                Insert(Clipboard.Text, EditKind.Other);
                break;
            case ScancodeZ when ctrl:
                if (shift)
                {
                    Redo();
                }
                else
                {
                    Undo();
                }
                break;
            case ScancodeY when ctrl:
                Redo();
                break;
            default:
                if ((e.Modifiers & (KeyModifiers.Ctrl | KeyModifiers.Alt | KeyModifiers.Gui)) != 0)
                {
                    return; // a shortcut that isn't the field's: the game's
                }
                break;
        }
        // Every other key is the field's too: its characters arrive as text,
        // and Space mustn't activate anything.
        e.Handled = true;
    }

    // -----------------------------------------------------------------------
    // The pointer
    // -----------------------------------------------------------------------

    protected override void OnPointerDown(PointerEvent e)
    {
        if (e.Button != PointerButton.Left)
        {
            return;
        }
        e.Handled = true;
        int at = IndexAt(e.Position);
        switch ((e.ClickCount - 1) % 3)
        {
            case 0:
                Select((e.Modifiers & KeyModifiers.Shift) != 0 ? _anchor : at, at);
                _selecting = true;
                break;
            case 1:
                Select(WordEdge(at, -1, true), WordEdge(at, 1, true));
                break;
            default:
                if (_multiline)
                {
                    int start = _text.LastIndexOf('\n', Math.Max(at - 1, 0)) + 1;
                    int end = _text.IndexOf('\n', at);
                    Select(at > 0 && _text[at - 1] == '\n' ? at : start, end < 0 ? _text.Length : end);
                }
                else
                {
                    SelectAll();
                }
                break;
        }
    }

    protected override void OnPointerMove(PointerEvent e)
    {
        if (_selecting)
        {
            e.Handled = true;
            MoveCaret(IndexAt(e.Position), true);
        }
    }

    protected override void OnPointerUp(PointerEvent e)
    {
        if (e.Button == PointerButton.Left)
        {
            _selecting = false;
        }
    }

    // The caret index nearest a point in the root's units.
    private int IndexAt(Vector2 position)
    {
        if (_shaped.Layout is not { } layout)
        {
            return 0;
        }
        Thickness padding = Padding;
        Vector2 local = position - new Vector2(Bounds.X + padding.Left - _scrollX, Bounds.Y + padding.Top);
        return layout.HitTest(local);
    }

    // -----------------------------------------------------------------------
    // Editing
    // -----------------------------------------------------------------------

    // Replaces the selection, as one undo step unless it continues a run of
    // typing (or of deleting) right where the last left off.
    private void Insert(string inserted, EditKind kind)
    {
        inserted = _multiline ? inserted.Replace("\r\n", "\n").Replace('\r', '\n') : Clean(inserted);
        int start = SelectionStart, length = SelectionLength;
        if (inserted.Length == 0 && length == 0)
        {
            return;
        }
        string text = string.Concat(_text.AsSpan(0, start), inserted, _text.AsSpan(start + length));
        int caret = start + inserted.Length;
        Edit(text, caret, caret, kind);
    }

    private void Edit(string text, int caret, int anchor, EditKind kind)
    {
        bool continues = kind is EditKind.Typing or EditKind.Deleting && kind == _lastEdit && _undo.Count > 0;
        if (!continues)
        {
            Push(_undo, new Snapshot(_text, _caret, _anchor));
        }
        _redo.Clear();
        _lastEdit = kind == EditKind.Other ? EditKind.None : kind;
        Replace(text, caret, anchor);
    }

    private bool Restore(List<Snapshot> from, List<Snapshot> to)
    {
        if (from.Count == 0)
        {
            return false;
        }
        Snapshot snapshot = from[^1];
        from.RemoveAt(from.Count - 1);
        Push(to, new Snapshot(_text, _caret, _anchor));
        _lastEdit = EditKind.None;
        Replace(snapshot.Text, snapshot.Caret, snapshot.Anchor);
        return true;
    }

    private static void Push(List<Snapshot> stack, Snapshot snapshot)
    {
        if (stack.Count == UndoLimit)
        {
            stack.RemoveAt(0);
        }
        stack.Add(snapshot);
    }

    private void Replace(string text, int caret, int anchor)
    {
        bool changed = text != _text;
        _text = text;
        _caret = Math.Clamp(caret, 0, text.Length);
        _anchor = Math.Clamp(anchor, 0, text.Length);
        _goalX = float.NaN;
        if (changed)
        {
            InvalidateMeasure();
        }
        CaretMoved();
        if (changed)
        {
            OnTextChanged();
            TextChanged?.Invoke(this);
        }
    }

    /// <summary>After every change to the text, before <see cref="TextChanged"/>.</summary>
    protected virtual void OnTextChanged()
    {
    }

    // Sets the text before any root, history, or event.
    private void SetTextCore(string text)
    {
        _text = Clean(text ?? "");
        _revert = _text;
        _caret = _anchor = _text.Length;
    }

    private void MoveCaret(int to, bool extend)
    {
        Select(extend ? _anchor : to, to);
    }

    // Restarts the blink (shown) and scrolls the caret into view at the next layout.
    private void CaretMoved()
    {
        _blinkStart = Root?.Now ?? 0;
        _caretShown = true;
        _revealCaret = true;
        InvalidateArrange();
        InvalidateVisual();
    }

    private void MoveVertically(int direction, bool extend)
    {
        if (_shaped.Layout is not { } layout)
        {
            return;
        }
        TextCaret caret = layout.Caret(_caret);
        float goal = float.IsNaN(_goalX) ? caret.X : _goalX;
        int line = (int)caret.Line + direction;
        int to;
        if (line < 0)
        {
            to = 0;
        }
        else if (line >= layout.LineCount)
        {
            to = _text.Length;
        }
        else
        {
            TextLine target = layout.Line(line);
            to = layout.HitTest(new Vector2(goal, target.Top + target.Height * 0.5f));
        }
        Select(extend ? _anchor : to, to);
        _goalX = goal;
    }

    // The start (-1) or end (1) of the caret's line as laid out; the end of
    // a wrapped line is before its last character, so the caret stays on it.
    private int LineEdge(int direction)
    {
        if (_shaped.Layout is not { } layout)
        {
            return direction < 0 ? 0 : _text.Length;
        }
        TextLine line = layout.Line((int)layout.Caret(_caret).Line);
        if (direction < 0)
        {
            return (int)line.Start;
        }
        int end = (int)line.End;
        bool wrapped = end < _text.Length && _text[end] != '\n';
        return wrapped ? Step(end, -1) : end;
    }

    // The next caret position: whole characters (surrogate pairs and
    // combining marks stay together).
    private int Step(int index, int direction)
    {
        if (direction > 0)
        {
            return index >= _text.Length ? _text.Length : index + StringInfo.GetNextTextElementLength(_text, index);
        }
        int previous = 0;
        for (int at = 0; at < index;)
        {
            previous = at;
            at += StringInfo.GetNextTextElementLength(_text, at);
        }
        return index <= 0 ? 0 : previous;
    }

    // Ctrl+arrows: past spaces, then past a word (or a run of punctuation).
    // Within a word (double-click), only the word itself.
    private int WordEdge(int index, int direction, bool within = false)
    {
        int at = index;
        if (direction > 0)
        {
            if (!within)
            {
                while (at < _text.Length && char.IsWhiteSpace(_text[at]))
                {
                    ++at;
                }
            }
            if (at < _text.Length)
            {
                int kind = Kind(_text[at]);
                while (at < _text.Length && Kind(_text[at]) == kind && (within || kind != 0))
                {
                    ++at;
                }
            }
            return at;
        }
        if (!within)
        {
            while (at > 0 && char.IsWhiteSpace(_text[at - 1]))
            {
                --at;
            }
        }
        if (at > 0)
        {
            int kind = within && at < _text.Length ? Kind(_text[at]) : Kind(_text[at - 1]);
            while (at > 0 && Kind(_text[at - 1]) == kind && (within || kind != 0))
            {
                --at;
            }
        }
        return at;
    }

    // 0 space, 1 a word's characters, 2 punctuation.
    private static int Kind(char c) => char.IsWhiteSpace(c) ? 0 : char.IsLetterOrDigit(c) || c == '_' ? 1 : 2;

    // One line: line breaks become spaces.
    private string Clean(string text) =>
        _multiline ? text.Replace("\r\n", "\n").Replace('\r', '\n') : text.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ');
}
