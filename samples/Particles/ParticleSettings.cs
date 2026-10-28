namespace Crucible.Samples.Particles;

/// <summary>
/// The sample's options, from the launcher's <c>--game-arg key=value</c>:
/// <c>particles</c> (100000), <c>steps</c> (fixed steps before quitting,
/// 600; 0 runs until closed), and <c>seed</c> (1).
/// </summary>
public sealed record ParticleSettings(int Particles, int Steps, uint Seed)
{
    public static ParticleSettings Default { get; } = new(100_000, 600, 1);

    public static ParticleSettings Parse(IReadOnlyDictionary<string, string> arguments)
    {
        ParticleSettings settings = Default;
        foreach ((string key, string value) in arguments)
        {
            settings = key switch
            {
                "particles" => settings with { Particles = Number(key, value, minimum: 1) },
                "steps" => settings with { Steps = Number(key, value, minimum: 0) },
                "seed" => settings with { Seed = (uint)Number(key, value, minimum: 1) },
                _ => throw new ArgumentException($"Unknown option {key}: expected particles, steps, or seed."),
            };
        }
        return settings;
    }

    private static int Number(string key, string value, int minimum) =>
        int.TryParse(value, out int number) && number >= minimum
            ? number
            : throw new ArgumentException($"{key} must be a whole number of at least {minimum}, not '{value}'.");
}
