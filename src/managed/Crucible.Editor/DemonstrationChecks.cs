using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Reflection;
using System.Text.Json.Nodes;
using Crucible.Assets;
using Crucible.Ecs;
using Crucible.Loop;
using Crucible.Platform;
using Crucible.Scenes;
using Crucible.UI;
using H = Crucible.Editor.HierarchyChecks;
using Png = Crucible.Samples.Diorama.Png;
using V = Crucible.Editor.ViewportChecks;

namespace Crucible.Editor;

/// <summary>
/// The M7 demonstration (ADR 0077), for CTest's launcher.jit.demonstration:
/// the milestone's "done when" as one session, from a brand-new project.
/// Run with <c>--game-arg check-demonstration=&lt;folder&gt;</c> (where the
/// project and captures go), <c>--game-arg demonstration-repo=&lt;repository&gt;</c>
/// (the Diorama's model and texture, and tests/demonstration/Project's code),
/// a settings file of its own, and optionally
/// <c>--game-arg compare=&lt;folder&gt;</c> (the other backend's captures).
/// It creates the project; opens it, building its C# and C++; drags a model
/// from the asset browser into the viewport (undone and redone); edits it
/// in the inspector (likewise) and gives it the game's components; saves,
/// closes, and reopens the scene; plays; edits the C# while playing and
/// sees it hot-reload; changes a texture while playing and sees it reload
/// on the GPU; stops; and runs it standalone. Captured after placing and
/// while playing.
/// </summary>
[System(Phases.RenderPrepare)]
internal sealed class DemonstrationChecks : GameSystem
{
    private const int ScancodeReturn = 40;

    private IEnumerator<int>? _script;
    private int _wait;
    private int _checks;
    private int _failures;
    private string _folder = "";
    private string _repository = "";
    private string? _compare;

    public override void Start(FrameContext context)
    {
        if (!context.GameArguments.TryGetValue("check-demonstration", out string? folder) ||
            !context.GameArguments.TryGetValue("demonstration-repo", out string? repository))
        {
            Enabled = false;
            return;
        }
        _folder = Path.GetFullPath(folder);
        _repository = repository;
        _compare = context.GameArguments.GetValueOrDefault("compare");
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
            ? $"DemonstrationChecks: all {_checks} checks passed"
            : $"DemonstrationChecks: {_failures} of {_checks} checks failed");
        context.QuitRequested = true;
    }

    private IEnumerable<int> Script()
    {
        Shell shell = Shell.Current ?? throw new InvalidOperationException("No editor shell");
        UIRoot root = shell.Main.Root;

        // 1. A brand-new project: one model, its texture, and the game's C# and C++.
        string folder = Path.Combine(_folder, "demonstration-project");
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }
        Project project = Project.Create(folder, "Demo");
        string diorama = Path.Combine(_repository, "samples", "Diorama", "Project", "Assets");
        string demo = Path.Combine(_repository, "tests", "demonstration", "Project");
        Directory.CreateDirectory(Path.Combine(project.AssetsFolder, "Models"));
        Directory.CreateDirectory(Path.Combine(project.AssetsFolder, "Textures"));
        Directory.CreateDirectory(Path.Combine(project.AssetsFolder, "Scenes"));
        foreach (string file in (string[])["Models/Tree.gltf", "Models/Tree.bin", "Textures/Leaves.png"])
        {
            File.Copy(Path.Combine(diorama, file), Path.Combine(project.AssetsFolder, file));
        }
        AssetChecks.CopyProject(demo, folder);

        // 2. Opened: its C++ and C# build and load; its components are known.
        shell.OpenProject(project.ProjectFilePath);
        string log = Log(shell);
        Check(log.Contains("Loaded Demo.Native", StringComparison.Ordinal), "the project's C++ builds and loads", Tail(log));
        Check(log.Contains("Loaded Demo.Game.dll", StringComparison.Ordinal), "and its C#", Tail(log));
        Check(SavedComponents.Find("Demo.Wobble") is not null && SavedComponents.Find("Demo.Pulse") is not null,
            "its components are known");
        shell.NewScene();
        shell.RestoreLayout(null);
        yield return 3;
        World world = shell.World;
        Light(shell);
        yield return 2;

        // 3. The model dragged from the asset browser into the viewport.
        AssetsPanel assets = shell.Main.DockSpace.Panels.OfType<AssetsPanel>().Single();
        assets.Group?.Activate(assets);
        assets.ShowFolder("Models");
        yield return 2;
        AssetTile? tile = assets.Tiles.FirstOrDefault(t => t.Path == "Models/Tree.gltf");
        Check(tile is { CanPlace: true }, "the model shows in the asset browser", string.Join(",", assets.Tiles.Select(t => t.Path)));
        if (tile is null)
        {
            yield break;
        }
        tile.Focus();
        yield return 2;
        SceneView view = shell.Main.DockSpace.Panels.OfType<ViewportPanel>().Single().View;
        Vector2 middle = new(view.Bounds.X + view.Bounds.Width / 2, view.Bounds.Y + view.Bounds.Height * 0.6f);
        H.Send(root, V.Motion(V.Center(tile)), V.Button(V.Center(tile), true), V.Motion(V.Center(tile) + new Vector2(8, 0)),
            V.Motion(middle));
        yield return 6;
        Check(!shell.Placing.IsNull, "dragging it over the viewport places it");
        H.Send(root, V.Button(middle, false));
        yield return 3;
        Entity tree = shell.Selection.Selected;
        ulong treeId = SceneIds.IdOf(world, tree);
        Check(!tree.IsNull && treeId != 0 && shell.History.NextUndo?.Title == "Place Tree", "the drop keeps it, as one step",
            shell.History.NextUndo?.Title ?? "");
        // The hierarchy reads the scene again a frame after it changes.
        yield return 3;
        V.Save(shell.Main.Surface, Capture("placed.png"));
        Compare("placed.png", view.Bounds);
        shell.Undo();
        yield return 2;
        Check(SceneIds.Find(world, treeId).IsNull, "undo takes it away");
        shell.Redo();
        yield return 2;
        tree = SceneIds.Find(world, treeId);
        Check(!tree.IsNull, "redo brings it back");
        shell.Selection.Selected = tree;
        yield return 3;

        // 4. Edited in the inspector: its scale typed; then the game's components added and set.
        InspectorPanel inspector = shell.Main.DockSpace.Panels.OfType<InspectorPanel>().Single();
        inspector.Group?.Activate(inspector);
        yield return 2;
        // (Fields are found and typed into by a helper: the script keeping one
        // would keep the game's types it shows, and the old build loaded.)
        if (!TypeInto(inspector, root, typeof(LocalTransform).FullName!, "Scale", "1.5"))
        {
            Check(false, "the inspector shows its transform");
            yield break;
        }
        yield return 2;
        Check(MathF.Abs(world.Read<LocalTransform>(tree).Scale.X - 1.5f) < 1e-4f, "a scale typed in the inspector sets it");
        shell.Undo();
        yield return 2;
        tree = SceneIds.Find(world, treeId);
        Check(MathF.Abs(world.Read<LocalTransform>(tree).Scale.X - 1) < 1e-4f, "undo puts it back");
        shell.Redo();
        yield return 2;
        tree = SceneIds.Find(world, treeId);
        Check(MathF.Abs(world.Read<LocalTransform>(tree).Scale.X - 1.5f) < 1e-4f, "redo sets it again");
        shell.AddComponent(tree, "Demo.Wobble");
        shell.AddComponent(tree, "Demo.Pulse");
        yield return 3;
        TypeInto(inspector, root, "Demo.Wobble", "Amount", "0.3");
        yield return 2;
        TypeInto(inspector, root, "Demo.Wobble", "Frames", "60");
        yield return 2;
        JsonNode? wobble = SceneIds.Read(world, tree, "Demo.Wobble");
        Check(wobble?["Amount"]?.GetValue<float>() == 0.3f && wobble?["Frames"]?.GetValue<float>() == 60f,
            "the game's component is added and edited in the inspector", wobble?.ToJsonString() ?? "none");

        // 5. Saved, closed, and reopened.
        string scenePath = Path.Combine(project.AssetsFolder, "Scenes", "Main.crscene");
        Check(shell.WriteScene(scenePath), "the scene saves");
        shell.NewScene();
        yield return 2;
        Check(SceneIds.Find(world, treeId).IsNull, "closing it clears the world");
        shell.OpenScene(scenePath);
        yield return 3;
        tree = SceneIds.Find(world, treeId);
        Check(!tree.IsNull && MathF.Abs(world.Read<LocalTransform>(tree).Scale.X - 1.5f) < 1e-4f &&
              SceneIds.Read(world, tree, "Demo.Wobble")?["Amount"]?.GetValue<float>() == 0.3f,
            "reopened, the tree is there with its edits");
        shell.Selection.Selected = Entity.Null;
        yield return 2;

        // 6. Played: the C# rocks it, the C++ counts it, C# reads the count.
        shell.Play();
        yield return 15;
        tree = SceneIds.Find(world, treeId);
        Check(shell.IsPlaying && MathF.Abs(world.Read<LocalTransform>(tree).Rotation.Z) > 1e-3f, "playing, the game's C# rocks it");
        Check(PulsesLastFrame(shell) == 1, "its C++ counts it, read from C#", PulsesLastFrame(shell).ToString());
        V.Save(shell.Main.Surface, Capture("playing.png"));
        Compare("playing.png", shell.Main.DockSpace.Panels.OfType<GamePanel>().Single().View.Bounds);

        // 7. The C# edited while playing: rebuilt, hot-reloaded, the edits kept.
        int before = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(shell.Game);
        string code = Path.Combine(project.CodeFolder, "Wobble.cs");
        File.WriteAllText(code, File.ReadAllText(code).Replace("Scale = 1f", "Scale = 2f", StringComparison.Ordinal));
        long started = Environment.TickCount64;
        while (System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(shell.Game) == before &&
               Environment.TickCount64 - started < 120000)
        {
            yield return 2;
        }
        yield return 5;
        tree = SceneIds.Find(world, treeId);
        log = Log(shell);
        Check(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(shell.Game) != before &&
              log.Contains("Reloaded Demo.Game.dll", StringComparison.Ordinal), "editing the C# while playing hot-reloads it",
            Tail(log));
        Check(!log.Contains("didn't unload", StringComparison.Ordinal), "and the old build unloads", Tail(log));
        Check(SceneIds.Read(world, tree, "Demo.Wobble")?["Amount"]?.GetValue<float>() == 0.3f && shell.IsPlaying,
            "the tree keeps its Wobble, still playing");

        // 8. A texture changed while playing: reloaded on the GPU.
        File.Copy(Path.Combine(diorama, "Textures", "Grass.png"), Path.Combine(project.AssetsFolder, "Textures", "Leaves.png"),
            overwrite: true);
        shell.RefreshAssets();
        yield return 3;
        Check(Log(shell).Contains("Reloaded Textures/Leaves.png on the GPU", StringComparison.Ordinal),
            "a texture changed while playing reloads on the GPU", Tail(Log(shell)));

        // 9. Stopped: the scene as it was.
        shell.Stop();
        yield return 3;
        tree = SceneIds.Find(world, treeId);
        Check(!shell.IsPlaying && world.Read<LocalTransform>(tree).Rotation == Quaternion.Identity, "Stop puts it back");

        // 10. Run standalone: the game, its module, and the scene as it is.
        Check(shell.RunStandalone("--headless", "--frames", "30"), "a standalone run starts");
        started = Environment.TickCount64;
        while (!Log(shell).Contains("The standalone run ended", StringComparison.Ordinal) &&
               Environment.TickCount64 - started < 120000)
        {
            yield return 2;
        }
        log = Log(shell);
        Check(log.Contains("[game] Crucible.Hosting: loaded Demo.Game.dll", StringComparison.Ordinal) &&
              log.Contains("[game] Crucible.Hosting: opened Main.crscene", StringComparison.Ordinal) &&
              log.Contains("[game] Crucible.Hosting: ran 30 frames", StringComparison.Ordinal) &&
              log.Contains("The standalone run ended (exit code 0)", StringComparison.Ordinal),
            "it runs standalone", Tail(log));
        Check(!log.Contains("which nothing registered", StringComparison.Ordinal) &&
              !log.Contains("couldn't be loaded", StringComparison.Ordinal), "with its module and model", Tail(log));
    }

    // A sun, ambient light, and a camera looking at where the model lands.
    private static void Light(Shell shell)
    {
        Entity sun = shell.CreateEmpty(Entity.Null);
        shell.SetComponent(sun, "Crucible.Scenes.EntityName", JsonNode.Parse("""{ "Value": "Sun" }"""), "Rename");
        shell.SetComponent(sun, "Crucible.Render.DirectionalLight",
            JsonNode.Parse("""{ "Color": [1, 0.97, 0.92], "Intensity": 3 }"""), "Sun");
        shell.SetComponent(sun, "Crucible.Render.AmbientLight",
            JsonNode.Parse("""{ "SkyColor": [0.5, 0.6, 0.8], "GroundColor": [0.3, 0.25, 0.2], "Intensity": 0.6 }"""), "Ambient");
        shell.SetComponent(sun, typeof(LocalTransform).FullName!,
            JsonNode.Parse("""{ "Position": [0, 0, 0], "Rotation": [0.35, 0.35, 0, 0.87], "Scale": [1, 1, 1] }"""), "Sun");
        Entity camera = shell.CreateEmpty(Entity.Null);
        shell.SetComponent(camera, "Crucible.Scenes.EntityName", JsonNode.Parse("""{ "Value": "Camera" }"""), "Rename");
        shell.SetComponent(camera, "Crucible.Render.Camera",
            JsonNode.Parse("""{ "Projection": "Perspective", "FieldOfViewY": 0.9, "NearPlane": 0.1 }"""), "Camera");
        shell.SetComponent(camera, typeof(LocalTransform).FullName!,
            JsonNode.Parse("""{ "Position": [0, 3, -9], "Rotation": [0.13, 0, 0, 0.99], "Scale": [1, 1, 1] }"""), "Camera");
    }

    // Types into a field's first number box, in the selected entity's section
    // for a component, and enters it; false if there's no such box.
    private static bool TypeInto(InspectorPanel inspector, UIRoot root, string component, string field, string text)
    {
        NumberField? box = inspector.Sections.FirstOrDefault(section => section.Component == component)?.Editors
            .FirstOrDefault(editor => editor.Field.Name == field)?.Controls
            .SelectMany(control => H.Descendants(control).Prepend(control)).OfType<NumberField>().FirstOrDefault();
        if (box is null)
        {
            return false;
        }
        box.Focus();
        box.SelectAll();
        H.Type(root, text);
        H.Send(root, H.KeyDown(ScancodeReturn, Keycode.Return));
        return true;
    }

    // demo::pulsesLastFrame through the game's generated bindings (Demo.Api).
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "The editor runs JIT only (ADR 0016)")]
    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "The editor runs JIT only (ADR 0016)")]
    private static long PulsesLastFrame(Shell shell)
    {
        Assembly? game = shell.Game?.Context.Assemblies.FirstOrDefault();
        MethodInfo? method = game?.GetType("Demo.Api")?.GetMethod("PulsesLastFrame");
        return method?.Invoke(null, null) is uint count ? count : -1;
    }

    private string Capture(string name)
    {
        Directory.CreateDirectory(_folder);
        return Path.Combine(_folder, name);
    }

    // ADR 0056's tolerance between backends, as the M6 demonstration's (ADR
    // 0064), over the 3D view's region: the rest of the window holds editor
    // state that varies between runs (the frame time shown, the order a new
    // entity lands in the hierarchy), not rendering.
    private void Compare(string name, System.Drawing.RectangleF region)
    {
        if (_compare is null)
        {
            return;
        }
        (int width, int height, byte[] mine) = Png.Read(Capture(name));
        (int otherWidth, int otherHeight, byte[] other) = Png.Read(Path.Combine(_compare, name));
        if (!Check(width == otherWidth && height == otherHeight, $"{name} matches the other capture's size"))
        {
            return;
        }
        float scale = Shell.Current?.Main.Root.Scale ?? 1; // logical units to pixels
        int left = Math.Max((int)(region.X * scale), 0), top = Math.Max((int)(region.Y * scale), 0);
        int right = Math.Min((int)(region.Right * scale), width), bottom = Math.Min((int)(region.Bottom * scale), height);
        double sum = 0;
        int far = 0, largest = 0, pixels = 0;
        for (int p = 0; p < width * height; ++p)
        {
            if (p % width < left || p % width >= right || p / width < top || p / width >= bottom)
            {
                continue;
            }
            ++pixels;
            int worst = 0;
            for (int c = 0; c < 3; ++c)
            {
                int d = Math.Abs(mine[p * 3 + c] - other[p * 3 + c]);
                sum += d;
                worst = Math.Max(worst, d);
            }
            far += worst > 8 ? 1 : 0;
            largest = Math.Max(largest, worst);
        }
        double mean = sum / Math.Max(pixels * 3, 1), farShare = 100.0 * far / Math.Max(pixels, 1);
        Console.WriteLine($"DemonstrationChecks: {name} against the other backend's: mean {mean:F3}/255, " +
                          $"{farShare:F3}% off by more than 8/255, largest {largest}/255");
        Check(mean < 1.0, $"{name}: the mean difference is under 1/255", $"{mean:F3}");
        Check(farShare < 0.5, $"{name}: under 0.5% of pixels differ by more than 8/255", $"{farShare:F3}%");
    }

    private static string Log(Shell shell) =>
        shell.LogPanel is { } log
            ? string.Join("\n", H.Descendants(log).OfType<Label>().Select(label => label.Text))
            : "";

    private static string Tail(string log) => string.Join(" | ", log.Split('\n').TakeLast(8));

    private bool Check(bool passed, string what, string detail = "")
    {
        ++_checks;
        if (!passed)
        {
            ++_failures;
            Console.WriteLine($"DemonstrationChecks: FAILED: {what} {detail}");
        }
        return passed;
    }
}
