using System.Globalization;
using System.Text;
using Crucible.Platform;
using Crucible.Render;

namespace Crucible.Loop;

/// <summary>
/// The latency harness (ADR 0003): records when each input arrived and when
/// the frame that read it reached each stage, on one clock
/// (Platform.Api.TimestampNow). Records go into arrays allocated up front, so
/// measuring adds no garbage to the frame; results are written at the end.
/// The name is a placeholder (docs/naming.md).
/// </summary>
/// <remarks>
/// Stages per input: read by the Input phase, ClearAndPresent returned
/// (submitted and presented), GPU finished (stamped by a native thread), and
/// present done: the next frame's pacing wait returning, which with max frame
/// latency 1 means this frame's present completed. Only known when pacing is
/// on and the device paces on presents (Render.Api.PacingWaitsForPresent).
/// What software can't see (the OS input stack before SDL, the compositor,
/// the display) needs present timing or a photodiode.
/// </remarks>
public sealed class LatencyRecorder
{
    /// <summary>Synthetic input arrives every 23 ms on average: not a multiple
    /// of common refresh rates, so inputs land at every point of a frame.</summary>
    public const uint MarkerIntervalUs = 23_000;

    private const int GpuLookback = 8;

    private enum InputKind : byte
    {
        Marker,
        Key,
        MouseButton,
    }

    private struct FrameRecord
    {
        public ulong StartNs;      // the pacing wait returned (or the frame began, unpaced)
        public ulong InputReadNs;
        public ulong SubmittedNs;  // ClearAndPresent returned
        public ulong GpuDoneNs;
        public ulong PresentDoneNs;
        public ulong SubmitIndex;  // Render.Api.FramesSubmitted after this frame; 0 if skipped
    }

    private struct InputRecord
    {
        public ulong ArrivalNs;
        public ulong Frame;
        public int Code;
        public InputKind Kind;
    }

    private readonly FrameRecord[] _frames;
    private readonly InputRecord[] _inputs;
    private int _inputCount;
    private ulong _frameCount;
    private bool _overflowed;

    public LatencyRecorder(int frameCapacity = 1 << 16, int inputCapacity = 1 << 16)
    {
        _frames = new FrameRecord[frameCapacity];
        _inputs = new InputRecord[inputCapacity];
    }

    /// <summary>Whether present-done times are recorded (pacing on, and pacing waits for presents).</summary>
    public bool RecordsPresents { get; init; }

    internal void FrameStarted(FrameContext context, ulong nowNs)
    {
        ulong frame = context.FrameNumber;
        if (!InRange(frame))
        {
            return;
        }
        _frames[frame] = new FrameRecord { StartNs = nowNs };
        _frameCount = frame + 1;
        if (frame > 0 && RecordsPresents && _frames[frame - 1].SubmitIndex != 0)
        {
            _frames[frame - 1].PresentDoneNs = nowNs;
        }
        CollectGpuTimes(context.Surface, frame);
    }

    internal void InputRead(FrameContext context, ulong nowNs)
    {
        if (InRange(context.FrameNumber))
        {
            _frames[context.FrameNumber].InputReadNs = nowNs;
        }
        foreach (ref readonly InputEvent inputEvent in context.Events)
        {
            InputKind? kind = inputEvent.Type switch
            {
                InputEventType.LatencyMarker => InputKind.Marker,
                InputEventType.KeyDown when inputEvent.Value == 0 => InputKind.Key, // not key repeats
                InputEventType.MouseButtonDown => InputKind.MouseButton,
                _ => null,
            };
            if (kind is null)
            {
                continue;
            }
            if (_inputCount == _inputs.Length)
            {
                _overflowed = true;
                return;
            }
            _inputs[_inputCount++] = new InputRecord
            {
                ArrivalNs = inputEvent.TimestampNs,
                Frame = context.FrameNumber,
                Code = inputEvent.Code,
                Kind = kind.Value,
            };
        }
    }

    internal void Submitted(FrameContext context, ulong nowNs, ulong submitIndex, bool submitted)
    {
        if (InRange(context.FrameNumber))
        {
            _frames[context.FrameNumber].SubmittedNs = nowNs;
            _frames[context.FrameNumber].SubmitIndex = submitted ? submitIndex : 0;
        }
    }

    /// <summary>
    /// Collects the last GPU times, writes the input CSV to <paramref name="path"/>
    /// and the per-frame CSV next to it (<c>name.frames.csv</c>), and returns the
    /// summary to print.
    /// </summary>
    public string Finish(SurfaceHandle surface, string path)
    {
        if (_frameCount > 0)
        {
            CollectGpuTimes(surface, _frameCount);
        }
        WriteInputs(path);
        WriteFrames(Path.ChangeExtension(path, ".frames.csv"));
        return Summary(path);
    }

    private bool InRange(ulong frame)
    {
        if (frame < (ulong)_frames.Length)
        {
            return true;
        }
        _overflowed = true;
        return false;
    }

    // The native timer keeps the last 256 frames; a few frames back is enough,
    // since the GPU finishes within a frame or two.
    private void CollectGpuTimes(SurfaceHandle surface, ulong currentFrame)
    {
        if (surface.Value == 0)
        {
            return;
        }
        ulong first = currentFrame > GpuLookback ? currentFrame - GpuLookback : 0;
        for (ulong frame = first; frame < currentFrame; ++frame)
        {
            ref FrameRecord record = ref _frames[frame];
            if (record.SubmitIndex != 0 && record.GpuDoneNs == 0)
            {
                record.GpuDoneNs = Render.Api.GpuFrameDoneNs(surface, record.SubmitIndex);
            }
        }
    }

    // A stage's latency for an input, in ms, or null if the stage wasn't seen.
    private double? Latency(in InputRecord input, Func<FrameRecord, ulong> stage)
    {
        if (input.Frame >= _frameCount)
        {
            return null;
        }
        ulong time = stage(_frames[input.Frame]);
        return time == 0 || time < input.ArrivalNs ? null : (time - input.ArrivalNs) / 1e6;
    }

    private static readonly (string Name, Func<FrameRecord, ulong> Stage)[] _stages =
    [
        ("read", frame => frame.InputReadNs),
        ("submitted", frame => frame.SubmittedNs),
        ("gpu_done", frame => frame.GpuDoneNs),
        ("present_done", frame => frame.PresentDoneNs),
    ];

    private static string Ms(double? value) =>
        value?.ToString("F3", CultureInfo.InvariantCulture) ?? "";

    private void WriteInputs(string path)
    {
        StringBuilder csv = new();
        csv.Append("kind,code,arrival_ns,frame");
        foreach ((string name, _) in _stages)
        {
            csv.Append(CultureInfo.InvariantCulture, $",{name}_ms");
        }
        csv.AppendLine();
        for (int i = 0; i < _inputCount; ++i)
        {
            ref readonly InputRecord input = ref _inputs[i];
            csv.Append(CultureInfo.InvariantCulture, $"{input.Kind},{input.Code},{input.ArrivalNs},{input.Frame}");
            foreach ((_, Func<FrameRecord, ulong> stage) in _stages)
            {
                csv.Append(',').Append(Ms(Latency(input, stage)));
            }
            csv.AppendLine();
        }
        File.WriteAllText(path, csv.ToString());
    }

    private void WriteFrames(string path)
    {
        StringBuilder csv = new();
        csv.AppendLine("frame,start_ns,read_ms,submitted_ms,gpu_done_ms,present_done_ms,submit_index");
        for (ulong frame = 0; frame < _frameCount; ++frame)
        {
            FrameRecord record = _frames[frame];
            string Since(ulong time) => time == 0 ? "" : Ms((time - record.StartNs) / 1e6);
            csv.AppendLine(CultureInfo.InvariantCulture,
                $"{frame},{record.StartNs},{Since(record.InputReadNs)},{Since(record.SubmittedNs)}," +
                $"{Since(record.GpuDoneNs)},{Since(record.PresentDoneNs)},{record.SubmitIndex}");
        }
        File.WriteAllText(path, csv.ToString());
    }

    private string Summary(string path)
    {
        StringBuilder text = new();
        int markers = 0;
        for (int i = 0; i < _inputCount; ++i)
        {
            markers += _inputs[i].Kind == InputKind.Marker ? 1 : 0;
        }
        text.AppendLine(CultureInfo.InvariantCulture,
            $"Crucible.Latency: {_frameCount} frames, {_inputCount} inputs ({markers} synthetic), written to {path}");
        if (_overflowed)
        {
            text.AppendLine("Crucible.Latency: the run outgrew the recording buffers; later frames are missing");
        }
        text.AppendLine("Crucible.Latency: input to stage, ms     median      p95      max    count");
        foreach ((string name, Func<FrameRecord, ulong> stage) in _stages)
        {
            List<double> values = [];
            for (int i = 0; i < _inputCount; ++i)
            {
                if (Latency(_inputs[i], stage) is { } value)
                {
                    values.Add(value);
                }
            }
            if (values.Count == 0)
            {
                text.AppendLine(CultureInfo.InvariantCulture, $"Crucible.Latency:   {name,-20} (not recorded)");
                continue;
            }
            values.Sort();
            text.AppendLine(CultureInfo.InvariantCulture,
                $"Crucible.Latency:   {name,-20} {Percentile(values, 0.5),8:F2} {Percentile(values, 0.95),8:F2} " +
                $"{values[^1],8:F2} {values.Count,8}");
        }

        List<double> intervals = [];
        for (ulong frame = 1; frame < _frameCount; ++frame)
        {
            intervals.Add((_frames[frame].StartNs - _frames[frame - 1].StartNs) / 1e6);
        }
        if (intervals.Count > 0)
        {
            intervals.Sort();
            text.AppendLine(CultureInfo.InvariantCulture,
                $"Crucible.Latency:   {"frame interval",-20} {Percentile(intervals, 0.5),8:F2} " +
                $"{Percentile(intervals, 0.95),8:F2} {intervals[^1],8:F2} {intervals.Count,8}");
        }
        return text.ToString();
    }

    // Nearest-rank percentile of sorted values.
    internal static double Percentile(List<double> sorted, double fraction)
    {
        int rank = (int)Math.Ceiling(fraction * sorted.Count);
        return sorted[Math.Clamp(rank - 1, 0, sorted.Count - 1)];
    }
}
