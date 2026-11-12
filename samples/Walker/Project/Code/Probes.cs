using System.Numerics;
using Crucible.Ecs;
using Crucible.Loop;
using Crucible.Physics;
using Crucible.Scenes;
using Crucible.Serialization;

namespace Walker;

// Queries.crscene's game code (ADR 0090): probes that ask the physics world
// what's there each frame while it plays, drawn by View > Queries (green, red
// where they hit). Move a probe, or what it points at, and watch it change.

public enum ProbeKind
{
    Ray,     // the first hit along a line
    Sphere,  // a sphere swept along the line
    Box,     // a box swept along the line, turned as the probe is
    Overlap, // what's inside a sphere at the probe
}

/// <summary>Asks the physics world a question from where its entity is, every frame while playing.</summary>
[Component, Serialized]
public partial struct Probe
{
    public ProbeKind Kind;

    [Tooltip("Which way it looks, in the world (normalized).")]
    public Vector3 Direction;

    [Range(0, 50), Tooltip("How far it looks, in metres.")]
    public float Distance;

    [Range(0.05f, 5), Tooltip("A sphere's or an overlap's radius, a box's width.")]
    public float Size;

    [Tooltip("Whether it sees trigger colliders.")]
    public bool Triggers;

    [Tooltip("Written while playing: how far away it hit, or -1 for nothing (an overlap: how many it found).")]
    public float Result;

    public Probe()
    {
        Direction = -Vector3.UnitY;
        Distance = 10;
        Size = 0.5f;
        Result = -1;
    }
}

/// <summary>Runs every probe's query, writing what it found into its <see cref="Probe.Result"/>.</summary>
[System(Phases.Update)]
public sealed class ProbeSystem : GameSystem
{
    private Query? _probes;
    private bool _report;
    private double _reportAt;

    public override void Start(FrameContext context)
    {
        _probes = context.World.CreateQuery([Components.Of<Probe>(), Components.Of<WorldTransform>()]);
        // --game-arg probes=<seconds>: print each probe's result once, then, headless, quit.
        _report = double.TryParse(context.GameArguments.GetValueOrDefault("probes"),
            System.Globalization.CultureInfo.InvariantCulture, out _reportAt);
    }

    public override void Run(FrameContext context)
    {
        World world = context.World;
        Ask job = new() { World = world };
        _probes?.ForEachChunk(ref job);
        if (!_report || (_reportAt -= context.DeltaSeconds) > 0)
        {
            return;
        }
        _report = false;
        Collect collect = new() { Found = [] };
        _probes?.ForEachChunk(ref collect);
        foreach (Entity probe in collect.Found)
        {
            string name = world.Has(probe, Components.Of<EntityName>()) ? world.GetManaged<EntityName>(probe).Value : "";
            Console.WriteLine(FormattableString.Invariant($"Walker.Probes: {name}: {world.Read<Probe>(probe).Result:0.00}"));
        }
        context.QuitRequested = true;
    }

    private struct Collect : IChunkJob
    {
        public List<Entity> Found;

        public readonly void Execute(in ChunkView chunk) => Found.AddRange(chunk.Entities);
    }

    private struct Ask : IChunkJob
    {
        public World World;

        public readonly void Execute(in ChunkView chunk)
        {
            ReadOnlySpan<Entity> entities = chunk.Entities;
            Span<Probe> probes = chunk.Write<Probe>();
            ReadOnlySpan<WorldTransform> placed = chunk.Read<WorldTransform>();
            Span<Entity> found = stackalloc Entity[16];
            for (int i = 0; i < entities.Length; ++i)
            {
                ref Probe probe = ref probes[i];
                Matrix4x4 matrix = placed[i].Matrix;
                Vector3 at = matrix.Translation;
                Quaternion turn = Quaternion.CreateFromRotationMatrix(matrix);
                Vector3 direction = probe.Direction.LengthSquared() > 0 ? probe.Direction : -Vector3.UnitY;
                QueryFilter filter = QueryFilter.Default with { Ignore = entities[i], Triggers = probe.Triggers };
                RayHit hit = probe.Kind switch
                {
                    ProbeKind.Ray => Crucible.Physics.Physics.Raycast(World, at, direction, probe.Distance, filter),
                    ProbeKind.Sphere => Crucible.Physics.Physics.ShapeCast(World,
                        new QueryShape { Kind = ShapeKind.Sphere, Size = new Vector3(probe.Size, 0, 0) }, at, Quaternion.Identity,
                        direction, probe.Distance, filter),
                    ProbeKind.Box => Crucible.Physics.Physics.ShapeCast(World,
                        new QueryShape { Kind = ShapeKind.Box, Size = new Vector3(probe.Size) }, at, turn, direction,
                        probe.Distance, filter),
                    _ => default,
                };
                probe.Result = probe.Kind == ProbeKind.Overlap
                    ? Crucible.Physics.Physics.Overlap(World,
                        new QueryShape { Kind = ShapeKind.Sphere, Size = new Vector3(probe.Size, 0, 0) }, at, turn, found, filter)
                    : hit.Hit ? hit.Distance : -1;
            }
        }
    }

    public override void Stop(FrameContext context) => _probes?.Dispose();
}
