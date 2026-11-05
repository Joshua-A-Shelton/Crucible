using System.Numerics;

namespace Crucible.UI;

public sealed partial class Theme
{
    /// <summary>
    /// The engine's plain default look (ADR 0063), which a root uses until
    /// given another: dark neutral grays, one blue accent, focus outlines,
    /// short hover and press transitions. A new theme each call, so a game
    /// can add its own styles after these (later styles win ties).
    /// </summary>
    public static Theme CreateDefault()
    {
        Vector4 text = Color(0xe8e8ec), muted = Color(0x9aa0aa), accent = Color(0x4c8bf5);
        Vector4 control = Color(0x353a42), hover = Color(0x434954), pressed = Color(0x2a2e35);
        Vector4 well = Color(0x24272d), edge = Color(0x5b616b);
        Transition quick = new(0.12f, Easing.EaseOut);
        Theme theme = new();
        theme.Add(new Style("Label").Set(UIElement.ForegroundProperty, text));
        theme.Add(new Style("Button")
            .Set(UIElement.BackgroundProperty, control)
            .Set(UIElement.CornerRadiiProperty, new Vector4(4))
            .Set(UIElement.PaddingProperty, new Thickness(14, 7, 14, 7))
            .Set(UIElement.BorderColorProperty, accent)
            .Transition(UIElement.BackgroundProperty, quick.Duration, quick.Easing)
            .Transition(UIElement.BorderWidthProperty, quick.Duration, quick.Easing));
        theme.Add(new Style("Button:hover").Set(UIElement.BackgroundProperty, hover));
        theme.Add(new Style("Button:pressed").Set(UIElement.BackgroundProperty, pressed));
        theme.Add(new Style("Button:focus").Set(UIElement.BorderWidthProperty, 2f));
        theme.Add(new Style("Button:disabled").Set(UIElement.OpacityProperty, 0.45f));

        theme.Add(new Style("Toggle")
            .Set(UIElement.PaddingProperty, new Thickness(4))
            .Set(UIElement.CornerRadiiProperty, new Vector4(4))
            .Set(UIElement.BorderColorProperty, accent));
        theme.Add(new Style("Toggle:focus").Set(UIElement.BorderWidthProperty, 2f));
        theme.Add(new Style("Toggle:disabled").Set(UIElement.OpacityProperty, 0.45f));
        theme.Add(new Style("ToggleBox")
            .Set(UIElement.WidthProperty, 18f)
            .Set(UIElement.HeightProperty, 18f)
            .Set(UIElement.CornerRadiiProperty, new Vector4(4))
            .Set(UIElement.BackgroundProperty, well)
            .Set(UIElement.BorderWidthProperty, 1.5f)
            .Set(UIElement.BorderColorProperty, edge)
            .Transition(UIElement.BackgroundProperty, quick.Duration, quick.Easing));
        theme.Add(new Style("ToggleBox.on").Set(UIElement.BackgroundProperty, accent).Set(UIElement.BorderColorProperty, accent));
        theme.Add(new Style("ToggleMark")
            .Set(UIElement.WidthProperty, 8f)
            .Set(UIElement.HeightProperty, 8f)
            .Set(UIElement.CornerRadiiProperty, new Vector4(2))
            .Set(UIElement.BackgroundProperty, text)
            .Set(UIElement.OpacityProperty, 0f)
            .Set(UIElement.RenderScaleProperty, new Vector2(0.5f))
            .Transition(UIElement.OpacityProperty, quick.Duration, quick.Easing)
            .Transition(UIElement.RenderScaleProperty, quick.Duration, quick.Easing));
        theme.Add(new Style("ToggleMark.on").Set(UIElement.OpacityProperty, 1f).Set(UIElement.RenderScaleProperty, Vector2.One));

        theme.Add(new Style("Slider")
            .Set(UIElement.WidthProperty, 200f)
            .Set(UIElement.HeightProperty, 24f)
            .Set(UIElement.CornerRadiiProperty, new Vector4(6))
            .Set(UIElement.BorderColorProperty, accent));
        theme.Add(new Style("Slider:focus").Set(UIElement.BorderWidthProperty, 2f));
        theme.Add(new Style("SliderTrack")
            .Set(UIElement.HeightProperty, 4f)
            .Set(UIElement.MarginProperty, new Thickness(8, 0, 8, 0))
            .Set(UIElement.CornerRadiiProperty, new Vector4(2))
            .Set(UIElement.BackgroundProperty, edge));
        theme.Add(new Style("SliderThumb")
            .Set(UIElement.WidthProperty, 16f)
            .Set(UIElement.HeightProperty, 16f)
            .Set(UIElement.CornerRadiiProperty, new Vector4(8))
            .Set(UIElement.BackgroundProperty, text)
            .Transition(UIElement.RenderScaleProperty, quick.Duration, quick.Easing));
        theme.Add(new Style("SliderThumb:hover").Set(UIElement.RenderScaleProperty, new Vector2(1.15f)));
        theme.Add(new Style("SliderThumb:pressed").Set(UIElement.BackgroundProperty, accent));

        theme.Add(new Style("ScrollBar.vertical").Set(UIElement.WidthProperty, 8f).Set(UIElement.PaddingProperty, new Thickness(2)));
        theme.Add(new Style("ScrollBar.horizontal").Set(UIElement.HeightProperty, 8f).Set(UIElement.PaddingProperty, new Thickness(2)));
        theme.Add(new Style("ScrollThumb")
            .Set(UIElement.CornerRadiiProperty, new Vector4(2))
            .Set(UIElement.BackgroundProperty, muted with { W = 0.5f })
            .Transition(UIElement.BackgroundProperty, quick.Duration, quick.Easing));
        theme.Add(new Style("ScrollThumb:hover").Set(UIElement.BackgroundProperty, muted with { W = 0.85f }));

        theme.Add(new Style("ListBox")
            .Set(UIElement.BackgroundProperty, well)
            .Set(UIElement.CornerRadiiProperty, new Vector4(4))
            .Set(UIElement.PaddingProperty, new Thickness(2)));
        theme.Add(new Style("ListItem")
            .Set(UIElement.PaddingProperty, new Thickness(10, 6, 10, 6))
            .Set(UIElement.CornerRadiiProperty, new Vector4(3))
            .Set(UIElement.BorderColorProperty, accent)
            .Transition(UIElement.BackgroundProperty, quick.Duration, quick.Easing));
        theme.Add(new Style("ListItem:hover").Set(UIElement.BackgroundProperty, hover));
        theme.Add(new Style("ListItem.selected").Set(UIElement.BackgroundProperty, accent with { W = 0.35f }));
        theme.Add(new Style("ListItem:focus").Set(UIElement.BorderWidthProperty, 1.5f));
        theme.Add(new Style("Popup")
            .Set(UIElement.BackgroundProperty, control)
            .Set(UIElement.CornerRadiiProperty, new Vector4(6))
            .Set(UIElement.BorderWidthProperty, 1f)
            .Set(UIElement.BorderColorProperty, edge)
            .Set(UIElement.PaddingProperty, new Thickness(4)));
        theme.Add(new Style("ListBox.dropdown").Set(UIElement.MaxHeightProperty, 240f));
        theme.Add(new Style("ListItem.tree")
            .Set(UIElement.PaddingProperty, new Thickness(4, 2, 8, 2))
            .Set(UIElement.CornerRadiiProperty, new Vector4(3)));
        theme.Add(new Style("ListItem.tree:focus").Set(UIElement.BorderWidthProperty, 1f));
        theme.Add(new Style("TreeExpander")
            .Set(UIElement.WidthProperty, 16f)
            .Set(UIElement.ForegroundProperty, muted));
        theme.Add(new Style("TreeExpander:hover").Set(UIElement.ForegroundProperty, text));
        theme.Add(new Style(".tree-drop").Set(UIElement.ForegroundProperty, accent));
        theme.Add(new Style("StackPanel.color-picker").Set(UIElement.PaddingProperty, new Thickness(8)));
        theme.Add(new Style("ColorField")
            .Set(UIElement.PaddingProperty, new Thickness(3))
            .Set(UIElement.CornerRadiiProperty, new Vector4(4))
            .Set(UIElement.BackgroundProperty, well)
            .Set(UIElement.BorderColorProperty, accent));
        theme.Add(new Style("ColorField:focus").Set(UIElement.BorderWidthProperty, 1.5f));
        theme.Add(new Style("StackPanel.dialog")
            .Set(UIElement.PaddingProperty, new Thickness(12))
            .Set(UIElement.MaxWidthProperty, 440f));

        theme.Add(new Style("Spinner")
            .Set(UIElement.PaddingProperty, new Thickness(4))
            .Set(UIElement.CornerRadiiProperty, new Vector4(4))
            .Set(UIElement.BackgroundProperty, well)
            .Set(UIElement.BorderColorProperty, accent));
        theme.Add(new Style("Spinner:focus").Set(UIElement.BorderWidthProperty, 2f));
        theme.Add(new Style("SpinnerArrow")
            .Set(UIElement.WidthProperty, 24f)
            .Set(UIElement.HeightProperty, 24f)
            .Set(UIElement.CornerRadiiProperty, new Vector4(4))
            .Set(UIElement.ForegroundProperty, muted)
            .Transition(UIElement.BackgroundProperty, quick.Duration, quick.Easing));
        theme.Add(new Style("SpinnerArrow:hover").Set(UIElement.BackgroundProperty, hover).Set(UIElement.ForegroundProperty, text));
        theme.Add(new Style("SpinnerArrow:pressed").Set(UIElement.BackgroundProperty, pressed));

        theme.Add(new Style("ComboBox")
            .Set(UIElement.BackgroundProperty, control)
            .Set(UIElement.CornerRadiiProperty, new Vector4(4))
            .Set(UIElement.PaddingProperty, new Thickness(12, 7, 8, 7))
            .Set(UIElement.MinWidthProperty, 160f)
            .Set(UIElement.BorderColorProperty, accent)
            .Transition(UIElement.BackgroundProperty, quick.Duration, quick.Easing));
        theme.Add(new Style("ComboBox:hover").Set(UIElement.BackgroundProperty, hover));
        theme.Add(new Style("ComboBox:focus").Set(UIElement.BorderWidthProperty, 2f));
        theme.Add(new Style("ComboBoxArrow")
            .Set(UIElement.WidthProperty, 16f)
            .Set(UIElement.HeightProperty, 16f)
            .Set(UIElement.MarginProperty, new Thickness(8, 0, 0, 0))
            .Set(UIElement.ForegroundProperty, muted));

        // Text fields (ADR 0065): a sunken well, an accent outline while editing.
        theme.Add(new Style("TextField")
            .Set(UIElement.BackgroundProperty, well)
            .Set(UIElement.ForegroundProperty, text)
            .Set(UIElement.CornerRadiiProperty, new Vector4(4))
            .Set(UIElement.PaddingProperty, new Thickness(8, 6, 8, 6))
            .Set(UIElement.MinWidthProperty, 160f)
            .Set(UIElement.BorderWidthProperty, 1f)
            .Set(UIElement.BorderColorProperty, edge)
            .Set(TextField.CaretColorProperty, text)
            .Set(TextField.SelectionColorProperty, accent with { W = 0.45f })
            .Transition(UIElement.BorderColorProperty, quick.Duration, quick.Easing));
        theme.Add(new Style("TextField:hover").Set(UIElement.BorderColorProperty, muted));
        theme.Add(new Style("TextField:focus").Set(UIElement.BorderColorProperty, accent).Set(UIElement.BorderWidthProperty, 2f));
        theme.Add(new Style("TextField:disabled").Set(UIElement.OpacityProperty, 0.45f));
        theme.Add(new Style("NumberField").Set(UIElement.MinWidthProperty, 80f));

        // Menus (ADR 0066): rows that light up under the pointer, a muted
        // shortcut column, the bar a strip of plain titles.
        theme.Add(new Style("MenuBar")
            .Set(UIElement.BackgroundProperty, well)
            .Set(UIElement.PaddingProperty, new Thickness(4, 2, 4, 2)));
        theme.Add(new Style("Menu").Set(UIElement.MinWidthProperty, 180f));
        theme.Add(new Style("MenuItem")
            .Set(UIElement.PaddingProperty, new Thickness(8, 5, 10, 5))
            .Set(UIElement.CornerRadiiProperty, new Vector4(4))
            .Set(UIElement.ForegroundProperty, text));
        theme.Add(new Style("MenuItem.bar").Set(UIElement.PaddingProperty, new Thickness(10, 4, 10, 4)));
        theme.Add(new Style("MenuItem:hover").Set(UIElement.BackgroundProperty, hover));
        theme.Add(new Style("MenuItem:focus").Set(UIElement.BackgroundProperty, hover));
        theme.Add(new Style("MenuItem.open").Set(UIElement.BackgroundProperty, hover));
        theme.Add(new Style("MenuItem:disabled").Set(UIElement.OpacityProperty, 0.45f));
        theme.Add(new Style("MenuSeparator")
            .Set(UIElement.HeightProperty, 1f)
            .Set(UIElement.MarginProperty, new Thickness(6, 4, 6, 4))
            .Set(UIElement.BackgroundProperty, edge));
        theme.Add(new Style("Label.shortcut").Set(UIElement.ForegroundProperty, muted));

        // Title bars of windows drawing their own frame (ADR 0066): flat
        // buttons, close turning red under the pointer.
        theme.Add(new Style("TitleBar")
            .Set(UIElement.BackgroundProperty, well)
            .Set(UIElement.MinHeightProperty, 32f)
            .Set(UIElement.PaddingProperty, new Thickness(4, 0, 0, 0)));
        theme.Add(new Style("Label.title").Set(UIElement.ForegroundProperty, muted));
        theme.Add(new Style("WindowButton")
            .Set(UIElement.WidthProperty, 44f)
            .Set(UIElement.ForegroundProperty, text)
            .Transition(UIElement.BackgroundProperty, quick.Duration, quick.Easing));
        theme.Add(new Style("WindowButton:hover").Set(UIElement.BackgroundProperty, hover));
        theme.Add(new Style("WindowButton:pressed").Set(UIElement.BackgroundProperty, pressed));
        theme.Add(new Style("WindowButton.close:hover").Set(UIElement.BackgroundProperty, Color(0xc42b1c)));
        return theme;
    }

    private static Vector4 Color(uint rgb) => new((rgb >> 16) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, 1);
}
