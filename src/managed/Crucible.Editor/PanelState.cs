using System.Text.Json;
using System.Text.Json.Nodes;

namespace Crucible.Editor;

/// <summary>
/// What a panel keeps across a code reload and in the saved layout (ADR
/// 0117): plain values by key. Numbers, strings, and bools only, so nothing
/// in it belongs to the code that wrote it, which a reload unloads.
/// </summary>
public sealed class PanelState
{
    private readonly JsonObject _values;

    internal PanelState(JsonObject? values = null) => _values = values?.DeepClone() as JsonObject ?? [];

    public void Set(string key, string value) => _values[Key(key)] = value ?? "";

    public void Set(string key, double value) => _values[Key(key)] = double.IsFinite(value) ? value : 0;

    public void Set(string key, bool value) => _values[Key(key)] = value;

    public string GetString(string key, string fallback = "") =>
        _values[key] is JsonValue value && value.GetValueKind() == JsonValueKind.String ? value.GetValue<string>() : fallback;

    public double GetNumber(string key, double fallback = 0) =>
        _values[key] is JsonValue value && value.GetValueKind() == JsonValueKind.Number ? value.GetValue<double>() : fallback;

    public bool GetBool(string key, bool fallback = false) =>
        _values[key] is JsonValue value && value.GetValueKind() is JsonValueKind.True or JsonValueKind.False
            ? value.GetValueKind() == JsonValueKind.True
            : fallback;

    internal bool IsEmpty => _values.Count == 0;

    internal JsonObject ToJson() => (JsonObject)_values.DeepClone();

    private static string Key(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        return key;
    }
}
