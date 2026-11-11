using System.Text.Json.Nodes;
using Crucible.Assets;
using Crucible.Ecs;
using Crucible.Loop;
using Crucible.Physics;
using Crucible.UI;
using H = Crucible.Editor.HierarchyChecks;

namespace Crucible.Editor;

/// <summary>
/// Import settings and engine messages checking themselves (ADR 0087), for
/// CTest's launcher.jit.import-settings: run with the Walker's project and
/// <c>--game-arg check-import-settings=&lt;folder&gt;</c>. On a copy of the
/// project (in the folder): a Convex collider on the Walker, whose model has
/// no collision data, shows the notice, and in play the core's warning
/// reaches the Log; the notice's button cooks it. Then the model's settings
/// in the inspector: an edit held, reverted, made again, and applied.
/// </summary>
[System(Phases.RenderPrepare)]
internal sealed class ImportSettingsChecks : GameSystem
{
    private IEnumerator<int>? _script;
    private int _wait;
    private int _checks;
    private int _failures;
    private string _folder = "";

    public override void Start(FrameContext context)
    {
        if (!context.GameArguments.TryGetValue("check-import-settings", out string? folder))
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
            ? $"ImportSettingsChecks: all {_checks} checks passed"
            : $"ImportSettingsChecks: {_failures} of {_checks} checks failed");
        context.QuitRequested = true;
    }

    private IEnumerable<int> Script()
    {
        Shell shell = Shell.Current ?? throw new InvalidOperationException("No editor shell");
        string source = Path.GetDirectoryName(shell.Project?.ProjectFilePath ?? "") ?? "";
        string copy = Path.Combine(Path.GetFullPath(_folder), "import-settings-project");
        if (Directory.Exists(copy))
        {
            Directory.Delete(copy, recursive: true);
        }
        AssetChecks.CopyProject(source, copy);
        shell.OpenProject(Path.Combine(copy, Path.GetFileName(shell.Project!.ProjectFilePath)));
        shell.OpenScene(Path.Combine(copy, "Assets", "Scenes", "Walk.crscene"));
        yield return 3;

        AssetDatabase database = shell.Database!;
        const string modelPath = "Models/Walker.gltf";
        AssetId model = database.Find(modelPath)!.Id;
        string sidecar = Path.Combine(copy, "Assets", "Models", "Walker.gltf" + ".crmeta");
        InspectorPanel inspector = shell.Main.DockSpace.Panels.OfType<InspectorPanel>().Single();

        // A static body with a Convex collider on the model, which has none cooked.
        Entity entity = shell.CreateEmpty(Entity.Null);
        string rigidBody = typeof(RigidBody).FullName!, collider = typeof(Collider).FullName!;
        shell.SetComponent(entity, rigidBody, new JsonObject { ["Kind"] = "Static" }, "Add Component");
        shell.SetComponent(entity, collider,
            new JsonObject { ["Shape"] = "Convex", ["Mesh"] = model.ToString(), ["MeshIndex"] = 0 }, "Add Component");
        shell.Selection.Selected = entity;
        yield return 2;
        ColliderNotice? notice = H.Descendants(inspector).OfType<ColliderNotice>().FirstOrDefault();
        Check(notice is { Visibility: Visibility.Visible }, "a Convex collider on a model without hulls shows the notice");
        Check(notice?.Text.Text == "Walker.gltf isn't imported with Convex collision",
            "the notice names the model and the kind", notice?.Text.Text ?? "");
        Check(notice?.ReimportButton.Text == "Reimport Walker.gltf with Convex collision", "the button offers to reimport it",
            notice?.ReimportButton.Text ?? "");

        // In play, the core's warning (stderr before) reaches the Log.
        shell.Play();
        string warning = "Warning: a Convex collider's model isn't imported with Convex collision";
        long started = Environment.TickCount64;
        while (!Log(shell).Contains(warning, StringComparison.Ordinal) && Environment.TickCount64 - started < 20000)
        {
            yield return 1;
        }
        Check(Log(shell).Contains(warning, StringComparison.Ordinal), "the core's warning is in the Log", Tail(Log(shell)));
        Check(H.Descendants(shell.LogPanel!).OfType<Label>().Any(label =>
                label.Text.StartsWith(warning, StringComparison.Ordinal) && label.Classes.Contains("log-warning")),
            "the warning is marked as one");
        shell.Stop();
        yield return 2;

        // The button cooks hulls into the model: the notice goes. (Stopping
        // restored the scene: the entity is found again.)
        World world = shell.World;
        entity = SceneIds.Of(world).Entities.FirstOrDefault(found => world.Has(found, Components.Of<Collider>()));
        Check(!entity.IsNull, "the collider's entity is back after play");
        shell.Selection.Selected = entity;
        yield return 2;
        notice = H.Descendants(inspector).OfType<ColliderNotice>().FirstOrDefault();
        if (notice is not null)
        {
            notice.ReimportButton.Focus(); // scrolled into view
            yield return 1;
            Press(notice.ReimportButton);
        }
        yield return 3;
        Check(File.ReadAllText(sidecar).Contains("\"Collision\": \"Convex\"", StringComparison.Ordinal),
            "cooking set the sidecar's Collision", File.ReadAllText(sidecar));
        Check(Log(shell).Contains($"Imported {modelPath}", StringComparison.Ordinal), "the model was imported again",
            Tail(Log(shell)));
        notice = H.Descendants(inspector).OfType<ColliderNotice>().FirstOrDefault();
        Check(notice is { Visibility: Visibility.Collapsed }, "the notice is gone once it's cooked");

        // The model's settings in the inspector, replacing the entity.
        shell.InspectedAsset = model;
        yield return 2;
        Check(shell.Selection.Selected.IsNull, "inspecting an asset clears the entity selection");
        ImportSettingsSection? section = inspector.ImportSettings;
        Check(section is not null, "the inspector shows the model's import settings");
        if (section is null)
        {
            yield break;
        }
        FieldEditor? hull = section.Editors.FirstOrDefault(editor => editor.Field.Name == "MaxHullVertices");
        Check(hull is not null, "its fields are the importer's settings",
            string.Join(", ", section.Editors.Select(editor => editor.Field.Name)));
        Check(section.Editors.Any(editor => editor.Field.Name == "Collision" && editor.Value?.GetValue<string>() == "Convex"),
            "it shows the applied Collision");
        Check(!section.HasChanges && !section.ApplyButton.IsEnabled, "nothing to apply before an edit");

        // An edit is held: marked, Apply enabled, the sidecar untouched; Revert drops it.
        IFieldOwner owner = section;
        string before = File.ReadAllText(sidecar);
        owner.Edit(["MaxHullVertices"], JsonValue.Create(16), "Edit");
        yield return 1;
        Check(section.HasChanges && section.ApplyButton.IsEnabled, "an edit enables Apply");
        Check(hull is { IsOverridden: true }, "the edited field is marked");
        Check(File.ReadAllText(sidecar) == before, "an edit isn't saved until Apply");
        section.RevertButton.Focus();
        yield return 1;
        Press(section.RevertButton);
        yield return 1;
        Check(!section.HasChanges && hull?.Value?.GetValue<int>() == 32, "Revert drops the edit",
            hull?.Value?.ToJsonString() ?? "");

        // Applied: saved to the sidecar and imported again.
        owner.Edit(["MaxHullVertices"], JsonValue.Create(16), "Edit");
        section.ApplyButton.Focus();
        yield return 1;
        Press(section.ApplyButton);
        yield return 3;
        Check(File.ReadAllText(sidecar).Contains("\"MaxHullVertices\": 16", StringComparison.Ordinal),
            "Apply saves the sidecar", File.ReadAllText(sidecar));
        Check(database.SettingsOf(model)?["MaxHullVertices"]?.GetValue<int>() == 16, "the database has the new settings");
        Check(Log(shell).Contains($"Applied {modelPath}'s import settings", StringComparison.Ordinal),
            "Apply is in the Log", Tail(Log(shell)));
        section = inspector.ImportSettings;
        Check(section is { HasChanges: false }, "after Apply there's nothing left to apply");

        // Selecting an entity shows it again.
        shell.Selection.Selected = entity;
        yield return 2;
        Check(shell.InspectedAsset.IsNone && inspector.ImportSettings is null, "selecting an entity replaces the asset");

        // The asset browser's right-click: Import Settings… and, on a model,
        // Collision, which applies and reimports at once.
        AssetsPanel assets = shell.Main.DockSpace.Panels.OfType<AssetsPanel>().Single();
        assets.Group?.Activate(assets);
        assets.ShowFolder("Models");
        yield return 2;
        AssetTile? tile = assets.Tiles.FirstOrDefault(found => found.Path == modelPath);
        Check(tile is not null, "the model is in the asset browser");
        if (tile is null)
        {
            yield break;
        }
        ContextMenu menu = assets.ShowMenu(tile, ViewportChecks.Center(tile));
        yield return 2;
        MenuItem[] items = [.. menu.Items.OfType<MenuItem>()];
        Check(items.Any(item => item.Title == "Import Settings…"), "the menu has Import Settings…",
            string.Join(", ", items.Select(item => item.Title)));
        MenuItem? collision = items.FirstOrDefault(item => item.Title == "Collision");
        MenuItem[] kinds = collision?.Submenu is { } submenu ? [.. submenu.Items.OfType<MenuItem>()] : [];
        Check(string.Join(",", kinds.Select(item => item.Title)) == "None,Mesh,Convex", "a model's Collision offers each kind",
            string.Join(",", kinds.Select(item => item.Title)));
        Check(kinds.FirstOrDefault(item => item.IsChecked)?.Title == "Convex", "with the applied one checked");
        if (collision is null || kinds.Length < 2)
        {
            yield break;
        }
        ViewportChecks.Click(menu.Root!, ViewportChecks.Center(collision));
        yield return 2;
        ViewportChecks.Click(kinds[1].Root!, ViewportChecks.Center(kinds[1]));
        yield return 3;
        Check(File.ReadAllText(sidecar).Contains("\"Collision\": \"Mesh\"", StringComparison.Ordinal),
            "choosing Mesh sets the model's Collision", File.ReadAllText(sidecar));

        // Now imported with Mesh, the Convex collider's notice says it'd replace it...
        shell.Selection.Selected = entity;
        yield return 2;
        notice = H.Descendants(inspector).OfType<ColliderNotice>().FirstOrDefault();
        Check(notice is { Visibility: Visibility.Visible } &&
              notice.ReimportButton.Text == "Reimport Walker.gltf with Convex collision (replaces Mesh)",
            "the notice says reimporting replaces the other kind", notice?.ReimportButton.Text ?? "");

        // ...and hides while the collider builds its own at load.
        JsonObject generating = new()
        {
            ["Shape"] = "Convex",
            ["Mesh"] = model.ToString(),
            ["MeshIndex"] = 0,
            ["GenerateIfMissing"] = true,
        };
        shell.SetComponent(entity, collider, generating, "Edit");
        yield return 2;
        notice = H.Descendants(inspector).OfType<ColliderNotice>().FirstOrDefault();
        Check(notice is { Visibility: Visibility.Collapsed }, "Generate If Missing hides the notice");
        Check(H.Descendants(inspector).OfType<FieldEditor>().Any(editor => editor.Field.Name == "GenerateIfMissing"),
            "the collider shows Generate If Missing");
    }

    // A real click at the button's center: it must be on screen (focused
    // first, which scrolls it into view) and enabled.
    private static void Press(Button button) => ViewportChecks.Click(button.Root!, ViewportChecks.Center(button));

    private static string Log(Shell shell) =>
        shell.LogPanel is { } log
            ? string.Join("\n", H.Descendants(log).OfType<Label>().Select(label => label.Text))
            : "";

    private static string Tail(string log) => string.Join(" | ", log.Split('\n').TakeLast(8));

    private void Check(bool passed, string what, string detail = "")
    {
        ++_checks;
        if (!passed)
        {
            ++_failures;
            Console.WriteLine($"ImportSettingsChecks: FAILED: {what} {detail}");
        }
    }
}
