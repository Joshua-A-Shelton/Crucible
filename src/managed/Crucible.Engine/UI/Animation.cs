namespace Crucible.UI;

/// <summary>
/// An easing curve (ADR 0061): progress through an animation (0 to 1) to
/// how far the value has moved, as CSS's cubic Béziers from (0, 0) to
/// (1, 1) through two control points.
/// </summary>
public readonly record struct Easing(float X1, float Y1, float X2, float Y2)
{
    public static Easing Linear => new(0, 0, 1, 1);

    /// <summary>CSS's "ease": a quick start, a gentle end.</summary>
    public static Easing Ease => new(0.25f, 0.1f, 0.25f, 1);

    public static Easing EaseIn => new(0.42f, 0, 1, 1);

    public static Easing EaseOut => new(0, 0, 0.58f, 1);

    public static Easing EaseInOut => new(0.42f, 0, 0.58f, 1);

    /// <summary>The curve's y at x = <paramref name="t"/>, clamped to [0, 1].</summary>
    public float Evaluate(float t)
    {
        t = Math.Clamp(t, 0, 1);
        if (X1 == Y1 && X2 == Y2)
        {
            return t; // linear
        }
        // Solve x(s) = t for the curve parameter s (Newton, then bisection
        // if the slope is flat), then return y(s).
        float s = t;
        for (int i = 0; i < 8; ++i)
        {
            float error = Bezier(s, X1, X2) - t;
            float slope = Slope(s, X1, X2);
            if (MathF.Abs(error) < 1e-6f)
            {
                return Bezier(s, Y1, Y2);
            }
            if (MathF.Abs(slope) < 1e-6f)
            {
                break;
            }
            s -= error / slope;
        }
        float low = 0, high = 1;
        s = t;
        for (int i = 0; i < 32; ++i)
        {
            float x = Bezier(s, X1, X2);
            if (MathF.Abs(x - t) < 1e-6f)
            {
                break;
            }
            if (x < t)
            {
                low = s;
            }
            else
            {
                high = s;
            }
            s = (low + high) * 0.5f;
        }
        return Bezier(s, Y1, Y2);
    }

    // One coordinate of the curve with end points 0 and 1.
    private static float Bezier(float s, float p1, float p2)
    {
        float u = 1 - s;
        return 3 * u * u * s * p1 + 3 * u * s * s * p2 + s * s * s;
    }

    private static float Slope(float s, float p1, float p2)
    {
        float u = 1 - s;
        return 3 * u * u * p1 + 6 * u * s * (p2 - p1) + 3 * s * s * (1 - p2);
    }

    /// <summary>A preset by name (Linear, Ease, EaseIn, EaseOut, EaseInOut), for themes.</summary>
    public static bool TryParse(string name, out Easing easing)
    {
        easing = name switch
        {
            "Linear" => Linear,
            "Ease" => Ease,
            "EaseIn" => EaseIn,
            "EaseOut" => EaseOut,
            "EaseInOut" => EaseInOut,
            _ => default,
        };
        return name is "Linear" or "Ease" or "EaseIn" or "EaseOut" or "EaseInOut";
    }
}

// A property moving from one value to another over time: a style's
// transition, or a tween from Animate.
internal abstract class Animation
{
    public double Start;
    public float Duration;
    public Easing Easing;
    public bool IsTween; // ends by setting the local value

    // Writes the value at `now` into the slot; true while it runs.
    public abstract bool Update(ValueSlot slot, double now);

    // The value the property ends at.
    public abstract object Target { get; }
}

internal sealed class Animation<T>(StyledProperty<T> property, T from, T to) : Animation
{
    public readonly T From = from;
    public readonly T To = to;

    public override object Target => To!;

    public override bool Update(ValueSlot slot, double now)
    {
        float progress = Duration <= 0 ? 1 : (float)Math.Clamp((now - Start) / Duration, 0, 1);
        var typed = (ValueSlot<T>)slot;
        typed.Animated = progress >= 1 || property.Interpolate is null
            ? progress >= 1 ? To : From
            : property.Interpolate(From, To, Easing.Evaluate(progress));
        return progress < 1;
    }
}
