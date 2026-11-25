using Crucible.Assets;
using Crucible.Ecs;
using Crucible.Loop;
using Crucible.Scenes;
using Crucible.UI;
using H = Crucible.Editor.HierarchyChecks;
using V = Crucible.Editor.ViewportChecks;

namespace Crucible.Editor;

/// <summary>
/// Custom inspectors checking themselves (ADR 0126), for CTest's
/// launcher.jit.drawers: run with the Walker's project and
/// <c>--game-arg check-drawers=&lt;folder&gt;</c>. On a copy of the project, game
/// code with a Stats component holding a DamageRange and a field marked
/// [Drawer("Percent")], and editor code drawing each: the component's
/// section by its drawer, the generated editors it placed, the field type's
/// and the marked field's drawers inside it; values read as their types and
/// set as undo steps, a change of several sets one step; the prefab
/// override seen; only the new drawers after a reload; and the editor's own
/// Collider drawer.
/// </summary>
[System(Phases.RenderPrepare)]
internal sealed class DrawerChecks : GameSystem
{
    private IEnumerator<int>? _script;
    private int _wait;
    private int _checks;
    private int _failures;
    private string _folder = "";
    private readonly List<string> _logged = [];

    public override void Start(FrameContext context)
    {
        if (!context.GameArguments.TryGetValue("check-drawers", out string? folder))
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
            ? $"DrawerChecks: all {_checks} checks passed"
            : $"DrawerChecks: {_failures} of {_checks} checks failed");
        context.QuitRequested = true;
    }

    private IEnumerable<int> Script()
    {
        Shell shell = Shell.Current ?? throw new InvalidOperationException("No editor shell");
        shell.Logged += _logged.Add;
        string copy = Path.Combine(Path.GetFullPath(_folder), "drawers-project");
        AssetChecks.OpenCopy(shell, copy, Path.Combine("Assets", "Scenes", "Walk.crscene"), withoutEditorCode: true);
        shell.RestoreLayout(null);
        yield return 3;
        Project project = shell.Project!;
        File.WriteAllText(Path.Combine(project.CodeFolder, "Stats.cs"), Game);
        string tools = Path.Combine(project.CodeFolder, "Editor", "StatsDrawers.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(tools)!);
        File.WriteAllText(tools, Drawers(1));
        Check(shell.BuildCode(), "game and editor code with drawers build", Logged());
        yield return 2;

        // The Walker (a prefab instance) given Stats, selected.
        World world = shell.World;
        Entity walker = SceneIds.Of(world).Entities.FirstOrDefault(entity => shell.LabelOf(entity) == "Walker");
        Check(!walker.IsNull, "the scene has the Walker");
        shell.AddComponent(walker, "Walker.Stats");
        shell.Selection.Selected = walker;
        yield return 3;
        InspectorPanel? inspector = shell.Windows.SelectMany(window => window.DockSpace.Panels).OfType<InspectorPanel>().FirstOrDefault();
        ComponentSection? stats = inspector?.Sections.FirstOrDefault(section => section.Component == "Walker.Stats");
        Check(stats is not null && stats.HasDrawer, "the Stats section is drawn by its component drawer");
        if (stats is null || inspector is null)
        {
            yield break;
        }
        UIRoot ui = shell.Main.Root;
        Check(Text(stats, "stats: strength 0, crit 0, overridden True"), "the drawer reads the value as its type, the component added on the instance overridden",
            string.Join(" | ", H.Descendants(stats).OfType<Label>().Select(label => label.Text)));
        Check(stats.Editors.Select(editor => editor.Field.Name).SequenceEqual(["Strength", "Damage", "Crit"]),
            "the generated editors it placed, where it placed them", string.Join(",", stats.Editors.Select(editor => editor.Field.Name)));
        Check(stats.Editors.FirstOrDefault(editor => editor.Field.Name == "Damage")?.HasDrawer == true, "a DamageRange field is drawn by its type's drawer");
        Check(stats.Editors.FirstOrDefault(editor => editor.Field.Name == "Crit")?.HasDrawer == true, "a field marked [Drawer(\"Percent\")] by that drawer");
        Check(stats.Editors.FirstOrDefault(editor => editor.Field.Name == "Strength")?.HasDrawer == false, "and the rest by the generated editors");

        // Sets as undo steps: the component's, the field type's, the marked field's.
        int steps = shell.History.UndoCount;
        Reveal(stats, "Train");
        yield return 1;
        Click(ui, stats, "Train");
        yield return 2;
        Check(Text(stats, "stats: strength 10, crit 0, overridden True") && shell.History.UndoCount == steps + 1,
            "the component drawer sets its value, one undo step", string.Join(" | ", Labels(stats)));
        Reveal(stats, "Widen");
        yield return 1;
        Click(ui, stats, "Widen");
        yield return 2;
        Check(Text(stats, "damage 1 to 9"), "the field type's drawer sets its field", string.Join(" | ", Labels(stats)));
        Reveal(stats, "Half");
        yield return 1;
        Click(ui, stats, "Half");
        yield return 2;
        Check(Text(stats, "50%") && Text(stats, "stats: strength 10, crit 0.5, overridden True"), "the marked field's drawer sets its float",
            string.Join(" | ", Labels(stats)));
        shell.Undo();
        yield return 2;
        Check(Text(stats, "stats: strength 10, crit 0, overridden True"), "and undoes");

        // Several sets in one change: one step.
        steps = shell.History.UndoCount;
        Reveal(stats, "Drag");
        yield return 1;
        Click(ui, stats, "Drag");
        yield return 2;
        Check(Text(stats, "stats: strength 13, crit 0, overridden True") && shell.History.UndoCount == steps + 1,
            "three sets in a change are one undo step", $"{shell.History.UndoCount - steps}");

        // The editor's own: a Collider's section by its drawer.
        Entity box = shell.CreateEmpty(Entity.Null);
        shell.AddComponent(box, "Crucible.Physics.Collider");
        shell.Selection.Selected = box;
        yield return 3;
        Check(inspector.Sections.FirstOrDefault(section => section.Component == "Crucible.Physics.Collider")?.HasDrawer == true,
            "the editor's own Collider section is a drawer too");

        // A reload: the new drawers, the old code gone.
        stats = null;
        shell.Selection.Selected = Entity.Null;
        yield return 2;
        File.WriteAllText(tools, Drawers(2));
        yield return 1;
        _logged.Clear();
        Check(shell.BuildCode(), "the drawers rebuild", Logged());
        Check(!_logged.Any(line => line.Contains("didn't unload", StringComparison.Ordinal)), "the old drawers unload", Logged());
        shell.Selection.Selected = walker;
        yield return 3;
        ComponentSection? again = inspector.Sections.FirstOrDefault(section => section.Component == "Walker.Stats");
        Check(again is not null && Text(again, "v2 stats: strength 13, crit 0, overridden True"), "only the new drawer draws it",
            again is null ? "" : string.Join(" | ", Labels(again)));
        shell.Logged -= _logged.Add;
    }

    private static IEnumerable<string> Labels(UIElement element) => H.Descendants(element).OfType<Label>().Select(label => label.Text);

    private static bool Text(UIElement element, string text) => Labels(element).Contains(text);

    // A button scrolled into the Inspector's view, to be clicked once laid out.
    private static void Reveal(UIElement within, string text)
    {
        if (H.Descendants(within).OfType<Button>().FirstOrDefault(button => button.Text == text) is { } button)
        {
            for (UIElement? at = button.Parent; at is not null; at = at.Parent)
            {
                if (at is ScrollView scroll)
                {
                    scroll.ScrollIntoView(button.Bounds);
                    break;
                }
            }
        }
    }

    private static void Click(UIRoot root, UIElement within, string text)
    {
        if (H.Descendants(within).OfType<Button>().FirstOrDefault(button => button.Text == text) is { } button)
        {
            V.Click(root, V.Center(button));
        }
    }

    private const string Game = """
        using Crucible.Ecs;
        using Crucible.Serialization;

        namespace Walker;

        [Serialized]
        public partial struct DamageRange
        {
            public float Min;
            public float Max;
        }

        [Component, Serialized]
        public partial struct Stats
        {
            public int Strength;
            public DamageRange Damage;
            [Drawer("Percent")] public float Crit;
        }
        """;

    private static string Drawers(int version) => $$"""
        using Crucible.Editor;
        using Crucible.UI;

        namespace Walker.Tools;

        public sealed class StatsDrawers : EditorExtension
        {
            public override void Start(EditorContext editor)
            {
                editor.Inspector.AddComponentDrawer(() => new StatsDrawer());
                editor.Inspector.AddFieldDrawer(() => new DamageDrawer());
                editor.Inspector.AddDrawer("Percent", () => new PercentDrawer());
            }
        }

        sealed class StatsDrawer : Drawer<Walker.Stats>
        {
            private readonly Label _summary = new();

            public override UIElement Build(DrawerContext<Walker.Stats> context)
            {
                StackPanel body = new() { Spacing = 3 };
                body.Add(_summary);
                body.Add(context.Default("Strength")!);
                body.Add(context.Default("Damage")!);
                body.Add(context.Default("Crit")!);
                Button train = new("Train");
                train.Clicked += _ => context.Set(context.Value with { Strength = context.Value.Strength + 10 }, "Train");
                body.Add(train);
                Button drag = new("Drag");
                drag.Clicked += _ =>
                {
                    context.BeginChange("Drag Strength");
                    for (int i = 0; i < 3; ++i)
                    {
                        context.Set(context.Value with { Strength = context.Value.Strength + 1 }, "Drag Strength");
                    }
                    context.EndChange();
                };
                body.Add(drag);
                return body;
            }

            public override void Update(DrawerContext<Walker.Stats> context) =>
                _summary.Text = $"{{(version == 2 ? "v2 " : "")}}stats: strength {context.Value.Strength}, crit {context.Value.Crit}, overridden {context.IsOverridden()}";
        }

        sealed class DamageDrawer : Drawer<Walker.DamageRange>
        {
            private readonly Label _range = new();

            public override UIElement Build(DrawerContext<Walker.DamageRange> context)
            {
                StackPanel row = new() { Orientation = Orientation.Horizontal, Spacing = 4 };
                row.Add(_range);
                Button widen = new("Widen");
                widen.Clicked += _ => context.Set(new Walker.DamageRange { Min = 1, Max = 9 }, "Widen");
                row.Add(widen);
                return row;
            }

            public override void Update(DrawerContext<Walker.DamageRange> context) =>
                _range.Text = $"damage {context.Value.Min} to {context.Value.Max}";
        }

        sealed class PercentDrawer : Drawer<float>
        {
            private readonly Label _percent = new();

            public override UIElement Build(DrawerContext<float> context)
            {
                StackPanel row = new() { Orientation = Orientation.Horizontal, Spacing = 4 };
                row.Add(_percent);
                Button half = new("Half");
                half.Clicked += _ => context.Set(0.5f, "Half");
                row.Add(half);
                return row;
            }

            public override void Update(DrawerContext<float> context) => _percent.Text = $"{context.Value * 100:0}%";
        }
        """;

    private string Logged() => string.Join(" | ", _logged);

    private void Check(bool passed, string what, string detail = "")
    {
        ++_checks;
        if (!passed)
        {
            ++_failures;
            Console.WriteLine($"DrawerChecks: FAILED: {what}{(detail.Length > 0 ? $" ({detail})" : "")}");
        }
    }
}
