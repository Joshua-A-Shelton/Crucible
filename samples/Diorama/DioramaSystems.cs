using System.Diagnostics;
using System.Numerics;
using Crucible.Assets;
using Crucible.Assets.Importers;
using Crucible.Ecs;
using Crucible.Loop;
using Crucible.Render;
using Crucible.Scenes;

namespace Crucible.Samples.Diorama;

// The M5 demonstration (ADR 0056). Each frame:
//   BeginFrame  DioramaSetup    (first frame) imports the project and builds the scene
//   Update      SpinSystem      turns the heroes
//   ...         the engine's propagation and render systems
//   EndFrame    DioramaFinish   captures, compares, or measures, then quits

/// <summary>
/// Imports the project (a copy, so its cache stays out of the source tree),
/// points the renderer at the imported files, and builds the scene: one
/// diorama, or a town of 36 when measuring.
/// </summary>
[System(Phases.BeginFrame)]
public sealed class DioramaSetup : GameSystem
{
    public static DioramaSettings Settings { get; private set; } = DioramaSettings.Default;
    public static List<Entity> Heroes { get; } = [];
    public static bool Failed { get; private set; }

    // The project's copy, deleted when the game stops.
    private static string? _work;

    public override void Start(FrameContext context)
    {
        try
        {
            Build(context);
        }
        catch (Exception exception)
        {
            Console.WriteLine($"Diorama: FAILED to set up: {exception}");
            Failed = true;
        }
    }

    private static void Build(FrameContext context)
    {
        Settings = DioramaSettings.Parse(context.GameArguments);
        string source = Path.GetFullPath(Settings.Project);
        string work = Path.Combine(Path.GetTempPath(), $"crucible-diorama-{Environment.ProcessId}");
        CopyFolder(Path.GetDirectoryName(source)!, work);
        _work = work;
        Project project = Project.Open(Path.Combine(work, Path.GetFileName(source)));
        AssetDatabase database =
            AssetDatabase.Open(project, [new TextureImporter(), new MeshImporter(), new ShaderImporter()]);
        Stopwatch watch = Stopwatch.StartNew();
        foreach (ImportResult result in database.ImportAll())
        {
            // Buffers have no importer: they are read as their models' dependencies.
            if (result.Outcome is not (ImportOutcome.Imported or ImportOutcome.UpToDate or ImportOutcome.NoImporter))
            {
                Console.WriteLine($"Diorama: FAILED to import {result.Path}: {result.Outcome} {result.Error}");
                Failed = true;
            }
        }
        Console.WriteLine($"Diorama: imported {database.Assets.Count()} assets in {watch.ElapsedMilliseconds} ms");
        RenderAssets.SetReader(id => database.ImportedFile(id) is { } path ? File.ReadAllBytes(path) : null);

        Models models = new(database);
        World world = context.World;
        PrefabLibrary library = database.Prefabs();
        int side = Settings.Measure > 0 ? 6 : 1;
        for (int x = 0; x < side; ++x)
        {
            for (int z = 0; z < side; ++z)
            {
                Vector3 offset = new((x - (side - 1) / 2f) * 32, 0, (z - (side - 1) / 2f) * 32);
                BuildDiorama(world, library, models, offset);
            }
        }
        AddViewAndLight(world);
        if (Settings.SaveScene is { } scene)
        {
            // The village as a scene file (the editor's Village.crscene, ADR 0067).
            world.UpdateWorldTransforms();
            File.WriteAllBytes(scene, Crucible.Scenes.Scene.Save(world, AssetId.New(), library));
            Console.WriteLine($"Diorama: saved the scene to {scene}");
        }
    }

    public override void Run(FrameContext context)
    {
    }

    public override void Stop(FrameContext context)
    {
        RenderAssets.SetReader(null);
        if (_work is not null && Directory.Exists(_work))
        {
            Directory.Delete(_work, recursive: true);
        }
    }

    private sealed record Models(AssetId Ground, AssetId House, AssetId Tree, AssetId LampPost, AssetId Hero)
    {
        public Models(AssetDatabase database)
            : this(Find(database, "Ground"), Find(database, "House"), Find(database, "Tree"),
                Find(database, "LampPost"), Find(database, "Hero"))
        {
        }

        private static AssetId Find(AssetDatabase database, string name) =>
            database.Find($"Models/{name}.gltf")?.Id ?? throw new InvalidOperationException($"no Models/{name}.gltf");
    }

    // A village square: houses around it, trees, lamp posts with their
    // lights (two of them shadowed), and the hero in the middle under a
    // shadowed spot light.
    private static void BuildDiorama(World world, PrefabLibrary library, Models models, Vector3 offset)
    {
        Entity Place(AssetId model, Vector3 position, float yaw)
        {
            Entity entity = Prefab.Instantiate(world, model, library);
            ref LocalTransform transform = ref world.Get<LocalTransform>(entity);
            transform.Position = offset + position;
            transform.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw);
            return entity;
        }

        Place(models.Ground, Vector3.Zero, 0);
        Place(models.House, new Vector3(-7, 0, 6), 0.3f);
        Place(models.House, new Vector3(7.5f, 0, 7), -0.4f);
        Place(models.House, new Vector3(-8.5f, 0, -3.5f), 1.6f);
        Place(models.House, new Vector3(9, 0, -2.5f), -1.4f);
        foreach (Vector3 tree in (Vector3[])[new(-3, 0, 10), new(3.5f, 0, 11.5f), new(-12, 0, 2), new(12, 0, 3),
                     new(-4.5f, 0, -8), new(5, 0, -9)])
        {
            Place(models.Tree, tree, tree.X);
        }
        Vector3[] lamps = [new(-2.5f, 0, 2.5f), new(3, 0, 4), new(-4, 0, -3), new(4.5f, 0, -2.5f)];
        for (int i = 0; i < lamps.Length; ++i)
        {
            Place(models.LampPost, lamps[i], 0);
            // The head, at the arm's end: glTF's +X is mirrored to -X on import.
            Entity light = world.CreateEntity(Components.Of<LocalTransform>(), Components.Of<PointLight>());
            world.Set(light, new LocalTransform
            {
                Position = offset + lamps[i] + new Vector3(-0.55f, 2.75f, 0), Rotation = Quaternion.Identity,
                Scale = Vector3.One,
            });
            world.Set(light, new PointLight
            {
                Color = new Vector3(1, 0.78f, 0.5f), Intensity = 7, Range = 7, CastsShadows = i < 2,
            });
        }
        Entity spot = world.CreateEntity(Components.Of<LocalTransform>(), Components.Of<SpotLight>());
        world.Set(spot, new LocalTransform
        {
            Position = offset + new Vector3(0, 6, -3),
            Rotation = Facing(Vector3.Normalize(new Vector3(0, -5, 3))),
            Scale = Vector3.One,
        });
        world.Set(spot, new SpotLight
        {
            Color = new Vector3(0.75f, 0.85f, 1), Intensity = 14, Range = 12, InnerConeAngle = 0.5f,
            OuterConeAngle = 0.7f, CastsShadows = true,
        });
        // Facing the camera (its nose on glTF's +Z, which the camera looks
        // along); SpinSystem turns it from there.
        Heroes.Add(Place(models.Hero, Vector3.Zero, MathF.PI));
    }

    // The camera with its finishing, the evening sun, the ambient light, and
    // the sky.
    private static void AddViewAndLight(World world)
    {
        Entity camera = world.CreateEntity(Components.Of<LocalTransform>(), Components.Of<Camera>(),
            Components.Of<PostProcess>());
        world.Set(camera, new LocalTransform
        {
            Position = new Vector3(0, 4.5f, -15), Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitX, 0.22f),
            Scale = Vector3.One,
        });
        world.Set(camera, new Camera
        {
            Projection = CameraProjection.Perspective, FieldOfViewY = 0.8f, NearPlane = 0.1f, FarPlane = 1000,
        });
        world.Set(camera, new PostProcess { BloomStrength = 0.04f, Saturation = 0.08f, Contrast = 0.05f });

        Entity sun = world.CreateEntity(Components.Of<LocalTransform>(), Components.Of<DirectionalLight>());
        world.Set(sun, new LocalTransform
        {
            Rotation = Facing(-Vector3.Normalize(new Vector3(-0.55f, 0.6f, -0.6f))), Scale = Vector3.One,
        });
        world.Set(sun, new DirectionalLight
        {
            Color = new Vector3(1, 0.8f, 0.62f), Intensity = 0.9f, CastsShadows = true, ShadowDistance = 60,
        });
        Entity ambient = world.CreateEntity(Components.Of<AmbientLight>());
        world.Set(ambient, new AmbientLight
        {
            SkyColor = new Vector3(0.35f, 0.4f, 0.55f), GroundColor = new Vector3(0.22f, 0.2f, 0.16f), Intensity = 0.75f,
        });
        Entity sky = world.CreateEntity(Components.Of<Sky>());
        world.Set(sky, new Sky
        {
            ZenithColor = new Vector3(0.18f, 0.32f, 0.7f), HorizonColor = new Vector3(0.95f, 0.75f, 0.55f),
            GroundColor = new Vector3(0.3f, 0.26f, 0.22f), Intensity = 1, SunSize = 0.04f, SunIntensity = 6,
        });
    }

    // A rotation turning +Z (where lights shine) toward `direction`.
    private static Quaternion Facing(Vector3 direction)
    {
        Vector3 forward = Vector3.UnitZ;
        float cosine = Vector3.Dot(forward, direction);
        Vector3 axis = Vector3.Cross(forward, direction);
        return axis.LengthSquared() < 1e-12f
            ? cosine > 0 ? Quaternion.Identity : Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI)
            : Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis), MathF.Acos(Math.Clamp(cosine, -1, 1)));
    }

    private static void CopyFolder(string from, string to)
    {
        if (Directory.Exists(to))
        {
            Directory.Delete(to, recursive: true);
        }
        foreach (string file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(from, file);
            if (relative.StartsWith("Cache", StringComparison.Ordinal))
            {
                continue;
            }
            string target = Path.Combine(to, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }
}

/// <summary>
/// Turns every hero a little each frame, by the frame's number, so every run
/// shows the same turn at the same frame.
/// </summary>
[System(Phases.Update)]
public sealed class SpinSystem : GameSystem
{
    private int _frames;

    public override void Run(FrameContext context)
    {
        ++_frames;
        World world = context.World;
        foreach (Entity hero in DioramaSetup.Heroes)
        {
            world.Get<LocalTransform>(hero).Rotation =
                Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI + 0.08f * _frames);
        }
    }
}

/// <summary>
/// After the scene has rendered: captures the last frame (and compares it),
/// or times frames; then quits, printing a report CTest reads.
/// </summary>
[System(Phases.EndFrame)]
public sealed class DioramaFinish : GameSystem
{
    private int _frames;
    private readonly List<double> _frameMs = [], _propagationMs = [], _renderMs = [];
    private long _lastTicks;
    private GameSystem? _propagation, _render;
    private readonly List<string> _failures = [];
    private int _checks;

    public override void Start(FrameContext context)
    {
        IReadOnlyList<GameSystem> systems = context.Schedule!.Systems;
        _propagation = systems.FirstOrDefault(system => system.Name == "crucible.TransformPropagation");
        _render = systems.FirstOrDefault(system => system.Name == typeof(RenderSystem).FullName);
    }

    public override void Run(FrameContext context)
    {
        ++_frames;
        DioramaSettings settings = DioramaSetup.Settings;
        if (DioramaSetup.Failed)
        {
            Finish(context);
            return;
        }
        if (settings.Measure > 0)
        {
            Measure(context, settings);
            return;
        }
        if (_frames < settings.Frames)
        {
            return;
        }
        if (settings.Capture is { } capture)
        {
            Capture(context, capture, settings.Compare);
        }
        Finish(context);
    }

    private void Capture(FrameContext context, string path, string? reference)
    {
        if (context.Surface.Value == 0)
        {
            Check(false, "a surface to capture (run the launcher with --headless)");
            return;
        }
        const int width = HeadlessWidth, height = HeadlessHeight;
        byte[] bgra = new byte[width * height * 4];
        Render.Api.ReadSurfacePixels(context.Surface, bgra);
        byte[] rgb = new byte[width * height * 3];
        HashSet<int> colors = [];
        double brightness = 0;
        for (int i = 0; i < width * height; ++i)
        {
            rgb[i * 3] = bgra[i * 4 + 2];
            rgb[i * 3 + 1] = bgra[i * 4 + 1];
            rgb[i * 3 + 2] = bgra[i * 4];
            colors.Add(rgb[i * 3] << 16 | rgb[i * 3 + 1] << 8 | rgb[i * 3 + 2]);
            brightness += (rgb[i * 3] + rgb[i * 3 + 1] + rgb[i * 3 + 2]) / 3.0;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        Png.Write(path, width, height, rgb);
        brightness /= width * height;
        Console.WriteLine($"Diorama: captured {width}x{height} to {path}: {colors.Count} colors, " +
                          $"mean brightness {brightness:F1}");
        // Not broken: a lit, detailed image, neither black nor washed out.
        Check(colors.Count > 2000, "the image has detail", $"{colors.Count} colors");
        Check(brightness is > 40 and < 220, "the image is lit, not black or white", $"mean {brightness:F1}");
        if (reference is not null)
        {
            Compare(path, reference, width, height, rgb);
        }
    }

    // ADR 0056's tolerance between backends on one machine: a mean
    // difference under 1/255, and under 0.5% of pixels off by more than 8/255.
    private void Compare(string path, string reference, int width, int height, byte[] rgb)
    {
        (int otherWidth, int otherHeight, byte[] other) = Png.Read(reference);
        if (!Check(otherWidth == width && otherHeight == height, "the images are the same size",
                $"{width}x{height} against {otherWidth}x{otherHeight}"))
        {
            return;
        }
        byte[] diff = new byte[rgb.Length];
        double sum = 0;
        int far = 0, largest = 0;
        for (int p = 0; p < width * height; ++p)
        {
            int worst = 0;
            for (int c = 0; c < 3; ++c)
            {
                int d = Math.Abs(rgb[p * 3 + c] - other[p * 3 + c]);
                sum += d;
                worst = Math.Max(worst, d);
                diff[p * 3 + c] = (byte)Math.Min(255, d * 16); // magnified to be seen
            }
            far += worst > 8 ? 1 : 0;
            largest = Math.Max(largest, worst);
        }
        string diffPath = path + ".diff.png";
        Png.Write(diffPath, width, height, diff);
        double mean = sum / rgb.Length;
        double farShare = 100.0 * far / (width * height);
        Console.WriteLine($"Diorama: against {reference}: mean difference {mean:F3}/255, " +
                          $"{farShare:F3}% of pixels off by more than 8/255, largest {largest}/255; " +
                          $"differences (x16) in {diffPath}");
        Check(mean < 1.0, "the mean difference is under 1/255", $"{mean:F3}");
        Check(farShare < 0.5, "under 0.5% of pixels differ by more than 8/255", $"{farShare:F3}%");
    }

    // Warm-up frames, then `measure` frames timed.
    private void Measure(FrameContext context, DioramaSettings settings)
    {
        int warmUp = settings.Frames;
        int measured = _frames - warmUp;
        long now = Stopwatch.GetTimestamp();
        if (measured > 0)
        {
            _frameMs.Add(Stopwatch.GetElapsedTime(_lastTicks, now).TotalMilliseconds);
            _propagationMs.Add(Ms(_propagation?.LastRunTicks ?? 0));
            _renderMs.Add(Ms(_render?.LastRunTicks ?? 0));
        }
        _lastTicks = now;
        if (measured == settings.Measure)
        {
            Report(World(context));
            Finish(context);
        }
    }

    private static string World(FrameContext context) =>
        $"{context.World.EntityCount} entities, {DioramaSetup.Heroes.Count} heroes moving";

    private void Report(string label)
    {
        static string Stats(List<double> values)
        {
            double[] sorted = [.. values.Order()];
            return $"median {sorted[sorted.Length / 2],7:F3} ms, 95th {sorted[sorted.Length * 95 / 100],7:F3} ms";
        }
        Console.WriteLine($"Diorama: {label}, {_frameMs.Count} frames");
        Console.WriteLine($"  frame        {Stats(_frameMs)}");
        Console.WriteLine($"  propagation  {Stats(_propagationMs)}");
        Console.WriteLine($"  render CPU   {Stats(_renderMs)}");
        _frameMs.Clear();
        _propagationMs.Clear();
        _renderMs.Clear();
    }

    private static double Ms(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;

    // The launcher's headless surface (Host.cs).
    private const int HeadlessWidth = 1280, HeadlessHeight = 720;

    private bool Check(bool passed, string what, string? detail = null)
    {
        ++_checks;
        if (!passed)
        {
            _failures.Add($"{what} ({detail})");
        }
        return passed;
    }

    private void Finish(FrameContext context)
    {
        if (DioramaSetup.Settings.Capture is not null && _checks == 0)
        {
            _failures.Add("nothing was checked");
        }
        if (DioramaSetup.Failed || _failures.Count > 0)
        {
            foreach (string failure in _failures)
            {
                Console.WriteLine($"Diorama: FAILED: {failure}");
            }
            Console.WriteLine("Diorama: FAILED");
        }
        else
        {
            Console.WriteLine($"Diorama: all {_checks} checks passed");
        }
        context.QuitRequested = true;
    }
}
