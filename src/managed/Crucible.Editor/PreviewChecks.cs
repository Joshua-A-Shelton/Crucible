using System.Numerics;
using Crucible.Animation;
using Crucible.Assets;
using Crucible.Ecs;
using Crucible.Loop;
using Crucible.Render;
using Crucible.Render2D;
using Crucible.UI;
using V = Crucible.Editor.ViewportChecks;

namespace Crucible.Editor;

/// <summary>
/// Preview viewports checking themselves (ADR 0125), for CTest's
/// launcher.jit.previews: run with the Walker's project and
/// <c>--game-arg check-previews=&lt;folder&gt;</c>. A panel holding a preview:
/// the Walker loaded into its world, framed, lit by the default rig, and
/// drawn, apart from the scene; its Walk clip playing on the preview's
/// clock, held with the clock off, and scrubbed; a sprite in 2D; and the
/// world freed when the panel closes. The window is saved as previews.png.
/// </summary>
[System(Phases.RenderPrepare)]
internal sealed class PreviewChecks : GameSystem
{
    private IEnumerator<int>? _script;
    private int _wait;
    private int _checks;
    private int _failures;
    private string _folder = "";

    public override void Start(FrameContext context)
    {
        if (!context.GameArguments.TryGetValue("check-previews", out string? folder))
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
            ? $"PreviewChecks: all {_checks} checks passed"
            : $"PreviewChecks: {_failures} of {_checks} checks failed");
        context.QuitRequested = true;
    }

    // A panel of the checks' own holding a preview, as an extension's would.
    private sealed class PreviewPanel : EditorPanel
    {
        public PreviewPanel() : base("Checks.Preview", "Preview")
        {
            View = new PreviewView();
            AddChild(View);
        }

        public PreviewView View { get; }
    }

    private IEnumerable<int> Script()
    {
        Shell shell = Shell.Current ?? throw new InvalidOperationException("No editor shell");
        string copy = Path.Combine(Path.GetFullPath(_folder), "previews-project");
        AssetChecks.OpenCopy(shell, copy, Path.Combine("Assets", "Scenes", "Walk.crscene"), withoutEditorCode: true);
        shell.RestoreLayout(null);
        long opening = Environment.TickCount64;
        while ((shell.Database is null || shell.Prefabs is null || !shell.HasWorld) && Environment.TickCount64 - opening < 30000)
        {
            yield return 1;
        }
        yield return 3;
        AssetDatabase database = shell.Database!;
        World scene = shell.World;
        int sceneEntities = scene.EntityCount;
        PreviewPanel? panel = null;
        shell.Panels.Register("Checks.Preview", "Preview", () => panel = new PreviewPanel());
        shell.OpenPanel("Checks.Preview");
        yield return 2;
        Check(panel is not null && panel.View.Root is not null, "a panel holds a preview");
        if (panel is null)
        {
            yield break;
        }
        PreviewView preview = panel.View;
        World world = preview.World;
        Check(!ReferenceEquals(world, scene) && !world.Handle.Equals(scene.Handle) && world.IsAlive(preview.KeyLight) && world.Has(preview.KeyLight, Components.Of<DirectionalLight>()) &&
              world.IsAlive(preview.FillLight) && world.Has(preview.FillLight, Components.Of<AmbientLight>()),
            "its world is its own, with the default rig: a key light and an ambient fill");

        // The Walker in it, framed, drawn; the scene untouched.
        AssetEntry? walker = database.Find("Models/Walker.gltf");
        Entity root = walker is null ? Entity.Null : preview.Load(walker.Id);
        Check(!root.IsNull && world.IsAlive(root), "a model loads into it", walker?.Path ?? "no Walker.gltf");
        Check(scene.EntityCount == sceneEntities, "and not into the scene");
        preview.Frame();
        Check(preview.Camera.Distance is > 0.5f and < 50, "Frame fits the model", $"{preview.Camera.Distance}");
        // An Animator on it, its skeleton the model's, as the Walk scene has it.
        Entity animated = root;
        if (walker is not null && world.IsAlive(root))
        {
            world.Set(root, new Animator { Skeleton = walker.Id });
        }
        long started = Environment.TickCount64;
        while (Environment.TickCount64 - started < 10000 && !Played(world, animated))
        {
            yield return 1;
        }
        Check(Played(world, animated), "its Walk clip plays");
        yield return 10;
        float early = Animator.TimeOf(world, animated, -1);
        yield return 10;
        float later = Animator.TimeOf(world, animated, -1);
        Check(later != early, "on the preview's clock", $"{early} then {later}");
        V.Save(shell.Main.Surface, Path.Combine(_folder, "previews.png"));

        // The clock off: held; scrubbed to a moment.
        preview.IsRunning = false;
        yield return 2;
        float held = Animator.TimeOf(world, animated, -1);
        yield return 10;
        Check(Animator.TimeOf(world, animated, -1) == held, "with the clock off it holds where it is");
        Animator.SetTime(world, animated, -1, 0.25f);
        yield return 2;
        Check(MathF.Abs(Animator.TimeOf(world, animated, -1) - 0.25f) < 1e-4f, "and scrubs to a moment",
            $"{Animator.TimeOf(world, animated, -1)}");

        // 2D: a sprite, framed by its texture.
        AssetEntry? texture = database.Assets.FirstOrDefault(asset => asset.Importer == "Texture");
        if (texture is not null)
        {
            foreach (Entity entity in Under(world, root).ToArray())
            {
                world.Destroy(entity);
            }
            preview.Is2D = true;
            Entity sprite = preview.Load(texture.Id);
            preview.Frame();
            yield return 3;
            Check(!sprite.IsNull && world.Has(sprite, Components.Of<Sprite>()) && preview.ViewHeight > 0.1f &&
                  preview.Camera.Yaw == 0 && preview.Camera.Pitch == 0,
                "a texture loads as a sprite, seen straight on in 2D", $"{preview.ViewHeight}");
        }

        // Closing the panel frees its world.
        shell.ClosePanel("Checks.Preview");
        yield return 2;
        Check(preview.IsDisposed && !shell.Previews.Contains(preview), "closing its panel frees its world");
        panel = null;
    }

    // Whether the Animator has a clip going (Walk, played once its skeleton is in).
    private static bool Played(World world, Entity entity)
    {
        if (entity.IsNull || !world.IsAlive(entity))
        {
            return false;
        }
        try
        {
            Animator.Play(world, entity, AssetId.Parse("10880ff6-b22a-451e-a23a-d6729d1f813d"));
            return true;
        }
        catch (CrucibleException)
        {
            return false; // still loading
        }
    }

    private static IEnumerable<Entity> Under(World world, Entity root)
    {
        Stack<Entity> pending = new([root]);
        while (pending.Count > 0)
        {
            Entity entity = pending.Pop();
            if (entity.IsNull || !world.IsAlive(entity))
            {
                continue;
            }
            yield return entity;
            foreach (Entity child in SceneIds.ChildrenOf(world, entity))
            {
                pending.Push(child);
            }
        }
    }

    private void Check(bool passed, string what, string detail = "")
    {
        ++_checks;
        if (!passed)
        {
            ++_failures;
            Console.WriteLine($"PreviewChecks: FAILED: {what}{(detail.Length > 0 ? $" ({detail})" : "")}");
        }
    }
}
