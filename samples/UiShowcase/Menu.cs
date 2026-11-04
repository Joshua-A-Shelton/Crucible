using System.Numerics;
using Crucible.UI;

namespace Crucible.Samples.UiShowcase;

/// <summary>
/// The showcase's menu: a main screen (Play, Options, Quit), an options
/// screen (a toggle, a slider, a spinner, a dropdown, Back; Cancel goes
/// back too), and a quit dialog in a popup. Screens fade in. Built fresh
/// for each journey.
/// </summary>
public sealed class Menu
{
    public AnchorPanel Page { get; } = new();
    public StackPanel Main { get; } = new() { Spacing = 10, Width = 300 };
    public StackPanel Options { get; } = new() { Spacing = 16, Width = 460 };
    public Button Play { get; } = new("Play");
    public Button OptionsButton { get; } = new("Options");
    public Button Quit { get; } = new("Quit");
    public Toggle Subtitles { get; } = new("On");
    public Slider Volume { get; } = new() { Minimum = 0, Maximum = 100, Step = 5, Value = 50 };
    public Spinner Difficulty { get; } = new() { Options = ["Easy", "Normal", "Hard"], SelectedIndex = 1 };
    public ComboBox Quality { get; } = new() { Options = ["Low", "Medium", "High", "Ultra"], SelectedIndex = 1 };
    public Button Back { get; } = new("Back");
    public Popup QuitDialog { get; } = new();
    public Button Yes { get; } = new("Yes");
    public Button No { get; } = new("No") { TabIndex = -1 }; // focused first: quitting needs a deliberate choice
    public Label Status { get; } = new("Ready");
    public bool Played { get; private set; }
    public bool QuitConfirmed { get; private set; }

    public Menu()
    {
        Page.Classes.Add("backdrop");

        Main.Classes.Add("panel");
        Label title = new("Crucible");
        title.Classes.Add("title");
        title.HorizontalAlignment = HorizontalAlignment.Center;
        Main.Add(title);
        Play.Classes.Add("primary");
        foreach (Button button in new[] { Play, OptionsButton, Quit })
        {
            Main.Add(button);
        }
        AnchorPanel.SetAnchors(Main, Anchors.Center);

        Options.Classes.Add("panel");
        Label heading = new("Options");
        heading.Classes.Add("title");
        Options.Add(heading);
        Grid rows = new() { ColumnSpacing = 16, RowSpacing = 14 };
        rows.Columns.AddRange([GridLength.Pixels(140), GridLength.Star()]);
        (string Name, UIElement Control)[] settings =
        [
            ("Subtitles", Subtitles),
            ("Volume", Volume),
            ("Difficulty", Difficulty),
            ("Quality", Quality),
        ];
        for (int i = 0; i < settings.Length; ++i)
        {
            rows.Rows.Add(GridLength.Auto);
            Label name = new(settings[i].Name) { VerticalAlignment = VerticalAlignment.Center };
            name.Classes.Add("caption");
            Grid.SetRow(name, i);
            UIElement control = settings[i].Control;
            control.HorizontalAlignment = HorizontalAlignment.Start;
            control.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetRow(control, i);
            Grid.SetColumn(control, 1);
            rows.Add(name);
            rows.Add(control);
        }
        Options.Add(rows);
        Back.HorizontalAlignment = HorizontalAlignment.End;
        Options.Add(Back);
        AnchorPanel.SetAnchors(Options, Anchors.Center);
        Options.Visibility = Visibility.Collapsed;

        StackPanel dialog = new() { Spacing = 16 };
        dialog.Add(new Label("Quit the showcase?"));
        StackPanel answers = new() { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.End };
        answers.Add(Yes);
        answers.Add(No);
        dialog.Add(answers);
        QuitDialog.Content = dialog;

        Status.Classes.Add("caption");
        AnchorPanel.SetAnchors(Status, Anchors.At(new Vector2(0, 1), new Vector2(24, -24)));

        Page.Add(Main);
        Page.Add(Options);
        Page.Add(Status);

        Play.Clicked += _ =>
        {
            Played = true;
            Status.Text = "Play pressed";
        };
        OptionsButton.Clicked += _ => ShowOptions();
        Back.Clicked += _ => Show(Main, OptionsButton);
        Options.Canceled += (_, e) =>
        {
            Show(Main, OptionsButton);
            e.Handled = true;
        };
        Quit.Clicked += _ => QuitDialog.Open(Quit.Root);
        Yes.Clicked += _ =>
        {
            QuitConfirmed = true;
            QuitDialog.Close();
        };
        No.Clicked += _ => QuitDialog.Close();
    }

    public string Screen => Options.Visibility == Visibility.Visible ? "options" : "main";

    /// <summary>Shows the main screen with Play focused (after the menu joins a root).</summary>
    public void Start() => Play.Focus();

    public void ShowOptions() => Show(Options, Subtitles);

    // One screen at a time, fading in.
    private void Show(StackPanel screen, UIElement focus)
    {
        StackPanel other = screen == Main ? Options : Main;
        other.Visibility = Visibility.Collapsed;
        screen.Visibility = Visibility.Visible;
        screen.Opacity = 0;
        screen.Animate(UIElement.OpacityProperty, 1f, 0.15f, Easing.EaseOut);
        Page.Root?.UpdateLayout(Page.Root.Size * Page.Root.Scale);
        focus.Focus();
    }
}
