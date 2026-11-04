using System.Numerics;
using Crucible.UI;

namespace Crucible.Tests;

// Styling and animation (ADR 0061), under JIT and NativeAOT, on a clock the
// tests move; launcher.jit.styles draws a styled tree on the GPU.
public class StyleTests
{
    private static readonly Vector4 _red = new(1, 0, 0, 1), _green = new(0, 1, 0, 1), _blue = new(0, 0, 1, 1);

    private sealed class Probe : UIElement;

    private sealed class Rig
    {
        public double Time;
        public readonly UIRoot Root;
        public readonly StackPanel Panel = new();

        public Rig(Theme theme)
        {
            Root = new UIRoot { Theme = theme, Content = Panel };
            Root.TestClock = () => Time;
            Update();
        }

        public void Update() => Root.UpdateLayout(new Vector2(400, 300));

        public T Add<T>(T element) where T : UIElement
        {
            Panel.Add(element);
            Update();
            return element;
        }
    }

    [Test]
    public async Task ValuesComeFromAnimationThenCodeThenStyleThenDefault()
    {
        Rig rig = new(new Theme().Add(new Style("Probe").Set(UIElement.BackgroundProperty, _red)));
        Probe probe = rig.Add(new Probe());
        await Assert.That(probe.Background).IsEqualTo(_red);
        await Assert.That(probe.Opacity).IsEqualTo(1f); // the default
        probe.Background = _green;
        rig.Update();
        await Assert.That(probe.Background).IsEqualTo(_green);
        await Assert.That(probe.IsSet(UIElement.BackgroundProperty)).IsTrue();
        probe.ClearValue(UIElement.BackgroundProperty);
        await Assert.That(probe.Background).IsEqualTo(_red);

        probe.Animate(UIElement.OpacityProperty, 0f, 1, Easing.Linear);
        rig.Time = 0.25;
        rig.Update();
        await Assert.That(probe.Opacity).IsEqualTo(0.75f);
        rig.Time = 2;
        rig.Update();
        await Assert.That(probe.Opacity).IsEqualTo(0f);
        await Assert.That(probe.IsSet(UIElement.OpacityProperty)).IsTrue(); // a tween ends set in code
        await Assert.That(rig.Root.IsAnimating).IsFalse();
    }

    [Test]
    public async Task MoreSpecificSelectorsWinThenLaterStyles()
    {
        Theme theme = new Theme()
            .Add(new Style("UIElement.big:hover").Set(UIElement.BackgroundProperty, _blue))
            .Add(new Style(".big").Set(UIElement.BackgroundProperty, _green))
            .Add(new Style("Probe").Set(UIElement.BackgroundProperty, _red).Set(UIElement.BorderWidthProperty, 2f))
            .Add(new Style("Panel").Set(UIElement.BorderWidthProperty, 5f)) // a base type matches its subclasses
            .Add(new Style("*").Set(UIElement.CornerRadiiProperty, new Vector4(3)))
            .Add(new Style("UIElement").Set(UIElement.CornerRadiiProperty, new Vector4(4)));
        Rig rig = new(theme);
        Probe probe = rig.Add(new Probe());
        await Assert.That(probe.Background).IsEqualTo(_red);
        await Assert.That(probe.CornerRadii).IsEqualTo(new Vector4(4)); // a type beats "*"
        await Assert.That(rig.Panel.BorderWidth).IsEqualTo(5f);

        probe.Classes.Add("big");
        rig.Update();
        await Assert.That(probe.Background).IsEqualTo(_green);
        probe.State = UIState.Hover;
        rig.Update();
        await Assert.That(probe.Background).IsEqualTo(_blue);
        probe.Classes.Remove("big");
        rig.Update();
        await Assert.That(probe.Background).IsEqualTo(_red);
        await Assert.That(probe.BorderWidth).IsEqualTo(2f);
    }

    [Test]
    public async Task StyleChangesTransitionButAFirstLookDoesnt()
    {
        Theme theme = new Theme()
            .Add(new Style("Probe").Set(UIElement.OpacityProperty, 0.5f)
                .Transition(UIElement.OpacityProperty, 1, Easing.Linear))
            .Add(new Style("Probe:hover").Set(UIElement.OpacityProperty, 1f));
        Rig rig = new(theme);
        Probe probe = rig.Add(new Probe());
        await Assert.That(probe.Opacity).IsEqualTo(0.5f);
        await Assert.That(rig.Root.IsAnimating).IsFalse();

        probe.State = UIState.Hover;
        rig.Update();
        await Assert.That(probe.Opacity).IsEqualTo(0.5f);
        rig.Time = 0.5;
        rig.Update();
        await Assert.That(probe.Opacity).IsEqualTo(0.75f);
        // Unhovered halfway: back from where it is.
        probe.State = UIState.None;
        rig.Update();
        rig.Time = 1;
        rig.Update();
        await Assert.That(probe.Opacity).IsEqualTo(0.625f);
        rig.Time = 3;
        rig.Update();
        await Assert.That(probe.Opacity).IsEqualTo(0.5f);
        await Assert.That(rig.Root.IsAnimating).IsFalse();

        // Code beats styles, and setting it stops a transition.
        probe.State = UIState.Hover;
        rig.Update();
        probe.Opacity = 0.1f;
        rig.Time = 3.5;
        rig.Update();
        await Assert.That(probe.Opacity).IsEqualTo(0.1f);
    }

    [Test]
    public async Task ChangingAThemeRestylesItsRoots()
    {
        Theme theme = new Theme().Add(new Style("Probe").Set(UIElement.BackgroundProperty, _red));
        Rig rig = new(theme);
        Probe probe = rig.Add(new Probe());
        theme.ReplaceWith(new Theme().Add(new Style("Probe").Set(UIElement.BackgroundProperty, _blue)));
        rig.Update();
        await Assert.That(probe.Background).IsEqualTo(_blue);
        theme.Clear();
        rig.Update();
        await Assert.That(probe.Background).IsEqualTo(Vector4.Zero);
    }

    [Test]
    public async Task EasingsFollowCssCurves()
    {
        foreach (Easing easing in new[] { Easing.Linear, Easing.Ease, Easing.EaseIn, Easing.EaseOut, Easing.EaseInOut })
        {
            await Assert.That(easing.Evaluate(0)).IsEqualTo(0f);
            await Assert.That(MathF.Abs(easing.Evaluate(1) - 1)).IsLessThan(1e-4f);
        }
        await Assert.That(Easing.Linear.Evaluate(0.3f)).IsEqualTo(0.3f);
        await Assert.That(MathF.Abs(Easing.EaseInOut.Evaluate(0.5f) - 0.5f)).IsLessThan(1e-3f);
        await Assert.That(Easing.EaseIn.Evaluate(0.5f)).IsLessThan(0.4f);
        await Assert.That(Easing.EaseOut.Evaluate(0.5f)).IsGreaterThan(0.6f);
        // CSS's ease at 0.5 is about 0.8024.
        await Assert.That(MathF.Abs(Easing.Ease.Evaluate(0.5f) - 0.8024f)).IsLessThan(2e-3f);
    }

    [Test]
    public async Task ColorsBlendInLinearLight()
    {
        Vector4 gray = StyleValues.LerpColor(new Vector4(0, 0, 0, 1), Vector4.One, 0.5f);
        await Assert.That(MathF.Abs(gray.X - 0.7354f)).IsLessThan(1e-3f);
        Vector4 same = StyleValues.LerpColor(_red, _red, 0.3f);
        await Assert.That(Vector4.Distance(same, _red)).IsLessThan(1e-5f);
    }

    [Test]
    public async Task ThemesReadFromJson()
    {
        Theme theme = Theme.Parse("""
            {
              "$guid": "1b2c3d4e-0000-4000-8000-000000000001",
              "Styles": [
                {
                  "Selector": "Probe.primary:hover:focus",
                  "Set": {
                    "Background": "#ff8000",
                    "BorderColor": "#0f08",
                    "Foreground": [0, 0, 1],
                    "CornerRadii": 6,
                    "Padding": [12, 6],
                    "Margin": [1, 2, 3, 4],
                    "RenderScale": 1.1,
                    "HorizontalAlignment": "Center"
                  },
                  "Transitions": {
                    "Background": { "Duration": 0.15, "Easing": "EaseOut" },
                    "RenderScale": { "Duration": 0.2, "Easing": [0.3, 0, 0.2, 1] }
                  }
                }
              ]
            }
            """);
        Style style = theme.Styles.Single();
        await Assert.That(style.Selector.Type).IsEqualTo("Probe");
        await Assert.That(style.Selector.Classes.Single()).IsEqualTo("primary");
        await Assert.That(style.Selector.States).IsEqualTo(UIState.Hover | UIState.Focus);
        await Assert.That((Vector4)style.Values[UIElement.BackgroundProperty]).IsEqualTo(new Vector4(1, 128 / 255f, 0, 1));
        await Assert.That((Vector4)style.Values[UIElement.BorderColorProperty]).IsEqualTo(new Vector4(0, 1, 0, 8 / 15f));
        await Assert.That((Thickness)style.Values[UIElement.PaddingProperty]).IsEqualTo(new Thickness(12, 6, 12, 6));
        await Assert.That((Thickness)style.Values[UIElement.MarginProperty]).IsEqualTo(new Thickness(1, 2, 3, 4));
        await Assert.That((Vector2)style.Values[UIElement.RenderScaleProperty]).IsEqualTo(new Vector2(1.1f));
        await Assert.That(style.Transitions[UIElement.BackgroundProperty]).IsEqualTo(new Transition(0.15f, Easing.EaseOut));
        await Assert.That(style.Transitions[UIElement.RenderScaleProperty].Easing).IsEqualTo(new Easing(0.3f, 0, 0.2f, 1));
    }

    [Test]
    public async Task ThemeMistakesSayWhere()
    {
        async Task Fails(string json, string message) =>
            await Assert.That(Assert.Throws<InvalidDataException>(() => Theme.Parse(json)).Message).Contains(message);

        await Fails("[]", "a Styles array");
        await Fails("""{ "Styles": [ { "Set": {} } ] }""", "Styles[0]: a style is an object with a Selector");
        await Fails("""{ "Styles": [ { "Selector": "Probe:wobbly" } ] }""", ":wobbly isn't a state");
        await Fails("""{ "Styles": [ { "Selector": "Probe", "Set": { "Colour": "#fff" } } ] }""",
            "Styles[0] (Probe).Set: Colour isn't a styled property");
        await Fails("""{ "Styles": [ { "Selector": "Probe", "Set": { "Background": "red" } } ] }""",
            "Styles[0] (Probe).Set.Background: expected a color");
        await Fails("""{ "Styles": [ { "Selector": "Probe", "Transitions": { "Opacity": { "Easing": "EaseOut" } } } ] }""",
            "a transition has a Duration");
        await Fails("""{ "Styles": [ { "Selector": "Probe", "Set": { "HorizontalAlignment": "Left" } } ] }""",
            "expected one of Stretch, Start, Center, End");
        await Fails("{ nope", "not JSON");
    }

    [Test]
    public async Task OpacityPushesNeedPops()
    {
        Canvas canvas = new();
        canvas.PushOpacity(0.5f);
        canvas.PopOpacity();
        await Assert.That(Assert.Throws<InvalidOperationException>(canvas.PopOpacity).Message).Contains("PopOpacity");
    }
}
