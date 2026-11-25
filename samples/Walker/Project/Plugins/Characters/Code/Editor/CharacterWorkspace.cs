using Crucible;
using Crucible.Animation;
using Crucible.Assets;
using Crucible.Ecs;
using Crucible.Editor;
using Crucible.Scenes;
using Crucible.UI;

namespace Characters.Editor;

/// <summary>
/// A character's workspace (ADR 0128): its model (its Animator's skeleton)
/// in a preview of its own, orbiting; its clips listed, one played with
/// Play/Pause and scrubbed with a slider. The stats are the Inspector's
/// (its drawer).
/// </summary>
public sealed class CharacterWorkspace : Workspace
{
    private readonly EditorContext _editor;
    private readonly StackPanel _clips = new() { Spacing = 2 };
    private readonly Toggle _pause = EditorIcons.Toggle("pause", "Pause, to scrub");
    private readonly Slider _scrub = new() { Minimum = 0, Maximum = 1, Width = 220 };
    private readonly Label _time = new();
    private readonly List<AssetId> _clipIds = [];
    private readonly List<Button> _clipButtons = [];
    private Entity _model;
    private AssetId _playing;
    private AssetId _wanted;
    private float _length;
    private bool _following;

    public CharacterWorkspace(EditorContext editor) : base("Character")
    {
        _editor = editor;
        Preview = new PreviewView();
        if (editor.Scene.World is { } world && world.IsAlive(Entity))
        {
            if (world.Has(Entity, Components.Of<EntityName>()))
            {
                Title = $"Character: {world.GetManaged<EntityName>(Entity).Value}";
            }
            if (world.Has(Entity, Components.Of<Animator>()) && world.Read<Animator>(Entity).Skeleton is { IsNone: false } model)
            {
                // A prefab instance without a name of its own: its model's.
                if (Title == "Character" && editor.Assets.Database?.Find(model) is { } file)
                {
                    Title = $"Character: {Path.GetFileNameWithoutExtension(file.Path)}";
                }
                _model = Preview.Load(model);
                if (!_model.IsNull)
                {
                    Preview.World.Set(_model, new Animator { Skeleton = model, Speed = 1, Loop = true });
                    Preview.Frame();
                }
                foreach (AssetEntry clip in editor.Assets.Database?.SubAssetsOf(model) ?? [])
                {
                    if (editor.Assets.Database!.KindOf(clip.Id) == ".crclip")
                    {
                        AssetId id = clip.Id;
                        _clipIds.Add(id);
                        Button button = new(clip.SubAssetName) { HorizontalAlignment = HorizontalAlignment.Stretch };
                        button.Clicked += _ => Play(id);
                        _clipButtons.Add(button);
                        _clips.Add(button);
                    }
                }
            }
        }
        _pause.Changed += toggle => Preview.IsRunning = !toggle.IsOn;
        _scrub.ValueChanged += slider =>
        {
            if (!_following && !Preview.IsRunning && !_playing.IsNone)
            {
                Animator.SetTime(Preview.World, _model, -1, slider.Value * _length);
            }
        };
        StackPanel side = new() { Spacing = 6, Margin = new Thickness(8), Width = 220 };
        Label heading = new("Clips");
        heading.Classes.Add("field-header");
        side.Add(heading);
        side.Add(_clips);
        StackPanel controls = new() { Orientation = Orientation.Horizontal, Spacing = 6 };
        controls.Add(_pause);
        controls.Add(_time);
        side.Add(controls);
        side.Add(_scrub);
        Arrange(null, Preview, side);
        if (_clipIds.Count > 0)
        {
            _wanted = _clipIds[0];
        }
    }

    /// <summary>The character's model, in a world of its own.</summary>
    public PreviewView Preview { get; }

    /// <summary>The clip playing; none until its skeleton has loaded.</summary>
    public AssetId Playing => _playing;

    /// <summary>Plays a clip on the model from its start (once the model has loaded).</summary>
    public void Play(AssetId clip) => _wanted = clip;

    public override void Tick()
    {
        if (Preview.IsDisposed || _model.IsNull)
        {
            return;
        }
        if (!_wanted.IsNone)
        {
            try
            {
                Animator.Play(Preview.World, _model, _wanted);
                _playing = _wanted;
                _wanted = AssetId.None;
                // Its length: setting a time past the end holds it at the end.
                Animator.SetTime(Preview.World, _model, -1, float.MaxValue);
                _length = Animator.TimeOf(Preview.World, _model, -1);
                Animator.SetTime(Preview.World, _model, -1, 0);
            }
            catch (CrucibleException)
            {
                return; // its skeleton is still loading
            }
        }
        if (_playing.IsNone)
        {
            return;
        }
        for (int i = 0; i < _clipButtons.Count; ++i)
        {
            _clipButtons[i].Classes.Set("on", _clipIds[i] == _playing);
        }
        float time = Animator.TimeOf(Preview.World, _model, -1);
        // A clip played before it had loaded measured 0 long: measured again
        // once it moves, put back where it had got to.
        if (_length <= 0 && time > 0)
        {
            Animator.SetTime(Preview.World, _model, -1, float.MaxValue);
            _length = Animator.TimeOf(Preview.World, _model, -1);
            Animator.SetTime(Preview.World, _model, -1, time);
        }
        _time.Text = _length > 0 ? $"{time:0.00} / {_length:0.00} s" : "Loading…";
        if (Preview.IsRunning && _length > 0)
        {
            _following = true;
            _scrub.Value = time / _length;
            _following = false;
        }
    }

    public override void Closed() => Preview.Dispose();
}
