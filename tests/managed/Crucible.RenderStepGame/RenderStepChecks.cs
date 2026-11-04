using System.Numerics;
using Crucible.Assets;
using Crucible.Loop;
using Crucible.Render;

namespace Crucible.RenderStepGame;

// Run headless by CTest (launcher.jit.render-steps) with
// --game-arg shader=<StepProbe.crshader>. Frame by frame:
//   1      the probe step joins the schedule after Tonemap, and is prepared
//   2      it draws: a compute pass copies four colors from the frame data
//          into a transient buffer; a graphics pass draws a quad per quarter
//          in them, the last quarter scissored away
//   3      it throws: that frame isn't submitted, and the next one is
//   6      the frame is read back and checked; the step leaves and is
//          released; the game quits, printing its checks

/// <summary>A C# step drawing through every kind of command record.</summary>
public sealed class ProbeStep : IRenderStep
{
    public static readonly AssetId Shader = AssetId.Parse("5e7a0b1c-2d3e-4f50-8172-93a4b5c6d7e8");

    private PipelineHandle _fill;
    private PipelineHandle _quads;

    public int Prepares { get; private set; }

    public int Releases { get; private set; }

    public bool ThrowNext { get; set; }

    private struct ProbeConstants
    {
        public uint Buffer;
        public uint FrameData;
        public uint Offset;
    }

    public void Prepare()
    {
        Prepares++;
        _fill = Render.Api.CreateComputePipeline(Shader, "fillColors");
        GraphicsPipelineState state = new() { Blend = BlendMode.Opaque, Topology = Topology.TriangleStrip, ColorCount = 1 };
        state.ColorFormats[0] = RenderFormat.B8G8R8A8UnormSrgb;
        _quads = Render.Api.CreateGraphicsPipeline(Shader, "quadVertex", "quadPixel", state);
    }

    public void Release()
    {
        Releases++;
        Render.Api.DestroyPipeline(_fill);
        Render.Api.DestroyPipeline(_quads);
    }

    public void AddPasses(RenderStepContext context)
    {
        if (ThrowNext)
        {
            ThrowNext = false;
            throw new InvalidOperationException("the probe's deliberate failure");
        }
        FrameData<Vector4> colors = context.Upload<Vector4>(4);
        colors.Data[0] = new Vector4(1, 0, 0, 1);
        colors.Data[1] = new Vector4(0, 1, 0, 1);
        colors.Data[2] = new Vector4(0, 0, 1, 1);
        colors.Data[3] = Vector4.One;
        FrameData<Vector4> tint = context.Upload<Vector4>(1);
        tint.Data[0] = Vector4.One;
        GraphBuffer buffer = context.CreateBuffer(64);

        RenderPass fill = context.AddPass("Test.Fill", PassQueue.Compute);
        uint writeSlot = fill.Use(buffer, ResourceUsage.ShaderWrite);
        RenderCommands commands = fill.Commands;
        commands.SetPipeline(_fill);
        commands.PushConstants(new ProbeConstants
        {
            Buffer = writeSlot,
            FrameData = context.FrameDataDescriptor,
            Offset = colors.Offset,
        });
        commands.Dispatch(1);

        RenderPass quads = context.AddPass("Test.Quads");
        uint readSlot = quads.Use(buffer, ResourceUsage.ShaderRead);
        quads.RenderTarget(context.Texture(BlackboardKey.Backbuffer));
        commands = quads.Commands;
        commands.BeginLabel("Test.Quads");
        commands.SetPipeline(_quads);
        commands.PushConstants(new ProbeConstants { Buffer = readSlot });
        commands.SetConstantBuffer(tint);
        commands.Draw(4, 3);
        commands.SetScissor(0, 0, context.Width / 2, context.Height);
        commands.Draw(4, 1, 0, 3);
        commands.EndLabel();
    }
}

[System(Phases.RenderPrepare)]
public sealed class RenderStepChecks : GameSystem
{
    private readonly ProbeStep _step = new();
    private byte[]? _shader;
    private int _frame;
    private ulong _submittedBeforeThrow;
    private int _checks;
    private int _failures;

    public override void Start(FrameContext context)
    {
        _shader = File.ReadAllBytes(context.GameArguments["shader"]);
        RenderAssets.SetReader(id => id == ProbeStep.Shader ? _shader : null);
        RenderSchedule.InsertAfter("Tonemap", "Test.Probe", _step);
        Check(_step.Prepares == 1, "the step is prepared when it joins a running renderer");
        IReadOnlyList<string> names = RenderSchedule.StepNames;
        Check(names.Count > 1 && names[^1] == "Test.Probe", "the step comes after Tonemap");
        Check(Throws<CrucibleException>(() => RenderSchedule.Add("Test.Probe", _step)), "names are unique");
        Check(Throws<CrucibleException>(() => RenderSchedule.Remove("Test.Nowhere")), "removing needs a step");
    }

    public override void Run(FrameContext context)
    {
        ++_frame;
        if (_frame == 3)
        {
            _step.ThrowNext = true;
            _submittedBeforeThrow = Render.Api.FramesSubmitted(context.Surface);
        }
        else if (_frame == 4)
        {
            Check(Render.Api.FramesSubmitted(context.Surface) == _submittedBeforeThrow,
                "the frame the step threw in isn't submitted");
        }
        else if (_frame == 5)
        {
            Check(Render.Api.FramesSubmitted(context.Surface) == _submittedBeforeThrow + 1, "the next frame is");
        }
        else if (_frame == 6)
        {
            CheckFrame(context);
            RenderSchedule.Remove("Test.Probe");
            Check(_step.Releases == 1, "the step is released when it leaves");
            Check(!RenderSchedule.StepNames.Contains("Test.Probe"), "it's gone from the schedule");
            RenderAssets.SetReader(null);
            Console.WriteLine(_failures == 0
                ? $"RenderStepGame: all {_checks} checks passed"
                : $"RenderStepGame: {_failures} of {_checks} checks failed");
            context.QuitRequested = true;
        }
    }

    // The last frame rendered (frame 5's): a quad per quarter.
    private void CheckFrame(FrameContext context)
    {
        const int width = 1280, height = 720; // the launcher's headless surface
        byte[] bgra = new byte[width * height * 4];
        Render.Api.ReadSurfacePixels(context.Surface, bgra);
        (int R, int G, int B) Pixel(int x, int y)
        {
            int i = (y * width + x) * 4;
            return (bgra[i + 2], bgra[i + 1], bgra[i]);
        }
        Check(Pixel(320, 180) == (255, 0, 0), "red top left", Pixel(320, 180).ToString());
        Check(Pixel(960, 180) == (0, 255, 0), "green top right", Pixel(960, 180).ToString());
        Check(Pixel(320, 540) == (0, 0, 255), "blue bottom left", Pixel(320, 540).ToString());
        (int r, int g, int b) = Pixel(960, 540);
        Check(r < 128 && g < 128 && b < 128, "the white quad scissored away", Pixel(960, 540).ToString());
    }

    private static bool Throws<T>(Action action) where T : Exception
    {
        try
        {
            action();
            return false;
        }
        catch (T)
        {
            return true;
        }
    }

    private void Check(bool passed, string what, string detail = "")
    {
        ++_checks;
        if (!passed)
        {
            ++_failures;
            Console.WriteLine($"RenderStepGame: FAILED: {what} {detail}");
        }
    }
}
