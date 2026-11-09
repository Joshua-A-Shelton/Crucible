using System.Numerics;
using System.Text.Json.Nodes;
using Crucible.Ecs;
using Crucible.Loop;
using Crucible.Platform;
using Crucible.Scenes;
using Crucible.UI;
using H = Crucible.Editor.HierarchyChecks;
using V = Crucible.Editor.ViewportChecks;

namespace Crucible.Editor;

/// <summary>
/// The inspector checking itself headless (ADR 0069), for CTest's
/// launcher.jit.inspector: run with a project and scene (the Diorama's
/// village), <c>--game-arg check-inspector=&lt;file.png&gt;</c>, and a settings
/// file of its own. A house's sections and values show; a typed number and a
/// scrubbed one each change the scene and undo as one step; a rotation shows
/// as degrees; an enum's drop-down sets it; a part of an instance is marked
/// overridden once edited, and Revert puts the prefab's value back;
/// components are added and removed. The window is captured with an edited
/// part of a house selected.
/// </summary>
[System(Phases.RenderPrepare)]
internal sealed class InspectorChecks : GameSystem
{
    private const int ScancodeReturn = 40;

    private IEnumerator<int>? _script;
    private int _wait;
    private int _checks;
    private int _failures;
    private string _capture = "";
    private FrameContext? _context;

    public override void Start(FrameContext context)
    {
        if (!context.GameArguments.TryGetValue("check-inspector", out string? capture))
        {
            Enabled = false;
            return;
        }
        _capture = capture;
        _context = context;
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
            ? $"InspectorChecks: all {_checks} checks passed"
            : $"InspectorChecks: {_failures} of {_checks} checks failed");
        context.QuitRequested = true;
    }

    private IEnumerable<int> Script()
    {
        Shell shell = Shell.Current ?? throw new InvalidOperationException("No editor shell");
        shell.RestoreLayout(null);
        yield return 3;
        InspectorPanel inspector = shell.Main.DockSpace.Panels.OfType<InspectorPanel>().Single();
        UIRoot root = shell.Main.Root;
        World world = _context!.World;
        Check(inspector.Sections.Count == 0, "nothing selected, no sections");

        // A house: its transform's section, with its position in the fields.
        Entity house = SceneIds.Of(world).Entities.First(entity => shell.LabelOf(entity) == "House");
        shell.Selection.Selected = house;
        yield return 2;
        ComponentSection? transform = inspector.Sections.FirstOrDefault(section => section.Component == typeof(LocalTransform).FullName);
        Check(transform is not null, "the house's transform has a section",
            string.Join(",", inspector.Sections.Select(section => section.Component)));
        if (transform is null)
        {
            yield break;
        }
        Check(string.Join(",", transform.Editors.Select(editor => editor.Field.Name)) == "Position,Rotation,Scale",
            "its fields are listed in order");
        Vector3 start = world.Read<LocalTransform>(house).Position;
        NumberField[] position = [.. Numbers(transform.Editors[0])];
        Check(position.Length == 3 && Math.Abs(position[0].Value - start.X) < 1e-3, "the position shows",
            $"{position.FirstOrDefault()?.Value} vs {start.X}");
        Check(transform.Editors[0].IsOverridden, "an instance's root position differs from its prefab");

        // Typed: Enter sets it, one undo step puts it back.
        int steps = shell.History.UndoCount;
        position[0].Focus();
        position[0].SelectAll();
        H.Type(root, "3.5");
        H.Send(root, H.KeyDown(ScancodeReturn, Keycode.Return));
        yield return 2;
        Check(Math.Abs(world.Read<LocalTransform>(house).Position.X - 3.5f) < 1e-4f, "a typed number sets the field",
            world.Read<LocalTransform>(house).Position.ToString());
        Check(shell.History.UndoCount == steps + 1, "as one step");
        shell.Undo();
        yield return 2;
        Check(Vector3.Distance(world.Read<LocalTransform>(house).Position, start) < 1e-4f, "undo puts it back");

        // Scrubbed: a drag across the Y field moves it, and undoes in one step.
        Vector2 at = V.Center(position[1]);
        steps = shell.History.UndoCount;
        H.Send(root, V.Motion(at), V.Button(at, true), V.Motion(at + new Vector2(12, 0)), V.Motion(at + new Vector2(40, 0)),
            V.Button(at + new Vector2(40, 0), false));
        yield return 2;
        float scrubbed = world.Read<LocalTransform>(house).Position.Y - start.Y;
        Check(scrubbed > 0.01f, "a drag scrubs the number", scrubbed.ToString());
        Check(shell.History.UndoCount == steps + 1, "the drag is one step", (shell.History.UndoCount - steps).ToString());
        shell.Undo();
        yield return 2;
        Check(Vector3.Distance(world.Read<LocalTransform>(house).Position, start) < 1e-4f, "and undoes at once");

        // Rotation in degrees: 90 typed into Y turns it a quarter around Y.
        NumberField[] angles = [.. Numbers(transform.Editors[1])];
        Check(angles.Length == 3, "a rotation shows three angles");
        angles[1].Focus();
        angles[1].SelectAll();
        H.Type(root, "90");
        H.Send(root, H.KeyDown(ScancodeReturn, Keycode.Return));
        yield return 2;
        Vector3 forward = Vector3.Transform(Vector3.UnitZ, world.Read<LocalTransform>(house).Rotation);
        Check(Vector3.Distance(forward, Vector3.UnitX) < 1e-3f, "90 degrees around Y turns +Z to +X", forward.ToString());
        Check(Math.Abs(FieldEditor.EulerDegrees(world.Read<LocalTransform>(house).Rotation).Y - 90) < 1e-2f,
            "and reads back as 90");
        shell.Undo();
        yield return 2;

        // A part of the house: the same as its prefab until edited; Revert puts the prefab's value back.
        Entity part = SceneIds.ChildrenOf(world, house).FirstOrDefault();
        Check(!part.IsNull, "the house has parts");
        shell.Selection.Selected = part;
        yield return 2;
        transform = inspector.Sections.First(section => section.Component == typeof(LocalTransform).FullName);
        Check(!transform.Editors[0].IsOverridden, "an unedited part matches its prefab");
        Vector3 partStart = world.Read<LocalTransform>(part).Position;
        shell.SetComponent(part, typeof(LocalTransform).FullName!,
            JsonNode.Parse($"{{\"Position\":[{partStart.X + 1},{partStart.Y},{partStart.Z}],\"Rotation\":[0,0,0,1],\"Scale\":[1,1,1]}}"),
            "Edit Position");
        yield return 2;
        Check(transform.Editors[0].IsOverridden, "an edited part is marked overridden");
        V.Save(shell.Main.Surface, _capture);
        transform.Revert("Position");
        yield return 2;
        Check(!transform.Editors[0].IsOverridden &&
              Vector3.Distance(world.Read<LocalTransform>(part).Position, partStart) < 1e-4f, "Revert puts the prefab's back");

        // Long field names stay in their column: none reaches the controls beside it.
        Entity spot = SceneIds.Of(world).Entities.FirstOrDefault(entity => world.Has(entity, Components.Of<Crucible.Render.SpotLight>()));
        Check(!spot.IsNull, "the village has a spot light");
        shell.Selection.Selected = spot;
        yield return 3;
        foreach (FieldEditor editor in inspector.Sections.SelectMany(section => section.Editors))
        {
            UIElement name = H.Descendants(editor).OfType<Label>().First();
            float controls = editor.Controls.Select(control => control.Bounds.Left).DefaultIfEmpty(float.MaxValue).Min();
            Check(name.Bounds.Right <= controls + 0.5f, "a field's name stays left of its controls",
                $"{editor.Field.Name}: {name.Bounds.Right} > {controls}");
            UIElement[] row = [.. editor.Controls];
            for (int i = 0; i < row.Length; ++i)
            {
                for (int j = i + 1; j < row.Length; ++j)
                {
                    Check(!row[i].Bounds.IntersectsWith(row[j].Bounds), "a field's controls don't overlap",
                        $"{editor.Field.Name}: {row[i].GetType().Name} {row[i].Bounds} and {row[j].GetType().Name} {row[j].Bounds}");
                }
            }
        }
        V.Save(shell.Main.Surface, Path.ChangeExtension(_capture, ".spot.png"));

        // Components added and removed, each undoable; an enum set from its drop-down.
        Entity empty = shell.CreateEmpty(Entity.Null);
        yield return 2;
        string camera = typeof(Crucible.Render.Camera).FullName!;
        shell.AddComponent(empty, camera);
        yield return 2;
        ComponentSection? cameraSection = inspector.Sections.FirstOrDefault(section => section.Component == camera);
        Check(cameraSection is not null, "Add Component adds a section");
        if (cameraSection is not null)
        {
            ComboBox? projection = cameraSection.Editors[0].Controls.OfType<ComboBox>().FirstOrDefault();
            Check(projection is not null && projection.Options.Count >= 2, "an enum is a drop-down");
            if (projection is not null)
            {
                projection.SelectedIndex = 1;
                yield return 2;
                Check(world.Read<Crucible.Render.Camera>(empty).Projection.ToString() == projection.Options[1],
                    "choosing sets it", world.Read<Crucible.Render.Camera>(empty).Projection.ToString());
            }
        }
        shell.RemoveComponent(empty, camera);
        yield return 2;
        Check(!world.Has(empty, Components.Of<Crucible.Render.Camera>()) &&
              inspector.Sections.All(section => section.Component != camera), "Remove Component removes it");
        shell.Undo();
        yield return 2;
        Check(world.Has(empty, Components.Of<Crucible.Render.Camera>()), "undoing Remove brings it back");
    }

    private static IEnumerable<NumberField> Numbers(FieldEditor editor) =>
        editor.Controls.SelectMany(control => H.Descendants(control).Prepend(control)).OfType<NumberField>();

    private void Check(bool passed, string what, string detail = "")
    {
        ++_checks;
        if (!passed)
        {
            ++_failures;
            Console.WriteLine($"InspectorChecks: FAILED: {what} {detail}");
        }
    }
}
