using Crucible.Assets;
using Crucible.Ecs;
using Crucible.Loop;
using Crucible.Scenes;
using Crucible.UI;

namespace Crucible.Editor;

/// <summary>
/// Scene edits for extensions checking themselves (ADR 0122), for CTest's
/// launcher.jit.scene-api: run with the Walker's project and
/// <c>--game-arg check-scene-api=&lt;folder&gt;</c>. On a copy of the
/// project, an extension's Create item making an entity with a child as one
/// undo step; a rename it cancels and one it lets through; a Duplicate it's
/// told of; a Delete it holds for its question, with work added after it,
/// and one it cancels; a texture; and only its new code answering after a
/// reload.
/// </summary>
[System(Phases.RenderPrepare)]
internal sealed class SceneApiChecks : GameSystem
{
    private IEnumerator<int>? _script;
    private int _wait;
    private int _checks;
    private int _failures;
    private string _folder = "";
    private readonly List<string> _logged = [];

    public override void Start(FrameContext context)
    {
        if (!context.GameArguments.TryGetValue("check-scene-api", out string? folder))
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
            ? $"SceneApiChecks: all {_checks} checks passed"
            : $"SceneApiChecks: {_failures} of {_checks} checks failed");
        context.QuitRequested = true;
    }

    private IEnumerable<int> Script()
    {
        Shell shell = Shell.Current ?? throw new InvalidOperationException("No editor shell");
        shell.Logged += _logged.Add;
        string copy = Path.Combine(Path.GetFullPath(_folder), "scene-api-project");
        AssetChecks.OpenCopy(shell, copy, Path.Combine("Assets", "Scenes", "Walk.crscene"), withoutEditorCode: true);
        shell.RestoreLayout(null);
        yield return 3;
        string tools = Path.Combine(shell.Project!.CodeFolder, "Editor", "Pairs.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(tools)!);
        File.WriteAllText(tools, Pairs(1));
        Check(shell.BuildCode(), "an extension using the scene builds", Logged());
        World world = shell.World;

        // Its Create item: an entity with a child, one undo step.
        ContextMenu menu = new();
        shell.AddCreateItems(menu, Entity.Null);
        MenuItem? pair = (menu.Items.OfType<MenuItem>().FirstOrDefault(item => item.Title == "Test")?.Submenu)
            ?.Items.OfType<MenuItem>().FirstOrDefault(item => item.Title == "Pair");
        Check(pair is not null, "its item is in the Hierarchy's Create menu, at its path");
        int before = SceneIds.Of(world).Entities.Count();
        shell.Commands.Find("Pairs.Make")?.Execute();
        Entity made = Named(world, "Pair");
        Check(!made.IsNull && SceneIds.ChildrenOf(world, made) is [var part] && NameOf(world, part) == "Part",
            "it makes an entity with a named child", Logged());
        shell.Undo();
        Check(SceneIds.Of(world).Entities.Count() == before, "undone in one step");
        shell.Redo();
        made = Named(world, "Pair");

        // Renaming: one it cancels, one it lets through.
        _logged.Clear();
        shell.RenameWithChanges(made, "Nope");
        Check(NameOf(world, made) == "Pair" && _logged.Contains("renaming to Nope"), "a rename it cancels doesn't happen", Logged());
        shell.RenameWithChanges(made, "Fine");
        Check(NameOf(world, made) == "Fine" && _logged.Contains("renamed Pair to Fine v1"), "one it lets through does, and it's told", Logged());
        shell.Undo();
        Check(NameOf(world, made) == "Pair", "and undoes");

        // Duplicate: told of the copy.
        _logged.Clear();
        Entity duplicate = shell.DuplicateWithChanges(made);
        Check(!duplicate.IsNull && _logged.Contains("duplicating") && _logged.Contains("duplicated"), "a Duplicate runs past it and tells it", Logged());
        shell.Undo();
        Check(!world.IsAlive(duplicate), "and undoes in one step");

        // Delete: held for its question, with work after; and one cancelled.
        _logged.Clear();
        shell.DeleteWithChanges(made);
        yield return 1;
        Check(world.IsAlive(made) && _logged.Contains("holding"), "a Delete it holds waits", Logged());
        shell.Commands.Find("Pairs.Go")?.Execute();
        Check(!world.IsAlive(made) && _logged.Contains("then ran") && _logged.Contains("deleted 2"),
            "let go, it happens, its work after it, and it's told", Logged());
        shell.Undo();
        made = Named(world, "Pair");
        Check(!made.IsNull, "one undo brings it back");
        _logged.Clear();
        shell.DeleteWithChanges(made);
        shell.Commands.Find("Pairs.Stop")?.Execute();
        Check(world.IsAlive(made) && !_logged.Contains("then ran"), "a Delete it cancels doesn't happen", Logged());

        // A texture to draw with, a frame or two after asking.
        AssetEntry? texture = shell.Database?.Assets.FirstOrDefault(asset => asset.Importer == "Texture");
        shell.Commands.Find("Pairs.Texture")?.Execute();
        yield return 5;
        _logged.Clear();
        shell.Commands.Find("Pairs.Texture")?.Execute();
        Check(texture is null || _logged.Contains("texture ready"), "a texture asset's texture, once ready", Logged());

        // A reload: only the new code answers. (The menu built here holds the
        // old code's item, which would keep it from unloading.)
        menu = null!;
        pair = null;
        File.WriteAllText(tools, Pairs(2));
        yield return 1;
        _logged.Clear();
        Check(shell.BuildCode(), "the extension rebuilds", Logged());
        Check(!_logged.Any(line => line.Contains("didn't unload", StringComparison.Ordinal)), "the old code unloads", Logged());
        _logged.Clear();
        shell.RenameWithChanges(made, "Again");
        Check(_logged.Count(line => line.StartsWith("renamed ", StringComparison.Ordinal)) == 1 && _logged.Contains("renamed Pair to Again v2"),
            "only the new code is told", Logged());
        shell.Logged -= _logged.Add;
    }

    private static Entity Named(World world, string name) =>
        SceneIds.Of(world).Entities.FirstOrDefault(entity => world.IsAlive(entity) && NameOf(world, entity) == name);

    private static string NameOf(World world, Entity entity) =>
        world.IsAlive(entity) && world.Has(entity, Components.Of<EntityName>()) ? world.GetManaged<EntityName>(entity).Value : "";

    private static string Pairs(int version) => $$"""
        using System.Collections.Generic;
        using Crucible.Assets;
        using Crucible.Ecs;
        using Crucible.Editor;
        using Crucible.UI;

        namespace Walker.Tools;

        public sealed class Pairs : EditorExtension
        {
            private static SceneChangeHold? _held;

            public override void Start(EditorContext editor)
            {
                void Make(Entity parent)
                {
                    editor.Undo.BeginGroup("Make Pair");
                    Entity pair = editor.Scene.Create("Pair", parent);
                    Entity part = editor.Scene.Create("Spare");
                    editor.Scene.SetParent(part, pair);
                    editor.Scene.Rename(part, "Part");
                    editor.Undo.EndGroup();
                }
                editor.Scene.AddCreate("Test/Pair", Make);
                editor.Commands.Add(new Command("Pairs.Make", "Make", () => Make(Entity.Null)), null);
                editor.Commands.Add(new Command("Pairs.Go", "Go", () => _held?.Go()), null);
                editor.Commands.Add(new Command("Pairs.Stop", "Stop", () => _held?.Cancel()), null);
                editor.Commands.Add(new Command("Pairs.Texture", "Texture", () =>
                {
                    foreach (AssetEntry asset in editor.Assets.Database!.Assets)
                    {
                        if (asset.Importer == "Texture")
                        {
                            if (editor.Assets.Texture(asset.Id) is not null)
                            {
                                editor.Log("texture ready");
                            }
                            break;
                        }
                    }
                }), null);
                editor.Scene.Renaming += change =>
                {
                    editor.Log($"renaming to {change.NewName}");
                    if (change.NewName == "Nope")
                    {
                        change.Cancel();
                    }
                };
                editor.Scene.Renamed += (entity, before, after) => editor.Log($"renamed {before} to {after} v{{version}}");
                editor.Scene.Duplicating += change => editor.Log("duplicating");
                editor.Scene.Duplicated += copies => editor.Log("duplicated");
                editor.Scene.Deleting += change =>
                {
                    editor.Log("holding");
                    _held = change.Hold();
                    change.Then(() => editor.Log("then ran"));
                };
                editor.Scene.Deleted += ids => editor.Log($"deleted {ids.Count}");
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
            Console.WriteLine($"SceneApiChecks: FAILED: {what}{(detail.Length > 0 ? $" ({detail})" : "")}");
        }
    }
}
