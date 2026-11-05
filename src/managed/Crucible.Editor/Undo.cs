using Crucible.Ecs;

namespace Crucible.Editor;

/// <summary>
/// One undoable change (ADR 0068), recorded after it's made. Steps name
/// what they change by scene ID, never by entity handle: undoing a delete
/// brings entities back as new handles with their old IDs, so steps after
/// it still find them.
/// </summary>
public abstract class UndoStep
{
    protected UndoStep(string title) => Title = title;

    /// <summary>What the Edit menu says: "Undo Move".</summary>
    public string Title { get; }

    public abstract void Undo(World world);

    public abstract void Redo(World world);

    /// <summary>
    /// Inside a group, folds a later step into this one (a drag's many moves
    /// of one entity becoming one); false keeps both.
    /// </summary>
    internal virtual bool TryMerge(UndoStep later) => false;
}

/// <summary>
/// The editor's undo and redo (ADR 0068): steps in the order made, and
/// those undone, which a new step clears. <see cref="BeginGroup"/> and
/// <see cref="EndGroup"/> make the steps between them one (a handle drag);
/// groups nest, and only the outermost counts. It also knows whether the
/// scene differs from when it was last saved.
/// </summary>
public sealed class UndoHistory
{
    /// <summary>Steps kept; the oldest go first.</summary>
    public const int Limit = 500;

    private readonly List<UndoStep> _done = [];
    private readonly List<UndoStep> _undone = [];
    private Group? _open;
    private int _depth;
    private UndoStep? _saved; // the newest step done when saved; null for none
    private bool _savedLost;  // saved with nothing done, and the oldest steps since dropped

    public bool CanUndo => _done.Count > 0 && _open is null;

    public bool CanRedo => _undone.Count > 0 && _open is null;

    /// <summary>The step undo would undo, or null.</summary>
    public UndoStep? NextUndo => _done.Count > 0 ? _done[^1] : null;

    public UndoStep? NextRedo => _undone.Count > 0 ? _undone[^1] : null;

    public int UndoCount => _done.Count;

    public int RedoCount => _undone.Count;

    /// <summary>Whether what's done differs from when <see cref="MarkSaved"/> was last called.</summary>
    public bool IsDirty =>
        _savedLost || (_open is not null && _open.Steps.Count > 0) || (_done.Count > 0 ? _done[^1] : null) != _saved;

    /// <summary>A step was recorded, undone, or redone, or the history was cleared or saved.</summary>
    public event Action<UndoHistory>? Changed;

    /// <summary>Records a change already made.</summary>
    public void Push(UndoStep step)
    {
        ArgumentNullException.ThrowIfNull(step);
        if (_open is not null)
        {
            if (_open.Steps.Count == 0 || !_open.Steps[^1].TryMerge(step))
            {
                _open.Steps.Add(step);
            }
            Changed?.Invoke(this);
            return;
        }
        Record(step);
    }

    /// <summary>Makes a change (its <see cref="UndoStep.Redo"/>) and records it.</summary>
    public void Do(UndoStep step, World world)
    {
        ArgumentNullException.ThrowIfNull(step);
        step.Redo(world);
        Push(step);
    }

    /// <summary>Starts a group of steps that undo as one, titled <paramref name="title"/>.</summary>
    public void BeginGroup(string title)
    {
        if (_depth++ == 0)
        {
            _open = new Group(title);
        }
    }

    /// <summary>Ends a group: its steps are recorded as one (none, if it had none).</summary>
    public void EndGroup()
    {
        if (_depth == 0)
        {
            throw new InvalidOperationException("EndGroup without BeginGroup");
        }
        if (--_depth > 0)
        {
            return;
        }
        Group group = _open!;
        _open = null;
        if (group.Steps.Count > 0)
        {
            Record(group);
        }
        else
        {
            Changed?.Invoke(this);
        }
    }

    public bool Undo(World world)
    {
        if (!CanUndo)
        {
            return false;
        }
        UndoStep step = _done[^1];
        _done.RemoveAt(_done.Count - 1);
        step.Undo(world);
        _undone.Add(step);
        Changed?.Invoke(this);
        return true;
    }

    public bool Redo(World world)
    {
        if (!CanRedo)
        {
            return false;
        }
        UndoStep step = _undone[^1];
        _undone.RemoveAt(_undone.Count - 1);
        step.Redo(world);
        _done.Add(step);
        Changed?.Invoke(this);
        return true;
    }

    /// <summary>What's done now is what's saved.</summary>
    public void MarkSaved()
    {
        _saved = _done.Count > 0 ? _done[^1] : null;
        _savedLost = false;
        Changed?.Invoke(this);
    }

    /// <summary>Forgets every step (a scene opened or made new); what's there counts as saved.</summary>
    public void Clear()
    {
        _done.Clear();
        _undone.Clear();
        _open = null;
        _depth = 0;
        _saved = null;
        _savedLost = false;
        Changed?.Invoke(this);
    }

    private void Record(UndoStep step)
    {
        // A saved step cleared from the redo steps never comes back to the
        // top, so the scene stays dirty without anything more.
        _undone.Clear();
        _done.Add(step);
        if (_done.Count > Limit)
        {
            // Saved with nothing done: undoing all that's kept no longer gets back there.
            _savedLost |= _saved is null;
            _done.RemoveAt(0);
        }
        Changed?.Invoke(this);
    }

    // Steps undone and redone as one: undone newest first.
    private sealed class Group(string title) : UndoStep(title)
    {
        public readonly List<UndoStep> Steps = [];

        public override void Undo(World world)
        {
            for (int i = Steps.Count - 1; i >= 0; --i)
            {
                Steps[i].Undo(world);
            }
        }

        public override void Redo(World world)
        {
            foreach (UndoStep step in Steps)
            {
                step.Redo(world);
            }
        }
    }
}
