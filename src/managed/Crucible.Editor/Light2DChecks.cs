using System.Numerics;
using Crucible.Assets;
using Crucible.Ecs;
using Crucible.Loop;
using Crucible.Render2D;
using Crucible.Scenes;
using V = Crucible.Editor.ViewportChecks;

namespace Crucible.Editor;

/// <summary>
/// 2D lights in the editor checking themselves (ADR 0113), for CTest's
/// launcher.jit.lights-2d: run with the Walker's project and <c>--game-arg
/// check-lights-2d=&lt;folder&gt;</c>. On a copy of the project: the
/// hierarchy's Create > 2D makes a Point Light, a Spot Light, and an Ambient
/// Light with their defaults, undoably, and deleted (the hierarchy shrinking
/// under its focused row, which once crashed the editor); a scene with them saves and opens
/// again with them; a selected spot's cone is drawn over the viewport (the
/// window saved as lights-2d.png); a sorting layer marked Unlit reaches the
/// renderer.
/// </summary>
[System(Phases.RenderPrepare)]
internal sealed class Light2DChecks : GameSystem
{
    private IEnumerator<int>? _script;
    private int _wait;
    private int _checks;
    private int _failures;
    private string _folder = "";

    public override void Start(FrameContext context)
    {
        if (!context.GameArguments.TryGetValue("check-lights-2d", out string? folder))
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
            ? $"Light2DChecks: all {_checks} checks passed"
            : $"Light2DChecks: {_failures} of {_checks} checks failed");
        context.QuitRequested = true;
    }

    private IEnumerable<int> Script()
    {
        Shell shell = Shell.Current ?? throw new InvalidOperationException("No editor shell");
        string copy = Path.Combine(Path.GetFullPath(_folder), "lights-2d-project");
        AssetChecks.OpenCopy(shell, copy, null);
        yield return 3;
        shell.NewScene();
        yield return 2;
        World world = shell.World;

        Entity point = shell.CreateLight2D(Entity.Null, Light2DKind.Point);
        Check(world.Read<PointLight2D>(point) is { Intensity: 1, Range: 3, Bands: 0 } p && p.Color == Vector3.One &&
              shell.LabelOf(point) == "Point Light" && shell.Selection.Selected.Value == point.Value,
            "Create > 2D > Point Light makes a white light of range 3, selected");
        ulong pointId = SceneIds.IdOf(world, point);
        shell.Undo();
        yield return 2;
        Check(SceneIds.Find(world, pointId).IsNull, "undo takes it away");
        shell.Redo();
        yield return 2;
        Check(!SceneIds.Find(world, pointId).IsNull, "redo brings it back");
        Entity spot = shell.CreateLight2D(Entity.Null, Light2DKind.Spot);
        Check(world.Read<SpotLight2D>(spot) is { Range: 3 } s && s.OuterConeAngle > s.InnerConeAngle && s.InnerConeAngle > 0,
            "a Spot Light has a cone, full inside its inner angle");
        // Deleted (as the Delete command does), its frames drawn, and back.
        point = SceneIds.Find(world, pointId); // made again by redo
        shell.Selection.Selected = point;
        yield return 2;
        shell.DeleteWithChanges(point);
        yield return 3;
        Check(SceneIds.Find(world, pointId).IsNull, "deleting a light deletes it");
        shell.Undo();
        yield return 3;
        Check(!SceneIds.Find(world, pointId).IsNull, "undo brings it back");
        point = SceneIds.Find(world, pointId);

        // The Delete command; a light made under a map, deleted in map mode.
        shell.Selection.Selected = point;
        yield return 2;
        shell.Commands.Find("Edit.Delete")!.Execute();
        yield return 3;
        Check(SceneIds.Find(world, pointId).IsNull, "the Delete command deletes a selected light");
        shell.Undo();
        yield return 3;
        // The map through the Tilemaps plugin's Create item (ADR 0123).
        Check(shell.RunCreateItem("2D/Tilemap", Entity.Null), "the Tilemaps plugin's Create > 2D > Tilemap is there");
        yield return 3;
        Entity map = shell.Selection.Selected;
        Entity under = shell.CreateLight2D(map, Light2DKind.Point);
        yield return 3;
        shell.Commands.Find("Edit.Delete")!.Execute();
        yield return 3;
        Workspace? mapMode = shell.Workspaces.FirstOrDefault(workspace => workspace.WorkspaceId == "Map");
        Check(mapMode is not null && !world.IsAlive(under), "a light under a map deletes in map mode");
        if (mapMode is not null)
        {
            shell.CloseWorkspace(mapMode);
        }
        mapMode = null;
        yield return 3;
        point = SceneIds.Find(world, pointId);
        Entity ambient = shell.CreateLight2D(Entity.Null, Light2DKind.Ambient);
        Check(world.Read<AmbientLight2D>(ambient) is { Intensity: 1 } a && a.Color.Z > a.Color.X,
            "an Ambient Light starts as a dim blue night");

        // Saved and opened again.
        string scene = Path.Combine(copy, "Assets", "Scenes", "Lights.crscene");
        Check(shell.WriteScene(scene), "the scene saves");
        shell.OpenScene(scene);
        yield return 3;
        world = shell.World;
        Check(SceneIds.Of(world).Entities.Count(e => world.Has(e, Components.Of<PointLight2D>())) == 1 &&
              SceneIds.Of(world).Entities.Count(e => world.Has(e, Components.Of<SpotLight2D>())) == 1 &&
              SceneIds.Of(world).Entities.Count(e => world.Has(e, Components.Of<AmbientLight2D>())) == 1,
            "the lights open again with the scene");

        // A spot selected: its cone over the viewport, framed straight on.
        spot = SceneIds.Of(world).Entities.First(e => world.Has(e, Components.Of<SpotLight2D>()));
        shell.Selection.Selected = spot;
        if (shell.Windows.SelectMany(window => window.DockSpace.Panels).OfType<ViewportPanel>().FirstOrDefault() is { } viewport)
        {
            viewport.View.Camera.Yaw = 0;
            viewport.View.Camera.Pitch = 0;
            viewport.View.Camera.Pivot = Vector3.Zero;
            viewport.View.Camera.Distance = 8;
        }
        yield return 3;
        V.Save(shell.Main.Surface, Path.Combine(_folder, "lights-2d.png"));

        // A sorting layer marked Unlit reaches the renderer.
        RuntimeSettings settings = RuntimeSettings.Current;
        settings.SortingLayers[0].Unlit = true;
        RuntimeSettings.Install(settings);
        Check(Crucible.Render2D.Api.UnlitLayers() == 1u, "a sorting layer marked Unlit isn't lit");
        settings.SortingLayers[0].Unlit = false;
        RuntimeSettings.Install(settings);
        Check(Crucible.Render2D.Api.UnlitLayers() == 0u, "and is again when it isn't");
    }

    private void Check(bool passed, string what, string detail = "")
    {
        ++_checks;
        if (!passed)
        {
            ++_failures;
            Console.WriteLine($"Light2DChecks: FAILED: {what} {detail}");
        }
    }
}
