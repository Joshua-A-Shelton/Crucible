using System.Numerics;
using System.Runtime.CompilerServices;

namespace Crucible.Render;

/// <summary>
/// What a C# step sees while it adds its passes for one frame (ADR 0058):
/// the surface's size, the blackboard, transient resources, frame data, and
/// <see cref="AddPass"/>. Valid only during <see cref="IRenderStep.AddPasses"/>;
/// the engine reuses one context, so passes and frame data kept past it
/// throw or are undefined.
/// </summary>
public sealed unsafe class RenderStepContext
{
    // A pass's command records: native memory C# writes into directly,
    // handed over again (and maybe moved) when it fills up.
    private struct PassStream
    {
        public uint Native; // the pass's number this frame
        public PassQueue Queue;
        public byte* Base;
        public uint Capacity;
        public uint Used;
    }

    private StepFrameHandle _frame;
    private StepFrameInfo _info;
    private PassStream[] _passes = new PassStream[8];
    private int _passCount;
    private uint _generation; // which AddPasses call passes belong to
    private bool _active;

    internal RenderStepContext()
    {
    }

    /// <summary>The surface's size in pixels.</summary>
    public uint Width => Active()._info.Width;

    public uint Height => Active()._info.Height;

    /// <summary>The surface's frame number, counting from 1.</summary>
    public ulong Frame => Active()._info.Frame;

    /// <summary>
    /// The frame data's slot in the resource heap: shaders read
    /// <see cref="Upload{T}"/>'s data as a ByteAddressBuffer through it, at
    /// <see cref="FrameData{T}.Offset"/>.
    /// </summary>
    public uint FrameDataDescriptor => Active()._info.FrameDataDescriptor;

    /// <summary>SceneColor's and SceneDepth's sample count, for pipelines drawing into them.</summary>
    public uint MsaaSamples => Active()._info.MsaaSamples;

    /// <summary>What an earlier step published; reading what none did fails the frame.</summary>
    public GraphTexture Texture(BlackboardKey key) => Api.StepTexture(Active()._frame, key);

    public GraphBuffer Buffer(BlackboardKey key) => Api.StepBuffer(Active()._frame, key);

    /// <summary>Makes a resource available to later steps under <paramref name="key"/> this frame.</summary>
    public void Publish(BlackboardKey key, GraphTexture texture) => Api.PublishTexture(Active()._frame, key, texture);

    public void Publish(BlackboardKey key, GraphBuffer buffer) => Api.PublishBuffer(Active()._frame, key, buffer);

    /// <summary>A texture for this frame only; its contents start undefined.</summary>
    public GraphTexture CreateTexture(in TransientTextureDesc desc) => Api.CreateStepTexture(Active()._frame, desc);

    /// <summary>A buffer for this frame only (a nonzero multiple of 4 bytes); its contents start undefined.</summary>
    public GraphBuffer CreateBuffer(uint size) => Api.CreateStepBuffer(Active()._frame, size);

    /// <summary>
    /// Room for <paramref name="count"/> values in the frame data, 256-byte
    /// aligned. Its contents start undefined: write all of it before
    /// <see cref="IRenderStep.AddPasses"/> returns. It's copied to the GPU
    /// once every step has run.
    /// </summary>
    public FrameData<T> Upload<T>(int count) where T : unmanaged
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        StepMemory memory = Api.UploadFrameData(Active()._frame, checked((uint)(count * sizeof(T))));
        return new FrameData<T>(new Span<T>((void*)memory.Address, count), memory.Offset);
    }

    /// <summary>
    /// Adds a pass after the frame's earlier ones. Declare its uses and
    /// attachments before adding the step's next pass; its commands may be
    /// written until the step returns. It starts with the frame's
    /// ViewConstants as its constant buffer.
    /// </summary>
    public RenderPass AddPass(string name, PassQueue queue = PassQueue.Graphics)
    {
        Active();
        uint native = Api.AddStepPass(_frame, name, queue);
        if (_passCount == _passes.Length)
        {
            Array.Resize(ref _passes, _passes.Length * 2);
        }
        _passes[_passCount] = new PassStream { Native = native, Queue = queue };
        return new RenderPass(this, _passCount++, _generation);
    }

    internal void Begin(StepFrameHandle frame)
    {
        _frame = frame;
        _info = Api.StepFrameInfo(frame);
        _passCount = 0;
        _generation++;
        _active = true;
    }

    // Hands every pass's records over: the last call for each is what plays back.
    internal void Commit()
    {
        for (int i = 0; i < _passCount; ++i)
        {
            ref PassStream stream = ref _passes[i];
            Api.PassCommands(_frame, stream.Native, stream.Used, 0);
        }
    }

    internal void End()
    {
        _active = false;
        _frame = default;
    }

    internal uint NativePass(int slot, uint generation) => Stream(slot, generation).Native;

    internal PassQueue Queue(int slot, uint generation) => Stream(slot, generation).Queue;

    internal StepFrameHandle FrameHandle => _frame;

    // Room for one record of `size` bytes in the pass's records.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal byte* Reserve(int slot, uint generation, uint size)
    {
        ref PassStream stream = ref Stream(slot, generation);
        if (stream.Capacity - stream.Used < size)
        {
            Grow(ref stream, size);
        }
        byte* at = stream.Base + stream.Used;
        stream.Used += size;
        return at;
    }

    private void Grow(ref PassStream stream, uint size)
    {
        StepMemory memory = Api.PassCommands(_frame, stream.Native, stream.Used, size);
        if (memory.Address == 0)
        {
            throw new InvalidOperationException("The pass's commands couldn't grow");
        }
        stream.Base = (byte*)memory.Address;
        stream.Capacity = memory.Size;
    }

    private ref PassStream Stream(int slot, uint generation)
    {
        if (!_active || generation != _generation)
        {
            throw new InvalidOperationException("A pass is used only during the AddPasses call that added it");
        }
        return ref _passes[slot];
    }

    private RenderStepContext Active() =>
        _active ? this : throw new InvalidOperationException("The context is used only during IRenderStep.AddPasses");
}

/// <summary>
/// Room in the frame data (ADR 0058): <see cref="Data"/> to fill, and where
/// it is in the frame data, for shaders (with
/// <see cref="RenderStepContext.FrameDataDescriptor"/>) and
/// <see cref="RenderCommands.SetConstantBuffer{T}(FrameData{T})"/>.
/// </summary>
public readonly ref struct FrameData<T> where T : unmanaged
{
    internal FrameData(Span<T> data, uint offset)
    {
        Data = data;
        Offset = offset;
    }

    public Span<T> Data { get; }

    /// <summary>The byte offset in the frame data; a multiple of 256.</summary>
    public uint Offset { get; }

    public unsafe uint Size => (uint)(Data.Length * sizeof(T));
}

/// <summary>
/// One of a C# step's passes this frame (ADR 0058): its uses and attachments,
/// declared before the step's next pass is added, and its
/// <see cref="Commands"/>.
/// </summary>
public readonly struct RenderPass
{
    private readonly RenderStepContext _context;
    private readonly int _slot;
    private readonly uint _generation;

    internal RenderPass(RenderStepContext context, int slot, uint generation)
    {
        _context = context;
        _slot = slot;
        _generation = generation;
    }

    public RenderCommands Commands => new(_context, _slot, _generation);

    /// <summary>
    /// Declares a use. For ShaderRead and ShaderWrite it returns the slot the
    /// shader reads or writes the texture through (a Texture2D, Texture2DMS,
    /// or Texture2DArray; an RWTexture2D or RWTexture2DArray).
    /// </summary>
    public uint Use(GraphTexture texture, ResourceUsage usage) =>
        Api.UseTexture(_context.FrameHandle, Native, texture, usage);

    /// <summary>
    /// Declares a use. For ShaderRead and ShaderWrite of a buffer a step made
    /// this frame it returns the slot of its ByteAddressBuffer (or
    /// RWByteAddressBuffer); otherwise 0.
    /// </summary>
    public uint Use(GraphBuffer buffer, ResourceUsage usage) =>
        Api.UseBuffer(_context.FrameHandle, Native, buffer, usage);

    public void RenderTarget(GraphTexture texture, LoadAction load = LoadAction.Load, StoreAction store = StoreAction.Store,
        Vector4 clearColor = default) =>
        Api.PassRenderTarget(_context.FrameHandle, Native, texture, load, store, clearColor);

    public void DepthTarget(GraphTexture texture, LoadAction load = LoadAction.Load, StoreAction store = StoreAction.Store,
        float clearDepth = 0, bool readOnly = false) =>
        Api.PassDepthTarget(_context.FrameHandle, Native, texture, load, store, clearDepth, readOnly);

    private uint Native => _context.NativePass(_slot, _generation);
}
