using System.Globalization;
using System.Numerics;
using Crucible.Assets;
using Crucible.Diagnostics;
using Crucible.Ecs;
using Crucible.Loop;
using Crucible.Physics2D;
using Crucible.Platform;
using Crucible.Render2D;
using Crucible.Scenes;
using Crucible.Serialization;

namespace Crossing;

// The M11 demonstration's game code (ADR 0114): a hero walked with the keys
// or a gamepad round an island, under a bridge and over it. FloorTriggers at
// the bridge's stairs switch the hero's floor: its sorting layer and its
// physics layer together, so on the ground it's drawn under the deck and held
// by the sunken road's walls, and on the bridge drawn over it and held by
// its railings.

/// <summary>Walks its entity with the keys (WASD, arrows) or a gamepad's left stick or d-pad, facing where it goes.</summary>
[Component, Serialized]
public partial struct Hero
{
    [Range(0.5f, 10), Tooltip("Metres a second.")]
    public float Speed;

    [Tooltip("Walking clips, by facing.")]
    public AssetId WalkDown, WalkLeft, WalkRight, WalkUp;

    [Tooltip("Standing clips, by facing.")]
    public AssetId IdleDown, IdleLeft, IdleRight, IdleUp;

    public Hero()
    {
        Speed = 4;
    }
}

/// <summary>A box (Size, metres, centred on it) that puts a Hero inside it on a floor: 0 the ground, 1 the bridge.</summary>
[Component, Serialized]
public partial struct FloorTrigger
{
    [Range(0, 1), Tooltip("0: the ground (drawn in Low, colliding as Ground); 1: the bridge (High, Bridge).")]
    public int Floor;

    public Vector2 Size;

    public FloorTrigger()
    {
        Size = new Vector2(1, 1);
    }
}

/// <summary>Keeps its entity on another's position (the pixel camera on the hero).</summary>
[Component, Serialized]
public partial struct Follow
{
    [EntityWith(typeof(Transform2D))]
    public Entity Target;
}

/// <summary>Turns its entity about its up axis (the rock out at sea).</summary>
[Component, Serialized]
public partial struct Spin
{
    [Range(-5, 5), Tooltip("Radians a second.")]
    public float RadiansPerSecond;
}

/// <summary>
/// Moves each Hero from its input: held keys and the gamepad, or, with the
/// game argument <c>walk-script=x,y[,seconds];…</c>, toward each point in
/// turn (for the check), as if steered there with the keys; gives up on a
/// point after its seconds (default 30), so a walk into a railing ends.
/// Sets its velocity, keeps it upright, and plays its clip for its facing.
/// With a script, logs each point reached or given up, the floors met under
/// and over the bridge, and quits when the script ends.
/// </summary>
[System(Phases.Update)]
public sealed class HeroSystem : GameSystem
{
    // SDL scancodes: where the keys are, whatever the layout.
    private const int A = 4, D = 7, S = 22, W = 26, Right = 79, Left = 80, Down = 81, Up = 82;

    private readonly HashSet<int> _held = [];
    private readonly HashSet<GamepadButton> _buttons = [];
    private Vector2 _stick;
    private List<(Vector2 Point, double Seconds)>? _script;
    private int _next;
    private double _onPoint;
    private Vector2 _facing = -Vector2.UnitY;
    private bool _underLogged, _overLogged;
    private float _lowestOnBridge = float.MaxValue;
    private Query? _heroes;

    public override void Start(FrameContext context)
    {
        _heroes = context.World.CreateQuery([Components.Of<Hero>(), Components.Of<Transform2D>()]);
        _held.Clear();
        _buttons.Clear();
        _stick = Vector2.Zero;
        _script = context.GameArguments.TryGetValue("walk-script", out string? script) ? Parse(script) : null;
        _next = 0;
        _onPoint = 0;
        _underLogged = _overLogged = false;
        _lowestOnBridge = float.MaxValue;
    }

    private static List<(Vector2, double)> Parse(string script) =>
    [
        .. script.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(point =>
        {
            float[] n = [.. point.Split(',').Select(v => float.Parse(v, CultureInfo.InvariantCulture))];
            return (new Vector2(n[0], n[1]), n.Length > 2 ? n[2] : 30.0);
        }),
    ];

    public override void Run(FrameContext context)
    {
        foreach (InputEvent e in context.Events)
        {
            switch (e.Type)
            {
                case InputEventType.KeyDown:
                    _held.Add(e.Code);
                    break;
                case InputEventType.KeyUp:
                    _held.Remove(e.Code);
                    break;
                case InputEventType.GamepadButtonDown:
                    _buttons.Add((GamepadButton)e.Code);
                    break;
                case InputEventType.GamepadButtonUp:
                    _buttons.Remove((GamepadButton)e.Code);
                    break;
                case InputEventType.GamepadAxis when e.Code == (int)GamepadAxis.LeftX:
                    _stick.X = e.X;
                    break;
                case InputEventType.GamepadAxis when e.Code == (int)GamepadAxis.LeftY:
                    _stick.Y = -e.X; // down is positive
                    break;
            }
        }
        World world = context.World;
        List<Entity> heroes = [];
        Collect collect = new() { Found = heroes };
        _heroes?.ForEachChunk(ref collect);
        foreach (Entity entity in heroes)
        {
            Vector2 position = world.Read<Transform2D>(entity).Position;
            Vector2 move = _script is null ? Held() : Scripted(context, position);
            Steer(world, entity, move);
            Watch(world, entity, position);
        }
    }

    // The keys and the gamepad: eight directions, the stick past a dead zone.
    private Vector2 Held()
    {
        Vector2 move = Vector2.Zero;
        bool Has(int a, int b, GamepadButton button) => _held.Contains(a) || _held.Contains(b) || _buttons.Contains(button);
        move.X += Has(D, Right, GamepadButton.DpadRight) ? 1 : 0;
        move.X -= Has(A, Left, GamepadButton.DpadLeft) ? 1 : 0;
        move.Y += Has(W, Up, GamepadButton.DpadUp) ? 1 : 0;
        move.Y -= Has(S, Down, GamepadButton.DpadDown) ? 1 : 0;
        if (move == Vector2.Zero && _stick.Length() > 0.3f)
        {
            move = _stick;
        }
        return move.Length() > 1 ? Vector2.Normalize(move) : move;
    }

    // Toward the script's point, until it's reached (or given up on).
    private Vector2 Scripted(FrameContext context, Vector2 position)
    {
        if (_next >= _script!.Count)
        {
            Log.Info(FormattableString.Invariant(
                $"Crossing.Route: done, the lowest point on the bridge {(_lowestOnBridge == float.MaxValue ? "none" : _lowestOnBridge.ToString("0.000", CultureInfo.InvariantCulture))}"));
            context.QuitRequested = true;
            _script = [];
            return Vector2.Zero;
        }
        (Vector2 point, double seconds) = _script[_next];
        _onPoint += context.DeltaSeconds;
        Vector2 to = point - position;
        bool reached = to.Length() < 0.15f;
        if (reached || _onPoint >= seconds)
        {
            Log.Info(FormattableString.Invariant(
                $"Crossing.Route: {(reached ? "reached" : "gave up on")} point {_next + 1} at ({position.X:0.00}, {position.Y:0.00})"));
            ++_next;
            _onPoint = 0;
            return Vector2.Zero;
        }
        return Vector2.Normalize(to);
    }

    // The hero's velocity, kept upright, and its clip for its facing.
    private void Steer(World world, Entity entity, Vector2 move)
    {
        Hero hero = world.Read<Hero>(entity);
        if (world.Has(entity, Components.Of<RigidBody2D>()))
        {
            ref RigidBody2D body = ref world.Get<RigidBody2D>(entity);
            body.LinearVelocity = move * hero.Speed;
            body.AngularVelocity = 0;
        }
        bool walking = move != Vector2.Zero;
        if (walking)
        {
            _facing = MathF.Abs(move.X) > MathF.Abs(move.Y) ? new Vector2(MathF.Sign(move.X), 0) : new Vector2(0, MathF.Sign(move.Y));
        }
        AssetId clip = (_facing.X, _facing.Y, walking) switch
        {
            ( > 0, _, true) => hero.WalkRight,
            ( < 0, _, true) => hero.WalkLeft,
            (_, > 0, true) => hero.WalkUp,
            (_, _, true) => hero.WalkDown,
            ( > 0, _, false) => hero.IdleRight,
            ( < 0, _, false) => hero.IdleLeft,
            (_, > 0, false) => hero.IdleUp,
            _ => hero.IdleDown,
        };
        if (world.Has(entity, Components.Of<SpriteAnimator>()) && world.Read<SpriteAnimator>(entity).Clip != clip)
        {
            world.Get<SpriteAnimator>(entity).Clip = clip;
        }
    }

    // Under or over the bridge where it crosses the sunken road: the floor the hero's on there, logged once each.
    private void Watch(World world, Entity entity, Vector2 position)
    {
        bool bridge = FloorSystem.FloorOf(world, entity) == 1;
        if (bridge)
        {
            _lowestOnBridge = MathF.Min(_lowestOnBridge, position.Y);
        }
        bool overRoad = position.X is >= -2 and < 2 && position.Y is >= -2 and < 2;
        if (overRoad && !bridge && !_underLogged)
        {
            _underLogged = true;
            Log.Info(FormattableString.Invariant($"Crossing.Under: under the deck on the ground at ({position.X:0.00}, {position.Y:0.00})"));
        }
        if (overRoad && bridge && !_overLogged)
        {
            _overLogged = true;
            Log.Info(FormattableString.Invariant($"Crossing.Over: over the road on the bridge at ({position.X:0.00}, {position.Y:0.00})"));
        }
    }

    public override void Stop(FrameContext context) => _heroes?.Dispose();

    internal struct Collect : IChunkJob
    {
        public List<Entity> Found;

        public readonly void Execute(in ChunkView chunk) => Found.AddRange(chunk.Entities);
    }
}

/// <summary>
/// Puts a Hero inside a FloorTrigger on its floor: the ground draws it in
/// the sorting layer Low and collides it as Ground; the bridge, High and
/// Bridge (bits 0 and 1 of the project's physics layers). Logs each change.
/// </summary>
[System(Phases.Update)]
public sealed class FloorSystem : GameSystem
{
    private Query? _heroes, _triggers;

    public override void Start(FrameContext context)
    {
        _heroes = context.World.CreateQuery([Components.Of<Hero>(), Components.Of<Transform2D>()]);
        _triggers = context.World.CreateQuery([Components.Of<FloorTrigger>(), Components.Of<Transform2D>()]);
    }

    /// <summary>A hero's floor, from its sorting layer: 1 in High, else 0.</summary>
    public static int FloorOf(World world, Entity hero) =>
        world.Has(hero, Components.Of<Crucible.Render2D.Sprite>()) &&
        world.Read<Crucible.Render2D.Sprite>(hero).SortingLayer == RuntimeSettings.Current.SortingLayer("High")
            ? 1
            : 0;

    public override void Run(FrameContext context)
    {
        World world = context.World;
        List<Entity> heroes = [], triggers = [];
        HeroSystem.Collect collect = new() { Found = heroes };
        _heroes?.ForEachChunk(ref collect);
        collect.Found = triggers;
        _triggers?.ForEachChunk(ref collect);
        foreach (Entity hero in heroes)
        {
            Vector2 at = world.Read<Transform2D>(hero).Position;
            foreach (Entity trigger in triggers)
            {
                FloorTrigger floor = world.Read<FloorTrigger>(trigger);
                Vector2 center = world.Read<Transform2D>(trigger).Position;
                Vector2 half = floor.Size * 0.5f;
                if (MathF.Abs(at.X - center.X) <= half.X && MathF.Abs(at.Y - center.Y) <= half.Y && FloorOf(world, hero) != floor.Floor)
                {
                    SetFloor(world, hero, floor.Floor);
                    Log.Info(FormattableString.Invariant(
                        $"Crossing.Floor: to the {(floor.Floor == 1 ? "bridge" : "ground")} at ({at.X:0.00}, {at.Y:0.00})"));
                }
            }
        }
    }

    /// <summary>A hero put on a floor: its sorting layer and its physics layer together.</summary>
    public static void SetFloor(World world, Entity hero, int floor)
    {
        if (world.Has(hero, Components.Of<Crucible.Render2D.Sprite>()))
        {
            world.Get<Crucible.Render2D.Sprite>(hero).SortingLayer =
                (uint)Math.Max(RuntimeSettings.Current.SortingLayer(floor == 1 ? "High" : "Low"), 0);
        }
        if (world.Has(hero, Components.Of<RigidBody2D>()))
        {
            ref RigidBody2D body = ref world.Get<RigidBody2D>(hero);
            body.Layers = 1u << floor;
            body.Mask = 1u << floor;
        }
    }

    public override void Stop(FrameContext context)
    {
        _heroes?.Dispose();
        _triggers?.Dispose();
    }
}

/// <summary>Moves each Follow's entity to its target's position, after the target has moved.</summary>
[System(Phases.LateUpdate)]
public sealed class FollowSystem : GameSystem
{
    private Query? _query;

    public override void Start(FrameContext context) =>
        _query = context.World.CreateQuery([Components.Of<Follow>(), Components.Of<Transform2D>()]);

    public override void Run(FrameContext context)
    {
        World world = context.World;
        List<Entity> found = [];
        HeroSystem.Collect collect = new() { Found = found };
        _query?.ForEachChunk(ref collect);
        foreach (Entity entity in found)
        {
            Entity target = world.Read<Follow>(entity).Target;
            if (target.Value != 0 && world.IsAlive(target) && world.Has(target, Components.Of<Transform2D>()))
            {
                world.Get<Transform2D>(entity).Position = world.Read<Transform2D>(target).Position;
            }
        }
    }

    public override void Stop(FrameContext context) => _query?.Dispose();
}

/// <summary>Turns each Spin's entity about its up axis.</summary>
[System(Phases.Update)]
public sealed class SpinSystem : GameSystem
{
    private Query? _query;

    public override void Start(FrameContext context) =>
        _query = context.World.CreateQuery([Components.Of<Spin>(), Components.Of<LocalTransform>()]);

    public override void Run(FrameContext context)
    {
        World world = context.World;
        List<Entity> found = [];
        HeroSystem.Collect collect = new() { Found = found };
        _query?.ForEachChunk(ref collect);
        foreach (Entity entity in found)
        {
            float angle = world.Read<Spin>(entity).RadiansPerSecond * (float)context.DeltaSeconds;
            ref LocalTransform transform = ref world.Get<LocalTransform>(entity);
            transform.Rotation = Quaternion.Normalize(Quaternion.CreateFromAxisAngle(Vector3.UnitY, angle) * transform.Rotation);
        }
    }

    public override void Stop(FrameContext context) => _query?.Dispose();
}
