// The M4 demonstration (ADR 0046): a project's assets imported, a scene
// built from model and prefab instances, saved as JSON, reloaded, and
// compared; then the imported model's mirroring checked.
//
//   Crucible.Samples.SceneRoundTrip [--project <file.crproject>] [--model <file>] [--work <folder>]
//
// --project  the project to start from (default: the one beside this sample)
// --model    a model of your own (.gltf, .glb, or .fbx) to use instead of
//            Lopsided; its folder is copied along, for buffers and textures
// --work     where the project is copied and run (default: a temporary folder)

using System.Diagnostics;
using System.Numerics;
using Crucible.Assets;
using Crucible.Assets.Importers;
using Crucible.Ecs;
using Crucible.Samples.SceneRoundTrip;
using Crucible.Scenes;
using Crucible.Serialization;

Dictionary<string, string> options = [];
for (int i = 0; i + 1 < args.Length; i += 2)
{
    options[args[i].TrimStart('-')] = args[i + 1];
}
string source = Path.GetFullPath(options.GetValueOrDefault("project")
    ?? Path.Combine("samples", "SceneRoundTrip", "Project", "Demo.crproject"));
string work = Path.GetFullPath(options.GetValueOrDefault("work")
    ?? Path.Combine(Path.GetTempPath(), "crucible-scene-roundtrip"));
string? customModel = options.GetValueOrDefault("model");

new Crucible.Generated.ComponentRegistration().Register();
Report report = new();

// 1. A fresh copy of the project, without its cache.
if (Directory.Exists(work))
{
    Directory.Delete(work, recursive: true);
}
CopyFolder(Path.GetDirectoryName(source)!, work, skip: "Cache");
string modelPath = "Models/Lopsided.gltf";
if (customModel is not null)
{
    string folder = Path.GetDirectoryName(Path.GetFullPath(customModel))!;
    CopyFolder(folder, Path.Combine(work, "Assets", "Custom"), skip: null);
    modelPath = "Custom/" + Path.GetFileName(customModel);
}
Project project = Project.Open(Path.Combine(work, Path.GetFileName(source)));
Console.WriteLine($"Project: {project.Root}");

// 2. Import everything, one asset at a time to time each.
AssetDatabase database = AssetDatabase.Open(project, [new TextureImporter(), new MeshImporter(), new FbxImporter()]);
Console.WriteLine();
Console.WriteLine("Imports:");
foreach (AssetEntry entry in database.Assets.Where(entry => entry.Importer.Length > 0))
{
    Stopwatch watch = Stopwatch.StartNew();
    ImportResult result = database.Import(entry.Id);
    watch.Stop();
    long size = result.OutputPath is null ? 0 : new FileInfo(result.OutputPath).Length;
    long sourceSize = new FileInfo(Path.Combine(project.AssetsFolder, entry.Path)).Length;
    Console.WriteLine($"  {entry.Path,-32} {entry.Importer,-8} {result.Outcome,-9} {watch.Elapsed.TotalMilliseconds,8:F1} ms" +
                      $"  {sourceSize,9:N0} -> {size,9:N0} bytes");
    foreach (string warning in result.Warnings)
    {
        Console.WriteLine($"      warning: {warning}");
    }
    report.Check(result.Outcome == ImportOutcome.Imported, $"{entry.Path} imports", result.Error);
}
AssetId model = database.Find(modelPath)?.Id ?? throw new InvalidOperationException($"there is no {modelPath} in the project");
AssetId pedestal = database.Find("Prefabs/Pedestal.prefab")!.Id;

// 3. A scene: the model twice (the second moved, turned, and with its
// first child node stretched), the pedestal prefab (which nests the
// Lopsided model), and a watcher looking at the first model's nose.
AssetId sceneId = AssetId.Parse("6c0f5a1e-2b3d-4c5e-8f70-9a1b2c3d4e10");
using World world = new();
PrefabLibrary library = database.Prefabs();
Entity first = Prefab.Instantiate(world, model, library);
Entity second = Prefab.Instantiate(world, model, library);
ref LocalTransform placed = ref world.Get<LocalTransform>(second);
placed.Position = new Vector3(2.5f, 0, 0);
placed.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2);
Entity stretched = FirstDescendantWithMesh(world, second);
world.Get<LocalTransform>(stretched).Scale = new Vector3(1, 2, 1);
Entity stand = Prefab.Instantiate(world, pedestal, library);
world.Get<LocalTransform>(stand).Position = new Vector3(-2.5f, 0, 0);
Entity watcher = world.CreateEntity();
world.Set(watcher, new LocalTransform { Position = new Vector3(0, 2, -5), Rotation = Quaternion.Identity, Scale = Vector3.One });
world.Set(watcher, new LookAt { Target = Descendants(world, first).Last() });
Entity[] roots = [first, second, stand, watcher];

// 4. Save, reload into a new world, compare, and save again.
byte[] saved = Scene.Save(world, sceneId, library);
string scenePath = Path.Combine(project.AssetsFolder, "Demo.scene");
File.WriteAllBytes(scenePath, saved);
byte[] expected = Scene.SaveUnpacked(world, sceneId);

using World loaded = new();
SceneLoad load = Scene.Load(loaded, File.ReadAllBytes(scenePath), prefabs: database.Prefabs());
Console.WriteLine();
Console.WriteLine($"Scene: {scenePath}");
Console.WriteLine($"  {world.EntityCount} entities from {roots.Length} roots; {saved.Length:N0} bytes of JSON " +
                  $"({expected.Length:N0} with every instance written out)");
Console.WriteLine();
Console.WriteLine("Round trip:");
report.Check(load.SkippedComponents.Count == 0, "every component is known on reload",
    string.Join(", ", load.SkippedComponents));
report.Check(loaded.EntityCount == world.EntityCount, "the same number of entities",
    $"{loaded.EntityCount} instead of {world.EntityCount}");
report.Check(Scene.SaveUnpacked(loaded, sceneId).AsSpan().SequenceEqual(expected),
    "the same entities, hierarchy, and component values");
report.Check(Instances(world, roots).SequenceEqual(Instances(loaded, load.Roots)), "the same prefab instances");
report.Check(Scene.Save(loaded, sceneId, database.Prefabs()).AsSpan().SequenceEqual(saved),
    "saving the reloaded scene gives the same file");

// 5. Mirroring, read from the imported model as a renderer would.
Console.WriteLine();
Console.WriteLine("Mirroring:");
MeshChecks mesh = new(database.ImportedFile(model)!);
(int insideOut, int faces) = mesh.CountInsideOutFaces();
string facesNote = $"{insideOut} of {faces} faces over {mesh.MeshCount} meshes and {mesh.LodCount} levels";
if (customModel is null)
{
    report.Check(insideOut == 0, "no inside-out faces", facesNote);
    foreach ((string name, float volume) in mesh.Volumes())
    {
        report.Check(volume > 0, $"{name} encloses a positive volume ({volume:F3} m³)");
    }
    Vector3? arm = mesh.NodePosition("RightArm");
    Vector3? nose = mesh.NodePosition("Nose");
    report.Check(arm is { X: > 0.3f }, $"the right arm is on +X (at {arm})");
    report.Check(nose is { Z: > 0.3f }, $"the nose faces +Z (at {nose})");
}
else
{
    // Any model's normals are its own business; this is information.
    Console.WriteLine($"  [info] faces disagreeing with their normals: {facesNote}");
}

Console.WriteLine();
Console.WriteLine(report.Failures == 0 ? $"All {report.Passes} checks passed." : $"{report.Failures} checks failed.");
return report.Failures == 0 ? 0 : 1;

// The model's entities, depth first.
static List<Entity> Descendants(World world, Entity root)
{
    List<Entity> all = [];
    Entity[] children = new Entity[64];
    void Visit(Entity entity)
    {
        all.Add(entity);
        int count = world.ChildrenOf(entity, children);
        foreach (Entity child in children[..count].ToArray())
        {
            Visit(child);
        }
    }
    Visit(root);
    return all;
}

static Entity FirstDescendantWithMesh(World world, Entity root) =>
    Descendants(world, root).First(entity => world.Has(entity, Components.Of<MeshInstance>()));

// Every marked instance root under the given roots: its scene ID and prefab.
static List<(ulong Id, AssetId Prefab)> Instances(World world, IEnumerable<Entity> roots) =>
    [.. roots.SelectMany(root => Descendants(world, root))
        .Where(entity => world.Has(entity, Components.Of<PrefabInstance>()))
        .Select(entity => (world.Read<SceneEntityId>(entity).Value, world.Read<PrefabInstance>(entity).Prefab))
        .Order()];

static void CopyFolder(string from, string to, string? skip)
{
    Directory.CreateDirectory(to);
    foreach (string file in Directory.GetFiles(from))
    {
        File.Copy(file, Path.Combine(to, Path.GetFileName(file)));
    }
    foreach (string folder in Directory.GetDirectories(from).Where(folder => Path.GetFileName(folder) != skip))
    {
        CopyFolder(folder, Path.Combine(to, Path.GetFileName(folder)), skip);
    }
}

namespace Crucible.Samples.SceneRoundTrip
{
    /// <summary>A watcher's target: an entity reference into a prefab instance, saved with the scene.</summary>
    [Component, Serialized]
    public partial struct LookAt
    {
        public Entity Target;
    }

    internal sealed class Report
    {
        public int Passes { get; private set; }
        public int Failures { get; private set; }

        public void Check(bool passed, string what, string? detail = null)
        {
            Console.WriteLine($"  [{(passed ? "ok" : "FAILED")}] {what}{(passed || string.IsNullOrEmpty(detail) ? "" : $": {detail}")}");
            if (passed)
            {
                ++Passes;
            }
            else
            {
                ++Failures;
            }
        }
    }
}
