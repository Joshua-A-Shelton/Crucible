using System.Numerics;
using Crucible.Ecs;
using Crucible.Loop;
using Crucible.Platform;
using Crucible.Render;
using Crucible.Scenes;
using Crucible.UI;
using Png = Crucible.Samples.Diorama.Png;

namespace Crucible.Editor;

/// <summary>
/// The viewport checking itself headless (ADR 0067), for CTest's
/// launcher.jit.viewport: run with a project and scene (the Diorama's
/// village), <c>--game-arg check-viewport=&lt;file.png&gt;</c>, and a settings
/// file of its own. The scene must show in the viewport's image; a click
/// picks and outlines what's under it; dragging the move handle's X axis
/// moves it along X; a click on empty sky clears the selection. The window
/// is captured with the selection outlined and its handles drawn.
/// </summary>
[System(Phases.RenderPrepare)]
internal sealed class ViewportChecks : GameSystem
{
    private const int Width = 1280, Height = 720; // the launcher's headless surface (Host.cs)

    private IEnumerator<int>? _script;
    private int _wait;
    private int _checks;
    private int _failures;
    private string _capture = "";
    private FrameContext? _context;

    public override void Start(FrameContext context)
    {
        if (!context.GameArguments.TryGetValue("check-viewport", out string? capture))
        {
            Enabled = false;
            return;
        }
        _capture = capture;
        _context = context;
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
            ? $"ViewportChecks: all {_checks} checks passed"
            : $"ViewportChecks: {_failures} of {_checks} checks failed");
        context.QuitRequested = true;
    }

    private IEnumerable<int> Script()
    {
        Shell shell = Shell.Current ?? throw new InvalidOperationException("No editor shell");
        shell.RestoreLayout(null);
        yield return 3;
        ViewportPanel viewport = shell.Main.DockSpace.Panels.OfType<ViewportPanel>().Single();
        SceneView view = viewport.View;
        UIRoot root = shell.Main.Root;
        World world = _context!.World;
        Check(world.EntityCount > 20, "the village is loaded", world.EntityCount.ToString());
        Check(view.Surface.Value != 0, "the viewport has its surface");

        // The scene in the viewport's image: most of it isn't the clear color.
        (byte[] image, int width, int height) = ReadView(view);
        int scene = 0;
        for (int i = 0; i < width * height; ++i)
        {
            scene += Math.Abs(image[i * 4 + 2] - 13) > 6 || Math.Abs(image[i * 4] - 15) > 6 ? 1 : 0;
        }
        Check(scene > width * height / 2, "the scene is drawn in the viewport", $"{scene} of {width * height}");

        // A click on a house's wall picks the house (its prefab instance's root, at (7.5, 0, 7)).
        Vector3 house = new(7.5f, 0, 7);
        Vector2? wall = view.Camera.Project(house + new Vector3(0, 1.2f, 0), view.ActualSize);
        Check(wall is not null, "the house is in view");
        Click(root, new Vector2(view.Bounds.X, view.Bounds.Y) + (wall ?? Vector2.Zero));
        yield return 4;
        Entity picked = shell.Selection.Selected;
        Check(picked.Value != 0, "a click picked something");
        if (picked.Value == 0)
        {
            yield break;
        }
        Check(world.ParentOf(picked).Value == 0, "the pick is its hierarchy's top");
        Vector3 at = world.Read<LocalTransform>(picked).Position;
        Check(Vector3.Distance(at, house) < 0.5f, "the house was picked", at.ToString());
        (image, width, height) = ReadView(view);
        int orange = 0;
        for (int i = 0; i < width * height; ++i)
        {
            orange += image[i * 4 + 2] > 200 && image[i * 4 + 1] is > 100 and < 210 && image[i * 4] < 80 ? 1 : 0;
        }
        Check(orange > 20 && orange < width * height / 20, "the house is outlined, and only it", orange.ToString());

        // Dragging the move handle's X axis moves it along X only.
        view.Handles.Mode = HandleMode.Move;
        view.Handles.Space = HandleSpace.World;
        Vector3 before = world.Read<LocalTransform>(picked).Position;
        Vector2 offset = new(view.Bounds.X, view.Bounds.Y);
        Vector2? origin = view.Camera.Project(before, view.ActualSize);
        float length = view.Camera.WorldSize(before, TransformHandles.SizePixels, view.ActualSize.Y);
        Vector2? tip = view.Camera.Project(before + Vector3.UnitX * length, view.ActualSize);
        Check(origin is not null && tip is not null, "the handles are in view");
        if (origin is { } o && tip is { } t)
        {
            Vector2 grab = offset + o + (t - o) * 0.7f;
            Vector2 along = Vector2.Normalize(t - o) * 60;
            Send(root, Motion(grab), Button(grab, true), Motion(grab + along * 0.5f), Motion(grab + along), Button(grab + along, false));
            yield return 2;
            Vector3 after = world.Read<LocalTransform>(picked).Position;
            Check(after.X > before.X + 0.1f, "the X axis moved it along +X", $"{before} to {after}");
            Check(MathF.Abs(after.Y - before.Y) < 1e-3f && MathF.Abs(after.Z - before.Z) < 1e-3f, "and nowhere else",
                $"{before} to {after}");

            // Again with Ctrl held: the move snaps to half meters.
            Vector2? from = view.Camera.Project(after, view.ActualSize);
            Vector2? end = view.Camera.Project(after + Vector3.UnitX * length, view.ActualSize);
            if (from is { } f && end is { } e)
            {
                Vector2 hold = offset + f + (e - f) * 0.7f;
                Vector2 farther = Vector2.Normalize(e - f) * 47;
                InputEvent Held(InputEvent input) => input with { Modifiers = (uint)KeyModifiers.Ctrl };
                Send(root, Held(Motion(hold)), Held(Button(hold, true)), Held(Motion(hold + farther)), Held(Button(hold + farther, false)));
                yield return 2;
                float snapped = world.Read<LocalTransform>(picked).Position.X - after.X;
                Check(MathF.Abs(snapped / 0.5f - MathF.Round(snapped / 0.5f)) < 1e-3f && MathF.Abs(snapped) >= 0.5f,
                    "with Ctrl held it snaps to half meters", snapped.ToString());
            }
        }
        yield return 2;
        Save(shell.Main.Surface, _capture);

        // A click on the sky, above the village, clears the selection.
        Click(root, new Vector2(view.Bounds.X + view.Bounds.Width / 2, view.Bounds.Y + 4));
        yield return 4;
        Check(shell.Selection.Selected.Value == 0, "a click on nothing cleared the selection");
    }

    private static (byte[] Pixels, int Width, int Height) ReadView(SceneView view)
    {
        TextureSize size = Render.Api.TextureSize(SurfaceTextureOf(view));
        byte[] pixels = new byte[size.Width * size.Height * 4];
        Render.Api.ReadSurfacePixels(view.Surface, pixels);
        return (pixels, (int)size.Width, (int)size.Height);
    }

    private static TextureHandle SurfaceTextureOf(SceneView view)
    {
        Render.Api.SurfaceTexture(view.Surface, out TextureHandle texture);
        return texture;
    }

    internal static Vector2 Center(UIElement element) =>
        new(element.Bounds.X + element.Bounds.Width / 2, element.Bounds.Y + element.Bounds.Height / 2);

    private static void Send(UIRoot root, params InputEvent[] events) => root.ProcessInput(events);

    internal static void Click(UIRoot root, Vector2 at) => Send(root, Motion(at), Button(at, true), Button(at, false));

    internal static InputEvent Motion(Vector2 at) => new() { Type = InputEventType.MouseMotion, X = at.X, Y = at.Y };

    internal static InputEvent Button(Vector2 at, bool down) =>
        new() { Type = down ? InputEventType.MouseButtonDown : InputEventType.MouseButtonUp, X = at.X, Y = at.Y, Code = 1 };

    internal static void Save(SurfaceHandle surface, string path)
    {
        byte[] bgra = new byte[Width * Height * 4];
        Render.Api.ReadSurfacePixels(surface, bgra);
        byte[] rgb = new byte[Width * Height * 3];
        for (int i = 0; i < Width * Height; ++i)
        {
            rgb[i * 3] = bgra[i * 4 + 2];
            rgb[i * 3 + 1] = bgra[i * 4 + 1];
            rgb[i * 3 + 2] = bgra[i * 4];
        }
        Png.Write(path, Width, Height, rgb);
    }

    private void Check(bool passed, string what, string detail = "")
    {
        ++_checks;
        if (!passed)
        {
            ++_failures;
            Console.WriteLine($"ViewportChecks: FAILED: {what} {detail}");
        }
    }
}
