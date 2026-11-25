using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Reflection;
using System.Text.Json.Nodes;
using Crucible.Assets;
using Crucible.Ecs;
using Crucible.Serialization;
using Crucible.UI;

namespace Crucible.Editor;

/// <summary>
/// A custom inspector (ADR 0126): how the Inspector shows a component, every
/// field of a type, or the fields marked with its name. It builds its own UI
/// once (<see cref="Build"/>), placing the generated editor for any field
/// where it wants (<see cref="DrawerContext{T}.Default"/>), and follows the
/// value each frame (<see cref="Update"/>). It reads and sets the value as
/// its C# type, each set an undo step.
/// </summary>
/// <typeparam name="T">
/// What it draws: a component or <c>[Serialized]</c> type, or for a marked
/// field a plain value (a number, a bool, a string, a vector, an asset ID).
/// </typeparam>
public abstract class Drawer<T>
{
    /// <summary>Its UI, made once when the Inspector shows what it draws.</summary>
    public abstract UIElement Build(DrawerContext<T> context);

    /// <summary>Each frame: the value read again (an undo, a handle's drag), for the UI to follow.</summary>
    public virtual void Update(DrawerContext<T> context)
    {
    }
}

/// <summary>What a <see cref="Drawer{T}"/> draws and changes (ADR 0126).</summary>
public sealed class DrawerContext<T>
{
    private readonly DrawerHost _host;
    private readonly DrawerCodec<T> _codec;
    private string? _read;
    private T _value = default!;

    internal DrawerContext(DrawerHost host, DrawerCodec<T> codec, EditorContext editor)
    {
        _host = host;
        _codec = codec;
        Editor = editor;
    }

    /// <summary>The value as it is now (read again each frame).</summary>
    public T Value
    {
        get
        {
            JsonNode? node = _host.Read();
            string text = node?.ToJsonString() ?? "null";
            if (text != _read)
            {
                _read = text;
                _value = _codec.Read(node);
            }
            return _value;
        }
    }

    /// <summary>Sets the value: one undo step titled <paramref name="title"/> (inside a change, part of it).</summary>
    public void Set(T value, string title)
    {
        _host.Write(_codec.Write(value), title ?? $"Edit {Label}");
        _read = null;
    }

    /// <summary>Starts a change made of several sets (a drag): one undo step until <see cref="EndChange"/>.</summary>
    public void BeginChange(string title) => _host.Owner.BeginGroup(title ?? $"Edit {Label}");

    public void EndChange() => _host.Owner.EndGroup();

    /// <summary>
    /// The generated editor for one of its fields (by its name), to place in
    /// the drawer's UI; it follows and edits that field as it would anywhere.
    /// Null for a name it hasn't.
    /// </summary>
    public UIElement? Default(string field) => _host.Default(field);

    /// <summary>Whether a field (or, with null, the whole value) differs from its prefab's, on a prefab instance.</summary>
    public bool IsOverridden(string? field = null) => _host.IsOverridden(field);

    /// <summary>The entity whose component it shows; none for an asset's settings.</summary>
    public Entity Entity => _host.Owner.Entity;

    /// <summary>The editor, for the rest of the API (the scene, assets, undo).</summary>
    public EditorContext Editor { get; }

    /// <summary>What it's called in the Inspector: the component's or the field's label.</summary>
    public string Label => _host.Label;
}

/// <summary>
/// Drawers registered by extensions (ADR 0126), each taken out when its
/// extension stops (the Inspector then shows the generated fields again).
/// </summary>
public sealed class EditorInspector
{
    private readonly Shell _shell;
    private readonly EditorContext _context;

    internal EditorInspector(Shell shell, EditorContext context)
    {
        _shell = shell;
        _context = context;
    }

    /// <summary>Draws component <typeparamref name="T"/>'s whole section body.</summary>
    public void AddComponentDrawer<T>(Func<Drawer<T>> create)
    {
        ArgumentNullException.ThrowIfNull(create);
        _shell.AddDrawer(new DrawerRegistration(DrawerTarget.Component, typeof(T), null, null, Adapter(create), _context));
    }

    /// <summary>Draws every field of <c>[Serialized]</c> type <typeparamref name="T"/>, wherever it appears.</summary>
    public void AddFieldDrawer<T>(Func<Drawer<T>> create)
    {
        ArgumentNullException.ThrowIfNull(create);
        IReadOnlyList<SerializedField> table = DrawerCodecs.TableOf(typeof(T))
            ?? throw new ArgumentException($"{typeof(T).Name} isn't a [Serialized] type", nameof(create));
        _shell.AddDrawer(new DrawerRegistration(DrawerTarget.FieldType, typeof(T), table, null, Adapter(create), _context));
    }

    /// <summary>Draws the fields marked <c>[Drawer("name")]</c>, which must hold a <typeparamref name="T"/>.</summary>
    public void AddDrawer<T>(string name, Func<Drawer<T>> create)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(create);
        _shell.AddDrawer(new DrawerRegistration(DrawerTarget.Named, typeof(T), null, name, Adapter(create), _context));
    }

    private Func<DrawerHost, IDrawerInstance> Adapter<T>(Func<Drawer<T>> create)
    {
        DrawerCodec<T> codec = DrawerCodecs.For<T>();
        EditorContext context = _context;
        return host => new DrawerInstance<T>(create(), new DrawerContext<T>(host, codec, context));
    }
}

internal enum DrawerTarget
{
    Component,
    FieldType,
    Named,
}

/// <summary>A drawer registered: what it draws, how to make it, and whose it is.</summary>
internal sealed record DrawerRegistration(DrawerTarget Target, Type Type, IReadOnlyList<SerializedField>? Table, string? Name,
    Func<DrawerHost, IDrawerInstance> Create, EditorContext Owner);

/// <summary>A drawer made for one section or field, its type hidden.</summary>
internal interface IDrawerInstance
{
    UIElement Build();

    void Update();
}

internal sealed class DrawerInstance<T>(Drawer<T> drawer, DrawerContext<T> context) : IDrawerInstance
{
    public UIElement Build() => drawer.Build(context);

    public void Update() => drawer.Update(context);
}

/// <summary>
/// Where a drawer sits (ADR 0126): a component's section, or a field of one;
/// its value as JSON read and written there, and the generated editors it
/// placed kept up to date.
/// </summary>
internal sealed class DrawerHost
{
    private readonly Func<JsonNode?> _read;
    private readonly Action<JsonNode?, string> _write;
    private readonly Func<string?, bool> _overridden;
    private readonly IReadOnlyList<SerializedField> _fields;
    private readonly object[] _path;
    private readonly List<FieldEditor> _defaults = [];

    public DrawerHost(IFieldOwner owner, string label, object[] path, IReadOnlyList<SerializedField> fields,
        Func<JsonNode?> read, Action<JsonNode?, string> write, Func<string?, bool> overridden)
    {
        Owner = owner;
        Label = label;
        _path = path;
        _fields = fields;
        _read = read;
        _write = write;
        _overridden = overridden;
    }

    public IFieldOwner Owner { get; }

    public string Label { get; }

    /// <summary>The generated editors it placed (the checks).</summary>
    public IReadOnlyList<FieldEditor> Defaults => _defaults;

    public JsonNode? Read() => _read();

    public void Write(JsonNode? value, string title) => _write(value, title);

    public bool IsOverridden(string? field) => _overridden(field);

    public UIElement? Default(string name)
    {
        if (_fields.FirstOrDefault(field => field.Name == name) is not { } field)
        {
            return null;
        }
        FieldEditor editor = new(Owner, field, field.Label ?? InspectorPanel.Pretty(field.Name), [.. _path, field.Name]);
        _defaults.Add(editor);
        return editor;
    }

    // Each frame: the editors it placed follow their fields.
    public void UpdateDefaults()
    {
        JsonObject? value = _read() as JsonObject;
        foreach (FieldEditor editor in _defaults)
        {
            editor.Update(value?[editor.Field.Name], _overridden(editor.Field.Name));
        }
    }
}

/// <summary>A value's JSON as its C# type and back (ADR 0126).</summary>
internal sealed record DrawerCodec<T>(Func<JsonNode?, T> Read, Func<T, JsonNode?> Write);

/// <summary>
/// Codecs for drawers' types: the generated serializer for a component or
/// <c>[Serialized]</c> type; the Inspector's own JSON for plain values.
/// </summary>
internal static class DrawerCodecs
{
    private const string JitOnly = "The editor runs JIT only (ADR 0016)";

    [UnconditionalSuppressMessage("Trimming", "IL2060", Justification = JitOnly)]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = JitOnly)]
    public static DrawerCodec<T> For<T>()
    {
        if (Generated(typeof(T)) is { } method)
        {
            return (DrawerCodec<T>)method.MakeGenericMethod(typeof(T)).Invoke(null, null)!;
        }
        return Plain<T>() ?? throw new ArgumentException(
            $"A drawer can't draw a {typeof(T).Name}: it draws a [Serialized] type, a number, a bool, a string, a vector, or an asset ID");
    }

    /// <summary>A [Serialized] type's field table; null for another type.</summary>
    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = JitOnly)]
    public static IReadOnlyList<SerializedField>? TableOf(Type type) =>
        type.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IJsonSerialized<>))
            ? type.GetProperty("SerializedFields", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as IReadOnlyList<SerializedField>
              ?? StaticTable(type)
            : null;

    // The table through the interface's static member, for a type that implements it explicitly.
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = JitOnly)]
    [UnconditionalSuppressMessage("Trimming", "IL2060", Justification = JitOnly)]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = JitOnly)]
    private static IReadOnlyList<SerializedField>? StaticTable(Type type) =>
        typeof(DrawerCodecs).GetMethod(nameof(TableVia), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(type).Invoke(null, null) as IReadOnlyList<SerializedField>;

    private static IReadOnlyList<SerializedField> TableVia<T>() where T : IJsonSerialized<T> => T.SerializedFields;

    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = JitOnly)]
    private static MethodInfo? Generated(Type type) =>
        type.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IJsonSerialized<>))
            ? typeof(DrawerCodecs).GetMethod(nameof(Serialized), BindingFlags.NonPublic | BindingFlags.Static)
            : null;

    private static DrawerCodec<T> Serialized<T>() where T : IJsonSerialized<T> => new(
        node => node is null ? default! : Json.Deserialize<T>(node.ToJsonString()),
        value => JsonNode.Parse(Json.Serialize(value)));

    // The Inspector's JSON for plain values: numbers, bools, strings, vectors
    // and quaternions as arrays of numbers, asset IDs as strings.
    private static DrawerCodec<T>? Plain<T>()
    {
        object? codec = typeof(T) switch
        {
            Type t when t == typeof(float) => new DrawerCodec<float>(n => (float)Number(n), v => JsonValue.Create(v)),
            Type t when t == typeof(double) => new DrawerCodec<double>(n => Number(n), v => JsonValue.Create(v)),
            Type t when t == typeof(int) => new DrawerCodec<int>(n => (int)Number(n), v => JsonValue.Create(v)),
            Type t when t == typeof(uint) => new DrawerCodec<uint>(n => (uint)Number(n), v => JsonValue.Create(v)),
            Type t when t == typeof(bool) => new DrawerCodec<bool>(
                n => n?.GetValueKind() == System.Text.Json.JsonValueKind.True, v => JsonValue.Create(v)),
            Type t when t == typeof(string) => new DrawerCodec<string>(
                n => n is JsonValue value && value.TryGetValue(out string? text) ? text : "", v => JsonValue.Create(v ?? "")),
            Type t when t == typeof(Vector2) => new DrawerCodec<Vector2>(n => new Vector2(At(n, 0), At(n, 1)), v => new JsonArray(v.X, v.Y)),
            Type t when t == typeof(Vector3) => new DrawerCodec<Vector3>(n => new Vector3(At(n, 0), At(n, 1), At(n, 2)),
                v => new JsonArray(v.X, v.Y, v.Z)),
            Type t when t == typeof(Vector4) => new DrawerCodec<Vector4>(n => new Vector4(At(n, 0), At(n, 1), At(n, 2), At(n, 3)),
                v => new JsonArray(v.X, v.Y, v.Z, v.W)),
            Type t when t == typeof(Quaternion) => new DrawerCodec<Quaternion>(n => new Quaternion(At(n, 0), At(n, 1), At(n, 2), At(n, 3)),
                v => new JsonArray(v.X, v.Y, v.Z, v.W)),
            Type t when t == typeof(AssetId) => new DrawerCodec<AssetId>(
                n => n is JsonValue value && value.TryGetValue(out string? text) && AssetId.TryParse(text, out AssetId id) ? id : AssetId.None,
                v => v.IsNone ? null : JsonValue.Create(v.ToString())),
            _ => null,
        };
        return codec as DrawerCodec<T>;
    }

    // A JSON number, however it's held (parsed, or made from a float).
    private static double Number(JsonNode? node) =>
        node is JsonValue value && value.GetValueKind() == System.Text.Json.JsonValueKind.Number &&
        double.TryParse(value.ToJsonString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double number)
            ? number
            : 0;

    private static float At(JsonNode? node, int index) => node is JsonArray array && index < array.Count ? (float)Number(array[index]) : 0;
}
