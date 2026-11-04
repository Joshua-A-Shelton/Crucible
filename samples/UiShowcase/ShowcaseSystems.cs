using System.Diagnostics;
using System.Numerics;
using Crucible.Assets;
using Crucible.Assets.Importers;
using Crucible.Loop;
using Crucible.Platform;
using Crucible.Render;
using Crucible.UI;
using Png = Crucible.Samples.Diorama.Png;

namespace Crucible.Samples.UiShowcase;

/// <summary>
/// The M6 demonstration (ADR 0064): imports the project (a font, a theme),
/// then, one frame at a time, drives the menu through the same journey with
/// each input device, checking focus, values, and screens after every
/// step; then shows it at four DPI scales, checking that text and edges
/// are crisp, saving and comparing captures. Prints its checks and quits.
/// </summary>
[System(Phases.RenderPrepare)]
public sealed class ShowcaseSystem : GameSystem
{
    private const int Width = 1280, Height = 720; // the launcher's headless surface (Host.cs)
    private static readonly float[] _scales = [1, 1.25f, 1.5f, 2];

    private readonly UIRoot _root = new();
    private readonly List<string> _failures = [];
    private ShowcaseSettings _settings = ShowcaseSettings.Default;
    private string? _work;
    private IEnumerator<int>? _script;
    private int _wait;
    private int _checks;
    private string _stage = "setup";
    private FrameContext? _context;
    private readonly Dictionary<float, int> _inkHeights = [];

    public override void Start(FrameContext context)
    {
        try
        {
            _settings = ShowcaseSettings.Parse(context.GameArguments);
            Theme theme = Import();
            _root.Theme = theme;
            RenderSchedule.InsertAfter("Tonemap", "UI", _root);
            _script = Script().GetEnumerator();
        }
        catch (Exception exception)
        {
            Console.WriteLine($"UiShowcase: FAILED to set up: {exception}");
            _failures.Add("setup");
            context.QuitRequested = true;
        }
    }

    public override void Run(FrameContext context)
    {
        _context = context;
        if (_script is null || _wait-- > 0)
        {
            return;
        }
        if (_script.MoveNext())
        {
            _wait = _script.Current;
            return;
        }
        Finish(context);
        _script = null;
    }

    public override void Stop(FrameContext context)
    {
        if (RenderSchedule.StepNames.Contains("UI"))
        {
            RenderSchedule.Remove("UI");
        }
        _root.Content = null;
        RenderAssets.SetReader(null);
        if (_work is not null && Directory.Exists(_work))
        {
            Directory.Delete(_work, recursive: true);
        }
    }

    // The project's font and theme, through the importers; the theme goes after the default one.
    private Theme Import()
    {
        string source = Path.GetFullPath(_settings.Project);
        _work = Path.Combine(Path.GetTempPath(), $"crucible-uishowcase-{Environment.ProcessId}");
        CopyFolder(Path.GetDirectoryName(source)!, _work);
        Project project = Project.Open(Path.Combine(_work, Path.GetFileName(source)));
        AssetDatabase database = AssetDatabase.Open(project, [new FontImporter(), new StyleImporter()]);
        foreach (ImportResult result in database.ImportAll())
        {
            if (result.Outcome is not (ImportOutcome.Imported or ImportOutcome.UpToDate or ImportOutcome.NoImporter))
            {
                throw new InvalidOperationException($"importing {result.Path}: {result.Outcome} {result.Error}");
            }
        }
        RenderAssets.SetReader(id => database.ImportedFile(id) is { } path ? File.ReadAllBytes(path) : null);
        AssetEntry style = database.Assets.Single(asset => asset.Path.EndsWith(".crstyle", StringComparison.Ordinal));
        Theme theme = Theme.CreateDefault();
        foreach (Style rule in Theme.Load(File.ReadAllBytes(database.ImportedFile(style.Id)!)).Styles)
        {
            theme.Add(rule);
        }
        Console.WriteLine($"UiShowcase: imported {database.Assets.Count()} assets");
        return theme;
    }

    // -----------------------------------------------------------------------
    // The script: a frame count to wait after each step.
    // -----------------------------------------------------------------------

    private IEnumerable<int> Script()
    {
        if (_settings.Inputs.Length == 0)
        {
            foreach (int wait in ByHand())
            {
                yield return wait;
            }
            yield break;
        }
        foreach (string input in _settings.Inputs)
        {
            _stage = input;
            Menu menu = Fresh(1);
            yield return 1;
            IEnumerable<int> journey = input switch
            {
                "mouse" => Mouse(menu),
                "keyboard" => Keyboard(menu),
                _ => Gamepad(menu),
            };
            foreach (int wait in journey)
            {
                yield return wait;
            }
            Check(menu.Status.Text == "Ready" || input == "mouse", "nothing pressed Play by accident");
            Console.WriteLine($"UiShowcase: the {input} journey ran");
        }
        foreach (float scale in _scales)
        {
            _stage = $"scale {scale}";
            Menu menu = Fresh(scale);
            Label[] probes = AddProbes(menu);
            yield return 1;
            Stopwatch settle = Stopwatch.StartNew();
            while (_root.IsAnimating || settle.ElapsedMilliseconds < 50)
            {
                yield return 1;
            }
            yield return 2; // drawn and read back
            CheckCrisp(menu, probes, scale);
            // The options screen too, for the eye and the other backend.
            menu.ShowOptions();
            settle.Restart();
            while (_root.IsAnimating || settle.ElapsedMilliseconds < 50)
            {
                yield return 1;
            }
            yield return 2;
            Capture($"options-{ScaleName(scale)}.png", ReadPixels());
        }
    }

    // No script: the menu in the window, fed its input and scale by
    // UIInputSystem; confirming Quit quits.
    private IEnumerable<int> ByHand()
    {
        _stage = "by hand";
        Menu menu = Fresh(1);
        _root.Window = _context!.Window;
        Console.WriteLine("UiShowcase: try the menu with the mouse, keyboard, or a gamepad; Quit, then Yes, to leave");
        while (!menu.QuitConfirmed)
        {
            yield return 0;
        }
        _root.Window = default;
    }

    // The journey every device makes: Options; switch subtitles on; volume
    // up; difficulty to Hard; quality to High; back; open Quit and cancel.

    private IEnumerable<int> Mouse(Menu menu)
    {
        Send(Motion(menu.Play));
        yield return 1;
        Check((menu.Play.State & UIState.Hover) != 0, "the pointer hovers Play");
        Send(Click(menu.Play));
        yield return 1;
        Check(menu.Played && menu.Status.Text == "Play pressed", "a click presses Play");

        Send(Click(menu.OptionsButton));
        yield return 1;
        Check(menu.Screen == "options", "a click opens Options");
        Send(Click(menu.Subtitles));
        yield return 1;
        Check(menu.Subtitles.IsOn, "a click switches subtitles on");
        Send(Click(SliderPoint(menu.Volume, 0.75f)));
        yield return 1;
        Check(menu.Volume.Value == 75, "a click on the slider sets the volume", menu.Volume.Value.ToString());
        Send(Click(Descendant<SpinnerArrow>(menu.Difficulty, arrow => arrow.Classes.Contains("next"))!));
        yield return 1;
        Check(menu.Difficulty.SelectedOption == "Hard", "the spinner's arrow steps it", menu.Difficulty.SelectedOption);
        Send(Click(menu.Quality));
        yield return 1;
        Check(menu.Quality.IsOpen, "a click opens the dropdown");
        Send(Click(Descendant<ListItem>(menu.Quality.Popup, item => item.Index == 2)!));
        yield return 1;
        Check(!menu.Quality.IsOpen && menu.Quality.SelectedOption == "High", "a click picks High",
            menu.Quality.SelectedOption);
        Send(Click(menu.Back));
        yield return 1;
        Check(menu.Screen == "main", "Back returns to the main screen");

        Send(Click(menu.Quit));
        yield return 1;
        Check(menu.QuitDialog.IsOpen && _root.FocusedElement == menu.No, "Quit asks, with No focused");
        Send(Click(menu.No));
        yield return 1;
        Check(!menu.QuitDialog.IsOpen && !menu.QuitConfirmed, "No closes the dialog without quitting");
    }

    private IEnumerable<int> Keyboard(Menu menu)
    {
        const int enter = 40, escape = 41, space = 44, right = 79, down = 81;
        Check(_root.FocusedElement == menu.Play, "Play starts focused");
        Send(Key(down));
        yield return 1;
        Check(_root.FocusedElement == menu.OptionsButton, "Down moves to Options");
        Send(Key(enter));
        yield return 1;
        Check(menu.Screen == "options" && _root.FocusedElement == menu.Subtitles, "Enter opens Options, focused on Subtitles");
        Send(Key(space));
        yield return 1;
        Check(menu.Subtitles.IsOn, "Space switches subtitles on");
        Send(Key(down), Key(right), Key(right), Key(right));
        yield return 1;
        Check(_root.FocusedElement == menu.Volume && menu.Volume.Value == 65, "Right raises the volume a step at a time",
            menu.Volume.Value.ToString());
        Send(Key(down), Key(right));
        yield return 1;
        Check(menu.Difficulty.SelectedOption == "Hard", "Right steps the spinner", menu.Difficulty.SelectedOption);
        Send(Key(down), Key(enter));
        yield return 1;
        Check(menu.Quality.IsOpen && _root.FocusedElement is ListItem { Index: 1 }, "Enter opens the dropdown on Medium");
        Send(Key(down), Key(enter));
        yield return 1;
        Check(!menu.Quality.IsOpen && menu.Quality.SelectedOption == "High" && _root.FocusedElement == menu.Quality,
            "Down and Enter pick High, focus back on the dropdown", menu.Quality.SelectedOption);
        Send(Key(escape));
        yield return 1;
        Check(menu.Screen == "main" && _root.FocusedElement == menu.OptionsButton, "Escape goes back, to Options");

        Send(Key(down), Key(enter));
        yield return 1;
        Check(menu.QuitDialog.IsOpen && _root.FocusedElement == menu.No, "Enter on Quit asks, with No focused");
        Send(Key(escape));
        yield return 1;
        Check(!menu.QuitDialog.IsOpen && _root.FocusedElement == menu.Quit, "Escape closes it, focus back on Quit");
    }

    private IEnumerable<int> Gamepad(Menu menu)
    {
        Send(Pad(GamepadButton.DpadDown), Pad(GamepadButton.DpadDown, false));
        yield return 1;
        Check(_root.FocusedElement == menu.OptionsButton, "the D-pad moves to Options");
        Send(Pad(GamepadButton.South), Pad(GamepadButton.South, false));
        yield return 1;
        Check(menu.Screen == "options", "South opens Options");
        Send(Pad(GamepadButton.South), Pad(GamepadButton.South, false));
        yield return 1;
        Check(menu.Subtitles.IsOn, "South switches subtitles on");
        // The stick, past its threshold and back, steps once each time.
        Send(Stick(GamepadAxis.LeftY, 0.9f), Stick(GamepadAxis.LeftY, 0));
        Send(Stick(GamepadAxis.LeftX, 0.9f), Stick(GamepadAxis.LeftX, 0), Stick(GamepadAxis.LeftX, 0.9f),
            Stick(GamepadAxis.LeftX, 0));
        yield return 1;
        Check(_root.FocusedElement == menu.Volume && menu.Volume.Value == 60, "the stick raises the volume",
            menu.Volume.Value.ToString());
        Send(Pad(GamepadButton.DpadDown), Pad(GamepadButton.DpadDown, false), Pad(GamepadButton.DpadRight),
            Pad(GamepadButton.DpadRight, false));
        yield return 1;
        Check(menu.Difficulty.SelectedOption == "Hard", "the D-pad steps the spinner", menu.Difficulty.SelectedOption);
        Send(Pad(GamepadButton.DpadDown), Pad(GamepadButton.DpadDown, false), Pad(GamepadButton.South),
            Pad(GamepadButton.South, false));
        yield return 1;
        Check(menu.Quality.IsOpen, "South opens the dropdown");
        Send(Pad(GamepadButton.DpadDown), Pad(GamepadButton.DpadDown, false), Pad(GamepadButton.South),
            Pad(GamepadButton.South, false));
        yield return 1;
        Check(!menu.Quality.IsOpen && menu.Quality.SelectedOption == "High", "the D-pad and South pick High",
            menu.Quality.SelectedOption);
        Send(Pad(GamepadButton.East), Pad(GamepadButton.East, false));
        yield return 1;
        Check(menu.Screen == "main", "East goes back");

        Send(Pad(GamepadButton.DpadDown), Pad(GamepadButton.DpadDown, false), Pad(GamepadButton.South),
            Pad(GamepadButton.South, false));
        yield return 1;
        Check(menu.QuitDialog.IsOpen && _root.FocusedElement == menu.No, "South on Quit asks, with No focused");
        Send(Pad(GamepadButton.East), Pad(GamepadButton.East, false));
        yield return 1;
        Check(!menu.QuitDialog.IsOpen && _root.FocusedElement == menu.Quit, "East closes it, focus back on Quit");
    }

    // -----------------------------------------------------------------------
    // Crispness
    // -----------------------------------------------------------------------

    // The same text three times down the top left corner (clear of the
    // menu at every scale), drawn moved by logical offsets across and down
    // that fall between pixels. A render offset isn't snapped by layout, so
    // only the text's own placement can keep them crisp.
    private static readonly float[] _probeOffsets = [0, 0.3f, 0.7f];

    private static Label[] AddProbes(Menu menu)
    {
        Label[] probes = new Label[_probeOffsets.Length];
        for (int i = 0; i < probes.Length; ++i)
        {
            probes[i] = new Label("Crisp 0123") { RenderOffset = new Vector2(_probeOffsets[i]) };
            probes[i].Classes.Add("probe");
            AnchorPanel.SetAnchors(probes[i], Anchors.At(Vector2.Zero, new Vector2(16, 16 + i * 40)));
            menu.Page.Add(probes[i]);
        }
        return probes;
    }

    private void CheckCrisp(Menu menu, Label[] probes, float scale)
    {
        byte[] bgra = ReadPixels();
        (int R, int G, int B) Pixel(int x, int y)
        {
            int i = (Math.Clamp(y, 0, Height - 1) * Width + Math.Clamp(x, 0, Width - 1)) * 4;
            return (bgra[i + 2], bgra[i + 1], bgra[i]);
        }

        // Shift invariance: each probe's pixels, from its snapped origin,
        // are the first probe's exactly.
        System.Drawing.RectangleF first = probes[0].Bounds;
        int w = (int)MathF.Ceiling(first.Width * scale) + 2, h = (int)MathF.Ceiling(first.Height * scale) + 2;
        (int R, int G, int B)[] Block(Label probe)
        {
            // Where the text lands: its origin, offset, rounded to a pixel.
            Vector2 origin = (new Vector2(probe.Bounds.X, probe.Bounds.Y) + probe.RenderOffset) * scale;
            int x0 = (int)MathF.Round(origin.X) - 1, y0 = (int)MathF.Round(origin.Y) - 1;
            (int, int, int)[] block = new (int, int, int)[w * h];
            for (int y = 0; y < h; ++y)
            {
                for (int x = 0; x < w; ++x)
                {
                    block[y * w + x] = Pixel(x0 + x, y0 + y);
                }
            }
            return block;
        }
        (int R, int G, int B)[] reference = Block(probes[0]);
        int ink = reference.Count(p => p.R > 128);
        Check(ink > 40 * scale, $"the probe text is drawn at {scale}x", $"{ink} light pixels");
        for (int i = 1; i < probes.Length; ++i)
        {
            (int R, int G, int B)[] block = Block(probes[i]);
            int differing = block.Zip(reference).Count(pair => pair.First != pair.Second);
            Check(differing == 0, $"text placed between pixels lands crisp at {scale}x (probe {i})",
                $"{differing} pixels differ");
        }
        // Text grows with the scale: the ink's height in physical pixels.
        int rows = Enumerable.Range(0, h).Count(y => Enumerable.Range(0, w).Any(x => reference[y * w + x].R > 128));
        _inkHeights[scale] = rows;
        if (_inkHeights.TryGetValue(1, out int atOne) && scale != 1)
        {
            float ratio = (float)rows / atOne;
            Check(MathF.Abs(ratio - scale) < scale * 0.15f, $"text is {scale}x as tall", $"{rows} rows against {atOne}");
        }

        // A panel's edge: the pixel outside is the backdrop, the one inside the panel, no blend.
        System.Drawing.RectangleF panel = menu.Main.Bounds;
        int edge = (int)MathF.Round(panel.X * scale), mid = (int)((panel.Y + panel.Height / 2) * scale);
        Check(Near(Pixel(edge - 1, mid), (0x1b, 0x1e, 0x24), 1), $"outside the panel's edge is the backdrop at {scale}x",
            Pixel(edge - 1, mid).ToString());
        Check(Near(Pixel(edge, mid), (0x3a, 0x40, 0x4b), 2), $"its edge pixel is its border, unblended at {scale}x",
            Pixel(edge, mid).ToString());

        Capture($"scale-{ScaleName(scale)}.png", bgra);
    }

    private static string ScaleName(float scale) => scale.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private byte[] ReadPixels()
    {
        byte[] bgra = new byte[Width * Height * 4];
        Render.Api.ReadSurfacePixels(_context!.Surface, bgra);
        return bgra;
    }

    // Saved, and compared with the other backend's, when asked.
    private void Capture(string name, byte[] bgra)
    {
        if (_settings.Capture is not { } folder)
        {
            return;
        }
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, name);
        byte[] rgb = new byte[Width * Height * 3];
        for (int i = 0; i < Width * Height; ++i)
        {
            rgb[i * 3] = bgra[i * 4 + 2];
            rgb[i * 3 + 1] = bgra[i * 4 + 1];
            rgb[i * 3 + 2] = bgra[i * 4];
        }
        Png.Write(path, Width, Height, rgb);
        if (_settings.Compare is { } other)
        {
            Compare(path, Path.Combine(other, name), rgb);
        }
    }

    // ADR 0056's tolerance between backends.
    private void Compare(string path, string reference, byte[] rgb)
    {
        (int width, int height, byte[] other) = Png.Read(reference);
        if (!Check(width == Width && height == Height, $"{Path.GetFileName(path)} matches the other capture's size"))
        {
            return;
        }
        double sum = 0;
        int far = 0, largest = 0;
        for (int p = 0; p < Width * Height; ++p)
        {
            int worst = 0;
            for (int c = 0; c < 3; ++c)
            {
                int d = Math.Abs(rgb[p * 3 + c] - other[p * 3 + c]);
                sum += d;
                worst = Math.Max(worst, d);
            }
            far += worst > 8 ? 1 : 0;
            largest = Math.Max(largest, worst);
        }
        double mean = sum / rgb.Length, farShare = 100.0 * far / (Width * Height);
        Console.WriteLine($"UiShowcase: {Path.GetFileName(path)} against {reference}: mean {mean:F3}/255, " +
                          $"{farShare:F3}% off by more than 8/255, largest {largest}/255");
        Check(mean < 1.0, $"{Path.GetFileName(path)}: the mean difference is under 1/255", $"{mean:F3}");
        Check(farShare < 0.5, $"{Path.GetFileName(path)}: under 0.5% of pixels differ by more than 8/255", $"{farShare:F3}%");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private Menu Fresh(float scale)
    {
        _root.Content = null;
        _root.Scale = scale;
        Menu menu = new();
        _root.Content = menu.Page;
        _root.UpdateLayout(new Vector2(Width, Height));
        menu.Start();
        return menu;
    }

    private void Send(params InputEvent[] events)
    {
        // Scripted runs are headless: the surface is the launcher's 1280x720.
        _root.UpdateLayout(new Vector2(Width, Height));
        _root.ProcessInput(events);
        _root.UpdateLayout(new Vector2(Width, Height));
    }

    private Vector2 Physical(Vector2 logical) => logical * _root.Scale;

    private static Vector2 Center(UIElement element) =>
        new(element.Bounds.X + element.Bounds.Width / 2, element.Bounds.Y + element.Bounds.Height / 2);

    // Where the slider's thumb center sits at a fraction of its travel.
    private static Vector2 SliderPoint(Slider slider, float fraction)
    {
        float thumb = slider.Thumb.DesiredSize.X;
        System.Drawing.RectangleF b = slider.Bounds;
        float travel = b.Width - slider.Padding.Horizontal - thumb;
        return new Vector2(b.X + slider.Padding.Left + thumb / 2 + fraction * travel, b.Y + b.Height / 2);
    }

    private InputEvent Motion(UIElement element) => Motion(Center(element));

    private InputEvent Motion(Vector2 logical)
    {
        Vector2 at = Physical(logical);
        return new InputEvent { Type = InputEventType.MouseMotion, X = at.X, Y = at.Y };
    }

    private InputEvent[] Click(UIElement element) => Click(Center(element));

    private InputEvent[] Click(Vector2 logical)
    {
        Vector2 at = Physical(logical);
        return
        [
            new InputEvent { Type = InputEventType.MouseMotion, X = at.X, Y = at.Y },
            new InputEvent { Type = InputEventType.MouseButtonDown, X = at.X, Y = at.Y, Code = 1 },
            new InputEvent { Type = InputEventType.MouseButtonUp, X = at.X, Y = at.Y, Code = 1 },
        ];
    }

    private static InputEvent Key(int scancode) => new() { Type = InputEventType.KeyDown, Code = scancode };

    private static InputEvent Pad(GamepadButton button, bool down = true) =>
        new() { Type = down ? InputEventType.GamepadButtonDown : InputEventType.GamepadButtonUp, Code = (int)button };

    private static InputEvent Stick(GamepadAxis axis, float value) =>
        new() { Type = InputEventType.GamepadAxis, Code = (int)axis, X = value };

    private static T? Descendant<T>(UIElement element, Func<T, bool> match) where T : UIElement
    {
        if (element is T typed && match(typed))
        {
            return typed;
        }
        foreach (UIElement child in element.Children)
        {
            if (Descendant(child, match) is { } found)
            {
                return found;
            }
        }
        return null;
    }

    private static bool Near((int R, int G, int B) p, (int R, int G, int B) q, int tolerance) =>
        Math.Abs(p.R - q.R) <= tolerance && Math.Abs(p.G - q.G) <= tolerance && Math.Abs(p.B - q.B) <= tolerance;

    private bool Check(bool passed, string what, string? detail = null)
    {
        ++_checks;
        if (!passed)
        {
            _failures.Add($"[{_stage}] {what}" + (detail is null ? "" : $" ({detail})"));
        }
        return passed;
    }

    private void Finish(FrameContext context)
    {
        if (_failures.Count > 0)
        {
            foreach (string failure in _failures)
            {
                Console.WriteLine($"UiShowcase: FAILED: {failure}");
            }
            Console.WriteLine($"UiShowcase: {_failures.Count} of {_checks} checks failed");
        }
        else
        {
            Console.WriteLine($"UiShowcase: all {_checks} checks passed");
        }
        context.QuitRequested = true;
    }

    private static void CopyFolder(string from, string to)
    {
        foreach (string directory in Directory.GetDirectories(from, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(directory.Replace(from, to));
        }
        Directory.CreateDirectory(to);
        foreach (string file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
        {
            File.Copy(file, file.Replace(from, to), overwrite: true);
        }
    }
}
