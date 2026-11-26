using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using Crucible.Assets;
using Crucible.Assets.Importers;
using Crucible.Binary;
using Crucible.Ecs;
using Crucible.Importers;
using Crucible.Render;
using Crucible.Render2D;
using Crucible.Scenes;
using Crucible.UI;

namespace Crucible.Editor;

/// <summary>
/// A view with a small world of its own (ADR 0125), apart from the scene,
/// for a panel or workspace to show a model, a character, a sprite: what's
/// loaded into it (<see cref="Load"/>) or made in <see cref="World"/>, lit by
/// a default rig (<see cref="KeyLight"/>, <see cref="FillLight"/>,
/// <see cref="BackgroundColor"/>), seen by a camera that orbits (or, in 2D, pans
/// over the XY plane). The engine's animation runs on it each frame on its
/// own clock (<see cref="IsRunning"/>, <see cref="Speed"/>), so animators
/// play as in the game; with the clock off, it stays posed and can be
/// scrubbed (<c>Animator.SetTime</c>). Physics, audio, and the game's systems
/// don't run on it.
/// </summary>
/// <remarks>
/// Its world is freed by <see cref="Dispose"/>, which the editor calls when
/// the panel or workspace holding it closes (or its extension stops, which
/// closes those).
/// </remarks>
public sealed class PreviewView : UIElement, IDisposable
{
    private readonly Shell _shell;
    private World? _world;
    private SurfaceHandle _surface;
    private TextureHandle _texture;
    private bool _drawn;
    private uint _lastRun;
    private Vector2 _drag;
    private PointerButton _dragging;
    private readonly bool _listed;
    private SurfaceHandle _capture;

    public PreviewView() : this(Shell.Current ?? throw new InvalidOperationException("A PreviewView needs the editor running"))
    {
    }

    // Unlisted (thumbnails, ADR 0132): the shell neither ticks nor frees it; its owner does.
    internal PreviewView(Shell shell, bool listed = true)
    {
        _listed = listed;
        _shell = shell;
        _world = new World();
        IsFocusable = true;
        ClipToBounds = true;
        // In front of what's loaded (models face +Z), a little to its right.
        Camera.Pitch = 0.3f;
        Camera.Yaw = MathF.PI - 0.6f;
        Camera.Distance = 4;
        Camera.Pivot = new Vector3(0, 1, 0);
        MakeRig();
        if (listed)
        {
            shell.AddPreview(this);
        }
    }

    /// <summary>Its world: what it shows. Throws once it's disposed.</summary>
    public World World => _world ?? throw new ObjectDisposedException(nameof(PreviewView));

    /// <summary>Whether it's been disposed (its world freed).</summary>
    public bool IsDisposed => _world is null;

    /// <summary>The camera: it orbits (left drag), pans (middle drag), and dollies (the wheel).</summary>
    public EditorCamera Camera { get; } = new();

    /// <summary>
    /// 2D: an orthographic camera looking straight at the XY plane, x right
    /// and y up, panned (left or middle drag) and zoomed (the wheel); for
    /// sprites and tilemaps.
    /// </summary>
    public bool Is2D
    {
        get;
        set
        {
            field = value;
            if (value)
            {
                Camera.Yaw = 0;
                Camera.Pitch = 0;
            }
        }
    }

    /// <summary>In 2D, how many world units the view shows top to bottom.</summary>
    public float ViewHeight { get; set; } = 6;

    /// <summary>The clock: on, animators advance each frame; off, they hold where they are (scrub them with Animator.SetTime).</summary>
    public bool IsRunning { get; set; } = true;

    /// <summary>The clock's speed: 1 is real time.</summary>
    public float Speed { get; set; } = 1;

    /// <summary>Seconds the clock has run (at its speed) since it was made.</summary>
    public double Time { get; private set; }

    /// <summary>The default rig's key light: a directional light from above and to the side. Change or destroy it.</summary>
    public Entity KeyLight { get; private set; }

    /// <summary>The default rig's fill: an ambient light, sky and ground. Change or destroy it.</summary>
    public Entity FillLight { get; private set; }

    /// <summary>What's behind everything (sRGB).</summary>
    public Vector3 BackgroundColor { get; set; } = new(0.05f, 0.05f, 0.06f);

    /// <summary>
    /// Loads an asset into the world at the origin and returns its root: a
    /// model or prefab instanced as the scene would place it; a texture as a
    /// sprite; a tilemap as an entity per layer under a root. Null for
    /// anything else, or what can't be read.
    /// </summary>
    public Entity Load(AssetId asset)
    {
        World world = World;
        if (_shell.Database?.Find(asset) is not { } entry)
        {
            return Entity.Null;
        }
        try
        {
            if (entry.Importer is "Mesh" or "Fbx" || entry.Path.EndsWith(Prefab.Extension, StringComparison.OrdinalIgnoreCase))
            {
                return _shell.Prefabs is { } prefabs ? Prefab.Instantiate(world, asset, prefabs) : Entity.Null;
            }
            if (entry.Importer == "Texture")
            {
                Entity sprite = world.CreateEntity();
                world.Set(sprite, Transform2D.Identity);
                world.Set(sprite, new Sprite { Texture = asset });
                return sprite;
            }
            if (entry.Path.EndsWith(TilemapImporter.FileExtension, StringComparison.OrdinalIgnoreCase) &&
                _shell.Project?.FileOf(entry.Path) is { } file && File.Exists(file))
            {
                TilemapData data = TilemapImporter.Read(File.ReadAllBytes(file));
                Entity root = world.CreateEntity();
                world.Set(root, Transform2D.Identity);
                world.Set(root, new TilemapLayers { Map = asset });
                int order = 0;
                foreach (TileLayer layer in data.Layers)
                {
                    Entity drawn = world.CreateEntity();
                    world.Set(drawn, Transform2D.Identity);
                    world.Set(drawn, new Tilemap { Map = asset, Layer = (uint)layer.Id, OrderInLayer = order++ });
                    world.SetParent(drawn, root);
                }
                return root;
            }
        }
        catch (Exception exception) when (exception is System.Text.Json.JsonException or InvalidDataException or IOException)
        {
            _shell.Log($"The preview can't load {entry.Path}: {exception.Message}");
        }
        return Entity.Null;
    }

    /// <summary>
    /// The camera framing everything in the world but the rig: models by
    /// their meshes' bounds, sprites by their textures', the rest by where
    /// they are. In 2D the view's height fits them.
    /// </summary>
    public void Frame()
    {
        (Vector3 min, Vector3 max) = ContentBounds();
        Vector3 center = (min + max) * 0.5f;
        float radius = MathF.Max((max - min).Length() * 0.5f, 0.25f);
        if (Is2D)
        {
            Camera.Pivot = center with { Z = 0 };
            ViewHeight = MathF.Max(max.Y - min.Y, (max.X - min.X) * (ActualSize.Y > 0 ? ActualSize.Y / MathF.Max(ActualSize.X, 1) : 1)) * 1.2f + 0.5f;
            Camera.Distance = 50;
            return;
        }
        Camera.Frame(center, radius);
    }

    // What Frame fits, in world space: everything but the rig (a unit box around the origin for nothing).
    internal (Vector3 Min, Vector3 Max) ContentBounds()
    {
        World world = World;
        PlaceTransforms(world);
        Vector3 min = new(float.MaxValue), max = new(float.MinValue);
        foreach (Entity entity in SceneTree(world))
        {
            if (entity.Value == KeyLight.Value || entity.Value == FillLight.Value || !world.Has(entity, Components.Of<WorldTransform>()))
            {
                continue;
            }
            Matrix4x4 transform = world.Read<WorldTransform>(entity).Matrix;
            (Vector3 low, Vector3 high) = LocalBounds(world, entity);
            foreach (Vector3 corner in Corners(low, high))
            {
                Vector3 at = Vector3.Transform(corner, transform);
                min = Vector3.Min(min, at);
                max = Vector3.Max(max, at);
            }
        }
        if (min.X > max.X)
        {
            min = new Vector3(-1);
            max = new Vector3(1);
        }
        return (min, max);
    }

    /// <summary>Frees its world and its image; the editor does it when what holds it closes.</summary>
    public void Dispose()
    {
        if (_world is null)
        {
            return;
        }
        if (_listed)
        {
            _shell.RemovePreview(this);
        }
        if (_surface.Value != 0)
        {
            Crucible.Render.Api.DestroySurface(_surface);
            _surface = default;
        }
        if (_capture.Value != 0)
        {
            Crucible.Render.Api.DestroySurface(_capture);
            _capture = default;
        }
        _world.Dispose();
        _world = null;
        _drawn = false;
        InvalidateVisual();
    }

    // The rig a new world starts with.
    private void MakeRig()
    {
        World world = World;
        KeyLight = world.CreateEntity();
        world.Set(KeyLight, LocalTransform.Identity with { Rotation = Quaternion.CreateFromYawPitchRoll(MathF.PI - 0.9f, 0.7f, 0) });
        world.Set(KeyLight, new DirectionalLight { Intensity = 1.4f, ShadowDistance = 20 });
        FillLight = world.CreateEntity();
        world.Set(FillLight, LocalTransform.Identity);
        world.Set(FillLight, new AmbientLight { Intensity = 0.6f });
    }

    // Each frame (the shell): the clock and the engine's animation on the
    // world, then, while it's shown, drawn.
    internal void Tick(double deltaSeconds)
    {
        if (_world is not { } world)
        {
            return;
        }
        if (IsRunning)
        {
            float step = (float)(deltaSeconds * Speed);
            Time += step;
            ulong now = (ulong)(Stopwatch.GetTimestamp() * (1e9 / Stopwatch.Frequency));
            Crucible.Animation.Api.AdvancePoses(world.Live, step, now);
        }
        Crucible.Animation.Api.EvaluatePoses(world.Live);
        Crucible.Animation.Api.PlaceBoneAttachments(world.Live);
        PlaceTransforms(world);
        if (Root is { } root && Visibility == Visibility.Visible && ActualSize.X >= 1 && ActualSize.Y >= 1)
        {
            Render(world, root);
        }
    }

    private void Render(World world, UIRoot root)
    {
        uint width = (uint)MathF.Max(MathF.Round(ActualSize.X * root.Scale), 1);
        uint height = (uint)MathF.Max(MathF.Round(ActualSize.Y * root.Scale), 1);
        if (_surface.Value == 0)
        {
            _surface = Crucible.Render.Api.CreateOffscreenSurface(width, height);
            if (Crucible.Render.Api.SurfaceTexture(_surface, out _texture) < 0)
            {
                _shell.Log("The preview's image can't be drawn");
                return;
            }
            Crucible.Render.Api.SetSurfaceGrid(_surface, false);
        }
        else if (Crucible.Render.Api.ResizeOffscreenSurface(_surface, width, height) < 0)
        {
            return;
        }
        Camera camera = Is2D
            ? new Camera { Projection = CameraProjection.Orthographic, OrthographicHeight = ViewHeight, NearPlane = 0.1f, FarPlane = 1000 }
            : Camera.Camera;
        Crucible.Render.Api.SetSurfaceCamera(_surface, Camera.Position, Camera.Rotation, camera);
        Status rendered = Crucible.Render.Api.RenderFrame(_surface, world.Handle, _lastRun, BackgroundColor.X, BackgroundColor.Y, BackgroundColor.Z, 1);
        // What changes from now on is newer than this draw, as the frame
        // loop advances the scene's world after each system.
        _lastRun = world.CurrentVersion;
        world.AdvanceVersion();
        if (rendered < 0)
        {
            return;
        }
        _drawn = true;
        InvalidateVisual();
    }

    /// <summary>
    /// Draws it once into a square image of its own, on a background (linear),
    /// and reads it back as rows of B8G8R8A8 (thumbnails, ADR 0132). False
    /// if it can't be drawn.
    /// </summary>
    internal bool Capture(uint size, Vector3 background, byte[] bgra)
    {
        if (_world is not { } world)
        {
            return false;
        }
        try
        {
            if (_capture.Value == 0)
            {
                _capture = Crucible.Render.Api.CreateOffscreenSurface(size, size);
                Crucible.Render.Api.SetSurfaceGrid(_capture, false);
            }
            Camera camera = Is2D
                ? new Camera { Projection = CameraProjection.Orthographic, OrthographicHeight = ViewHeight, NearPlane = 0.1f, FarPlane = 1000 }
                : Camera.Camera;
            Crucible.Render.Api.SetSurfaceCamera(_capture, Camera.Position, Camera.Rotation, camera);
            Status rendered = Crucible.Render.Api.RenderFrame(_capture, world.Handle, _lastRun, background.X, background.Y, background.Z, 1);
            _lastRun = world.CurrentVersion;
            world.AdvanceVersion();
            if (rendered < 0)
            {
                return false;
            }
            Crucible.Render.Api.ReadSurfacePixels(_capture, bgra);
            return true;
        }
        catch (CrucibleException)
        {
            return false;
        }
    }

    // The engine's transform propagation, as its system does it for the
    // scene: entities with a LocalTransform or Transform2D and no
    // WorldTransform given one, then every WorldTransform placed.
    private static void PlaceTransforms(World world)
    {
        foreach (Entity entity in Missing(world, Components.Of<LocalTransform>(), null)
                     .Concat(Missing(world, Components.Of<Transform2D>(), Components.Of<LocalTransform>())))
        {
            world.Set(entity, new WorldTransform { Matrix = world.ComputeWorldTransform(entity) });
        }
        world.UpdateWorldTransforms();
    }

    private static List<Entity> Missing(World world, ComponentId local, ComponentId? alsoNot)
    {
        ComponentId[] excluded = alsoNot is { } other ? [Components.Of<WorldTransform>(), other] : [Components.Of<WorldTransform>()];
        using Query query = world.CreateQuery([local], excluded);
        Collector collector = new() { Found = [] };
        query.ForEachChunk(ref collector);
        return collector.Found;
    }

    protected override void OnDraw(Canvas canvas)
    {
        if (_drawn)
        {
            canvas.DrawImage(_texture, Vector2.Zero, ActualSize);
        }
    }

    protected override void OnPointerDown(PointerEvent e)
    {
        Focus();
        _dragging = e.Button;
        _drag = e.Position; // the root keeps the pointer on it until release
        e.Handled = true;
    }

    protected override void OnPointerMove(PointerEvent e)
    {
        if (_dragging == PointerButton.None)
        {
            return;
        }
        Vector2 delta = e.Position - _drag;
        _drag = e.Position;
        if (Is2D)
        {
            float perPixel = ViewHeight / MathF.Max(ActualSize.Y, 1);
            Camera.Pivot += new Vector3(-delta.X * perPixel, delta.Y * perPixel, 0);
        }
        else if (_dragging == PointerButton.Left)
        {
            Camera.Orbit(delta);
        }
        else
        {
            Camera.Pan(delta, ActualSize.Y);
        }
        e.Handled = true;
    }

    protected override void OnPointerUp(PointerEvent e)
    {
        _dragging = PointerButton.None;
        e.Handled = true;
    }

    protected override void OnPointerWheel(PointerEvent e)
    {
        if (Is2D)
        {
            ViewHeight = Math.Clamp(ViewHeight * MathF.Pow(0.85f, e.Wheel.Y), 0.1f, 10000);
        }
        else
        {
            Camera.Dolly(e.Wheel.Y);
        }
        e.Handled = true;
    }

    // Every entity of the world: those under no parent, and theirs.
    private static List<Entity> SceneTree(World world)
    {
        using Query query = world.CreateQuery([Components.Of<WorldTransform>()]);
        Collector collector = new() { Found = [] };
        query.ForEachChunk(ref collector);
        return collector.Found;
    }

    private struct Collector : IChunkJob
    {
        public List<Entity> Found;

        public readonly void Execute(in ChunkView chunk) => Found.AddRange(chunk.Entities);
    }

    // An entity's own extent: its mesh's bounds, its sprite's texture at its
    // pixels per unit, or a point.
    private (Vector3 Min, Vector3 Max) LocalBounds(World world, Entity entity)
    {
        if (world.Has(entity, Components.Of<MeshInstance>()) && MeshBounds(world.Read<MeshInstance>(entity)) is { } mesh)
        {
            return mesh;
        }
        if (world.Has(entity, Components.Of<Sprite>()) && SpriteSize(world.Read<Sprite>(entity).Texture) is { } size)
        {
            return (new Vector3(-size * 0.5f, 0), new Vector3(size * 0.5f, 0));
        }
        if (world.Has(entity, Components.Of<Tilemap>()) && TilemapBounds(world.Read<Tilemap>(entity)) is { } cells)
        {
            return cells;
        }
        return (Vector3.Zero, Vector3.Zero);
    }

    // A tilemap layer's painted cells (ADR 0132): from its source, in cells of its imported size.
    private (Vector3, Vector3)? TilemapBounds(Tilemap tilemap)
    {
        if (_shell.Database is not { } database || database.Find(tilemap.Map) is not { } entry ||
            database.ImportedFile(tilemap.Map) is not { } imported)
        {
            return null;
        }
        try
        {
            TilemapInfo info;
            using (ImportedFile file = ImportedFile.Open(imported, TilemapImporter.Kind, TilemapImporter.KindVersion))
            {
                info = MemoryMarshal.Read<TilemapInfo>(file.Read(file.Find(Tag.Of("INFO"))));
            }
            TilemapData data = TilemapImporter.Read(File.ReadAllBytes(database.Project.FileOf(entry.Path)));
            TileLayer? layer = data.Layers.FirstOrDefault(each => each.Id == tilemap.Layer);
            if (layer is null || layer.Cells.Count == 0)
            {
                return null;
            }
            int left = layer.Cells.Keys.Min(cell => cell.X), right = layer.Cells.Keys.Max(cell => cell.X) + 1;
            int bottom = layer.Cells.Keys.Min(cell => cell.Y), top = layer.Cells.Keys.Max(cell => cell.Y) + 1;
            return (new Vector3(left * info.CellWidth, bottom * info.CellHeight, 0), new Vector3(right * info.CellWidth, top * info.CellHeight, 0));
        }
        catch (Exception exception) when (exception is CrucibleException or IOException or InvalidDataException or System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private (Vector3, Vector3)? MeshBounds(MeshInstance instance)
    {
        if (_shell.Database?.ImportedFile(instance.Model) is not { } path)
        {
            return null;
        }
        try
        {
            using ImportedFile file = ImportedFile.Open(path, MeshImporter.Kind, MeshImporter.KindVersion);
            int meshes = file.Find(Tag.Of("MSHS"));
            if (meshes < 0)
            {
                return null;
            }
            ReadOnlySpan<MeshEntry> entries = MemoryMarshal.Cast<byte, MeshEntry>(file.Read(meshes));
            if (instance.Mesh < 0 || instance.Mesh >= entries.Length)
            {
                return null;
            }
            MeshEntry entry = entries[instance.Mesh];
            return (new Vector3(entry.BoundsMin[0], entry.BoundsMin[1], entry.BoundsMin[2]),
                new Vector3(entry.BoundsMax[0], entry.BoundsMax[1], entry.BoundsMax[2]));
        }
        catch (CrucibleException)
        {
            return null;
        }
    }

    private Vector2? SpriteSize(AssetId texture)
    {
        if (_shell.Database?.ImportedFile(texture) is not { } path)
        {
            return null;
        }
        try
        {
            using ImportedFile file = ImportedFile.Open(path, TextureImporter.Kind, TextureImporter.KindVersion);
            TextureInfo info = MemoryMarshal.Read<TextureInfo>(file.Read(file.Find(TextureImporter.InfoSection)));
            float perUnit = _shell.Database.SettingsOf(texture)?["PixelsPerUnit"]?.GetValue<float>() ?? 16;
            return new Vector2(info.Width, info.Height) / (perUnit > 0 ? perUnit : 16);
        }
        catch (Exception exception) when (exception is CrucibleException or InvalidOperationException)
        {
            return null;
        }
    }

    private static IEnumerable<Vector3> Corners(Vector3 min, Vector3 max)
    {
        for (int i = 0; i < 8; ++i)
        {
            yield return new Vector3((i & 1) != 0 ? max.X : min.X, (i & 2) != 0 ? max.Y : min.Y, (i & 4) != 0 ? max.Z : min.Z);
        }
    }
}
