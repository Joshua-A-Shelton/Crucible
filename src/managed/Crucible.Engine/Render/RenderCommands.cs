using System.Runtime.CompilerServices;
using System.Text;

namespace Crucible.Render;

/// <summary>
/// A pass's commands (ADR 0058), written as compact records straight into
/// native memory and played back as Slag calls when the graph records the
/// pass. Nothing here crosses into native code except when the pass's memory
/// fills up. Records are checked when the step returns; a bad one fails the
/// frame, naming the step, pass, and record.
/// </summary>
public readonly unsafe struct RenderCommands
{
    private readonly RenderStepContext _context;
    private readonly int _slot;
    private readonly uint _generation;

    internal RenderCommands(RenderStepContext context, int slot, uint generation)
    {
        _context = context;
        _slot = slot;
        _generation = generation;
    }

    public void SetPipeline(PipelineHandle pipeline) =>
        Write(RenderCommandType.SetPipeline, new SetPipelineCommand { Pipeline = pipeline });

    /// <summary>Up to 128 bytes shared by every stage, at a 4-byte multiple.</summary>
    public void PushConstants<T>(in T value, uint offset = 0) where T : unmanaged
    {
        uint size = (uint)sizeof(T);
        if (size % 4 != 0 || offset % 4 != 0 || offset + size > 128)
        {
            throw new ArgumentException($"Push constants are at most 128 bytes, at 4-byte multiples ({sizeof(T)} at {offset})");
        }
        byte* at = Begin(RenderCommandType.PushConstants, (uint)sizeof(PushConstantsCommand), size);
        Unsafe.WriteUnaligned(at, new PushConstantsCommand { Offset = offset, Size = size });
        Unsafe.WriteUnaligned(at + sizeof(PushConstantsCommand), value);
    }

    /// <summary>A slice of the frame data as the constant buffer (register b1).</summary>
    public void SetConstantBuffer<T>(FrameData<T> data) where T : unmanaged =>
        Write(RenderCommandType.SetConstantBuffer,
            new SetConstantBufferCommand { Buffer = new GraphBuffer { Index = uint.MaxValue }, Offset = data.Offset, Size = data.Size });

    /// <summary>
    /// A slice of a buffer the pass uses as ConstantBuffer; the offset is a
    /// multiple of 256.
    /// </summary>
    public void SetConstantBuffer(GraphBuffer buffer, uint offset, uint size) =>
        Write(RenderCommandType.SetConstantBuffer, new SetConstantBufferCommand { Buffer = buffer, Offset = offset, Size = size });

    /// <summary>Graphics passes; the pass starts with its targets' whole size.</summary>
    public void SetViewport(float x, float y, float width, float height, float minDepth = 0, float maxDepth = 1)
    {
        RequireQueue(PassQueue.Graphics, "SetViewport");
        Write(RenderCommandType.SetViewport, new SetViewportCommand
        {
            X = x,
            Y = y,
            Width = width,
            Height = height,
            MinDepth = minDepth,
            MaxDepth = maxDepth,
        });
    }

    public void SetScissor(int x, int y, uint width, uint height)
    {
        RequireQueue(PassQueue.Graphics, "SetScissor");
        Write(RenderCommandType.SetScissor, new SetScissorCommand { X = x, Y = y, Width = width, Height = height });
    }

    /// <summary>Graphics passes, with a graphics pipeline set.</summary>
    public void Draw(uint vertexCount, uint instanceCount = 1, uint firstVertex = 0, uint firstInstance = 0)
    {
        RequireQueue(PassQueue.Graphics, "Draw");
        Write(RenderCommandType.Draw, new DrawCommand
        {
            VertexCount = vertexCount,
            InstanceCount = instanceCount,
            FirstVertex = firstVertex,
            FirstInstance = firstInstance,
        });
    }

    /// <summary>Compute passes, with a compute pipeline set.</summary>
    public void Dispatch(uint x, uint y = 1, uint z = 1)
    {
        RequireQueue(PassQueue.Compute, "Dispatch");
        Write(RenderCommandType.Dispatch, new DispatchCommand { X = x, Y = y, Z = z });
    }

    /// <summary>A named region for GPU debuggers and profilers, ended by <see cref="EndLabel"/>.</summary>
    public void BeginLabel(string name)
    {
        uint length = (uint)Encoding.UTF8.GetByteCount(name) + 1; // and a NUL
        byte* at = Begin(RenderCommandType.BeginLabel, (uint)sizeof(BeginLabelCommand), length);
        Unsafe.WriteUnaligned(at, new BeginLabelCommand { Length = length });
        byte* text = at + sizeof(BeginLabelCommand);
        Encoding.UTF8.GetBytes(name, new Span<byte>(text, (int)length - 1));
        text[length - 1] = 0;
    }

    public void EndLabel() => Begin(RenderCommandType.EndLabel, 0, 0);

    private void Write<T>(RenderCommandType type, in T command) where T : unmanaged =>
        Unsafe.WriteUnaligned(Begin(type, (uint)sizeof(T), 0), command);

    // A record's header, written; returns where its command goes.
    private byte* Begin(RenderCommandType type, uint payload, uint extra)
    {
        uint size = (uint)sizeof(RenderCommandHeader) + payload + ((extra + 3) & ~3u);
        if (size > ushort.MaxValue * 4u)
        {
            throw new ArgumentException("A command record is at most 256 KiB");
        }
        byte* at = _context.Reserve(_slot, _generation, size);
        Unsafe.WriteUnaligned(at, new RenderCommandHeader { Type = type, Words = (ushort)(size / 4) });
        return at + sizeof(RenderCommandHeader);
    }

    private void RequireQueue(PassQueue queue, string command)
    {
        if (_context.Queue(_slot, _generation) != queue)
        {
            throw new InvalidOperationException($"{command} is for {queue.ToString().ToLowerInvariant()} passes");
        }
    }
}

public partial struct BlackboardKey
{
    /// <summary>The same name always gives the same key; plugins register their own.</summary>
    public static BlackboardKey Register(string name) => Api.RegisterBlackboardKey(name);

    public string Name => Api.BlackboardKeyName(this);

    /// <summary>The surface's image, published before the first step runs.</summary>
    public static BlackboardKey Backbuffer => Api.BackbufferKey();

    /// <summary>The frame's ViewConstants (shared.slang), an upload buffer published before the first step runs.</summary>
    public static BlackboardKey View => Api.ViewKey();

    /// <summary>The MSAA scene targets (ADR 0051): depth from DepthPrepass, linear color from Opaque.</summary>
    public static BlackboardKey SceneDepth => Api.SceneDepthKey();

    public static BlackboardKey SceneColor => Api.SceneColorKey();

    /// <summary>Bloom's chain (ADR 0055), when the camera has bloom.</summary>
    public static BlackboardKey Bloom => Api.BloomKey();
}
