namespace Crucible.Samples.UiShowcase;

/// <summary>
/// The sample's options, from the launcher's <c>--game-arg key=value</c>:
/// <list type="bullet">
/// <item><c>project</c>: the project to import (default: the one beside the
/// sample, from the working directory).</item>
/// <item><c>input</c>: which scripted journeys to run: <c>mouse</c>,
/// <c>keyboard</c>, <c>gamepad</c>, or <c>all</c> (the default); or
/// <c>none</c>, to try the menu by hand in a window.</item>
/// <item><c>capture</c>: a folder to save the menu at each DPI scale in
/// (<c>scale-1.png</c>, <c>scale-1.25.png</c>, …).</item>
/// <item><c>compare</c>: a folder of another backend's captures to compare
/// these with (ADR 0056's tolerance).</item>
/// </list>
/// </summary>
public sealed record ShowcaseSettings(string Project, string[] Inputs, string? Capture, string? Compare)
{
    public static readonly string[] AllInputs = ["mouse", "keyboard", "gamepad"];

    public static ShowcaseSettings Default { get; } =
        new(Path.Combine("samples", "UiShowcase", "Project", "UiShowcase.crproject"), AllInputs, null, null);

    public static ShowcaseSettings Parse(IReadOnlyDictionary<string, string> arguments)
    {
        ShowcaseSettings settings = Default;
        foreach ((string key, string value) in arguments)
        {
            settings = key switch
            {
                "project" => settings with { Project = value },
                "input" => settings with
                {
                    Inputs = value == "all" ? AllInputs
                        : value == "none" ? []
                        : AllInputs.Contains(value) ? [value]
                        : throw new ArgumentException($"input is mouse, keyboard, gamepad, all, or none, not {value}."),
                },
                "capture" => settings with { Capture = value },
                "compare" => settings with { Compare = value },
                _ => throw new ArgumentException($"Unknown option {key}: expected project, input, capture, or compare."),
            };
        }
        if (settings.Compare is not null && settings.Capture is null)
        {
            throw new ArgumentException("compare needs capture: the images compared are the ones captured.");
        }
        return settings;
    }
}
