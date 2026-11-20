using Crucible.Assets;
using Crucible.Assets.Importers;
using Crucible.Importers;
using Crucible.Loop;
using Crucible.UI;

namespace Crucible.Editor;

/// <summary>
/// The Sprite Sheet Editor checking itself (ADR 0107), for CTest's
/// launcher.jit.sprite-sheet: run with the Walker's project and
/// <c>--game-arg check-sprite-sheet=&lt;folder&gt;</c>. On a copy of the
/// project (in the folder) with a 32x16 sheet added: the Inspector's and the
/// asset browser's Edit Sprite Sheet… open it, and so does a texture
/// becoming a sprite; a grid cut, a clip made and previewed, applied into
/// sub-assets; rectangles drawn and reverted.
/// </summary>
[System(Phases.RenderPrepare)]
internal sealed class SpriteSheetChecks : GameSystem
{
    private IEnumerator<int>? _script;
    private int _wait;
    private int _checks;
    private int _failures;
    private string _folder = "";

    public override void Start(FrameContext context)
    {
        if (!context.GameArguments.TryGetValue("check-sprite-sheet", out string? folder))
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
            ? $"SpriteSheetChecks: all {_checks} checks passed"
            : $"SpriteSheetChecks: {_failures} of {_checks} checks failed");
        context.QuitRequested = true;
    }

    private IEnumerable<int> Script()
    {
        Shell shell = Shell.Current ?? throw new InvalidOperationException("No editor shell");
        string source = Path.GetDirectoryName(AssetChecks.SourceProjectFile(shell)) ?? "";
        string copy = Path.Combine(Path.GetFullPath(_folder), "sprite-sheet-project");
        if (Directory.Exists(copy))
        {
            Directory.Delete(copy, recursive: true);
        }
        AssetChecks.CopyProject(source, copy);
        // A sheet of 4x2 cells of 8x8, each its own color.
        File.WriteAllBytes(Path.Combine(copy, "Assets", "Hero.tga"),
            Tga(32, 16, (x, y) => ((byte)(x / 8 * 60), (byte)(y / 8 * 120), 200, 255)));
        shell.OpenProject(Path.Combine(copy, Path.GetFileName(AssetChecks.SourceProjectFile(shell))));
        yield return 3;

        AssetDatabase database = shell.Database!;
        AssetId sheet = database.Find("Hero.tga")!.Id;
        InspectorPanel inspector = shell.Main.DockSpace.Panels.OfType<InspectorPanel>().Single();
        shell.InspectedAsset = sheet;
        yield return 2;
        Button? edit = inspector.ImportSettings?.SpriteSheetButton;
        Check(edit is not null, "a texture's import settings offer Edit Sprite Sheet…");
        if (edit is null)
        {
            yield break;
        }
        edit.Focus();
        yield return 1;
        ViewportChecks.Click(edit.Root!, ViewportChecks.Center(edit));
        yield return 2;
        SpriteSheetPanel? panel = Panel(shell);
        Check(panel is not null && panel.Asset == sheet, "Edit Sprite Sheet… opens the sheet");
        if (panel is null)
        {
            yield break;
        }
        shell.ClosePanel(panel);
        yield return 1;

        // The asset browser's right-click offers it too.
        AssetsPanel assets = shell.Main.DockSpace.Panels.OfType<AssetsPanel>().Single();
        assets.Group?.Activate(assets);
        assets.ShowFolder("");
        yield return 2;
        AssetTile? tile = assets.Tiles.FirstOrDefault(found => found.Path == "Hero.tga");
        Check(tile is not null, "the sheet is in the asset browser");
        if (tile is not null)
        {
            ContextMenu menu = assets.ShowMenu(tile, ViewportChecks.Center(tile));
            yield return 2;
            Check(menu.Items.OfType<MenuItem>().Any(item => item.Title == "Edit Sprite Sheet…"),
                "the asset browser's menu has Edit Sprite Sheet…");
            menu.Close();
            yield return 1;
        }

        // Becoming a sprite opens it.
        Check(Panel(shell) is null, "closed before the usage changes");
        System.Text.Json.Nodes.JsonObject settings = database.SettingsOf(sheet)!;
        settings["Usage"] = nameof(TextureUsage.Sprite);
        settings["Mipmaps"] = false;
        shell.ApplyImportSettings(sheet, settings);
        yield return 3;
        panel = Panel(shell);
        Check(panel is not null && panel.Asset == sheet, "a texture becoming a sprite opens its sheet");
        if (panel is null)
        {
            yield break;
        }
        long started = Environment.TickCount64;
        while (panel.TextureSize.Width == 0 && Environment.TickCount64 - started < 10000)
        {
            yield return 1;
        }
        Check(panel.TextureSize == (32, 16), "it shows the texture", $"{panel.TextureSize}");
        Check(panel.Frames.Count == 1 && !panel.HasChanges && !panel.ApplyButton.IsEnabled,
            "without a cell size the sheet is one frame, nothing to apply");

        // A grid, and a clip of two of its frames, previewed and applied.
        panel.Edit(s =>
        {
            s.CellWidth = 8;
            s.CellHeight = 8;
        });
        Check(panel.Frames.Count == 8, "an 8x8 grid cuts eight frames", $"{panel.Frames.Count}");
        panel.AddClip();
        panel.AddFrameToClip(1);
        panel.AddFrameToClip(6);
        Check(panel.SelectedClip == 0 && panel.Edited.Clips[0].Frames.Select(f => f.Frame).SequenceEqual([1, 6]),
            "frames are added to the selected clip");
        Check(MathF.Abs(panel.Edited.Clips[0].Frames[0].Duration - 1 / 12f) < 1e-5f, "at the clip's frame rate");
        yield return 5;
        Check(panel.Preview.FrameShown >= 0, "the preview plays the clip");
        Check(panel.HasChanges && panel.ApplyButton.IsEnabled, "edits enable Apply");
        panel.Apply();
        yield return 4;
        AssetEntry[] clips = [.. database.SubAssetsOf(sheet)];
        Check(clips.Length == 1 && clips[0].SubAssetName == "Clip" && database.ImportedFile(clips[0].Id) is not null,
            "Apply imports the clip as a sub-asset", string.Join(", ", clips.Select(c => c.SubAssetName)));
        Check(!panel.HasChanges, "after Apply there's nothing left to apply");

        // Drawing a rectangle switches to rectangles; Revert drops it.
        panel.AddRectangle(2, 3, 5, 4);
        Check(panel.Edited.Slicing == SpriteSlicing.Rectangles && panel.Frames.Count == 1 && panel.SelectedFrame == 0,
            "a drawn rectangle is the sheet's one frame, selected");
        panel.Revert();
        Check(panel.Edited.Slicing == SpriteSlicing.Grid && panel.Frames.Count == 8, "Revert brings the grid back");
        panel.GridToRectangles();
        Check(panel.Edited.Slicing == SpriteSlicing.Rectangles && panel.Frames.Count == 8 && panel.Frames[5].X == 8 &&
              panel.Frames[5].Y == 8, "a grid becomes rectangles to adjust");
        panel.Revert();
    }

    private static SpriteSheetPanel? Panel(Shell shell) =>
        shell.Windows.SelectMany(window => window.DockSpace.Panels).OfType<SpriteSheetPanel>().FirstOrDefault();

    // An uncompressed 32-bit TGA, top row first.
    private static byte[] Tga(int width, int height, Func<int, int, (byte R, byte G, byte B, byte A)> texel)
    {
        byte[] file = new byte[18 + width * height * 4];
        file[2] = 2;
        BitConverter.TryWriteBytes(file.AsSpan(12), (ushort)width);
        BitConverter.TryWriteBytes(file.AsSpan(14), (ushort)height);
        file[16] = 32;
        file[17] = 0x28;
        for (int y = 0; y < height; ++y)
        {
            for (int x = 0; x < width; ++x)
            {
                (byte r, byte g, byte b, byte a) = texel(x, y);
                int at = 18 + (y * width + x) * 4;
                (file[at], file[at + 1], file[at + 2], file[at + 3]) = (b, g, r, a);
            }
        }
        return file;
    }

    private void Check(bool passed, string what, string detail = "")
    {
        ++_checks;
        if (!passed)
        {
            ++_failures;
            Console.WriteLine($"SpriteSheetChecks: FAILED: {what} {detail}");
        }
    }
}
