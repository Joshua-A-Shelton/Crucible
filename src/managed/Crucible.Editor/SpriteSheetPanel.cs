using System.Diagnostics;
using System.Numerics;
using System.Text;
using System.Text.Json.Nodes;
using Crucible.Assets;
using Crucible.Assets.Importers;
using Crucible.Render;
using Crucible.Serialization;
using Crucible.UI;

namespace Crucible.Editor;

/// <summary>
/// The Sprite Sheet Editor (ADR 0107): a texture's sprite sheet settings,
/// edited where the sheet can be seen. The sheet is drawn zoomed with
/// square pixels (the wheel zooms, the middle or right button pans), its
/// frames outlined; a click selects one. Slicing is a grid (cell size,
/// margin, spacing) or rectangles, drawn by dragging. Clips list frames,
/// each with a duration and events, played in a preview. Apply saves them
/// into the texture's import settings, which imports it again; Revert
/// drops the edits.
/// </summary>
public sealed class SpriteSheetPanel : EditorPanel
{
    private const int ScancodeDelete = 76, ScancodeBackspace = 42;

    private readonly Shell _shell;
    private readonly Label _title = new() { Wrap = true };
    private readonly Label _status = new() { Wrap = true };
    private readonly ComboBox _slicing = new() { Options = ["Grid", "Rectangles"] };
    private readonly NumberField _cellWidth = Whole(), _cellHeight = Whole(), _margin = Whole(), _spacing = Whole();
    private readonly NumberField _x = Whole(), _y = Whole(), _width = Whole(), _height = Whole();
    private readonly StackPanel _gridFields = new() { Spacing = 3 };
    private readonly StackPanel _rectangleFields = new() { Spacing = 3 };
    private readonly StackPanel _clips = new() { Spacing = 3 };
    private readonly StackPanel _clipFrames = new() { Spacing = 3 };
    private readonly StackPanel _clipEditor = new() { Spacing = 4 };
    private readonly TextField _clipName = new() { MinWidth = 0 };
    private readonly NumberField _framesPerSecond = new() { MinWidth = 0, Width = 64, Minimum = 0.1, Step = 1 };
    private readonly Toggle _loop = new("Loop");
    private readonly Button _apply = new("Apply");
    private readonly Button _revert = new("Revert");
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private JsonObject? _applied;
    private TextureImportSettings _edited = new();
    private TextureHandle _texture;
    private bool _refreshing;
    private double _lastTick;

    internal SpriteSheetPanel(Shell shell) : base("SpriteSheet", "Sprite Sheet Editor")
    {
        _shell = shell;
        _status.Classes.Add("hint");
        View = new SheetView(this);
        Preview = new PreviewView(this);

        _slicing.SelectionChanged += box =>
        {
            if (!_refreshing && box.SelectedIndex >= 0)
            {
                Edit(s => s.Slicing = box.SelectedIndex == 1 ? SpriteSlicing.Rectangles : SpriteSlicing.Grid);
            }
        };
        Bind(_cellWidth, (s, v) => s.CellWidth = v);
        Bind(_cellHeight, (s, v) => s.CellHeight = v);
        Bind(_margin, (s, v) => s.Margin = v);
        Bind(_spacing, (s, v) => s.Spacing = v);
        BindFrame(_x, (f, v) => f.X = v);
        BindFrame(_y, (f, v) => f.Y = v);
        BindFrame(_width, (f, v) => f.Width = Math.Max(v, 1));
        BindFrame(_height, (f, v) => f.Height = Math.Max(v, 1));
        Button toRectangles = new("Make Rectangles");
        toRectangles.Clicked += _ => GridToRectangles();
        _gridFields.Add(Row("Cell Width", _cellWidth));
        _gridFields.Add(Row("Cell Height", _cellHeight));
        _gridFields.Add(Row("Margin", _margin));
        _gridFields.Add(Row("Spacing", _spacing));
        _gridFields.Add(toRectangles);
        Button removeFrame = new("Remove Frame");
        removeFrame.Clicked += _ => RemoveSelectedRectangle();
        _rectangleFields.Add(Hint("Drag on the sheet to draw a frame."));
        _rectangleFields.Add(Row("X", _x));
        _rectangleFields.Add(Row("Y", _y));
        _rectangleFields.Add(Row("Width", _width));
        _rectangleFields.Add(Row("Height", _height));
        _rectangleFields.Add(removeFrame);

        Button addClip = new("Add Clip");
        addClip.Clicked += _ => AddClip();
        _clipName.Committed += field => EditClip(c => c.Name = field.Text.Trim());
        _framesPerSecond.ValueChanged += field =>
        {
            if (!_refreshing)
            {
                EditClip(c =>
                {
                    // Frames at the old rate's duration follow the new one.
                    float old = 1 / c.FramesPerSecond, now = 1 / (float)field.Value;
                    foreach (SpriteClipFrameSettings frame in c.Frames.Where(f => MathF.Abs(f.Duration - old) < 1e-4f))
                    {
                        frame.Duration = now;
                    }
                    c.FramesPerSecond = (float)field.Value;
                });
            }
        };
        _loop.Changed += toggle =>
        {
            if (!_refreshing)
            {
                EditClip(c => c.Loop = toggle.IsOn);
            }
        };
        Button addSelected = new("Add Selected Frame");
        addSelected.Clicked += _ => AddFrameToClip(SelectedFrame);
        Button addEvery = new("Add Every Frame");
        addEvery.Clicked += _ => AddEveryFrame();
        Button removeClip = new("Remove Clip");
        removeClip.Clicked += _ => RemoveClip();
        _clipEditor.Add(Row("Name", _clipName));
        _clipEditor.Add(Row("Frames/Second", _framesPerSecond));
        _clipEditor.Add(_loop);
        _clipEditor.Add(Preview);
        _clipEditor.Add(_clipFrames);
        StackPanel adders = new() { Orientation = Orientation.Horizontal, Spacing = 6 };
        adders.Add(addSelected);
        adders.Add(addEvery);
        _clipEditor.Add(adders);
        _clipEditor.Add(removeClip);

        _apply.Classes.Add("primary");
        _apply.Clicked += _ => Apply();
        _revert.Clicked += _ => Revert();
        StackPanel buttons = new() { Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.End };
        buttons.Add(_revert);
        buttons.Add(_apply);

        StackPanel side = new() { Spacing = 8, Width = 300 };
        side.Add(_title);
        side.Add(Heading("Slicing"));
        side.Add(_slicing);
        side.Add(_gridFields);
        side.Add(_rectangleFields);
        side.Add(Heading("Clips"));
        side.Add(_clips);
        side.Add(addClip);
        side.Add(_clipEditor);
        side.Add(buttons);
        side.Add(_status);
        ScrollView scroll = new() { Content = side, Padding = new Thickness(8) };
        DockPanel.SetDock(scroll, Dock.Right);
        DockPanel layout = new();
        layout.Add(scroll);
        layout.Add(View);
        AddChild(layout);
        Refresh();
    }

    /// <summary>The texture shown, or none.</summary>
    public AssetId Asset { get; private set; }

    /// <summary>The sheet, zoomed and panned.</summary>
    internal SheetView View { get; }

    /// <summary>The selected clip, played.</summary>
    internal PreviewView Preview { get; }

    /// <summary>The settings as edited, not yet applied.</summary>
    public TextureImportSettings Edited => _edited;

    /// <summary>Whether there are edits not yet applied.</summary>
    public bool HasChanges => _applied is not null && !JsonNode.DeepEquals(ToJson(_edited), _applied);

    public int SelectedFrame { get; private set; } = -1;

    public int SelectedClip { get; private set; } = -1;

    public Button ApplyButton => _apply;

    /// <summary>The texture's size in pixels; zero until it's loaded.</summary>
    public (int Width, int Height) TextureSize
    {
        get
        {
            TextureSize size = _texture.Generation != 0 ? Render.Api.TextureSize(_texture) : default;
            return ((int)size.Width, (int)size.Height);
        }
    }

    /// <summary>The frames the settings, as edited, cut the sheet into.</summary>
    public IReadOnlyList<SpriteFrameSettings> Frames
    {
        get
        {
            (int width, int height) = TextureSize;
            return width > 0 ? TextureImporter.FramesOf(_edited, width, height) : [];
        }
    }

    /// <summary>Shows a texture's sheet; edits to another not yet applied are dropped.</summary>
    public void Show(AssetId asset)
    {
        if (asset == Asset && _applied is not null)
        {
            return;
        }
        ReleaseTexture();
        Asset = asset;
        _applied = _shell.Database?.SettingsOf(asset);
        _edited = _applied is null ? new TextureImportSettings() : FromJson(_applied);
        SelectedFrame = -1;
        SelectedClip = _edited.Clips.Count > 0 ? 0 : -1;
        if (_applied is not null && _shell.Database?.ImportedFile(asset) is not null)
        {
            _texture = RenderAssets.AcquireTexture(asset);
        }
        View.FitNextDraw();
        Refresh();
    }

    /// <summary>Saves the edits into the texture's import settings, which imports it again.</summary>
    public void Apply()
    {
        if (HasChanges)
        {
            _shell.ApplyImportSettings(Asset, ToJson(_edited));
        }
    }

    /// <summary>Drops the edits.</summary>
    public void Revert()
    {
        if (_applied is not null)
        {
            _edited = FromJson(_applied);
            SelectedClip = Math.Min(SelectedClip, _edited.Clips.Count - 1);
            Refresh();
        }
    }

    /// <summary>Each frame: the preview moves on, and settings applied elsewhere are read again.</summary>
    public override void Tick()
    {
        double now = _clock.Elapsed.TotalSeconds;
        Preview.Advance(now - _lastTick);
        _lastTick = now;
        if (Asset.IsNone || _shell.Database is not { } database)
        {
            return;
        }
        JsonObject? applied = database.SettingsOf(Asset);
        if (applied is not null && _applied is not null && !JsonNode.DeepEquals(applied, _applied))
        {
            bool changed = HasChanges;
            _applied = applied;
            if (!changed)
            {
                _edited = FromJson(applied);
                Refresh();
            }
        }
        if (_texture.Generation == 0 && database.ImportedFile(Asset) is not null)
        {
            _texture = RenderAssets.AcquireTexture(Asset);
        }
        UpdateButtons();
    }

    protected override void OnDetached(UIRoot root) => ReleaseTexture();

    internal TextureHandle Texture => _texture;

    // --- Edits -------------------------------------------------------------

    internal void SelectFrame(int frame)
    {
        SelectedFrame = frame;
        Refresh();
    }

    /// <summary>A new rectangle (switching to rectangles), selected.</summary>
    public void AddRectangle(int x, int y, int width, int height)
    {
        Edit(s =>
        {
            s.Slicing = SpriteSlicing.Rectangles;
            s.Frames.Add(new SpriteFrameSettings { X = x, Y = y, Width = Math.Max(width, 1), Height = Math.Max(height, 1) });
        });
        SelectedFrame = _edited.Frames.Count - 1;
        Refresh();
    }

    /// <summary>The grid's cells become rectangles, to adjust one by one.</summary>
    public void GridToRectangles()
    {
        List<SpriteFrameSettings> cells = [.. Frames.Select(f => new SpriteFrameSettings { X = f.X, Y = f.Y, Width = f.Width, Height = f.Height })];
        Edit(s =>
        {
            s.Slicing = SpriteSlicing.Rectangles;
            s.Frames = cells;
        });
    }

    public void AddClip()
    {
        Edit(s =>
        {
            string name = "Clip";
            for (int n = 2; s.Clips.Any(c => c.Name == name); ++n)
            {
                name = $"Clip {n}";
            }
            s.Clips.Add(new SpriteClipSettings { Name = name });
        });
        SelectedClip = _edited.Clips.Count - 1;
        Refresh();
    }

    public void SelectClip(int clip)
    {
        SelectedClip = clip;
        Preview.Restart();
        Refresh();
    }

    /// <summary>Adds the sheet's frame to the selected clip, shown for one frame at its rate.</summary>
    public void AddFrameToClip(int frame)
    {
        if (frame < 0 || frame >= Frames.Count)
        {
            _status.Text = "Select a frame on the sheet first.";
            return;
        }
        EditClip(c => c.Frames.Add(new SpriteClipFrameSettings { Frame = frame, Duration = 1 / c.FramesPerSecond }));
    }

    private void AddEveryFrame()
    {
        int count = Frames.Count;
        EditClip(c =>
        {
            for (int f = 0; f < count; ++f)
            {
                c.Frames.Add(new SpriteClipFrameSettings { Frame = f, Duration = 1 / c.FramesPerSecond });
            }
        });
    }

    private void RemoveClip()
    {
        if (SelectedClip < 0)
        {
            return;
        }
        int at = SelectedClip;
        Edit(s => s.Clips.RemoveAt(at));
        SelectedClip = Math.Min(at, _edited.Clips.Count - 1);
        Refresh();
    }

    private void RemoveSelectedRectangle()
    {
        if (_edited.Slicing != SpriteSlicing.Rectangles || SelectedFrame < 0 || SelectedFrame >= _edited.Frames.Count)
        {
            return;
        }
        int at = SelectedFrame;
        Edit(s => s.Frames.RemoveAt(at));
        SelectedFrame = -1;
        Refresh();
    }

    internal void DeleteKey() => RemoveSelectedRectangle();

    internal void Edit(Action<TextureImportSettings> change)
    {
        if (_applied is null)
        {
            return;
        }
        change(_edited);
        Refresh();
    }

    private void EditClip(Action<SpriteClipSettings> change)
    {
        if (SelectedClip >= 0 && SelectedClip < _edited.Clips.Count)
        {
            Edit(s => change(s.Clips[SelectedClip]));
        }
    }

    // --- Showing -----------------------------------------------------------

    private void Refresh()
    {
        _refreshing = true;
        AssetEntry? entry = Asset.IsNone ? null : _shell.Database?.Find(Asset);
        bool shown = entry is not null && _applied is not null;
        _title.Text = shown ? entry!.Path : "No texture: double-click one in the Assets panel, or use Edit Sprite Sheet… in its Inspector.";
        bool rectangles = _edited.Slicing == SpriteSlicing.Rectangles;
        _slicing.SelectedIndex = rectangles ? 1 : 0;
        _cellWidth.Value = _edited.CellWidth;
        _cellHeight.Value = _edited.CellHeight;
        _margin.Value = _edited.Margin;
        _spacing.Value = _edited.Spacing;
        _gridFields.Visibility = shown && !rectangles ? Visibility.Visible : Visibility.Collapsed;
        _rectangleFields.Visibility = shown && rectangles ? Visibility.Visible : Visibility.Collapsed;
        if (rectangles && SelectedFrame >= 0 && SelectedFrame < _edited.Frames.Count)
        {
            SpriteFrameSettings frame = _edited.Frames[SelectedFrame];
            _x.Value = frame.X;
            _y.Value = frame.Y;
            _width.Value = frame.Width;
            _height.Value = frame.Height;
        }

        _clips.Clear();
        for (int i = 0; i < _edited.Clips.Count; ++i)
        {
            int at = i;
            SpriteClipSettings clip = _edited.Clips[i];
            Button pick = new($"{(clip.Name.Length > 0 ? clip.Name : "(no name)")}  ·  {clip.Frames.Count} frames")
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            pick.Classes.Set("primary", i == SelectedClip);
            pick.Clicked += _ => SelectClip(at);
            _clips.Add(pick);
        }
        if (_edited.Clips.Count == 0 && shown)
        {
            _clips.Add(Hint("No clips yet."));
        }
        SpriteClipSettings? selected = SelectedClip >= 0 && SelectedClip < _edited.Clips.Count ? _edited.Clips[SelectedClip] : null;
        _clipEditor.Visibility = selected is not null ? Visibility.Visible : Visibility.Collapsed;
        _clipFrames.Clear();
        if (selected is not null)
        {
            _clipName.Text = selected.Name;
            _framesPerSecond.Value = selected.FramesPerSecond;
            _loop.IsOn = selected.Loop;
            for (int i = 0; i < selected.Frames.Count; ++i)
            {
                _clipFrames.Add(FrameRow(selected, i));
            }
            if (selected.Frames.Count == 0)
            {
                _clipFrames.Add(Hint("Select a frame on the sheet and add it, or double-click one."));
            }
        }
        _refreshing = false;
        _status.Text = Describe();
        UpdateButtons();
        View.InvalidateVisual();
    }

    private string Describe()
    {
        if (Asset.IsNone)
        {
            return "";
        }
        (int width, int height) = TextureSize;
        string size = width > 0 ? $"{width}x{height}, {Frames.Count} frames" : "Loading…";
        if (SelectedFrame >= 0 && SelectedFrame < Frames.Count)
        {
            SpriteFrameSettings f = Frames[SelectedFrame];
            size += $". Frame {SelectedFrame}: {f.Width}x{f.Height} at ({f.X}, {f.Y})";
        }
        return size;
    }

    private void UpdateButtons()
    {
        UIState disabled = HasChanges ? 0 : UIState.Disabled;
        _apply.State = (_apply.State & ~UIState.Disabled) | disabled;
        _revert.State = (_revert.State & ~UIState.Disabled) | disabled;
    }

    // A clip's frame: which of the sheet's, how long, its events (by name,
    // separated by commas), and moving or removing it.
    private UIElement FrameRow(SpriteClipSettings clip, int index)
    {
        SpriteClipFrameSettings frame = clip.Frames[index];
        NumberField which = Whole();
        which.Width = 48;
        which.Value = frame.Frame;
        which.ValueChanged += field => EditClip(c => c.Frames[index].Frame = (int)field.Value);
        NumberField duration = new() { MinWidth = 0, Width = 64, Minimum = 0.001, Step = 0.01, Value = frame.Duration };
        duration.ValueChanged += field => EditClip(c => c.Frames[index].Duration = (float)field.Value);
        TextField events = new(string.Join(", ", frame.Events)) { MinWidth = 0, Width = 90 };
        events.Committed += field => EditClip(c => c.Frames[index].Events =
            [.. field.Text.Split(',').Select(e => e.Trim()).Where(e => e.Length > 0)]);
        IconButton up = new("arrow-up", "Move Up");
        up.Clicked += _ => EditClip(c => (c.Frames[index - 1], c.Frames[index]) = (c.Frames[index], c.Frames[index - 1]));
        up.State |= index == 0 ? UIState.Disabled : 0;
        IconButton down = new("arrow-down", "Move Down");
        down.Clicked += _ => EditClip(c => (c.Frames[index], c.Frames[index + 1]) = (c.Frames[index + 1], c.Frames[index]));
        down.State |= index + 1 == clip.Frames.Count ? UIState.Disabled : 0;
        IconButton remove = new("x", "Remove");
        remove.Clicked += _ => EditClip(c => c.Frames.RemoveAt(index));
        StackPanel row = new() { Orientation = Orientation.Horizontal, Spacing = 4 };
        row.Add(which);
        row.Add(duration);
        row.Add(events);
        row.Add(up);
        row.Add(down);
        row.Add(remove);
        return row;
    }

    private void Bind(NumberField field, Action<TextureImportSettings, int> set) =>
        field.ValueChanged += f =>
        {
            if (!_refreshing)
            {
                Edit(s => set(s, (int)f.Value));
            }
        };

    private void BindFrame(NumberField field, Action<SpriteFrameSettings, int> set) =>
        field.ValueChanged += f =>
        {
            if (!_refreshing && SelectedFrame >= 0 && SelectedFrame < _edited.Frames.Count)
            {
                int at = SelectedFrame;
                Edit(s => set(s.Frames[at], (int)f.Value));
            }
        };

    private void ReleaseTexture()
    {
        if (_texture.Generation != 0)
        {
            RenderAssets.ReleaseTexture(_texture);
            _texture = default;
        }
    }

    private static Vector2 Snap(Vector2 v) => new(MathF.Round(v.X), MathF.Round(v.Y));

    private static NumberField Whole() => new() { MinWidth = 0, Width = 64, Minimum = 0, Step = 1 };

    private static UIElement Row(string label, UIElement field)
    {
        Label name = new(label) { Width = 100, VerticalAlignment = VerticalAlignment.Center };
        DockPanel row = new();
        DockPanel.SetDock(name, Dock.Left);
        row.Add(name);
        row.Add(field);
        return row;
    }

    private static Label Heading(string text)
    {
        Label heading = new(text);
        heading.Classes.Add("field-header");
        return heading;
    }

    private static Label Hint(string text)
    {
        Label hint = new(text) { Wrap = true };
        hint.Classes.Add("hint");
        return hint;
    }

    private static TextureImportSettings FromJson(JsonObject json) =>
        Json.Deserialize<TextureImportSettings>(Encoding.UTF8.GetBytes(json.ToJsonString()));

    private static JsonObject ToJson(TextureImportSettings settings) =>
        (JsonObject)JsonNode.Parse(Json.Serialize(settings))!;

    // -----------------------------------------------------------------------

    /// <summary>
    /// The sheet: a whole-number zoom so every texel is a square of screen
    /// pixels, frames outlined, the selected one in the accent color.
    /// </summary>
    internal sealed class SheetView : UIElement
    {
        private static readonly int[] _zooms = [1, 2, 3, 4, 6, 8, 12, 16, 24, 32];
        private static readonly Vector4 _backdrop = new(0.12f, 0.12f, 0.14f, 1), _check = new(0.17f, 0.17f, 0.2f, 1);
        private static readonly Vector4 _outline = new(1, 1, 1, 0.55f), _selected = new(0.3f, 0.55f, 0.96f, 1);

        private readonly SpriteSheetPanel _panel;
        private Vector2 _pan;
        private bool _fit = true;
        private Vector2 _last;
        private Vector2? _drawFrom; // texel where a rectangle being drawn started
        private Vector2 _drawTo;
        private bool _panning;

        public SheetView(SpriteSheetPanel panel)
        {
            _panel = panel;
            IsFocusable = true;
            ClipToBounds = true;
        }

        /// <summary>Screen units a texel.</summary>
        public int Zoom { get; private set; } = 4;

        public void FitNextDraw() => _fit = true;

        // Where the texture's top-left corner is, locally.
        private Vector2 Origin(Vector2 size)
        {
            (int width, int height) = _panel.TextureSize;
            return Snap((size - new Vector2(width, height) * Zoom) * 0.5f + _pan);
        }

        /// <summary>The texel under a local point (not clamped).</summary>
        public Vector2 TexelAt(Vector2 local) => (local - Origin(ActualSize)) / Zoom;

        private Vector2 Local(PointerEvent e) => e.Position - new Vector2(Bounds.X, Bounds.Y);

        protected override void OnDraw(Canvas canvas)
        {
            Vector2 size = ActualSize;
            canvas.FillRect(Vector2.Zero, size, _backdrop);
            (int width, int height) = _panel.TextureSize;
            if (width <= 0)
            {
                return;
            }
            if (_fit)
            {
                _fit = false;
                _pan = Vector2.Zero;
                Zoom = _zooms.LastOrDefault(z => width * z <= size.X - 16 && height * z <= size.Y - 16, 1);
            }
            Vector2 origin = Origin(size), extent = new Vector2(width, height) * Zoom;
            // A checkerboard behind, so transparent texels show as such.
            const float cell = 8;
            canvas.PushClip(origin, extent);
            for (float y = 0; y < extent.Y; y += cell)
            {
                for (float x = ((int)(y / cell) % 2) * cell; x < extent.X; x += cell * 2)
                {
                    canvas.FillRect(origin + new Vector2(x, y), new Vector2(cell), _check);
                }
            }
            canvas.PopClip();
            canvas.DrawImage(_panel.Texture, origin, extent, new Vector4(0, 0, 1, 1), Vector4.One, default, pixelated: true);
            IReadOnlyList<SpriteFrameSettings> frames = _panel.Frames;
            for (int i = 0; i < frames.Count; ++i)
            {
                SpriteFrameSettings f = frames[i];
                bool selected = i == _panel.SelectedFrame;
                canvas.StrokeRect(origin + new Vector2(f.X, f.Y) * Zoom, new Vector2(f.Width, f.Height) * Zoom,
                    selected ? _selected : _outline, selected ? 2 : 1);
            }
            if (_drawFrom is { } from)
            {
                (Vector2 at, Vector2 span) = Rectangle(from, _drawTo);
                canvas.StrokeRect(origin + at * Zoom, span * Zoom, _selected, 2);
            }
        }

        // The texels a drag from one point to another covers, whole texels.
        private static (Vector2 At, Vector2 Size) Rectangle(Vector2 a, Vector2 b)
        {
            Vector2 min = Vector2.Min(a, b), max = Vector2.Max(a, b);
            min = new Vector2(MathF.Floor(min.X), MathF.Floor(min.Y));
            max = new Vector2(MathF.Ceiling(max.X), MathF.Ceiling(max.Y));
            return (min, Vector2.Max(max - min, Vector2.One));
        }

        private int FrameAt(Vector2 texel)
        {
            IReadOnlyList<SpriteFrameSettings> frames = _panel.Frames;
            for (int i = frames.Count; i-- > 0;)
            {
                SpriteFrameSettings f = frames[i];
                if (texel.X >= f.X && texel.Y >= f.Y && texel.X < f.X + f.Width && texel.Y < f.Y + f.Height)
                {
                    return i;
                }
            }
            return -1;
        }

        protected override void OnPointerDown(PointerEvent e)
        {
            Focus();
            e.Handled = true;
            _last = e.Position;
            if (e.Button is PointerButton.Middle or PointerButton.Right)
            {
                _panning = true;
                return;
            }
            if (e.Button != PointerButton.Left)
            {
                return;
            }
            Vector2 texel = TexelAt(Local(e));
            int frame = FrameAt(texel);
            if (frame >= 0 && e.ClickCount == 2)
            {
                _panel.AddFrameToClip(frame);
                return;
            }
            if (frame >= 0)
            {
                _panel.SelectFrame(frame);
                return;
            }
            _panel.SelectFrame(-1);
            if (_panel.Edited.Slicing == SpriteSlicing.Rectangles)
            {
                _drawFrom = _drawTo = texel;
            }
        }

        protected override void OnPointerMove(PointerEvent e)
        {
            if (_panning)
            {
                _pan += e.Position - _last;
            }
            else if (_drawFrom is not null)
            {
                _drawTo = TexelAt(Local(e));
            }
            else
            {
                return;
            }
            _last = e.Position;
            e.Handled = true;
            InvalidateVisual();
        }

        protected override void OnPointerUp(PointerEvent e)
        {
            _panning = false;
            if (_drawFrom is { } from)
            {
                _drawFrom = null;
                (int width, int height) = _panel.TextureSize;
                (Vector2 at, Vector2 span) = Rectangle(from, TexelAt(Local(e)));
                Vector2 min = Vector2.Clamp(at, Vector2.Zero, new Vector2(width - 1, height - 1));
                Vector2 max = Vector2.Clamp(at + span, min + Vector2.One, new Vector2(width, height));
                _panel.AddRectangle((int)min.X, (int)min.Y, (int)(max.X - min.X), (int)(max.Y - min.Y));
            }
            InvalidateVisual();
        }

        protected override void OnPointerWheel(PointerEvent e)
        {
            int at = Array.IndexOf(_zooms, Zoom);
            int next = Math.Clamp(at + (e.Wheel.Y > 0 ? 1 : e.Wheel.Y < 0 ? -1 : 0), 0, _zooms.Length - 1);
            if (next != at)
            {
                // The texel under the pointer stays under it.
                Vector2 local = Local(e), texel = TexelAt(local);
                Zoom = _zooms[next];
                _pan += local - (Origin(ActualSize) + texel * Zoom);
                InvalidateVisual();
            }
            e.Handled = true;
        }

        protected override void OnKeyDown(KeyEvent e)
        {
            if (e.Scancode is ScancodeDelete or ScancodeBackspace)
            {
                _panel.DeleteKey();
                e.Handled = true;
            }
        }
    }

    /// <summary>The selected clip playing, its frame scaled up by whole numbers to fit.</summary>
    internal sealed class PreviewView : UIElement
    {
        private readonly SpriteSheetPanel _panel;
        private double _time;
        private int _shown = -1;

        public PreviewView(SpriteSheetPanel panel)
        {
            _panel = panel;
            Height = 128;
            HorizontalAlignment = HorizontalAlignment.Stretch;
        }

        /// <summary>The clip's frame showing now (an index into its frames), or -1.</summary>
        public int FrameShown => _shown;

        public void Restart() => _time = 0;

        public void Advance(double seconds)
        {
            SpriteClipSettings? clip = Clip();
            if (clip is null || clip.Frames.Count == 0)
            {
                _shown = -1;
                return;
            }
            double length = clip.Frames.Sum(f => Math.Max(f.Duration, 1e-3));
            _time += seconds;
            _time = clip.Loop ? _time % length : Math.Min(_time, length);
            double at = 0;
            int shown = clip.Frames.Count - 1;
            for (int i = 0; i < clip.Frames.Count; ++i)
            {
                at += Math.Max(clip.Frames[i].Duration, 1e-3);
                if (_time < at)
                {
                    shown = i;
                    break;
                }
            }
            if (shown != _shown)
            {
                _shown = shown;
                InvalidateVisual();
            }
        }

        private SpriteClipSettings? Clip() =>
            _panel.SelectedClip >= 0 && _panel.SelectedClip < _panel.Edited.Clips.Count
                ? _panel.Edited.Clips[_panel.SelectedClip]
                : null;

        protected override void OnDraw(Canvas canvas)
        {
            Vector2 size = ActualSize;
            canvas.FillRect(Vector2.Zero, size, new Vector4(0.12f, 0.12f, 0.14f, 1), 4);
            SpriteClipSettings? clip = Clip();
            IReadOnlyList<SpriteFrameSettings> frames = _panel.Frames;
            (int width, int height) = _panel.TextureSize;
            if (clip is null || _shown < 0 || _shown >= clip.Frames.Count || width <= 0)
            {
                return;
            }
            int index = clip.Frames[_shown].Frame;
            if (index < 0 || index >= frames.Count)
            {
                return;
            }
            SpriteFrameSettings f = frames[index];
            float scale = MathF.Max(1, MathF.Floor(MathF.Min((size.X - 8) / f.Width, (size.Y - 8) / f.Height)));
            Vector2 drawn = new Vector2(f.Width, f.Height) * scale;
            Vector4 uv = new((float)f.X / width, (float)f.Y / height, (float)(f.X + f.Width) / width,
                (float)(f.Y + f.Height) / height);
            canvas.DrawImage(_panel.Texture, Snap((size - drawn) * 0.5f), drawn, uv, Vector4.One, default, pixelated: true);
        }
    }
}
