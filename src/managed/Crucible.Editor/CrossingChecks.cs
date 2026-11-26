using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;
using Crucible.Assets;
using Crucible.Ecs;
using Crucible.Loop;
using Crucible.Render2D;
using Crucible.Scenes;
using Crucible.UI;
using H = Crucible.Editor.HierarchyChecks;
using Png = Crucible.Samples.Diorama.Png;

namespace Crucible.Editor;

/// <summary>
/// The M11 demonstration checking itself (ADR 0114), for CTest's
/// samples.crossing: run with the Crossing sample and <c>--game-arg
/// check-crossing=&lt;folder&gt;</c>. On a copy of the sample: the scene run
/// standalone, headless, at a fixed 60 fps, the hero walked by a script
/// (held by the bank, through a ramp into the sunken road, under the bridge,
/// out the other ramp, up the east stairs onto the bridge, over the road,
/// into a railing, down the west stairs), its floors and positions read from
/// the game's log; then the scene drawn in the editor into images of
/// 640x360, 1280x720, 1920x1080, and 1000x700: the 2D exactly its 320x180
/// image scaled up by whole pixels (black bars round the last), the sea
/// showing round the island, a lamp's light, and the hero drawn under the
/// deck on the ground and over it on the bridge, its head going under the
/// deck first coming up the road, and drawn over the railing behind it on
/// the stairs. The images are saved as
/// crossing-&lt;width&gt;.png.
/// </summary>
[System(Phases.RenderPrepare)]
internal sealed partial class CrossingChecks : GameSystem
{
    private const int VirtualWidth = 320, VirtualHeight = 180, PixelsPerUnit = 16;

    private IEnumerator<int>? _script;
    private int _wait;
    private int _checks;
    private int _failures;
    private string _folder = "";

    public override void Start(FrameContext context)
    {
        if (!context.GameArguments.TryGetValue("check-crossing", out string? folder))
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
            ? $"CrossingChecks: all {_checks} checks passed"
            : $"CrossingChecks: {_failures} of {_checks} checks failed");
        context.QuitRequested = true;
    }

    // The walk: points (x, y[, seconds to try]) from the hero's start at (-12.5, -8).
    private const string Walk =
        "-6,-2.5,3;" +            // 1: north into the bank's face: held below it
        "-4.5,-8;-0.5,-8;" +      // 2, 3: east, through the south ramp into the sunken road
        "-0.5,-2.4;" +            // 4: north to just short of the deck: drawn Under it already
        "-0.5,6;0,7.8;3.5,7.8;" + // 5-7: on under the bridge, out through the north ramp
        "11,3;11,0.44;9,0.44;" +  // 8-10: round to the east stairs, partway up them: still on the ground, in Low
        "7,0;-0.5,0;" +           // 11, 12: on up onto the bridge, and over the road
        "-0.5,-2.5,3;" +          // 13: south into the railing: held by it
        "-8.5,0;-12,0";           // 14, 15: west, down the west stairs

    private IEnumerable<int> Script()
    {
        Shell shell = Shell.Current ?? throw new InvalidOperationException("No editor shell");
        string source = Path.GetDirectoryName(AssetChecks.SourceProjectFile(shell)) ?? "";
        string copy = Path.Combine(Path.GetFullPath(_folder), "crossing-project");
        if (Directory.Exists(copy))
        {
            Directory.Delete(copy, recursive: true);
        }
        AssetChecks.CopyProject(source, copy, AssetChecks.IsWarm(shell));
        shell.OpenProject(Path.Combine(copy, "Crossing.crproject"));
        shell.OpenScene(Path.Combine(copy, "Assets", "Scenes", "Crossing.crscene"));
        yield return 3;

        // The walk, standalone.
        int ended = Count(Log(shell), "The standalone run ended");
        Check(shell.RunStandalone("--headless", "--fixed-clock", "--no-pacing", "--frame-rate", "60", "--game-arg", $"walk-script={Walk}"),
            "the run starts");
        long started = Environment.TickCount64;
        while (Count(Log(shell), "The standalone run ended") == ended && Environment.TickCount64 - started < 180000)
        {
            yield return 2;
        }
        string run = LastRun(Log(shell));
        Console.WriteLine("CrossingChecks: the walk:\n  " + string.Join("\n  ", run.Split('\n').Where(line => line.Contains("Crossing.", StringComparison.Ordinal))));
        MatchCollection points = Point().Matches(run);
        Check(points.Count == 15, "the hero walks every point of the script", $"{points.Count} | {Tail(run)}");
        if (points.Count == 15)
        {
            Check(points[0].Groups["how"].Value == "gave up on" && Y(points[0]) < -3.3f,
                "on the ground the bank holds it below its face", points[0].Value);
            Check(points.Cast<Match>().Where((_, i) => i is not 0 and not 12).All(p => p.Groups["how"].Value == "reached"),
                "it reaches every other point: the ramps, the road under the bridge, the stairs");
            Check(points[12].Groups["how"].Value == "gave up on" && Y(points[12]) > -1.2f,
                "on the bridge the railing holds it", points[12].Value);
            Check(points[2].Groups["layer"].Value == "Low" && points[3].Groups["layer"].Value == "Under",
                "on the road it's drawn in Low, and Under as it comes up to the deck: its head goes under first",
                $"{points[2].Value} | {points[3].Value}");
            Check(points[9].Groups["layer"].Value == "Low" && points[11].Groups["layer"].Value == "High",
                "partway up the stairs on the ground it's in Low (over the railing behind it); on the bridge in High",
                $"{points[9].Value} | {points[11].Value}");
        }
        Check(run.Contains("Crossing.Under: under the deck on the ground", StringComparison.Ordinal),
            "it walks under the deck on the ground");
        Check(run.Contains("Crossing.Over: over the road on the bridge", StringComparison.Ordinal),
            "and over the road on the bridge");
        MatchCollection floors = FloorChange().Matches(run);
        Check(floors.Count == 2 && floors[0].Groups["floor"].Value == "bridge" && X(floors[0]) > 8 &&
              floors[1].Groups["floor"].Value == "ground" && X(floors[1]) < -10,
            "the floor switches to the bridge at the east stairs' top and to the ground at the west stairs' bottom",
            string.Join(" | ", floors.Select(f => f.Value)));
        Check(run.Contains("Crossing.Route: done", StringComparison.Ordinal), "the script ends", Tail(run));

        // Drawn in the editor at four sizes: the 2D exactly its virtual image.
        World world = shell.World;
        Entity hero = Named(world, "Hero");
        Entity camera = Named(world, "Pixel Camera");
        Check(!hero.IsNull && !camera.IsNull, "the scene has the hero and the pixel camera");
        Dictionary<int, byte[]> virtuals = [];
        foreach ((int width, int height) in new[] { (640, 360), (1280, 720), (1920, 1080), (1000, 700) })
        {
            byte[]? image = null;
            foreach (int wait in Capture(width, height, result => image = result))
            {
                yield return wait;
            }
            Save(image!, width, height, Path.Combine(_folder, $"crossing-{width}.png"));
            int scale = Math.Min(width / VirtualWidth, height / VirtualHeight);
            int left = (width - VirtualWidth * scale) / 2, top = (height - VirtualHeight * scale) / 2;
            // Inside the island, away from the sea: every virtual pixel a
            // block of scale x scale equal window pixels.
            (int x0, int y0, int x1, int y1) = VirtualRect(world, camera, new Vector2(-15, -10), new Vector2(-4, -3.5f));
            Check(Blocky(image!, width, left, top, scale, x0, y0, x1, y1), $"at {width}x{height} the island is its 320x180 image scaled {scale}x");
            virtuals[width] = Downsampled(image!, width, left, top, scale, x0, y0, x1, y1);
            (int sx, int sy, _, _) = VirtualRect(world, camera, new Vector2(-19.5f, -6), new Vector2(-19, -5.5f));
            byte[] sea = Pixel(image!, width, left + sx * scale + scale / 2, top + sy * scale + scale / 2);
            Check(sea[0] > sea[2] && sea[0] > 30, $"at {width}x{height} the 3D sea shows beyond the island", $"BGR {sea[0]} {sea[1]} {sea[2]}");
            if (left > 0 || top > 0)
            {
                Check(Pixel(image!, width, 2, 2) is [0, 0, 0, _] && Pixel(image!, width, width / 2, 2) is [0, 0, 0, _],
                    $"at {width}x{height} black bars round the image");
            }
        }
        Check(virtuals.Values.All(v => v.AsSpan().SequenceEqual(virtuals[640])), "the virtual image is the same at every size");

        // A lamp's light, over the dusk.
        {
            byte[]? image = null;
            foreach (int wait in Capture(640, 360, result => image = result))
            {
                yield return wait;
            }
            float lit = Luminance(world, camera, image!, new Vector2(-13.5f, -4.0f));
            float dusk = Luminance(world, camera, image!, new Vector2(-8.5f, -10.0f));
            Check(lit > dusk * 1.4f, "the grass by a lamp is brighter than the grass in the dusk", $"{lit:0} and {dusk:0}");
        }

        // The hero under the deck on the ground, over it on the bridge.
        Vector2 under = new(0, -0.5f);
        world.Set(hero, world.Read<Transform2D>(hero) with { Position = under });
        world.Set(camera, world.Read<Transform2D>(camera) with { Position = under });
        byte[] hidden = [], onGround = [], onBridge = [];
        Sprite sprite = world.Read<Sprite>(hero);
        foreach ((Action set, Action<byte[]> keep) in new (Action, Action<byte[]>)[]
                 {
                     (() => world.Set(hero, sprite with { Tint = Vector4.Zero }), image => hidden = image),
                     (() => world.Set(hero, sprite with { SortingLayer = Layer("Under") }), image => onGround = image),
                     (() => world.Set(hero, sprite with { SortingLayer = Layer("High") }), image => onBridge = image),
                 })
        {
            set();
            yield return 2;
            foreach (int wait in Capture(640, 360, keep))
            {
                yield return wait;
            }
        }
        // The hero's 16x16 frame, its feet at the camera's middle.
        Check(Same(hidden, onGround, 640, 2, 152, 74, 168, 90), "on the ground the hero is drawn under the deck: hidden");
        Check(!Same(hidden, onBridge, 640, 2, 152, 74, 168, 90), "on the bridge it's drawn over it");

        // Coming up the road to the bridge, in Under: its head goes under
        // the deck's south railing first. Partway up the stairs, on the
        // ground in Low: its head is drawn over the north railing behind it.
        foreach ((Vector2 at, string layer, bool shows, string what) in new[]
                 {
                     (new Vector2(0, -2.4f), "Under", false, "coming up the road to the bridge its head goes under the railing first"),
                     (new Vector2(9, 0.44f), "Low", true, "partway up the stairs its head is drawn over the railing behind it"),
                 })
        {
            world.Set(hero, world.Read<Transform2D>(hero) with { Position = at });
            world.Set(camera, world.Read<Transform2D>(camera) with { Position = at });
            byte[] without = [], with = [];
            foreach ((Action set, Action<byte[]> keep) in new (Action, Action<byte[]>)[]
                     {
                         (() => world.Set(hero, sprite with { Tint = Vector4.Zero }), image => without = image),
                         (() => world.Set(hero, sprite with { SortingLayer = Layer(layer) }), image => with = image),
                     })
            {
                set();
                yield return 2;
                foreach (int wait in Capture(640, 360, keep))
                {
                    yield return wait;
                }
            }
            // Its head: the frame's top six rows, over a railing's row.
            Check(Same(without, with, 640, 2, 152, 74, 168, 80) != shows, what);
        }
        world.Set(hero, sprite);
    }

    private static uint Layer(string name) => (uint)Math.Max(RuntimeSettings.Current.SortingLayer(name), 0);

    // A world rectangle as virtual pixels (x right, y down from the top), the pixel camera where it is now.
    private static (int X0, int Y0, int X1, int Y1) VirtualRect(World world, Entity camera, Vector2 low, Vector2 high)
    {
        Vector2 center = world.Read<Transform2D>(camera).Position;
        // The pixel camera's own rounding: its bottom-left corner on a whole pixel.
        Vector2 half = new(VirtualWidth / 2f / PixelsPerUnit, VirtualHeight / 2f / PixelsPerUnit);
        Vector2 corner = new(MathF.Round((center.X - half.X) * PixelsPerUnit), MathF.Round((center.Y - half.Y) * PixelsPerUnit));
        int x0 = (int)MathF.Floor(low.X * PixelsPerUnit - corner.X), x1 = (int)MathF.Ceiling(high.X * PixelsPerUnit - corner.X);
        int bottom = (int)MathF.Floor(low.Y * PixelsPerUnit - corner.Y), topRow = (int)MathF.Ceiling(high.Y * PixelsPerUnit - corner.Y);
        return (Math.Clamp(x0, 0, VirtualWidth), Math.Clamp(VirtualHeight - topRow, 0, VirtualHeight),
            Math.Clamp(x1, 0, VirtualWidth), Math.Clamp(VirtualHeight - bottom, 0, VirtualHeight));
    }

    private static float Luminance(World world, Entity camera, byte[] image, Vector2 at)
    {
        (int x0, int y0, int x1, int y1) = VirtualRect(world, camera, at - new Vector2(0.5f), at + new Vector2(0.5f));
        float sum = 0;
        int count = 0;
        for (int y = y0; y < y1; ++y)
        {
            for (int x = x0; x < x1; ++x)
            {
                byte[] p = Pixel(image, 640, x * 2 + 1, y * 2 + 1);
                sum += 0.11f * p[0] + 0.59f * p[1] + 0.3f * p[2];
                ++count;
            }
        }
        return count > 0 ? sum / count : 0;
    }

    // The scene drawn into an offscreen image of a size, as the game panel draws it (BGRA).
    private static IEnumerable<int> Capture(int width, int height, Action<byte[]> done)
    {
        Shell shell = Shell.Current!;
        Crucible.Render.SurfaceHandle surface = Crucible.Render.Api.CreateOffscreenSurface((uint)width, (uint)height);
        for (int frame = 0; frame < 6; ++frame)
        {
            Crucible.Render.Api.RenderFrame(surface, shell.World.Handle, 0, 0, 0, 0, 1);
            yield return 1;
        }
        byte[] bgra = new byte[width * height * 4];
        Crucible.Render.Api.ReadSurfacePixels(surface, bgra);
        Crucible.Render.Api.DestroySurface(surface);
        done(bgra);
    }

    private static byte[] Pixel(byte[] bgra, int width, int x, int y) => bgra.AsSpan((y * width + x) * 4, 4).ToArray();

    private static bool Blocky(byte[] image, int width, int left, int top, int scale, int x0, int y0, int x1, int y1)
    {
        for (int vy = y0; vy < y1; ++vy)
        {
            for (int vx = x0; vx < x1; ++vx)
            {
                byte[] first = Pixel(image, width, left + vx * scale, top + vy * scale);
                for (int dy = 0; dy < scale; ++dy)
                {
                    for (int dx = 0; dx < scale; ++dx)
                    {
                        if (!Pixel(image, width, left + vx * scale + dx, top + vy * scale + dy).AsSpan().SequenceEqual(first))
                        {
                            return false;
                        }
                    }
                }
            }
        }
        return true;
    }

    private static byte[] Downsampled(byte[] image, int width, int left, int top, int scale, int x0, int y0, int x1, int y1)
    {
        List<byte> pixels = [];
        for (int vy = y0; vy < y1; ++vy)
        {
            for (int vx = x0; vx < x1; ++vx)
            {
                pixels.AddRange(Pixel(image, width, left + vx * scale, top + vy * scale));
            }
        }
        return [.. pixels];
    }

    // Whether two images agree over a rectangle of virtual pixels (scale window pixels each).
    private static bool Same(byte[] a, byte[] b, int width, int scale, int x0, int y0, int x1, int y1)
    {
        int left = (width - VirtualWidth * scale) / 2, top = (360 - VirtualHeight * scale) / 2;
        for (int y = y0 * scale; y < y1 * scale; ++y)
        {
            for (int x = x0 * scale; x < x1 * scale; ++x)
            {
                if (!Pixel(a, width, left + x, top + y).AsSpan().SequenceEqual(Pixel(b, width, left + x, top + y)))
                {
                    return false;
                }
            }
        }
        return true;
    }

    private static void Save(byte[] bgra, int width, int height, string path)
    {
        byte[] rgb = new byte[width * height * 3];
        for (int i = 0; i < width * height; ++i)
        {
            (rgb[i * 3], rgb[i * 3 + 1], rgb[i * 3 + 2]) = (bgra[i * 4 + 2], bgra[i * 4 + 1], bgra[i * 4]);
        }
        Png.Write(path, width, height, rgb);
    }

    private static Entity Named(World world, string name) =>
        SceneIds.Of(world).Entities.FirstOrDefault(e => world.Has(e, Components.Of<EntityName>()) && world.GetManaged<EntityName>(e).Value == name);

    private static float X(Match match) => float.Parse(match.Groups["x"].Value, CultureInfo.InvariantCulture);

    private static float Y(Match match) => float.Parse(match.Groups["y"].Value, CultureInfo.InvariantCulture);

    [GeneratedRegex(@"Crossing\.Route: (?<how>reached|gave up on) point (?<n>\d+) at \((?<x>-?[\d.]+), (?<y>-?[\d.]+)\) in (?<layer>\w+)")]
    private static partial Regex Point();

    [GeneratedRegex(@"Crossing\.Floor: to the (?<floor>bridge|ground) at \((?<x>-?[\d.]+), (?<y>-?[\d.]+)\)")]
    private static partial Regex FloorChange();

    private static string Log(Shell shell) =>
        shell.LogPanel is { } log ? string.Join("\n", log.Lines) : "";

    private static string LastRun(string log)
    {
        string[] lines = log.Split('\n');
        int start = Array.FindLastIndex(lines, line => line.StartsWith("Running ", StringComparison.Ordinal) &&
                                                       line.EndsWith(" standalone", StringComparison.Ordinal));
        return string.Join("\n", lines[Math.Max(start, 0)..]);
    }

    private static int Count(string log, string text) => log.Split('\n').Count(line => line.Contains(text, StringComparison.Ordinal));

    private static string Tail(string log) => string.Join(" | ", log.Split('\n').TakeLast(8));

    private void Check(bool passed, string what, string detail = "")
    {
        ++_checks;
        if (!passed)
        {
            ++_failures;
            Console.WriteLine($"CrossingChecks: FAILED: {what} {detail}");
        }
    }
}
