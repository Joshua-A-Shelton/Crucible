using Crucible.Editor;

namespace Characters.Editor;

/// <summary>
/// The character editor (ADR 0128), the editor extensions demonstration:
/// a workspace for a Character's entity (its model in a preview, its clips
/// played and scrubbed), opened from the component, and a drawer for its
/// stats in the Inspector. Only the public editor API.
/// </summary>
public sealed class CharactersExtension : EditorExtension
{
    public const string WorkspaceId = "Characters.Character";

    public override void Start(EditorContext editor)
    {
        editor.Workspaces.Add(WorkspaceId, "Character", () => new CharacterWorkspace(editor));
        editor.Workspaces.OpensComponent<Character>(WorkspaceId);
        editor.Inspector.AddComponentDrawer(() => new CharacterDrawer());
    }
}
