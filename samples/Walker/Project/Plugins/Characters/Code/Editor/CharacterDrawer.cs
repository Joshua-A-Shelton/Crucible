using System.Numerics;
using Crucible.Editor;
using Crucible.UI;

namespace Characters.Editor;

/// <summary>
/// A Character's stats in the Inspector (ADR 0128): a bar for each, a
/// derived Power, and the generated editors below to change them (each
/// change an undo step).
/// </summary>
public sealed class CharacterDrawer : Drawer<Character>
{
    private readonly Label _power = new();
    private readonly StatBar _strength = new(new Vector4(0.9f, 0.45f, 0.35f, 1));
    private readonly StatBar _agility = new(new Vector4(0.4f, 0.8f, 0.45f, 1));
    private readonly StatBar _pace = new(new Vector4(0.35f, 0.6f, 0.95f, 1));

    public override UIElement Build(DrawerContext<Character> context)
    {
        StackPanel body = new() { Spacing = 4 };
        body.Add(_power);
        body.Add(_strength);
        body.Add(_agility);
        body.Add(_pace);
        foreach (string field in (string[])["Strength", "Agility", "Pace"])
        {
            if (context.Default(field) is { } editor)
            {
                body.Add(editor);
            }
        }
        return body;
    }

    public override void Update(DrawerContext<Character> context)
    {
        Character character = context.Value;
        _strength.Fill = character.Strength / 100f;
        _agility.Fill = character.Agility / 100f;
        _pace.Fill = (character.Pace - 0.5f) / 1.5f;
        _power.Text = $"Power {character.Strength * 2 + character.Agility}, moving at {character.Pace:0.##}x";
    }

    // A filled bar, 0 to 1.
    private sealed class StatBar(Vector4 color) : UIElement
    {
        public float Fill
        {
            get;
            set
            {
                field = Math.Clamp(value, 0, 1);
                InvalidateVisual();
            }
        }

        protected override Vector2 MeasureCore(Vector2 available) => new(0, 8);

        protected override void OnDraw(Canvas canvas)
        {
            canvas.FillRect(Vector2.Zero, ActualSize, new Vector4(1, 1, 1, 0.08f), 3);
            canvas.FillRect(Vector2.Zero, ActualSize with { X = ActualSize.X * Fill }, color, 3);
        }
    }
}
