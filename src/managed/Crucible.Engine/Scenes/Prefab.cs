using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Crucible.Assets;
using Crucible.Binary;
using Crucible.Ecs;
using Crucible.Serialization;

namespace Crucible.Scenes;

/// <summary>
/// Marks the root of a prefab instance (ADR 0045), so saving writes it back
/// as the prefab plus its changes. Removing it unpacks the instance into
/// plain entities. Not saved itself: the file format carries it.
/// </summary>
public struct PrefabInstance
{
    public AssetId Prefab;
}

/// <summary>
/// Prefabs (ADR 0021, 0045): a <c>.prefab</c> file is a scene with one root
/// entity; an imported model (a MESH file) is one too, one entity per node.
/// Instances keep the prefab's entities under IDs derived from the
/// instance's, and save only what they change. Names are placeholders
/// (docs/naming.md).
/// </summary>
public static class Prefab
{
    public const string Extension = ".prefab";

    /// <summary>
    /// Creates an instance of <paramref name="prefab"/> with a new ID, under
    /// <paramref name="parent"/> if given, and returns its root. A structural
    /// change: call it between frames.
    /// </summary>
    public static Entity Instantiate(World world, AssetId prefab, PrefabLibrary library, Entity parent = default)
    {
        EngineComponents.Register();
        SceneFile file = library.Get(prefab);
        SceneLoader loader = new(world, library);
        Frame frame = loader.Load(file, parent, outermost: false);
        ulong instance = PrefabIds.NewId();
        ulong rootId = file.Roots[0].Id;
        foreach (Frame.Entry entry in frame.Entries)
        {
            ulong id = entry.Id == rootId ? instance : PrefabIds.Derive(instance, entry.Id);
            world.Set(entry.Entity, new SceneEntityId { Value = id });
        }
        Entity root = frame.ToWorld(rootId);
        world.Set(root, new PrefabInstance { Prefab = prefab });
        return root;
    }

    /// <summary>
    /// Writes <paramref name="root"/> and its descendants as the prefab
    /// <paramref name="id"/>. Instances inside it are written as instances,
    /// which needs <paramref name="library"/>.
    /// </summary>
    public static byte[] Save(World world, Entity root, AssetId id, PrefabLibrary? library = null) =>
        SceneWriter.Write(world, [root], id, library);
}

/// <summary>
/// Reads prefabs and models for instancing (ADR 0045), each once, through a
/// function the host supplies: the asset database's in the editor. It
/// returns a <c>.prefab</c> file's bytes or a model's imported MESH file, or
/// null when there is no such asset.
/// </summary>
public sealed class PrefabLibrary(Func<AssetId, byte[]?> read)
{
    private readonly Dictionary<AssetId, SceneFile> _files = [];
    private readonly Lock _lock = new();

    /// <summary>Drops a cached prefab, after its file changed or its model was reimported.</summary>
    public void Forget(AssetId id)
    {
        lock (_lock)
        {
            _files.Remove(id);
        }
    }

    internal SceneFile Get(AssetId id)
    {
        lock (_lock)
        {
            if (_files.TryGetValue(id, out SceneFile? cached))
            {
                return cached;
            }
        }
        byte[] bytes = read(id) ?? throw new JsonException($"the prefab {id} can't be found");
        SceneFile file;
        if (ModelPrefab.IsModel(bytes))
        {
            file = ModelPrefab.Read(bytes, id);
        }
        else
        {
            file = SceneFile.Parse(bytes);
            if (file.Roots.Count != 1)
            {
                throw new JsonException($"the prefab {id} has {file.Roots.Count} root entities; a prefab has one");
            }
        }
        lock (_lock)
        {
            _files[id] = file;
        }
        return file;
    }
}

// How instance entities get their IDs (ADR 0045). Stable forever: files
// depend on these values without storing them.
internal static class PrefabIds
{
    public static ulong NewId() => (ulong)Random.Shared.NextInt64(2, long.MaxValue);

    /// <summary>The ID, in the file holding an instance, of the entity <paramref name="inner"/> of its prefab.</summary>
    public static ulong Derive(ulong instance, ulong inner) => NonZero(Mix(instance ^ Mix(inner ^ 0x9e3779b97f4a7c15)));

    /// <summary>A model node's ID in its model: a hash of its name path.</summary>
    public static ulong OfPath(string path)
    {
        ulong hash = 0xcbf29ce484222325; // FNV-1a
        foreach (byte value in Encoding.UTF8.GetBytes(path))
        {
            hash = (hash ^ value) * 0x100000001b3;
        }
        ulong id = Mix(hash);
        return id <= 1 ? id + 2 : id; // 1 is the model's root
    }

    // SplitMix64's finalizer.
    private static ulong Mix(ulong z)
    {
        z = (z ^ (z >> 30)) * 0xbf58476d1ce4e5b9;
        z = (z ^ (z >> 27)) * 0x94d049bb133111eb;
        return z ^ (z >> 31);
    }

    private static ulong NonZero(ulong id) => id == 0 ? 1 : id;
}

// An imported model as a prefab (ADR 0045): a root, then one entity per
// node with its LocalTransform and, if it has one, its MeshInstance.
internal static class ModelPrefab
{
    public static readonly uint Kind = Tag.Of("MESH");
    public const uint KindVersion = 1;
    public const ulong RootId = 1;

    // The MESH file's MeshNode (src/native/importers/include/crucible/importers/Mesh.h).
    [StructLayout(LayoutKind.Sequential)]
    private struct Node
    {
        public int Parent;
        public int Mesh;
        public float Tx, Ty, Tz;
        public float Rx, Ry, Rz, Rw;
        public float Sx, Sy, Sz;
        public uint NameOffset;
        public uint NameLength;
    }

    public static bool IsModel(ReadOnlySpan<byte> bytes) => bytes.StartsWith("CRBN"u8);

    public static SceneFile Read(byte[] bytes, AssetId model)
    {
        using ImportedFile file = ImportedFile.Parse(bytes, Kind, KindVersion);
        Node[] nodes = MemoryMarshal.Cast<byte, Node>(file.Read(file.Find(Tag.Of("NODE")))).ToArray();
        byte[] names = file.Read(file.Find(Tag.Of("NAME")));

        SceneFile scene = new() { Id = model };
        EntityNode root = new() { Id = RootId };
        root.Components.Add(Component(LocalTransform.Identity));
        scene.Roots.Add(root);

        EntityNode[] entities = new EntityNode[nodes.Length];
        string[] paths = new string[nodes.Length];
        HashSet<ulong> ids = [RootId];
        Dictionary<(EntityNode Parent, string Name), int> seen = [];
        // Parents first, whatever order the file lists nodes in.
        foreach (int index in ParentsFirst(nodes))
        {
            Node node = nodes[index];
            EntityNode parent = node.Parent < 0 ? root : entities[node.Parent];
            string name = Encoding.UTF8.GetString(names, (int)node.NameOffset, (int)node.NameLength);
            string prefix = node.Parent < 0 ? "" : paths[node.Parent] + "/";
            // The k-th sibling with a name already taken is "name#k".
            int repeat = seen.GetValueOrDefault((parent, name));
            seen[(parent, name)] = repeat + 1;
            string path = prefix + (repeat == 0 ? name : $"{name}#{repeat}");
            paths[index] = path;
            ulong id = PrefabIds.OfPath(path);
            if (!ids.Add(id))
            {
                throw new JsonException($"the model {model}'s node {path} has the same ID as another");
            }
            EntityNode entity = new() { Id = id };
            entity.Components.Add(Component(new LocalTransform
            {
                Position = new(node.Tx, node.Ty, node.Tz),
                Rotation = new(node.Rx, node.Ry, node.Rz, node.Rw),
                Scale = new(node.Sx, node.Sy, node.Sz),
            }));
            if (node.Mesh >= 0)
            {
                entity.Components.Add(Component(new MeshInstance { Model = model, Mesh = node.Mesh }));
            }
            entities[index] = entity;
            parent.Children.Add(entity);
        }
        return scene;
    }

    private static IEnumerable<int> ParentsFirst(Node[] nodes)
    {
        int[] depth = new int[nodes.Length];
        for (int i = 0; i < nodes.Length; ++i)
        {
            for (int p = nodes[i].Parent, steps = 0; p >= 0; p = nodes[p].Parent)
            {
                if (++steps > nodes.Length || p >= nodes.Length)
                {
                    throw new JsonException("a model's node hierarchy has a cycle or a bad parent");
                }
                ++depth[i];
            }
        }
        return Enumerable.Range(0, nodes.Length).OrderBy(i => depth[i]).ThenBy(i => i);
    }

    private static ComponentJson Component<T>(T value) where T : IJsonSerialized<T> =>
        new(Components.NameOf(typeof(T)), Json.Serialize(value));
}
