using Crucible.Ecs;

namespace Crucible.Editor;

/// <summary>
/// The editor about to duplicate, rename, or delete entities (ADR 0122),
/// handed to extensions before it does: any may <see cref="Cancel"/> it,
/// <see cref="Hold"/> it while asking a question, or add work that runs in
/// its undo step after it (<see cref="Then"/>).
/// </summary>
public sealed class SceneChange
{
    private readonly List<Action> _then = [];
    private int _holds;
    private Action? _ready;

    internal SceneChange(IReadOnlyList<Entity> entities, string? newName = null)
    {
        Entities = entities;
        NewName = newName;
    }

    /// <summary>What it changes: the entities duplicated, renamed, or deleted (with what's under them).</summary>
    public IReadOnlyList<Entity> Entities { get; }

    /// <summary>The name a rename gives; null for the others.</summary>
    public string? NewName { get; }

    /// <summary>Whether it's been cancelled.</summary>
    public bool IsCancelled { get; private set; }

    /// <summary>Doesn't happen.</summary>
    public void Cancel()
    {
        IsCancelled = true;
    }

    /// <summary>Waits until the hold says Go (or Cancel), for a question's answer.</summary>
    public SceneChangeHold Hold()
    {
        ++_holds;
        return new SceneChangeHold(this);
    }

    /// <summary>Runs after it, in its undo step: one undo takes both back.</summary>
    public void Then(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        _then.Add(action);
    }

    internal IReadOnlyList<Action> ThenActions => _then;

    // Runs once nothing holds it, unless cancelled.
    internal void WhenReady(Action ready)
    {
        _ready = ready;
        Release(0);
    }

    internal void Release(int held)
    {
        _holds -= held;
        if (_holds == 0 && _ready is { } ready)
        {
            _ready = null;
            if (!IsCancelled)
            {
                ready();
            }
        }
    }
}

/// <summary>A <see cref="SceneChange"/> held for a question: Go when it's answered, or Cancel.</summary>
public sealed class SceneChangeHold
{
    private readonly SceneChange _change;
    private bool _released;

    internal SceneChangeHold(SceneChange change) => _change = change;

    public void Go() => Release(cancel: false);

    public void Cancel() => Release(cancel: true);

    private void Release(bool cancel)
    {
        if (_released)
        {
            return;
        }
        _released = true;
        if (cancel)
        {
            _change.Cancel();
        }
        _change.Release(1);
    }
}
