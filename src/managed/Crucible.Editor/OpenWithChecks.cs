using Crucible.Assets;
using Crucible.Ecs;
using Crucible.Loop;
using Crucible.Scenes;
using Crucible.UI;

namespace Crucible.Editor;

/// <summary>
/// Opening editors checking itself (ADR 0120), for CTest's
/// launcher.jit.open-with: run with the Walker's project and
/// <c>--game-arg check-open-with=&lt;folder&gt;</c>. On a copy of the
/// project, an extension's workspace opening textures (as the Sprite Sheet
/// Editor does) and entities with a LocalTransform: a texture's
/// double-click asks which, Always Open With remembers it, Open With shows
/// it and Ask Each Time forgets it; the Inspector's Open button and the
/// menus open an entity in it.
/// </summary>
[System(Phases.RenderPrepare)]
internal sealed class OpenWithChecks : GameSystem
{
    private IEnumerator<int>? _script;
    private int _wait;
    private int _checks;
    private int _failures;
    private string _folder = "";
    private readonly List<string> _logged = [];

    public override void Start(FrameContext context)
    {
        if (!context.GameArguments.TryGetValue("check-open-with", out string? folder))
        {
            Enabled = false;
            return;
        }
        _folder = folder;
        _script = Script().GetEnumerator();
        _wait = 3;
    }

    public override void Run(FrameContext context)
    {
        if (_script is null || _wait-- > 0)
        {
            return;
        }
        try
        {
            if (_script.MoveNext())
            {
                _wait = _script.Current;
                return;
            }
        }
        catch (Exception exception)
        {
            Check(false, "the script ran", exception.ToString());
        }
        _script = null;
        Console.WriteLine(_failures == 0
            ? $"OpenWithChecks: all {_checks} checks passed"
            : $"OpenWithChecks: {_failures} of {_checks} checks failed");
        context.QuitRequested = true;
    }

    private IEnumerable<int> Script()
    {
        Shell shell = Shell.Current ?? throw new InvalidOperationException("No editor shell");
        shell.Logged += _logged.Add;
        string copy = Path.Combine(Path.GetFullPath(_folder), "open-with-project");
        AssetChecks.OpenCopy(shell, copy, Path.Combine("Assets", "Scenes", "Walk.crscene"), withoutEditorCode: true);
        shell.RestoreLayout(null);
        shell.Settings.OpenWith.Clear();
        yield return 3;
        Project project = shell.Project!;
        string tools = Path.Combine(project.CodeFolder, "Editor", "Notes.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(tools)!);
        File.WriteAllText(tools, Notes);
        Check(shell.BuildCode(), "an extension whose workspace opens textures and transforms builds", Logged());

        // A texture: the Sprite Sheet Editor's, and now the extension's.
        AssetEntry? texture = shell.Database?.Assets.FirstOrDefault(asset => asset.Importer == "Texture");
        Check(texture is not null, "the project has a texture");
        if (texture is null)
        {
            yield break;
        }
        IReadOnlyList<(string Id, string Title)> editors = shell.EditorsFor(texture);
        Check(editors.Select(editor => editor.Id).SequenceEqual(["SpriteSheet", "Notes"]),
            "two editors open textures, the editor's own first", string.Join(", ", editors.Select(editor => editor.Id)));

        // Double-clicked: which? Notes, always.
        Check(shell.OpenAsset(texture) && shell.DropQuestion is { } question &&
              question.Message == $"Open {Path.GetFileName(texture.Path)} with:" &&
              question.Choices.SequenceEqual(["Sprite Sheet Editor", "Notes"]) && question.ForTheRest.Text == "Always Open With",
            "with several, opening asks which, offering Always Open With", shell.DropQuestion?.Message ?? "no question");
        shell.DropQuestion?.Answer(1, forTheRest: true);
        yield return 2;
        Check(shell.Workspaces.Any(workspace => workspace.WorkspaceId == "Notes" && workspace.Asset == texture.Id) &&
              shell.RememberedEditor("Texture") == "Notes", "it opens in the one chosen, remembered for textures");
        int tabs = shell.Workspaces.Count();
        Check(shell.OpenAsset(texture) && shell.DropQuestion is null && shell.Workspaces.Count() == tabs,
            "opened again, no question: the remembered one");

        // Open With shows it, and Ask Each Time forgets it.
        ContextMenu menu = new();
        shell.AddOpenItems(menu, texture);
        Menu? openWith = menu.Items.OfType<MenuItem>().FirstOrDefault(item => item.Title == "Open With")?.Submenu;
        MenuItem? notes = openWith?.Items.OfType<MenuItem>().FirstOrDefault(item => item.Title == "Notes");
        MenuItem? ask = openWith?.Items.OfType<MenuItem>().FirstOrDefault(item => item.Title == "Ask Each Time");
        Check(notes is { IsChecked: true } && ask is { IsChecked: false }, "Open With lists them, the remembered one checked");
        shell.ForgetEditor("Texture"); // what Ask Each Time does
        Check(shell.RememberedEditor("Texture") is null, "Ask Each Time forgets it");
        Check(shell.OpenAsset(texture) && shell.DropQuestion is not null, "so it asks again");
        shell.DropQuestion?.Answer(-1);
        yield return 1;

        // An entity, by its component: the Inspector's button, the menus.
        Entity entity = shell.Selection.Selected = FirstTop(shell);
        yield return 2;
        string transform = Components.NameOf(typeof(LocalTransform));
        Check(shell.EditorsFor(entity, transform).Select(editor => editor.Id).SequenceEqual(["Notes"]),
            "its transform's editor is the extension's");
        ComponentSection? section = (shell.Windows.SelectMany(window => window.DockSpace.Panels).OfType<InspectorPanel>().FirstOrDefault())
            ?.Sections.FirstOrDefault(found => found.Component == transform);
        Check(section is not null && OpenButton(section) is { } button && button.Text == "Open in Notes",
            "the Inspector's header names it on its Open button");
        Check(shell.OpenEntity(entity, transform) &&
              shell.Workspaces.Any(workspace => workspace.WorkspaceId == "Notes" && workspace.Entity.Value == entity.Value),
            "and opens the entity in it");
        Check(shell.EditorsFor(entity).Any(editor => editor.Id == "Notes"), "the Hierarchy's menu has Open in Notes for it");
        shell.Logged -= _logged.Add;
    }

    private static Entity FirstTop(Shell shell)
    {
        World world = shell.World;
        foreach (Entity entity in SceneIds.Of(world).Entities)
        {
            if (world.IsAlive(entity) && world.Has(entity, Components.Of<LocalTransform>()))
            {
                return entity;
            }
        }
        return Entity.Null;
    }

    private static Button? OpenButton(UIElement element)
    {
        foreach (UIElement child in element.Children)
        {
            if (child is Button { Text: { } text } button && text.StartsWith("Open", StringComparison.Ordinal))
            {
                return button;
            }
            if (OpenButton(child) is { } found)
            {
                return found;
            }
        }
        return null;
    }

    private const string Notes = """
        using Crucible.Editor;
        using Crucible.Scenes;
        using Crucible.UI;

        namespace Walker.Tools;

        public sealed class Notes : Workspace
        {
            public Notes() : base("Notes") => Arrange(null, new Label("notes"));

            public override bool AllowsSeveral => true;
        }

        public sealed class NotesTools : EditorExtension
        {
            public override void Start(EditorContext editor)
            {
                editor.Workspaces.Add("Notes", "Notes", () => new Notes());
                editor.Workspaces.OpensAssets("Notes", "Texture");
                editor.Workspaces.OpensComponent<LocalTransform>("Notes");
            }
        }
        """;

    private string Logged() => string.Join(" | ", _logged);

    private void Check(bool passed, string what, string detail = "")
    {
        ++_checks;
        if (!passed)
        {
            ++_failures;
            Console.WriteLine($"OpenWithChecks: FAILED: {what}{(detail.Length > 0 ? $" ({detail})" : "")}");
        }
    }
}
