using System.Diagnostics;
using System.Text;
using Crucible.Loop;

namespace Crucible.Samples.Particles;

/// <summary>
/// Times the fixed steps: each system's run and each sync point, sampled
/// once per frame that ran a step (the frame's last step), into arrays made
/// at startup so frames allocate nothing. Quits after the configured number
/// of steps and prints medians, 95th percentiles, and maxima.
/// </summary>
[System(Phases.Update)]
public sealed class ParticleReport : GameSystem
{
    private const int MaxSamples = 100_000;

    private readonly List<(string Label, Func<long> Read)> _metrics = [];
    private long[][] _samples = [];
    private int _sampleCount;
    private long _steps;

    public override void Start(FrameContext context)
    {
        Schedule schedule = context.Schedule ?? throw new InvalidOperationException("No schedule to report on.");
        Dictionary<string, GameSystem> systems = schedule.Systems.ToDictionary(system => system.Name);
        Dictionary<string, FramePhase> phases = schedule.Phases.SelectMany(phase => phase.SubPhases.Prepend(phase))
            .ToDictionary(phase => phase.Name);

        _metrics.Clear();
        void System(string name, string label)
        {
            GameSystem system = systems[name];
            _metrics.Add((label, () => system.LastRunTicks));
        }
        void Sync(string phase, string label)
        {
            FramePhase found = phases[phase];
            _metrics.Add((label, () => found.LastSyncTicks));
        }
        System("crucible.demo.Respawn", "Respawn (C++, records creations)");
        Sync(Phases.PrePhysics, "PrePhysics sync (applies them)");
        System("crucible.demo.Steer", "Steer (C++, parallel)");
        System(typeof(IntegrateSystem).FullName!, "Integrate (C#, parallel)");
        Sync(Phases.Physics, "Physics sync");
        System(typeof(ExpireSystem).FullName!, "Expire (C#, parallel, records destroys)");
        Sync(Phases.PostPhysics, "PostPhysics sync (applies them)");

        int capacity = ParticleSetup.Settings.Steps > 0 ? Math.Min(ParticleSetup.Settings.Steps, MaxSamples) : MaxSamples;
        _samples = [.. Enumerable.Range(0, _metrics.Count + 1).Select(_ => new long[capacity])];
        _sampleCount = 0;
        _steps = 0;
    }

    public override void Run(FrameContext context)
    {
        if (context.FixedSteps == 0)
        {
            return;
        }
        _steps += context.FixedSteps;
        if (_sampleCount < _samples[0].Length)
        {
            long total = 0;
            for (int i = 0; i < _metrics.Count; ++i)
            {
                long ticks = _metrics[i].Read();
                _samples[i][_sampleCount] = ticks;
                total += ticks;
            }
            _samples[_metrics.Count][_sampleCount] = total;
            ++_sampleCount;
        }
        int limit = ParticleSetup.Settings.Steps;
        if (limit > 0 && _steps >= limit)
        {
            context.QuitRequested = true;
        }
    }

    public override void Stop(FrameContext context)
    {
        if (_sampleCount == 0)
        {
            Console.WriteLine("Crucible.Particles: no fixed steps ran.");
            return;
        }
        StringBuilder report = new();
        report.AppendLine(
            $"Crucible.Particles: {ParticleSetup.Settings.Particles} particles, {_steps} fixed steps over " +
            $"{context.FrameNumber} frames, {Jobs.JobSystem.WorkerCount} workers");
        report.AppendLine($"  {"per step, in ms",-42} {"median",8} {"p95",8} {"max",8}");
        for (int i = 0; i <= _metrics.Count; ++i)
        {
            string label = i < _metrics.Count ? _metrics[i].Label : "Total";
            long[] sorted = _samples[i][.._sampleCount];
            Array.Sort(sorted);
            report.AppendLine($"  {label,-42} {Ms(sorted[sorted.Length / 2]),8:F3} " +
                              $"{Ms(sorted[(int)(sorted.Length * 0.95)]),8:F3} {Ms(sorted[^1]),8:F3}");
        }
        report.Append($"  {Demo.Api.DemoRespawned()} particles respawned; {context.World.EntityCount} alive at the end");
        Console.WriteLine(report.ToString());
    }

    private static double Ms(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;
}
