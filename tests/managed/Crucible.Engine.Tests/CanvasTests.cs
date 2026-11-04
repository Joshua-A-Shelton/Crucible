using System.Numerics;
using Crucible.UI;

namespace Crucible.Tests;

// The Canvas's bookkeeping (ADR 0059), under JIT and NativeAOT. Drawing on
// the GPU is launcher.jit.canvas's: every primitive, checked at pixels.
public class CanvasTests
{
    [Test]
    public async Task ACanvasHoldsWhatIsDrawnUntilCleared()
    {
        Canvas canvas = new() { Scale = 1.5f };
        canvas.FillRect(Vector2.Zero, Vector2.One, Vector4.One);
        canvas.StrokeRect(Vector2.Zero, Vector2.One, Vector4.One, 2);
        canvas.FillRect(Vector2.Zero, Vector2.One, Gradient.Linear(Vector2.Zero, Vector2.UnitX, Vector4.Zero, Vector4.One));
        canvas.DrawLine(Vector2.Zero, Vector2.One, 1, Vector4.One);
        canvas.DrawPolyline([Vector2.Zero, Vector2.One, Vector2.UnitX], 1, Vector4.One); // two segments
        canvas.DrawShadow(Vector2.Zero, Vector2.One, 2, 4, Vector4.One);
        await Assert.That(canvas.Count).IsEqualTo(7);
        canvas.Clear();
        await Assert.That(canvas.Count).IsEqualTo(0);
    }

    [Test]
    public async Task PopsNeedPushes()
    {
        Canvas canvas = new();
        canvas.PushTransform(Matrix3x2.CreateRotation(1));
        canvas.PushClip(Vector2.Zero, new Vector2(10));
        canvas.PopClip();
        canvas.PopTransform();
        await Assert.That(Assert.Throws<InvalidOperationException>(canvas.PopClip).Message).Contains("PopClip");
        await Assert.That(Assert.Throws<InvalidOperationException>(canvas.PopTransform).Message).Contains("PopTransform");
        // Clear forgets pushes left open.
        canvas.PushClip(Vector2.Zero, Vector2.One);
        canvas.Clear();
        Assert.Throws<InvalidOperationException>(canvas.PopClip);
    }
}
