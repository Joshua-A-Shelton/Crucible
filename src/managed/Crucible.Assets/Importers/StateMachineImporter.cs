using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Crucible.Binary;
using Crucible.Serialization;

namespace Crucible.Assets.Importers;

/// <summary>A state machine has no import settings: everything is in the file.</summary>
[Serialized]
public partial class StateMachineImportSettings;

/// <summary>
/// Imports state machines (ADR 0081): an authored <c>.crstates</c> (JSON with
/// its GUID inside, like materials), checked so a mistake fails the import
/// saying where, into a <c>.crstatemachine</c>: a STAT file
/// (StateMachineFile.h) the pose runtime plays on a character's base.
/// </summary>
/// <remarks>
/// <code>
/// {
///   "$guid": "…",
///   "Parameters": [ { "Name": "Speed", "Type": "Float", "Default": 0 } ],   Float, Bool, or Trigger
///   "States": [
///     { "Name": "Idle", "Clip": "…" },                                       a clip's asset ID
///     { "Name": "Move", "Blend": { "Parameters": ["Speed"],
///         "Clips": [ { "Clip": "…", "At": [0] }, { "Clip": "…", "At": [1.5] } ] },
///       "Speed": 1, "Loop": true }
///   ],
///   "Start": "Idle",                                                         the first state if left out
///   "Transitions": [
///     { "From": "Idle", "To": "Move", "Blend": 0.25,
///       "Conditions": [ { "Parameter": "Speed", "Op": ">", "Value": 0.1 } ] },
///     { "Any": true, "To": "Hit", "Conditions": [ { "Parameter": "Hit" } ] }, a Trigger needs no Op
///     { "From": "Attack", "To": "Idle", "ExitTime": 0.9 }
///   ]
/// }
/// </code>
/// A Float compares with <c>&gt;</c> or <c>&lt;</c>, a Bool with <c>==</c> or
/// <c>!=</c> against true or false. Transitions are checked in order; the
/// first whose conditions hold (and whose exit time was passed) is taken.
/// </remarks>
public sealed class StateMachineImporter : Importer<StateMachineImportSettings>
{
    public static readonly uint Kind = Tag.Of("STAT");
    public const uint KindVersion = 1; // as stateMachineFileVersion
    public const string FileExtension = ".crstates";

    public override string Name => "StateMachine";

    public override int Version => 1;

    public override IReadOnlyList<string> Extensions => [FileExtension];

    public override string OutputExtension => ".crstatemachine";

    public override void Import(ImportContext context, StateMachineImportSettings settings)
    {
        using JsonDocument document = JsonDocument.Parse(context.ReadSource());
        ImportedFileWriter output = context.CreateOutput(Kind, KindVersion);
        foreach ((string tag, byte[] bytes) in Build(document.RootElement))
        {
            output.Add(Tag.Of(tag), 1, bytes);
        }
    }

    /// <summary>The file's sections, by tag, from its JSON; throws, saying where, if it's wrong.</summary>
    public static IEnumerable<(string Tag, byte[] Bytes)> Build(JsonElement root)
    {
        Names names = new();
        List<StateParameter> parameters = [];
        Dictionary<string, int> parameterIndex = new(StringComparer.Ordinal);
        foreach (JsonElement parameter in Array(root, "Parameters", required: false))
        {
            string name = Text(parameter, "Name", "a parameter");
            ParameterType type = Text(parameter, "Type", $"parameter {name}") switch
            {
                "Float" => ParameterType.Float,
                "Bool" => ParameterType.Bool,
                "Trigger" => ParameterType.Trigger,
                string other => throw new InvalidDataException($"parameter {name}: Type is Float, Bool, or Trigger, not {other}"),
            };
            if (!parameterIndex.TryAdd(name, parameters.Count))
            {
                throw new InvalidDataException($"there are two parameters named {name}");
            }
            float initial = !parameter.TryGetProperty("Default", out JsonElement value) ? 0
                : type == ParameterType.Float ? Number(value, $"parameter {name}'s Default")
                : Flag(value, $"parameter {name}'s Default") ? 1 : 0;
            (uint offset, uint length) = names.Add(name);
            parameters.Add(new StateParameter { NameOffset = offset, NameLength = length, Type = type, DefaultValue = initial });
        }

        List<StateEntry> states = [];
        List<StateMotion> motions = [];
        Dictionary<string, int> stateIndex = new(StringComparer.Ordinal);
        foreach (JsonElement state in Array(root, "States", required: true))
        {
            string name = Text(state, "Name", "a state");
            if (!stateIndex.TryAdd(name, states.Count))
            {
                throw new InvalidDataException($"there are two states named {name}");
            }
            (uint offset, uint length) = names.Add(name);
            StateEntry entry = new()
            {
                NameOffset = offset,
                NameLength = length,
                FirstMotion = (uint)motions.Count,
                Speed = state.TryGetProperty("Speed", out JsonElement speed) ? Number(speed, $"state {name}'s Speed") : 1,
                Loop = !state.TryGetProperty("Loop", out JsonElement loop) || Flag(loop, $"state {name}'s Loop") ? 1u : 0u,
            };
            entry.BlendParameters[0] = -1;
            entry.BlendParameters[1] = -1;
            bool hasClip = state.TryGetProperty("Clip", out JsonElement clip);
            bool hasBlend = state.TryGetProperty("Blend", out JsonElement blend);
            if (hasClip == hasBlend)
            {
                throw new InvalidDataException($"state {name} needs a Clip or a Blend (one of them)");
            }
            if (hasClip)
            {
                motions.Add(new StateMotion { Clip = Clip(clip, $"state {name}") });
            }
            else
            {
                Blend(blend, name, ref entry, parameterIndex, parameters, motions);
            }
            entry.MotionCount = (uint)motions.Count - entry.FirstMotion;
            states.Add(entry);
        }
        if (states.Count == 0)
        {
            throw new InvalidDataException("States is empty: a state machine needs a state");
        }
        uint start = root.TryGetProperty("Start", out JsonElement startName)
            ? (uint)State(stateIndex, startName.GetString() ?? "", "Start")
            : 0;

        List<StateTransition> transitions = [];
        List<TransitionCondition> conditions = [];
        int number = 0;
        foreach (JsonElement transition in Array(root, "Transitions", required: false))
        {
            string where = $"transition {++number}";
            bool any = transition.TryGetProperty("Any", out JsonElement anyValue) && Flag(anyValue, $"{where}'s Any");
            bool hasFrom = transition.TryGetProperty("From", out JsonElement from);
            if (any == hasFrom)
            {
                throw new InvalidDataException($"{where} needs a From state, or \"Any\": true (one of them)");
            }
            StateTransition entry = new()
            {
                From = any ? -1 : State(stateIndex, from.GetString() ?? "", $"{where}'s From"),
                To = (uint)State(stateIndex, Text(transition, "To", where), $"{where}'s To"),
                BlendSeconds = transition.TryGetProperty("Blend", out JsonElement seconds)
                    ? Math.Max(Number(seconds, $"{where}'s Blend"), 0)
                    : 0,
                ExitTime = transition.TryGetProperty("ExitTime", out JsonElement exit)
                    ? Number(exit, $"{where}'s ExitTime")
                    : -1,
                FirstCondition = (uint)conditions.Count,
            };
            if (transition.TryGetProperty("ExitTime", out _) && entry.ExitTime is < 0 or > 1)
            {
                throw new InvalidDataException($"{where}'s ExitTime is a share of the state, 0 to 1");
            }
            foreach (JsonElement condition in Array(transition, "Conditions", required: false))
            {
                conditions.Add(Condition(condition, where, parameterIndex, parameters));
            }
            entry.ConditionCount = (uint)conditions.Count - entry.FirstCondition;
            transitions.Add(entry);
        }

        StateMachineInfo info = new()
        {
            Start = start,
            ParameterCount = (uint)parameters.Count,
            StateCount = (uint)states.Count,
            TransitionCount = (uint)transitions.Count,
        };
        yield return ("INFO", MemoryMarshal.AsBytes(new Span<StateMachineInfo>(ref info)).ToArray());
        yield return ("PARM", Bytes(parameters));
        yield return ("STAT", Bytes(states));
        yield return ("MOTN", Bytes(motions));
        yield return ("TRAN", Bytes(transitions));
        yield return ("COND", Bytes(conditions));
        yield return ("NAME", names.Bytes());
    }

    // A blend space: its parameters (Floats; one, until 2D, docs/open-items.md)
    // and its clips, sorted by where they sit.
    private static void Blend(JsonElement blend, string state, ref StateEntry entry, Dictionary<string, int> parameterIndex,
        List<StateParameter> parameters, List<StateMotion> motions)
    {
        string where = $"state {state}'s Blend";
        JsonElement[] by = Array(blend, "Parameters", required: true).ToArray();
        if (by.Length == 2)
        {
            throw new InvalidDataException($"{where}: 2D blend spaces aren't supported yet; give one parameter");
        }
        if (by.Length != 1)
        {
            throw new InvalidDataException($"{where}: Parameters names the one Float it blends by");
        }
        string parameter = by[0].GetString() ?? "";
        int index = parameterIndex.TryGetValue(parameter, out int found)
            ? found
            : throw new InvalidDataException($"{where}: there's no parameter {parameter}");
        if (parameters[index].Type != ParameterType.Float)
        {
            throw new InvalidDataException($"{where}: {parameter} isn't a Float");
        }
        entry.Dimensions = 1;
        entry.BlendParameters[0] = index;
        List<StateMotion> clips = [];
        foreach (JsonElement clip in Array(blend, "Clips", required: true))
        {
            JsonElement[] at = clip.TryGetProperty("At", out JsonElement position) &&
                               position.ValueKind == JsonValueKind.Array
                ? position.EnumerateArray().ToArray()
                : throw new InvalidDataException($"{where}: each clip needs At, where it sits: [{parameter}]");
            if (at.Length != 1)
            {
                throw new InvalidDataException($"{where}: At has one number per parameter");
            }
            StateMotion motion = new() { Clip = Clip(clip.GetProperty("Clip"), where) };
            motion.Position[0] = Number(at[0], $"{where}'s At");
            clips.Add(motion);
        }
        if (clips.Count == 0)
        {
            throw new InvalidDataException($"{where}: Clips is empty");
        }
        motions.AddRange(clips.OrderBy(motion => motion.Position[0]));
    }

    private static TransitionCondition Condition(JsonElement condition, string where, Dictionary<string, int> parameterIndex,
        List<StateParameter> parameters)
    {
        string name = Text(condition, "Parameter", $"{where}'s condition");
        int index = parameterIndex.TryGetValue(name, out int found)
            ? found
            : throw new InvalidDataException($"{where}: there's no parameter {name}");
        bool hasOp = condition.TryGetProperty("Op", out JsonElement opValue);
        string op = hasOp ? opValue.GetString() ?? "" : "";
        bool hasValue = condition.TryGetProperty("Value", out JsonElement value);
        TransitionCondition result = new() { Parameter = (uint)index };
        switch (parameters[index].Type)
        {
        case ParameterType.Trigger:
            if (hasOp || hasValue)
            {
                throw new InvalidDataException($"{where}: {name} is a Trigger, which needs no Op or Value");
            }
            result.Op = ConditionOp.Set;
            break;
        case ParameterType.Float:
            result.Op = op switch
            {
                ">" => ConditionOp.Greater,
                "<" => ConditionOp.Less,
                _ => throw new InvalidDataException($"{where}: {name} is a Float, compared with > or <"),
            };
            result.Value = hasValue ? Number(value, $"{where}'s Value") : throw new InvalidDataException($"{where}: {name} needs a Value");
            break;
        default:
            result.Op = op switch
            {
                "==" => ConditionOp.Equal,
                "!=" => ConditionOp.NotEqual,
                _ => throw new InvalidDataException($"{where}: {name} is a Bool, compared with == or !="),
            };
            result.Value = hasValue ? Flag(value, $"{where}'s Value") ? 1 : 0 : throw new InvalidDataException($"{where}: {name} needs a Value");
            break;
        }
        return result;
    }

    private static AssetId Clip(JsonElement value, string where) =>
        value.ValueKind == JsonValueKind.String && AssetId.TryParse(value.GetString(), out AssetId id) && !id.IsNone
            ? id
            : throw new InvalidDataException($"{where}: a clip is given by its asset ID");

    private static int State(Dictionary<string, int> states, string name, string where) =>
        states.TryGetValue(name, out int index)
            ? index
            : throw new InvalidDataException($"{where}: there's no state {name}");

    private static IEnumerable<JsonElement> Array(JsonElement owner, string name, bool required)
    {
        if (!owner.TryGetProperty(name, out JsonElement value))
        {
            return required ? throw new InvalidDataException($"{name} is missing") : [];
        }
        return value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().ToArray()
            : throw new InvalidDataException($"{name} is a list");
    }

    private static string Text(JsonElement owner, string name, string where) =>
        owner.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()!
            : throw new InvalidDataException($"{where} needs a {name}");

    private static float Number(JsonElement value, string where) =>
        value.ValueKind == JsonValueKind.Number ? value.GetSingle() : throw new InvalidDataException($"{where} is a number");

    private static bool Flag(JsonElement value, string where) => value.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => throw new InvalidDataException($"{where} is true or false"),
    };

    private static byte[] Bytes<T>(List<T> items) where T : unmanaged =>
        MemoryMarshal.AsBytes(CollectionsMarshal.AsSpan(items)).ToArray();

    // The NAME section: every name's UTF-8, end to end.
    private sealed class Names
    {
        private readonly List<byte> _bytes = [];

        public (uint Offset, uint Length) Add(string name)
        {
            byte[] utf8 = Encoding.UTF8.GetBytes(name);
            uint offset = (uint)_bytes.Count;
            _bytes.AddRange(utf8);
            return (offset, (uint)utf8.Length);
        }

        public byte[] Bytes() => [.. _bytes];
    }
}
