using System.Numerics;
using Crucible.UI;
using Rig = Crucible.Tests.TreeViewTests.Rig;

namespace Crucible.Tests;

// The color swatch and picker (ADR 0069): conversions, the square and
// strips under the pointer, hex, and alpha only where there is one.
[NotInParallel("text")]
public class ColorPickerTests
{
    private const int Return = 40;

    private static bool Near(Vector4 a, Vector4 b, float tolerance = 2e-3f) => Vector4.Distance(a, b) <= tolerance;

    [Test]
    public async Task ColorsConvertBetweenLinearSrgbHsvAndHex()
    {
        await Assert.That(ColorPicker.ToSrgb(ColorPicker.ToLinear(0.5f))).IsEqualTo(0.5f).Within(1e-5f);
        await Assert.That(ColorPicker.ToLinear(1)).IsEqualTo(1f).Within(1e-6f);
        // Linear 0.2159 is sRGB 0.502, so 0x80.
        await Assert.That(ColorPicker.Hex(new Vector4(1, 0.2159f, 0, 1), alpha: false)).IsEqualTo("FF8000");
        await Assert.That(ColorPicker.Hex(new Vector4(4, 0, 0, 0.5f), alpha: true)).IsEqualTo("FF000080"); // HDR clamped
        await Assert.That(ColorPicker.TryParseHex("#336699", out Vector4 parsed)).IsTrue();
        await Assert.That(Near(parsed, new Vector4(0.2f, 0.4f, 0.6f, 1))).IsTrue();
        await Assert.That(ColorPicker.TryParseHex("12345", out _)).IsFalse();
        (float h, float s, float v) = ColorPicker.ToHsv(new Vector3(0, 0.5f, 1));
        await Assert.That(Near(new Vector4(ColorPicker.FromHsv(h, s, v), 1), new Vector4(0, 0.5f, 1, 1))).IsTrue();
        await Assert.That(h).IsEqualTo(7 / 12f).Within(1e-5f);
    }

    [Test]
    public async Task TheSwatchOpensAPickerThatPicksUnderThePointer()
    {
        using Rig rig = new();
        ColorField field = rig.Add(new ColorField { Color = new Vector4(1, 0, 0, 1) });
        int changes = 0;
        field.ColorChanged += _ => ++changes;
        Vector2 at = new(field.Bounds.X + field.Bounds.Width / 2, field.Bounds.Y + field.Bounds.Height / 2);
        rig.Send(TreeViewTests.Motion(at), TreeViewTests.Button(at, true), TreeViewTests.Button(at, false));
        ColorPicker picker = field.Picker;
        await Assert.That(picker.IsOpen).IsTrue();

        // The square's bottom left is black, whatever the hue; its top right the hue itself.
        UIElement area = Descendants(picker).First(element => element.GetType().Name == "Area");
        Vector2 topRight = new(area.Bounds.Right - 0.5f, area.Bounds.Top + 0.5f);
        Vector2 bottomLeft = new(area.Bounds.Left + 0.5f, area.Bounds.Bottom - 0.5f);
        rig.Send(TreeViewTests.Motion(bottomLeft), TreeViewTests.Button(bottomLeft, true));
        await Assert.That(Near(field.Color, new Vector4(0, 0, 0, 1), 1e-2f)).IsTrue();
        rig.Send(TreeViewTests.Motion(topRight), TreeViewTests.Button(topRight, false));
        await Assert.That(Near(field.Color, new Vector4(1, 0, 0, 1), 2e-2f)).IsTrue();
        await Assert.That(changes).IsGreaterThanOrEqualTo(2);

        // Half way down the hue strip is cyan; the hue stays when the square goes gray.
        UIElement strip = Descendants(picker).First(element => element.GetType().Name == "Strip");
        Vector2 middle = new(strip.Bounds.X + strip.Bounds.Width / 2, strip.Bounds.Y + strip.Bounds.Height / 2);
        rig.Send(TreeViewTests.Motion(middle), TreeViewTests.Button(middle, true), TreeViewTests.Button(middle, false));
        await Assert.That(Near(field.Color, new Vector4(0, 1, 1, 1), 2e-2f)).IsTrue(); // the pick is half a pixel in
        rig.Send(TreeViewTests.Motion(bottomLeft), TreeViewTests.Button(bottomLeft, true), TreeViewTests.Button(bottomLeft, false));
        await Assert.That(picker.Hsv.X).IsEqualTo(0.5f).Within(1e-2f);

        // Hex typed and committed.
        TextField hex = Descendants(picker).OfType<TextField>().First(element => element is not NumberField);
        hex.Focus();
        hex.SelectAll();
        rig.Type("336699");
        rig.Send(TreeViewTests.Key(Return));
        await Assert.That(Near(field.Color, new Vector4(ColorPicker.ToLinear(0.2f), ColorPicker.ToLinear(0.4f), ColorPicker.ToLinear(0.6f), 1))).IsTrue();
    }

    [Test]
    public async Task AlphaIsEditedOnlyWhenThereIsOne()
    {
        using Rig rig = new();
        ColorField field = rig.Add(new ColorField());
        field.Color = new Vector4(1, 1, 1, 0.25f);
        await Assert.That(field.Color.W).IsEqualTo(1f); // a Vector3's color
        field.HasAlpha = true;
        field.Color = new Vector4(1, 1, 1, 0.25f);
        await Assert.That(field.Color.W).IsEqualTo(0.25f);
        await Assert.That(ColorPicker.Hex(field.Color, true)).EndsWith("40");
    }

    private static IEnumerable<UIElement> Descendants(UIElement element)
    {
        foreach (UIElement child in element.Children)
        {
            yield return child;
            foreach (UIElement inner in Descendants(child))
            {
                yield return inner;
            }
        }
    }
}
