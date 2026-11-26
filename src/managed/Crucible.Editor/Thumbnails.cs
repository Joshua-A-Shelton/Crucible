using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Crucible.Assets;
using Crucible.Binary;
using Crucible.Ecs;
using Crucible.Render;
using Crucible.Render2D;
using Crucible.Scenes;

namespace Crucible.Editor;

/// <summary>
/// The Assets panel's rendered thumbnails (ADR 0132): models, prefabs,
/// materials (on a sphere, through a MaterialOverride), and tilemaps, drawn
/// once in a hidden preview world, from the front-left and a little above,
/// on a clear background. Each is cached in the project's
/// <c>Cache/Thumbnails/</c> by a key made from its import and those of what it
/// names, so a reimport renders it again and a restart doesn't. Tiles
/// acquire what they show; one thumbnail renders at a time, in the order
/// they were asked for, once everything it draws has loaded.
/// </summary>
internal sealed partial class Thumbnails : IDisposable
{
    /// <summary>The rendered image's size, sharp at the largest tile.</summary>
    public const int Size = 256;

    /// <summary>The editor's sphere materials are shown on (EditorAssets/Sphere.gltf, from scripts/make-sphere.py).</summary>
    public static readonly AssetId Sphere = AssetId.Parse("04983ea0-f255-40c3-9cbb-c56692dee376");

    // Changes whenever thumbnails would look different, so old ones render again.
    private const string Look = "1";

    // A thumbnail whose loads haven't finished by then is drawn as it is.
    private const double LoadSeconds = 10;

    // Frames drawn before capturing, so the renderer has resolved and placed everything.
    private const int WarmFrames = 2;

    private readonly Shell _shell;
    private readonly Dictionary<string, Thumbnail> _byKey = new(StringComparer.Ordinal);
    private readonly List<Thumbnail> _queue = [];
    private Job? _job;

    public Thumbnails(Shell shell) => _shell = shell;

    /// <summary>Where the editor's own assets are: EditorAssets beside its Fonts folder.</summary>
    public static string EditorAssetsFolder() => Path.Combine(Path.GetDirectoryName(Shell.FontsFolder())!, "EditorAssets");

    /// <summary>Whether a kind gets a rendered thumbnail (a texture and a tileset show their image instead).</summary>
    public static bool Renders(string kind) => kind is "Model" or "Prefab" or "Material" or "Tilemap";

    /// <summary>Thumbnails rendered since the editor started (for the checks).</summary>
    public int Rendered { get; private set; }

    /// <summary>Thumbnails read from the cache since the editor started (for the checks).</summary>
    public int ReadFromCache { get; private set; }

    /// <summary>Whether anything is queued or rendering.</summary>
    public bool IsBusy => _job is not null || _queue.Count > 0;

    /// <summary>The cache folder of the project open.</summary>
    public string? Folder => _shell.Project is { } project ? Path.Combine(project.CacheFolder, "Thumbnails") : null;

    /// <summary>
    /// An asset's thumbnail, shared by every tile showing it; null for one
    /// that doesn't render or isn't imported. Its texture arrives later if
    /// it isn't cached: <see cref="Thumbnail.Ready"/> says when.
    /// </summary>
    public Thumbnail? Acquire(AssetEntry asset, string kind)
    {
        if (!Renders(kind) || KeyOf(asset.Id) is not { } key)
        {
            return null;
        }
        if (_byKey.TryGetValue(key, out Thumbnail? shared))
        {
            ++shared.Uses;
            return shared;
        }
        Thumbnail thumbnail = new(asset.Id, kind, key) { Uses = 1 };
        _byKey[key] = thumbnail;
        if (!Read(thumbnail))
        {
            _queue.Add(thumbnail);
        }
        return thumbnail;
    }

    public void Release(Thumbnail thumbnail)
    {
        if (--thumbnail.Uses > 0)
        {
            return;
        }
        // One from before a Clear is already forgotten.
        if (_byKey.TryGetValue(thumbnail.Key, out Thumbnail? kept) && kept == thumbnail)
        {
            _byKey.Remove(thumbnail.Key);
        }
        _queue.Remove(thumbnail);
        if (_job?.Thumbnail == thumbnail)
        {
            _job.Dispose();
            _job = null;
        }
        thumbnail.Unload();
    }

    /// <summary>Each frame: textures that have become ready, then a step of the thumbnail being rendered.</summary>
    public void Update()
    {
        foreach (Thumbnail thumbnail in _byKey.Values)
        {
            thumbnail.Poll();
        }
        if (_job is null && _queue.Count > 0)
        {
            Thumbnail next = _queue[0];
            _queue.RemoveAt(0);
            _job = Start(next);
        }
        if (_job is not null && _job.Step(this))
        {
            _job.Dispose();
            _job = null;
        }
    }

    /// <summary>Forgets every thumbnail (a project closing): tiles acquire again.</summary>
    public void Clear()
    {
        _job?.Dispose();
        _job = null;
        _queue.Clear();
        foreach (Thumbnail thumbnail in _byKey.Values)
        {
            thumbnail.Unload();
        }
        _byKey.Clear();
    }

    public void Dispose() => Clear();

    // -----------------------------------------------------------------------
    // Keys
    // -----------------------------------------------------------------------

    /// <summary>
    /// The thumbnail's key: its import's (a prefab's own bytes), and those of
    /// every asset it names, all the way down, so a texture changing renders
    /// its model's thumbnail again. Null if it isn't imported yet.
    /// </summary>
    internal string? KeyOf(AssetId asset)
    {
        if (_shell.Database is not { } database)
        {
            return null;
        }
        StringBuilder parts = new(Look);
        HashSet<AssetId> seen = [];
        List<AssetId> pending = [asset];
        while (pending.Count > 0)
        {
            AssetId id = pending[^1];
            pending.RemoveAt(pending.Count - 1);
            if (!seen.Add(id) || database.Find(id) is not { } entry)
            {
                continue;
            }
            if (entry.Path.EndsWith(Prefab.Extension, StringComparison.OrdinalIgnoreCase))
            {
                // Prefabs aren't imported: their bytes, and what they name by ID.
                string file = database.Project.FileOf(entry.Path);
                if (!File.Exists(file))
                {
                    return null;
                }
                byte[] bytes = File.ReadAllBytes(file);
                parts.Append('|').Append(Convert.ToHexString(SHA256.HashData(bytes)));
                foreach (Match match in Guids().Matches(Encoding.UTF8.GetString(bytes)))
                {
                    if (AssetId.TryParse(match.Value, out AssetId named))
                    {
                        pending.Add(named);
                    }
                }
                continue;
            }
            if (database.ImportedFile(id) is not { } imported)
            {
                if (id == asset)
                {
                    return null;
                }
                continue; // something it names that doesn't import draws as missing, as in the scene
            }
            parts.Append('|').Append(Path.GetFileNameWithoutExtension(imported));
            pending.AddRange(database.ReferencesOf(id));
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(parts.ToString())))[..32].ToLowerInvariant();
    }

    [GeneratedRegex("[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}")]
    private static partial Regex Guids();

    private string? FileOf(Thumbnail thumbnail) => Folder is { } folder ? Path.Combine(folder, thumbnail.Key + ".crtex") : null;

    // A cached thumbnail, loaded; false if there's none (or it doesn't load).
    private bool Read(Thumbnail thumbnail)
    {
        if (FileOf(thumbnail) is not { } file || !File.Exists(file))
        {
            return false;
        }
        try
        {
            thumbnail.Load(File.ReadAllBytes(file));
            ++ReadFromCache;
            return true;
        }
        catch (Exception exception) when (exception is IOException or CrucibleException)
        {
            return false;
        }
    }

    // -----------------------------------------------------------------------
    // Rendering
    // -----------------------------------------------------------------------

    private Job? Start(Thumbnail thumbnail)
    {
        PreviewView preview = new(_shell, listed: false) { IsRunning = false };
        World world = preview.World;
        Entity root = preview.Load(thumbnail.Kind == "Material" ? Sphere : thumbnail.Asset);
        if (root.Value == Entity.Null.Value)
        {
            preview.Dispose();
            return null;
        }
        if (thumbnail.Kind == "Material")
        {
            foreach (Entity entity in With<MeshInstance>(world))
            {
                world.Set(entity, new MaterialOverride { Material = thumbnail.Asset });
            }
        }
        // Drawn in 2D if nothing in it is a mesh: a tilemap, a prefab of sprites.
        preview.Is2D = With<MeshInstance>(world).Count == 0;
        return new Job(thumbnail, preview);
    }

    private static List<Entity> With<T>(World world) where T : unmanaged
    {
        using Query query = world.CreateQuery([Components.Of<T>()]);
        Collector collector = new() { Found = [] };
        query.ForEachChunk(ref collector);
        return collector.Found;
    }

    private struct Collector : IChunkJob
    {
        public List<Entity> Found;

        public readonly void Execute(in ChunkView chunk) => Found.AddRange(chunk.Entities);
    }

    // What's captured, written to the cache and loaded.
    private void Finish(Thumbnail thumbnail, byte[] rgba)
    {
        try
        {
            using ImportedFileWriter writer = new(TextureFileKind, 1);
            TextureInfo info = new() { Format = TextureFormat.Rgba8, Width = Size, Height = Size, MipCount = 0, Srgb = 1 };
            List<byte[]> mips = Mips(rgba);
            info.MipCount = (uint)mips.Count;
            writer.Add(TextureInfoSection, 1, MemoryMarshal.AsBytes(new ReadOnlySpan<TextureInfo>(in info)));
            for (int mip = 0; mip < mips.Count; ++mip)
            {
                writer.Add(MipSection(mip), 1, mips[mip]);
            }
            if (FileOf(thumbnail) is { } file)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                // Written beside it, then moved: a half-written thumbnail is never read.
                string partial = file + ".tmp-" + Environment.ProcessId;
                writer.Write(partial, Compression.Fast);
                File.Move(partial, file, overwrite: true);
                thumbnail.Load(File.ReadAllBytes(file));
            }
            ++Rendered;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or CrucibleException)
        {
            _shell.Log($"Can't keep a thumbnail: {exception.Message}");
        }
    }

    // The texture file the Texture importer writes (TXTR: INFO, then a MIPn per level).
    private static readonly uint TextureFileKind = Tag.Of("TXTR");
    private static readonly uint TextureInfoSection = Tag.Of("INFO");

    private static uint MipSection(int mip) => Tag.Of($"MIP{mip:X}");

    // Every level down to 4x4, each a box filter of the one above, weighted
    // by alpha so the clear background doesn't darken edges.
    private static List<byte[]> Mips(byte[] rgba)
    {
        List<byte[]> mips = [rgba];
        int size = Size;
        while (size > 4)
        {
            byte[] above = mips[^1];
            int half = size / 2;
            byte[] below = new byte[half * half * 4];
            for (int y = 0; y < half; ++y)
            {
                for (int x = 0; x < half; ++x)
                {
                    Vector3 color = Vector3.Zero;
                    float alpha = 0;
                    for (int i = 0; i < 4; ++i)
                    {
                        int at = ((y * 2 + i / 2) * size + x * 2 + i % 2) * 4;
                        float a = above[at + 3] / 255f;
                        color += new Vector3(ToLinear(above[at]), ToLinear(above[at + 1]), ToLinear(above[at + 2])) * a;
                        alpha += a;
                    }
                    int to = (y * half + x) * 4;
                    if (alpha > 0)
                    {
                        color /= alpha;
                    }
                    below[to] = ToSrgb(color.X);
                    below[to + 1] = ToSrgb(color.Y);
                    below[to + 2] = ToSrgb(color.Z);
                    below[to + 3] = (byte)MathF.Round(alpha / 4 * 255);
                }
            }
            mips.Add(below);
            size = half;
        }
        return mips;
    }

    private static float ToLinear(byte srgb)
    {
        float c = srgb / 255f;
        return c <= 0.04045f ? c / 12.92f : MathF.Pow((c + 0.055f) / 1.055f, 2.4f);
    }

    private static byte ToSrgb(float linear)
    {
        linear = Math.Clamp(linear, 0, 1);
        float c = linear <= 0.0031308f ? linear * 12.92f : 1.055f * MathF.Pow(linear, 1 / 2.4f) - 0.055f;
        return (byte)MathF.Round(c * 255);
    }

    /// <summary>
    /// The thumbnail on a clear background, from two captures: on black and
    /// on white. Where they agree the pixel is opaque; how far apart they
    /// are says how much background shows through. Straight RGBA, sRGB.
    /// </summary>
    internal static byte[] Unmatte(byte[] onBlack, byte[] onWhite)
    {
        int count = Size * Size;
        // The white as it came out (post-processing may have changed it), from a corner.
        Vector3 white = new(ToLinear(onWhite[2]), ToLinear(onWhite[1]), ToLinear(onWhite[0]));
        float whiteLevel = MathF.Max((white.X + white.Y + white.Z) / 3, 1e-3f);
        byte[] rgba = new byte[count * 4];
        for (int i = 0; i < count; ++i)
        {
            int at = i * 4;
            Vector3 black = new(ToLinear(onBlack[at + 2]), ToLinear(onBlack[at + 1]), ToLinear(onBlack[at]));
            Vector3 lit = new(ToLinear(onWhite[at + 2]), ToLinear(onWhite[at + 1]), ToLinear(onWhite[at]));
            Vector3 through = lit - black;
            float alpha = Math.Clamp(1 - (through.X + through.Y + through.Z) / 3 / whiteLevel, 0, 1);
            Vector3 color = alpha > 1 / 255f ? black / alpha : Vector3.Zero;
            rgba[at] = ToSrgb(color.X);
            rgba[at + 1] = ToSrgb(color.Y);
            rgba[at + 2] = ToSrgb(color.Z);
            rgba[at + 3] = (byte)MathF.Round(alpha * 255);
        }
        return rgba;
    }

    // One thumbnail being rendered: its world loading, a few frames drawn,
    // then captured on black and on white.
    private sealed class Job(Thumbnail thumbnail, PreviewView preview) : IDisposable
    {
        private readonly long _started = Stopwatch.GetTimestamp();
        private int _warm;
        private bool _framed;

        public Thumbnail Thumbnail { get; } = thumbnail;

        // A frame's worth; true once it's done (or given up).
        public bool Step(Thumbnails owner)
        {
            World world = preview.World;
            preview.Tick(0);
            bool loading = Crucible.Assets.Api.StartLoads(world.Live) > 0;
            if (loading && Stopwatch.GetElapsedTime(_started).TotalSeconds < LoadSeconds)
            {
                return false;
            }
            if (!_framed)
            {
                // Its bounds are known once its meshes have loaded.
                if (!preview.Is2D)
                {
                    preview.Camera.Pitch = 0.35f;
                    preview.Camera.Yaw = MathF.PI + 0.6f;
                }
                preview.Frame();
                if (!preview.Is2D)
                {
                    Fit(preview);
                }
                _framed = true;
            }
            byte[] onBlack = new byte[Size * Size * 4], onWhite = new byte[Size * Size * 4];
            if (_warm < WarmFrames)
            {
                ++_warm;
                preview.Capture(Size, Vector3.Zero, onBlack);
                return false;
            }
            if (preview.Capture(Size, Vector3.Zero, onBlack) && preview.Capture(Size, Vector3.One, onWhite))
            {
                owner.Finish(Thumbnail, Unmatte(onBlack, onWhite));
            }
            return true;
        }

        public void Dispose() => preview.Dispose();

        // Frame fits a sphere around it, loose for anything long or flat:
        // instead, just far enough back that every corner of its bounds is
        // inside the image, with a margin.
        private static void Fit(PreviewView preview)
        {
            const float Margin = 0.9f;
            (Vector3 min, Vector3 max) = preview.ContentBounds();
            EditorCamera camera = preview.Camera;
            camera.Pivot = (min + max) * 0.5f;
            float reach = MathF.Tan(camera.FieldOfViewY * 0.5f) * Margin;
            float distance = 0;
            for (int i = 0; i < 8; ++i)
            {
                Vector3 corner = new((i & 1) != 0 ? max.X : min.X, (i & 2) != 0 ? max.Y : min.Y, (i & 4) != 0 ? max.Z : min.Z);
                Vector3 offset = corner - camera.Pivot;
                float across = MathF.Max(MathF.Abs(Vector3.Dot(offset, camera.Right)), MathF.Abs(Vector3.Dot(offset, camera.Up)));
                // Its depth toward the camera, plus what its offset needs at this field of view.
                distance = MathF.Max(distance, -Vector3.Dot(offset, camera.Forward) + across / reach);
            }
            camera.Distance = MathF.Max(distance, 0.1f);
        }
    }
}

/// <summary>One asset's thumbnail (ADR 0132), shared by the tiles showing it.</summary>
internal sealed class Thumbnail(AssetId asset, string kind, string key)
{
    private TextureHandle _texture;
    private bool _ready;

    public AssetId Asset { get; } = asset;

    public string Kind { get; } = kind;

    public string Key { get; } = key;

    internal int Uses { get; set; }

    /// <summary>The image, once ready; default before.</summary>
    public TextureHandle Texture => _ready ? _texture : default;

    /// <summary>When the image becomes ready to draw.</summary>
    public event Action? Ready;

    internal void Load(byte[] file)
    {
        Unload();
        _texture = Crucible.Render.Api.LoadTexture(file);
        Poll();
    }

    internal void Poll()
    {
        if (!_ready && _texture.Generation != 0 && Crucible.Render.Api.IsTextureReady(_texture))
        {
            _ready = true;
            Ready?.Invoke();
        }
    }

    internal void Unload()
    {
        if (_texture.Generation != 0)
        {
            Crucible.Render.Api.UnloadTexture(_texture);
        }
        _texture = default;
        _ready = false;
    }
}
