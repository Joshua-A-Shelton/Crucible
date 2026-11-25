using System.Numerics;
using System.Runtime.CompilerServices;
using Crucible.Loop;
using Crucible.Platform;
using Crucible.UI;

namespace Crucible.Editor;

/// <summary>
/// Viewport tools checking themselves (ADR 0121), for CTest's
/// launcher.jit.viewport-tools: run with the Walker's project and
/// <c>--game-arg check-viewport-tools=&lt;folder&gt;</c>. On a copy of the
/// project, an extension's tool: its button and key beside Move, Rotate,
/// Scale, and Edit Points (and one wanting Move's key going without); the
/// left button its, the right the camera's; a plane under the pointer, the
/// entity and surface picked a frame later; its overlay drawn; its keys
/// first, an exception in it logged; Escape back to Move; on from its own
/// command; and gone with its code, back after a reload.
/// </summary>
[System(Phases.RenderPrepare)]
internal sealed class ViewportToolChecks : GameSystem
{
    private const int ScancodeB = 5, ScancodeJ = 13, ScancodeK = 14, ScancodeEscape = 41;

    private IEnumerator<int>? _script;
    private int _wait;
    private int _checks;
    private int _failures;
    private string _folder = "";
    private readonly List<string> _logged = [];

    public override void Start(FrameContext context)
    {
        if (!context.GameArguments.TryGetValue("check-viewport-tools", out string? folder))
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
            ? $"ViewportToolChecks: all {_checks} checks passed"
            : $"ViewportToolChecks: {_failures} of {_checks} checks failed");
        context.QuitRequested = true;
    }

    private IEnumerable<int> Script()
    {
        Shell shell = Shell.Current ?? throw new InvalidOperationException("No editor shell");
        shell.Logged += _logged.Add;
        string copy = Path.Combine(Path.GetFullPath(_folder), "viewport-tools-project");
        AssetChecks.OpenCopy(shell, copy, Path.Combine("Assets", "Scenes", "Walk.crscene"), withoutEditorCode: true);
        shell.RestoreLayout(null);
        yield return 3;
        string tools = Path.Combine(shell.Project!.CodeFolder, "Editor", "Marks.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(tools)!);
        File.WriteAllText(tools, Marks(1));
        _logged.Clear();
        Check(shell.BuildCode(), "an extension with tools builds", Logged());
        Check(_logged.Contains("Marks.Hog wants W, already used by Scene.Move; it has no shortcut"),
            "a tool wanting Move's key goes without it", Logged());
        ViewportPanel? viewport = shell.Windows.SelectMany(window => window.DockSpace.Panels).OfType<ViewportPanel>().FirstOrDefault();
        if (viewport is null)
        {
            Check(false, "the Viewport is open");
            yield break;
        }
        yield return 2;
        // The editor's as icons, their names and keys in their tooltips (ADR
        // 0124); the extension's, without icons, as text.
        Check(viewport.ToolButtons.Select(button => button.Button.Text ?? $"icon: {button.Button.ToolTip}")
                  .SequenceEqual(["icon: Move (W)", "icon: Rotate (E)", "icon: Scale (R)", "icon: Edit Points (P)", "Place (B)", "Hog"]),
            "the Viewport's toolbar has the editor's tools, then the extension's",
            string.Join(", ", viewport.ToolButtons.Select(button => button.Button.Text)));
        Check(ToolId(viewport) == "Scene.Move", "Move is on to start with");

        // Its key turns it on.
        UIRoot ui = shell.Main.Root;
        viewport.View.Focus();
        _logged.Clear();
        HierarchyChecks.Send(ui, HierarchyChecks.KeyDown(ScancodeB, Keycode.B));
        Check(ToolId(viewport) == "Marks.Place" && _logged.Contains("place on"), "its key turns it on", Logged());

        // The left button is its: a plane under the pointer, then picks.
        Vector2 center = ViewportChecks.Center(viewport.View);
        _logged.Clear();
        HierarchyChecks.Send(ui, ViewportChecks.Motion(center), ViewportChecks.Button(center, true), ViewportChecks.Button(center, false));
        Check(_logged.Contains("plane hit") && _logged.Contains("released"), "a click is its, with the plane under the pointer", Logged());
        yield return 6;
        Check(_logged.Any(line => line is "picked an entity" or "picked nothing") && _logged.Any(line => line is "surface" or "no surface"),
            "the entity and the surface under it answer a frame later", Logged());
        Check(_logged.Contains("drawn"), "its overlay is drawn", Logged());

        // The right button is still the camera's.
        float yaw = viewport.View.Camera.Yaw;
        HierarchyChecks.Send(ui, ViewportChecks.Motion(center), RightButton(center, true),
            ViewportChecks.Motion(center + new Vector2(40, 0)), RightButton(center + new Vector2(40, 0), false));
        Check(viewport.View.Camera.Yaw != yaw, "what it doesn't take goes to the camera");

        // Its keys first; one that throws is logged.
        viewport.View.Focus();
        _logged.Clear();
        HierarchyChecks.Send(ui, HierarchyChecks.KeyDown(ScancodeK, Keycode.K));
        Check(_logged.Contains("key v1"), "its keys first", Logged());
        HierarchyChecks.Send(ui, HierarchyChecks.KeyDown(ScancodeJ, Keycode.J));
        Check(_logged.Contains("Marks.Place: KeyDown failed: InvalidOperationException: j"), "an exception in it is logged", Logged());

        // Escape: back to Move.
        _logged.Clear();
        HierarchyChecks.Send(ui, HierarchyChecks.KeyDown(ScancodeEscape, Keycode.Escape));
        Check(ToolId(viewport) == "Scene.Move" && _logged.Contains("place off"), "Escape goes back to Move", Logged());

        // On from its own command.
        Check(shell.Commands.Find("Marks.On")?.Execute() == true && ToolId(viewport) == "Marks.Place",
            "an extension turns its tool on from its own command");

        // A reload: off with the old code, back with the new.
        File.WriteAllText(tools, Marks(2));
        yield return 1;
        _logged.Clear();
        Check(shell.BuildCode(), "the extension rebuilds", Logged());
        Check(ToolId(viewport) == "Scene.Move" && _logged.Contains("place off") &&
              !_logged.Any(line => line.Contains("didn't unload", StringComparison.Ordinal)),
            "its code reloaded, the Viewport is back on Move and the old code unloaded", Logged());
        Check(viewport.ToolButtons.Any(button => button.Button.Text == "Place (B)"), "its button is back with the new code");
        viewport.View.Focus();
        _logged.Clear();
        HierarchyChecks.Send(ui, HierarchyChecks.KeyDown(ScancodeB, Keycode.B));
        HierarchyChecks.Send(ui, HierarchyChecks.KeyDown(ScancodeK, Keycode.K));
        Check(_logged.Contains("key v2"), "its new version's keys", Logged());
        shell.Logged -= _logged.Add;
    }

    // Read in a call of its own: nothing here may keep the old code's tool.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string? ToolId(ViewportPanel viewport) => viewport.View.Tool?.Id;

    private static InputEvent RightButton(Vector2 at, bool down) =>
        new() { Type = down ? InputEventType.MouseButtonDown : InputEventType.MouseButtonUp, X = at.X, Y = at.Y, Code = 3 };

    private static string Marks(int version) => $$"""
        using System.Numerics;
        using Crucible.Editor;
        using Crucible.Platform;
        using Crucible.UI;

        namespace Walker.Tools;

        public sealed class Place : ViewportTool
        {
            private readonly EditorContext _editor;
            private Vector3? _mark;
            private bool _drawn;

            public Place(EditorContext editor) : base("Marks.Place", "Place") => _editor = editor;

            public override void Activated(ToolView view) => _editor.Log("place on");

            public override void Deactivated(ToolView view) => _editor.Log("place off");

            public override bool PointerDown(ToolView view, ToolPointer pointer)
            {
                if (pointer.Button != PointerButton.Left)
                {
                    return false;
                }
                // The plane facing the camera through what it looks at.
                Vector3 forward = view.Camera.Forward;
                _mark = view.OnPlane(pointer.Position, new Plane(-forward, Vector3.Dot(forward, view.Camera.Pivot)));
                _editor.Log(_mark is null ? "plane missed" : "plane hit");
                view.PickEntity(pointer.Position, entity =>
                {
                    _editor.Log(entity.IsNull ? "picked nothing" : "picked an entity");
                    view.PickSurface(pointer.Position, point => _editor.Log(point is null ? "no surface" : "surface"));
                });
                return true;
            }

            public override void PointerUp(ToolView view, ToolPointer pointer) => _editor.Log("released");

            public override bool KeyDown(ToolView view, KeyEvent key)
            {
                if (key.Keycode == Keycode.J)
                {
                    throw new System.InvalidOperationException("j");
                }
                if (key.Keycode != Keycode.K)
                {
                    return false;
                }
                _editor.Log("key v{{version}}");
                return true;
            }

            public override void Draw(ToolView view, ToolCanvas canvas)
            {
                if (_mark is { } mark)
                {
                    canvas.WorldLine(mark, mark + Vector3.UnitY, 2, new Vector4(1, 0.8f, 0.2f, 1));
                    if (canvas.WorldPoint(mark) is { } at)
                    {
                        canvas.Circle(at, 5, new Vector4(1, 0.8f, 0.2f, 1));
                        canvas.Text("mark", at + new Vector2(8, -8), new Vector4(1, 1, 1, 1));
                    }
                    if (!_drawn)
                    {
                        _drawn = true;
                        _editor.Log("drawn");
                    }
                }
            }
        }

        public sealed class Hog : ViewportTool
        {
            public Hog() : base("Marks.Hog", "Hog")
            {
            }
        }

        public sealed class MarkTools : EditorExtension
        {
            public override void Start(EditorContext editor)
            {
                editor.Tools.Add(new Place(editor), new Shortcut(KeyModifiers.None, Keycode.B));
                editor.Tools.Add(new Hog(), new Shortcut(KeyModifiers.None, Keycode.W));
                editor.Commands.Add(new Command("Marks.On", "Place", () => editor.Tools.Activate("Marks.Place")), "Tools/Marks");
            }
        }
        """;

    private string Logged() => string.Join(" | ", _logged);

    private void Check(bool passed, string what, string detail = "")
    {
        ++_checks;
        if (!passed)
        {
            ++_failures;
            Console.WriteLine($"ViewportToolChecks: FAILED: {what}{(detail.Length > 0 ? $" ({detail})" : "")}");
        }
    }
}
