using System.Text.Json.Nodes;
using Crucible.Animation;
using Crucible.Audio;
using Crucible.Ecs;
using Crucible.Loop;
using Crucible.Physics;
using Crucible.Scenes;
using Crucible.Serialization;
using Crucible.UI;
using H = Crucible.Editor.HierarchyChecks;
using V = Crucible.Editor.ViewportChecks;

namespace Crucible.Editor;

/// <summary>
/// Pickers offering only valid choices checking themselves (ADR 0089), for
/// CTest's launcher.jit.pickers: run with the Walker's project and
/// <c>--game-arg check-pickers=&lt;folder&gt;</c>. On a copy of the project (in
/// the folder), with the Diorama's house added as a model without a
/// skeleton: each drop-down is opened by a click and its choices read.
/// </summary>
[System(Phases.RenderPrepare)]
internal sealed class PickerChecks : GameSystem
{
    private IEnumerator<int>? _script;
    private int _wait;
    private int _checks;
    private int _failures;
    private string _folder = "";

    public override void Start(FrameContext context)
    {
        if (!context.GameArguments.TryGetValue("check-pickers", out string? folder))
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
            ? $"PickerChecks: all {_checks} checks passed"
            : $"PickerChecks: {_failures} of {_checks} checks failed");
        context.QuitRequested = true;
    }

    private IEnumerable<int> Script()
    {
        Shell shell = Shell.Current ?? throw new InvalidOperationException("No editor shell");
        string source = Path.GetDirectoryName(shell.Project?.ProjectFilePath ?? "") ?? "";
        string copy = Path.Combine(Path.GetFullPath(_folder), "pickers-project");
        if (Directory.Exists(copy))
        {
            Directory.Delete(copy, recursive: true);
        }
        AssetChecks.CopyProject(source, copy);
        // A model without a skeleton, beside the Walker (new sidecars, so new IDs).
        string diorama = Path.GetFullPath(Path.Combine(source, "..", "..", "Diorama", "Project", "Assets", "Models"));
        foreach (string file in (string[])["House.gltf", "House.bin"])
        {
            File.Copy(Path.Combine(diorama, file), Path.Combine(copy, "Assets", "Models", file));
        }
        shell.OpenProject(Path.Combine(copy, Path.GetFileName(shell.Project!.ProjectFilePath)));
        shell.OpenScene(Path.Combine(copy, "Assets", "Scenes", "Walk.crscene"));
        yield return 3;
        Assets.AssetDatabase database = shell.Database!;
        Assets.AssetId walker = database.Find("Models/Walker.gltf")!.Id;
        Check(database.Find("Models/House.gltf") is not null && database.KindOf(walker) == ".crmesh",
            "the house is a model, as the Walker is");
        Check(database.HasSkeleton(walker) && !database.HasSkeleton(database.Find("Models/House.gltf")!.Id),
            "the Walker has a skeleton and the house hasn't");
        InspectorPanel inspector = shell.Main.DockSpace.Panels.OfType<InspectorPanel>().Single();

        // A collider: its Mesh lists models only, its Mesh Index waits for one.
        Entity prop = Make(shell, "Prop");
        shell.SetComponent(prop, typeof(Collider).FullName!, new JsonObject { ["Shape"] = "Convex" }, "Add Component");
        shell.Selection.Selected = prop;
        yield return 2;
        string[] meshes = [];
        foreach (int wait in Open(inspector, typeof(Collider), "Mesh", options => meshes = options))
        {
            yield return wait;
        }
        Check(meshes.SequenceEqual(["None", "Models/House.gltf", "Models/Walker.gltf"]), "a collider's Mesh lists only models",
            string.Join(", ", meshes));
        ComboBox? index = Box(inspector, typeof(Collider), "MeshIndex");
        Check(index is { IsEnabled: false, SelectedOption: "Choose a model first" }, "Mesh Index waits for a model",
            index?.SelectedOption ?? "");
        shell.SetComponent(prop, typeof(Collider).FullName!,
            new JsonObject { ["Shape"] = "Convex", ["Mesh"] = walker.ToString(), ["MeshIndex"] = 0 }, "Edit");
        yield return 2;
        string[] names = [.. database.MeshNamesOf(walker)];
        index = Box(inspector, typeof(Collider), "MeshIndex");
        Check(names.Length > 0 && index is { IsEnabled: true } && index.SelectedOption == $"{names[0]} (0)",
            "with a model, Mesh Index names its meshes", $"{index?.SelectedOption} of {string.Join(", ", names)}");
        shell.SetComponent(prop, typeof(Collider).FullName!,
            new JsonObject { ["Shape"] = "Convex", ["Mesh"] = walker.ToString(), ["MeshIndex"] = 3 }, "Edit");
        yield return 2;
        Check(Box(inspector, typeof(Collider), "MeshIndex")?.SelectedOption == "Mesh 3 (missing)",
            "an index past the model's meshes shows as missing", Box(inspector, typeof(Collider), "MeshIndex")?.SelectedOption ?? "");

        // An animator: Skeleton lists skinned models, Clip clips, State Machine state machines.
        Entity actor = Make(shell, "Actor");
        shell.SetComponent(actor, typeof(Animator).FullName!, new JsonObject(), "Add Component");
        shell.Selection.Selected = actor;
        yield return 2;
        string[] skeletons = [], clips = [], machines = [];
        foreach (int wait in Open(inspector, typeof(Animator), "Skeleton", options => skeletons = options))
        {
            yield return wait;
        }
        foreach (int wait in Open(inspector, typeof(Animator), "Clip", options => clips = options))
        {
            yield return wait;
        }
        foreach (int wait in Open(inspector, typeof(Animator), "StateMachine", options => machines = options))
        {
            yield return wait;
        }
        Check(skeletons.SequenceEqual(["None", "Models/Walker.gltf"]), "Skeleton lists only skinned models",
            string.Join(", ", skeletons));
        Check(clips.Length > 1 && clips.Skip(1).All(clip => clip.StartsWith("Models/Walker.gltf: ", StringComparison.Ordinal)),
            "Clip lists only clips", string.Join(", ", clips));
        Check(machines.SequenceEqual(["None", "Models/Walker.crstates"]), "State Machine lists only state machines",
            string.Join(", ", machines));

        // A sound source lists sounds; so does a game's own field ([AssetKind]).
        Entity speaker = Make(shell, "Speaker");
        shell.SetComponent(speaker, typeof(AudioSource).FullName!, new JsonObject(), "Add Component");
        SavedComponent? footsteps = SavedComponents.All().FirstOrDefault(saved => saved.Type.Name == "Footsteps");
        if (footsteps is not null)
        {
            shell.SetComponent(speaker, footsteps.Name, new JsonObject(), "Add Component");
        }
        shell.Selection.Selected = speaker;
        yield return 2;
        string[] sounds = [], gameSounds = [];
        foreach (int wait in Open(inspector, typeof(AudioSource), "Clip", options => sounds = options))
        {
            yield return wait;
        }
        Check(sounds.Contains("Sounds/Footstep.wav") && sounds.Skip(1).All(path => path.EndsWith(".wav", StringComparison.Ordinal) ||
                  path.EndsWith(".ogg", StringComparison.Ordinal) || path.EndsWith(".flac", StringComparison.Ordinal)),
            "an audio source's Clip lists only sounds", string.Join(", ", sounds));
        Check(footsteps is not null, "the Walker's game has its Footsteps component");
        if (footsteps is not null)
        {
            foreach (int wait in Open(inspector, footsteps.Type, "Sound", options => gameSounds = options))
            {
                yield return wait;
            }
            Check(gameSounds.SequenceEqual(sounds), "a game's [AssetKind] field lists the same", string.Join(", ", gameSounds));
        }

        // A joint: Other lists other entities with a collider.
        Entity anchor = Make(shell, "Anchor");
        shell.SetComponent(anchor, typeof(Collider).FullName!, new JsonObject(), "Add Component");
        shell.SetComponent(prop, typeof(Joint).FullName!, new JsonObject(), "Add Component");
        shell.Selection.Selected = prop;
        yield return 2;
        string[] others = [];
        foreach (int wait in Open(inspector, typeof(Joint), "Other", options => others = options))
        {
            yield return wait;
        }
        Check(others.SequenceEqual(["None", "Anchor"]), "a joint's Other lists only other entities with a collider",
            string.Join(", ", others));
    }

    private static Entity Make(Shell shell, string name)
    {
        Entity entity = shell.CreateEmpty(Entity.Null);
        shell.Rename(entity, name);
        return entity;
    }

    private static ComboBox? Box(InspectorPanel inspector, Type component, string field) =>
        H.Descendants(inspector).OfType<ComponentSection>().FirstOrDefault(section => section.Component == component.FullName)
            ?.Editors.FirstOrDefault(editor => editor.Field.Name == field)?.Controls.OfType<ComboBox>().FirstOrDefault();

    // Opens a field's drop-down with a click (focused first, which scrolls
    // it into view), reads its choices, and closes it.
    private IEnumerable<int> Open(InspectorPanel inspector, Type component, string field, Action<string[]> read)
    {
        ComboBox? box = Box(inspector, component, field);
        Check(box is not null, $"{component.Name}.{field} is a drop-down");
        if (box is null)
        {
            yield break;
        }
        box.Focus();
        yield return 1;
        V.Click(box.Root!, V.Center(box));
        yield return 1;
        Check(box.IsOpen, $"{component.Name}.{field}'s list opens");
        read([.. box.Options]);
        box.Popup.Close();
        yield return 1;
    }

    private void Check(bool passed, string what, string detail = "")
    {
        ++_checks;
        if (!passed)
        {
            ++_failures;
            Console.WriteLine($"PickerChecks: FAILED: {what} {detail}");
        }
    }
}
