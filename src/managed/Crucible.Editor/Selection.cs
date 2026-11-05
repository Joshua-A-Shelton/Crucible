using Crucible.Ecs;

namespace Crucible.Editor;

/// <summary>
/// What's selected in the editor (ADR 0067): one entity or none, shared by
/// the viewport (picking, outlining, handles) and, from step 4, the
/// hierarchy and inspector.
/// </summary>
public sealed class Selection
{
    private Entity _selected;

    /// <summary>The selected entity; <see cref="Entity.Null"/> for none.</summary>
    public Entity Selected
    {
        get => _selected;
        set
        {
            if (_selected.Value != value.Value)
            {
                _selected = value;
                Changed?.Invoke(this);
            }
        }
    }

    public event Action<Selection>? Changed;
}
