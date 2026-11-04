using System.Numerics;
using Crucible.Render;
using Crucible.Shaders;
using Crucible.Text;

namespace Crucible.UI;

/// <summary>
/// The UI's 2D drawing API (ADR 0059): what elements and custom-draw
/// elements draw with. Positions and sizes are logical units, mapped to
/// physical pixels by the transform stack and <see cref="Scale"/>. Colors are
/// sRGB with straight alpha, as designers give them; the canvas blends them
/// in linear light. Every primitive becomes one instance of the UI shader,
/// drawn in order, usually in a single draw.
/// </summary>
/// <remarks>
/// A canvas is also the render step that draws it: add it to the schedule
/// (normally as "UI" after Tonemap) and it draws what it holds every frame,
/// until <see cref="Clear"/>. Text layouts drawn must stay alive while the
/// canvas holds them. Main thread only.
/// </remarks>
public sealed class Canvas : IRenderStep
{
    private const uint KindRect = 0, KindImage = 1, KindNineSlice = 2, KindLine = 5, KindShadow = 6;
    private const float Unbounded = 1e9f;

    private struct TextRun
    {
        public int At; // its place among the instances
        public TextLayoutHandle Layout;
        public Vector2 Origin;
        public Vector4 Color;
        public Matrix3x2 Transform;
        public Vector4 Clip;
    }

    private struct UiConstants
    {
        public Vector2 TargetSize;
        public uint FrameData;
        public uint First;
    }

    private UiInstance[] _items = new UiInstance[256];
    private int _count;
    private TextRun[] _texts = new TextRun[16];
    private int _textCount;
    private Matrix3x2[] _transforms = new Matrix3x2[8];
    private int _transformDepth;
    private Vector4[] _clips = new Vector4[8];
    private int _clipDepth;
    private PipelineHandle _pipeline;

    /// <summary>Physical pixels per logical unit: the DPI scale.</summary>
    public float Scale { get; set; } = 1;

    /// <summary>Primitives held, text runs counted once each.</summary>
    public int Count => _count + _textCount;

    /// <summary>Forgets everything drawn, and the transform and clip stacks.</summary>
    public void Clear()
    {
        _count = 0;
        Array.Clear(_texts, 0, _textCount);
        _textCount = 0;
        _transformDepth = 0;
        _clipDepth = 0;
    }

    /// <summary>Later drawing goes through <paramref name="transform"/> too, before the transforms pushed earlier.</summary>
    public void PushTransform(in Matrix3x2 transform)
    {
        Matrix3x2 current = _transformDepth == 0 ? Matrix3x2.Identity : _transforms[_transformDepth - 1];
        Push(ref _transforms, ref _transformDepth, transform * current);
    }

    public void PopTransform() => Pop(ref _transformDepth, "PopTransform");

    /// <summary>
    /// Clips later drawing to a rectangle, inside any clip already pushed: an
    /// axis-aligned box in physical pixels, whole pixels, around the
    /// rectangle as transformed.
    /// </summary>
    public void PushClip(Vector2 position, Vector2 size)
    {
        Matrix3x2 m = Effective();
        Vector2 a = Vector2.Transform(position, m), b = Vector2.Transform(position + new Vector2(size.X, 0), m);
        Vector2 c = Vector2.Transform(position + new Vector2(0, size.Y), m), d = Vector2.Transform(position + size, m);
        Vector2 min = Vector2.Min(Vector2.Min(a, b), Vector2.Min(c, d));
        Vector2 max = Vector2.Max(Vector2.Max(a, b), Vector2.Max(c, d));
        Vector4 clip = new(MathF.Round(min.X), MathF.Round(min.Y), MathF.Round(max.X), MathF.Round(max.Y));
        Vector4 outer = CurrentClip();
        clip = new Vector4(MathF.Max(clip.X, outer.X), MathF.Max(clip.Y, outer.Y), MathF.Min(clip.Z, outer.Z),
            MathF.Min(clip.W, outer.W));
        Push(ref _clips, ref _clipDepth, clip);
    }

    public void PopClip() => Pop(ref _clipDepth, "PopClip");

    /// <summary>A filled rectangle with rounded corners.</summary>
    public void FillRect(Vector2 position, Vector2 size, Vector4 color, float radius = 0) =>
        FillRect(position, size, color, new Vector4(radius));

    /// <summary>Corners: top left, top right, bottom right, bottom left.</summary>
    public void FillRect(Vector2 position, Vector2 size, Vector4 color, Vector4 radii)
    {
        ref UiInstance item = ref Add(KindRect, position, size);
        item.Radii = radii;
        item.Color = Linear(color);
    }

    /// <summary>A rectangle filled with a gradient, interpolated in linear light.</summary>
    public void FillRect(Vector2 position, Vector2 size, in Gradient gradient, Vector4 radii = default)
    {
        ref UiInstance item = ref Add(KindRect, position, size);
        item.Radii = radii;
        item.Color = Linear(gradient.StartColor);
        item.Color2 = Linear(gradient.EndColor);
        item.Extra = new Vector4(0, (float)gradient.Kind, 0, 0);
        item.Uv = gradient.Kind == GradientKind.Radial
            ? new Vector4(position + gradient.Start, gradient.Radius, 0)
            : new Vector4(position + gradient.Start, position.X + gradient.End.X, position.Y + gradient.End.Y);
    }

    /// <summary>A border <paramref name="width"/> wide, inside the rectangle.</summary>
    public void StrokeRect(Vector2 position, Vector2 size, Vector4 color, float width, Vector4 radii = default)
    {
        ref UiInstance item = ref Add(KindRect, position, size);
        item.Radii = radii;
        item.Extra = new Vector4(width, 0, 0, 0);
        item.Extra2 = Linear(color);
    }

    /// <summary>An image (sRGB textures decode to linear), tinted, with rounded corners.</summary>
    public void DrawImage(TextureHandle texture, Vector2 position, Vector2 size) =>
        DrawImage(texture, position, size, new Vector4(0, 0, 1, 1), Vector4.One, default);

    /// <summary><paramref name="uv"/>: the source rectangle (u0, v0, u1, v1).</summary>
    public void DrawImage(TextureHandle texture, Vector2 position, Vector2 size, Vector4 uv, Vector4 tint, Vector4 radii)
    {
        ref UiInstance item = ref Add(KindImage, position, size);
        item.Texture = RenderAssets.TextureDescriptor(texture);
        item.Uv = uv;
        item.Color = Linear(tint);
        item.Radii = radii;
    }

    /// <summary>
    /// An image whose corners keep their size: <paramref name="insets"/> are
    /// the destination's borders in logical units, and
    /// <paramref name="sourceInsets"/> the source's in uv (left, top, right,
    /// bottom); the edges and middle stretch.
    /// </summary>
    public void DrawNineSlice(TextureHandle texture, Vector2 position, Vector2 size, Vector4 insets,
        Vector4 sourceInsets, Vector4 tint)
    {
        ref UiInstance item = ref Add(KindNineSlice, position, size);
        item.Texture = RenderAssets.TextureDescriptor(texture);
        item.Uv = new Vector4(0, 0, 1, 1);
        item.Extra = insets;
        item.Extra2 = sourceInsets;
        item.Color = Linear(tint);
    }

    /// <summary>
    /// A layout's text, its top left at <paramref name="position"/>. Exact
    /// glyphs are sharp when the layout's scale is <see cref="Scale"/> and
    /// the transforms only move it; distance-field glyphs suit any
    /// transform.
    /// </summary>
    public void DrawText(TextLayout layout, Vector2 position, Vector4 color)
    {
        ArgumentNullException.ThrowIfNull(layout);
        if (_textCount == _texts.Length)
        {
            Array.Resize(ref _texts, _texts.Length * 2);
        }
        _texts[_textCount++] = new TextRun
        {
            At = _count,
            Layout = layout.Handle,
            Origin = position,
            Color = Linear(color),
            Transform = Effective(),
            Clip = CurrentClip(),
        };
    }

    /// <summary>An antialiased segment with round caps.</summary>
    public void DrawLine(Vector2 from, Vector2 to, float width, Vector4 color)
    {
        float half = width * 0.5f;
        Vector2 min = Vector2.Min(from, to) - new Vector2(half), max = Vector2.Max(from, to) + new Vector2(half);
        ref UiInstance item = ref Add(KindLine, min, max - min);
        item.Uv = new Vector4(from, to.X, to.Y);
        item.Extra = new Vector4(width, 0, 0, 0);
        item.Color = Linear(color);
    }

    /// <summary>
    /// Segments between successive points; curves are polylines. Joints are
    /// round, and a translucent line is darker where segments overlap.
    /// </summary>
    public void DrawPolyline(ReadOnlySpan<Vector2> points, float width, Vector4 color)
    {
        for (int i = 1; i < points.Length; ++i)
        {
            DrawLine(points[i - 1], points[i], width, color);
        }
    }

    /// <summary>
    /// A blurred rounded box, as CSS's box-shadow: <paramref name="blur"/> is
    /// the blur radius (twice the Gaussian's sigma). Draw it before what it
    /// shadows.
    /// </summary>
    public void DrawShadow(Vector2 position, Vector2 size, float radius, float blur, Vector4 color)
    {
        ref UiInstance item = ref Add(KindShadow, position, size);
        item.Radii = new Vector4(radius);
        item.Extra = new Vector4(MathF.Max(blur * 0.5f, 1e-3f), 0, 0, 0);
        item.Color = Linear(color);
    }

    void IRenderStep.Prepare()
    {
        GraphicsPipelineState state = new()
        {
            Blend = BlendMode.Premultiplied,
            Topology = Topology.TriangleStrip,
            ColorCount = 1,
        };
        state.ColorFormats[0] = RenderFormat.B8G8R8A8UnormSrgb;
        _pipeline = Render.Api.CreateEngineGraphicsPipeline("ui", "uiVertex", "uiPixel", state);
    }

    void IRenderStep.Release() => Render.Api.DestroyPipeline(_pipeline);

    void IRenderStep.AddPasses(RenderStepContext context)
    {
        if (Count == 0)
        {
            return;
        }
        int capacity = _count;
        for (int i = 0; i < _textCount; ++i)
        {
            capacity += (int)Text.Api.TextGlyphCount(_texts[i].Layout);
        }
        FrameData<UiInstance> data = context.Upload<UiInstance>(capacity);
        Span<UiInstance> output = data.Data;
        int written = 0, copied = 0;
        for (int i = 0; i < _textCount; ++i)
        {
            ref TextRun run = ref _texts[i];
            _items.AsSpan(copied, run.At - copied).CopyTo(output[written..]);
            written += run.At - copied;
            copied = run.At;
            Matrix3x2 m = run.Transform;
            written += (int)Text.Api.WriteTextQuads(run.Layout, run.Origin, new Vector4(m.M11, m.M12, m.M21, m.M22),
                m.Translation, run.Color, run.Clip,
                System.Runtime.InteropServices.MemoryMarshal.AsBytes(output[written..]));
        }
        _items.AsSpan(copied, _count - copied).CopyTo(output[written..]);
        written += _count - copied;
        if (written == 0)
        {
            return;
        }

        RenderPass pass = context.AddPass("UI");
        pass.RenderTarget(context.Texture(BlackboardKey.Backbuffer));
        RenderCommands commands = pass.Commands;
        commands.SetPipeline(_pipeline);
        commands.PushConstants(new UiConstants
        {
            TargetSize = new Vector2(context.Width, context.Height),
            FrameData = context.FrameDataDescriptor,
            First = data.Offset,
        });
        commands.Draw(4, (uint)written);
    }

    private ref UiInstance Add(uint kind, Vector2 position, Vector2 size)
    {
        if (_count == _items.Length)
        {
            Array.Resize(ref _items, _items.Length * 2);
        }
        ref UiInstance item = ref _items[_count++];
        Matrix3x2 m = Effective();
        item = new UiInstance
        {
            Rect = new Vector4(position, size.X, size.Y),
            Transform = new Vector4(m.M11, m.M12, m.M21, m.M22),
            Translation = m.Translation,
            Kind = kind,
            Clip = CurrentClip(),
        };
        return ref item;
    }

    private Matrix3x2 Effective()
    {
        Matrix3x2 user = _transformDepth == 0 ? Matrix3x2.Identity : _transforms[_transformDepth - 1];
        return user * Matrix3x2.CreateScale(Scale);
    }

    private Vector4 CurrentClip() =>
        _clipDepth == 0 ? new Vector4(-Unbounded, -Unbounded, Unbounded, Unbounded) : _clips[_clipDepth - 1];

    private static void Push<T>(ref T[] stack, ref int depth, T value)
    {
        if (depth == stack.Length)
        {
            Array.Resize(ref stack, stack.Length * 2);
        }
        stack[depth++] = value;
    }

    private static void Pop(ref int depth, string what)
    {
        if (depth == 0)
        {
            throw new InvalidOperationException($"{what} without a matching push");
        }
        --depth;
    }

    // sRGB with straight alpha to linear, premultiplied.
    private static Vector4 Linear(Vector4 srgb)
    {
        static float Decode(float c) => c <= 0.04045f ? c / 12.92f : MathF.Pow((c + 0.055f) / 1.055f, 2.4f);
        return new Vector4(Decode(srgb.X) * srgb.W, Decode(srgb.Y) * srgb.W, Decode(srgb.Z) * srgb.W, srgb.W);
    }
}

public enum GradientKind
{
    None = 0,
    Linear = 1,
    Radial = 2,
}

/// <summary>
/// A two-color gradient for <see cref="Canvas.FillRect(Vector2, Vector2, in Gradient, Vector4)"/>,
/// positioned from the rectangle's top left: linear from
/// <see cref="Start"/> to <see cref="End"/>, or radial around
/// <see cref="Start"/> out to <see cref="Radius"/>.
/// </summary>
public struct Gradient
{
    public GradientKind Kind;
    public Vector2 Start;
    public Vector2 End;
    public float Radius;
    public Vector4 StartColor;
    public Vector4 EndColor;

    public static Gradient Linear(Vector2 start, Vector2 end, Vector4 startColor, Vector4 endColor) =>
        new() { Kind = GradientKind.Linear, Start = start, End = end, StartColor = startColor, EndColor = endColor };

    public static Gradient Radial(Vector2 center, float radius, Vector4 startColor, Vector4 endColor) =>
        new() { Kind = GradientKind.Radial, Start = center, Radius = radius, StartColor = startColor, EndColor = endColor };
}
