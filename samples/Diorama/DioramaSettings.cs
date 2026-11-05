namespace Crucible.Samples.Diorama;

/// <summary>
/// The sample's options, from the launcher's <c>--game-arg key=value</c>:
/// <list type="bullet">
/// <item><c>project</c>: the project to import (default: the one beside the
/// sample, from the working directory).</item>
/// <item><c>frames</c>: frames rendered before capturing (8).</item>
/// <item><c>capture</c>: where to save the last frame as a PNG.</item>
/// <item><c>compare</c>: a PNG to compare the capture with, writing
/// <c>&lt;capture&gt;.diff.png</c>.</item>
/// <item><c>measure</c>: frames to time, after <c>frames</c> warm-up ones, in
/// a town of 36 dioramas; 0 skips.</item>
/// <item><c>save-scene</c>: where to save the village as a scene file (the
/// editor's sample scene, ADR 0067).</item>
/// </list>
/// </summary>
public sealed record DioramaSettings(string Project, int Frames, string? Capture, string? Compare, int Measure,
    string? SaveScene = null)
{
    public static DioramaSettings Default { get; } =
        new(Path.Combine("samples", "Diorama", "Project", "Diorama.crproject"), 8, null, null, 0);

    public static DioramaSettings Parse(IReadOnlyDictionary<string, string> arguments)
    {
        DioramaSettings settings = Default;
        foreach ((string key, string value) in arguments)
        {
            settings = key switch
            {
                "project" => settings with { Project = value },
                "frames" => settings with { Frames = Number(key, value, minimum: 1) },
                "capture" => settings with { Capture = value },
                "compare" => settings with { Compare = value },
                "measure" => settings with { Measure = Number(key, value, minimum: 0) },
                "save-scene" => settings with { SaveScene = value },
                _ => throw new ArgumentException(
                    $"Unknown option {key}: expected project, frames, capture, compare, measure, or save-scene."),
            };
        }
        if (settings.Compare is not null && settings.Capture is null)
        {
            throw new ArgumentException("compare needs capture: the image compared is the one captured.");
        }
        return settings;
    }

    private static int Number(string key, string value, int minimum) =>
        int.TryParse(value, out int number) && number >= minimum
            ? number
            : throw new ArgumentException($"{key} takes a whole number from {minimum}, not {value}.");
}
