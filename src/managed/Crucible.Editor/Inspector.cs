using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json.Nodes;
using Crucible.Ecs;
using Crucible.Scenes;
using Crucible.Serialization;
using Crucible.UI;

namespace Crucible.Editor;

/// <summary>
/// The selection's components (ADR 0069): a <see cref="ComponentSection"/>
/// for each saved component, its fields' editors made from the type's
/// generated field table. Edits change the component's JSON and write it
/// back, each an undo step (a drag of a number or a color, one step). Values
/// are read again every frame, so a handle drag shows here as it happens.
/// Fields of a prefab instance that differ from the prefab are marked, with
/// Revert in their right-click menu.
/// </summary>
public sealed class InspectorPanel : EditorPanel
{
    private readonly Shell _shell;
    private readonly Label _title = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _add = new("Add Component") { HorizontalAlignment = HorizontalAlignment.End };
    private readonly StackPanel _sections = new() { Spacing = 6 };
    private readonly Label _empty = new("Nothing selected") { HorizontalAlignment = HorizontalAlignment.Center };
    private Entity _entity;
    private string _shape = ""; // the entity and its component names, when the sections were made
    private bool _stale = true;
    private PrefabValues? _prefab;

    internal InspectorPanel(Shell shell) : base("Inspector", "Inspector")
    {
        _shell = shell;
        _title.Classes.Add("inspector-title");
        _empty.Classes.Add("placeholder");
        _add.Clicked += _ => ShowAddMenu();
        DockPanel header = new();
        header.Classes.Add("inspector-header");
        DockPanel.SetDock(_add, Dock.Right);
        header.Add(_add);
        header.Add(_title);
        DockPanel page = new();
        DockPanel.SetDock(header, Dock.Top);
        page.Add(header);
        page.Add(new ScrollView { Content = _sections, Padding = new Thickness(6, 4, 6, 8) });
        AddChild(page);
    }

    internal Shell Shell => _shell;

    /// <summary>The selected entity being shown, or <see cref="Entity.Null"/>.</summary>
    public Entity Entity => _entity;

    public IReadOnlyList<ComponentSection> Sections => [.. _sections.Children.OfType<ComponentSection>()];

    internal PrefabValues? Prefab => _prefab;

    protected override void OnAttached(UIRoot root)
    {
        _shell.SceneChanged += Invalidate;
        _stale = true;
    }

    protected override void OnDetached(UIRoot root) => _shell.SceneChanged -= Invalidate;

    private void Invalidate() => _stale = true;

    /// <summary>Each frame, before the viewports render: sections made again if the entity or its components changed, values read again.</summary>
    internal void Update()
    {
        Entity selected = _shell.HasWorld && _shell.Selection.Selected.Value != 0 && _shell.World.IsAlive(_shell.Selection.Selected)
            ? _shell.Selection.Selected
            : Entity.Null;
        string shape = selected.IsNull ? "" : $"{selected.Value}:{string.Join(",", Components(selected))}";
        if (shape != _shape || (_stale && selected.Value != _entity.Value))
        {
            Rebuild(selected, shape);
        }
        else if (_stale)
        {
            _prefab = selected.IsNull ? null : PrefabValues.Of(_shell.World, selected, _shell.Prefabs);
        }
        _stale = false;
        _add.Visibility = selected.IsNull ? Visibility.Collapsed : Visibility.Visible;
        _title.Text = selected.IsNull ? "" : _shell.LabelOf(selected);
        foreach (ComponentSection section in _sections.Children.OfType<ComponentSection>())
        {
            section.Update();
        }
    }

    // The saved components it has, the transform first, then by name.
    private string[] Components(Entity entity)
    {
        World world = _shell.World;
        return
        [
            .. SavedComponents.All().Where(saved => world.Has(entity, saved.Id))
                .OrderBy(saved => saved.Type != typeof(LocalTransform))
                .ThenBy(saved => saved.Name, StringComparer.Ordinal)
                .Select(saved => saved.Name),
        ];
    }

    private void Rebuild(Entity entity, string shape)
    {
        _entity = entity;
        _shape = shape;
        _sections.Clear();
        if (entity.IsNull)
        {
            _prefab = null;
            _sections.Add(_empty);
            return;
        }
        _prefab = PrefabValues.Of(_shell.World, entity, _shell.Prefabs);
        foreach (string name in Components(entity))
        {
            if (SavedComponents.Find(name) is { } saved)
            {
                _sections.Add(new ComponentSection(this, saved));
            }
        }
    }

    private void ShowAddMenu()
    {
        if (_entity.IsNull)
        {
            return;
        }
        ContextMenu menu = new();
        World world = _shell.World;
        foreach (SavedComponent saved in SavedComponents.All().Where(saved => !world.Has(_entity, saved.Id))
                     .OrderBy(saved => saved.Type.Name, StringComparer.Ordinal))
        {
            string name = saved.Name;
            menu.Add(saved.Type.Name, () => _shell.AddComponent(_entity, name));
        }
        menu.Open(_add, new Vector2(_add.Bounds.X, _add.Bounds.Bottom));
    }

    /// <summary>"FieldOfViewY" as "Field Of View Y"; "_secret" as "Secret".</summary>
    public static string Pretty(string name)
    {
        string trimmed = name.TrimStart('_');
        StringBuilder text = new();
        for (int i = 0; i < trimmed.Length; ++i)
        {
            char c = trimmed[i];
            bool boundary = i > 0 && char.IsUpper(c) &&
                            (!char.IsUpper(trimmed[i - 1]) || (i + 1 < trimmed.Length && char.IsLower(trimmed[i + 1])));
            if (boundary || (i > 0 && char.IsDigit(c) && !char.IsDigit(trimmed[i - 1])))
            {
                text.Append(' ');
            }
            text.Append(i == 0 ? char.ToUpperInvariant(c) : c);
        }
        return text.ToString();
    }
}

/// <summary>
/// One component in the inspector (ADR 0069): a header (its type, a fold,
/// and a menu with Remove Component and Revert All) over a
/// <see cref="FieldEditor"/> for each field. It reads the component's JSON
/// each frame and hands each editor its part.
/// </summary>
public sealed class ComponentSection : UIElement
{
    private readonly InspectorPanel _inspector;
    private readonly SavedComponent _saved;
    private readonly StackPanel _fields = new() { Spacing = 3 };
    private readonly IconButton _fold = new(IconButton.Icon.Open);
    private readonly Label _name;
    private readonly List<FieldEditor> _editors = [];
    private JsonNode? _value;

    internal ComponentSection(InspectorPanel inspector, SavedComponent saved)
    {
        _inspector = inspector;
        _saved = saved;
        _name = new Label(saved.Type.Name) { VerticalAlignment = VerticalAlignment.Center };
        _name.Classes.Add("component-title");
        _fold.Clicked += _ =>
        {
            _fields.Visibility = _fields.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
            _fold.Shows = _fields.Visibility == Visibility.Visible ? IconButton.Icon.Open : IconButton.Icon.Closed;
        };
        IconButton menu = new(IconButton.Icon.Menu);
        menu.Clicked += _ => ShowMenu(menu);
        DockPanel header = new();
        header.Classes.Add("component-header");
        DockPanel.SetDock(_fold, Dock.Left);
        DockPanel.SetDock(menu, Dock.Right);
        header.Add(_fold);
        header.Add(menu);
        header.Add(_name);
        header.PointerDown += (_, e) =>
        {
            if (e.Button == PointerButton.Right)
            {
                e.Handled = true;
                ShowMenu(header, e.Position);
            }
        };
        StackPanel body = new() { Spacing = 4 };
        body.Classes.Add("component");
        body.Add(header);
        body.Add(_fields);
        AddChild(body);
        foreach (SerializedField field in saved.Fields)
        {
            if (field.Header is { } heading)
            {
                Label label = new(heading);
                label.Classes.Add("field-header");
                _fields.Add(label);
            }
            FieldEditor editor = new(this, field, InspectorPanel.Pretty(field.Name), [field.Name]);
            _editors.Add(editor);
            _fields.Add(editor);
        }
        if (saved.Fields.Count == 0)
        {
            Label tag = new("No fields");
            tag.Classes.Add("placeholder");
            _fields.Add(tag);
        }
    }

    /// <summary>The component's name (its type's full name).</summary>
    public string Component => _saved.Name;

    public IReadOnlyList<FieldEditor> Editors => _editors;

    internal Shell Shell => _inspector.Shell;

    internal Entity Entity => _inspector.Entity;

    /// <summary>The prefab's value of this component, for an instance's entity; null otherwise or if the prefab hasn't it.</summary>
    internal JsonNode? PrefabValue =>
        _inspector.Prefab is { } prefab && prefab.Components.TryGetValue(Component, out JsonNode? value) ? value : null;

    internal bool InPrefab => _inspector.Prefab is not null;

    internal void Update()
    {
        _value = SceneIds.Read(Shell.World, Entity, Component);
        JsonObject? prefab = PrefabValue as JsonObject;
        bool added = InPrefab && PrefabValue is null;
        _name.Classes.Set("overridden", added);
        foreach (FieldEditor editor in _editors)
        {
            JsonNode? value = (_value as JsonObject)?[editor.Field.Name];
            bool overridden = added || (prefab is not null && !JsonNode.DeepEquals(value, prefab[editor.Field.Name]));
            editor.Update(value, overridden);
        }
    }

    /// <summary>Sets the value at a path inside the component (keys and list indices) and records it.</summary>
    internal void Edit(IReadOnlyList<object> path, JsonNode? value, string title)
    {
        if (_value?.DeepClone() is not JsonNode whole)
        {
            return;
        }
        if (!Json.Set(whole, path, value))
        {
            return;
        }
        Shell.SetComponent(Entity, Component, whole, title);
        Update();
    }

    internal void Revert(string field)
    {
        if (PrefabValue is JsonObject prefab && prefab.TryGetPropertyValue(field, out JsonNode? value))
        {
            Edit([field], value?.DeepClone(), "Revert");
        }
    }

    private void ShowMenu(UIElement at, Vector2? position = null)
    {
        ContextMenu menu = new();
        string component = Component;
        Entity entity = Entity;
        menu.Add("Remove Component", () => Shell.RemoveComponent(entity, component));
        if (InPrefab && !JsonNode.DeepEquals(PrefabValue, _value))
        {
            JsonNode? prefab = PrefabValue?.DeepClone();
            menu.Add("Revert All", () =>
            {
                if (prefab is null)
                {
                    Shell.RemoveComponent(entity, component); // added to the instance: reverting removes it
                }
                else
                {
                    Shell.SetComponent(entity, component, prefab, "Revert All");
                }
            });
        }
        menu.Open(at, position ?? new Vector2(at.Bounds.X, at.Bounds.Bottom));
    }

    // Values at paths inside a component's JSON.
    internal static class Json
    {
        public static JsonNode? Get(JsonNode? root, IReadOnlyList<object> path)
        {
            JsonNode? node = root;
            foreach (object step in path)
            {
                node = step switch
                {
                    string key when node is JsonObject item => item[key],
                    int index when node is JsonArray list && index < list.Count => list[index],
                    _ => null,
                };
            }
            return node;
        }

        public static bool Set(JsonNode root, IReadOnlyList<object> path, JsonNode? value)
        {
            if (Get(root, path.Take(path.Count - 1).ToArray()) is not { } parent)
            {
                return false;
            }
            switch (path[^1])
            {
                case string key when parent is JsonObject item:
                    item[key] = value;
                    return true;
                case int index when parent is JsonArray list && index < list.Count:
                    list[index] = value;
                    return true;
                default:
                    return false;
            }
        }
    }
}

/// <summary>
/// One field's editor (ADR 0069), chosen by its kind: a toggle, number
/// fields (with a slider for a range), text, a drop-down for an enum, an
/// asset, or an entity, a row of numbers for vectors, angles in degrees for
/// a rotation, a swatch for a color, and foldouts for nested types and
/// lists (which add and remove items at the end). Dictionaries are shown,
/// not edited. Marked <c>overridden</c> where a prefab instance differs from
/// its prefab; its tooltip shows in the status bar while the pointer is on it.
/// </summary>
public sealed class FieldEditor : UIElement
{
    private const float LabelWidth = 92;

    private readonly ComponentSection _section;
    private readonly object[] _path;
    private readonly string _label;
    private readonly Label _name;
    private readonly DockPanel _row = new();
    private readonly StackPanel _children = new() { Spacing = 3, Margin = new Thickness(12, 0, 0, 0) };
    private Action<JsonNode?>? _show; // puts a value into the controls
    private readonly List<FieldEditor> _items = [];
    private int _itemCount = -1;
    private bool _grouped; // an undo group is open for a drag
    private Quaternion _shownRotation = new(float.NaN, 0, 0, 0);
    private JsonNode? _value;

    internal FieldEditor(ComponentSection section, SerializedField field, string label, object[] path)
    {
        _section = section;
        Field = field;
        _label = label;
        _path = path;
        _name = new Label(label) { VerticalAlignment = VerticalAlignment.Center, Width = LabelWidth };
        _name.Classes.Add("field-name");
        DockPanel.SetDock(_name, Dock.Left);
        _row.Add(_name);
        StackPanel all = new() { Spacing = 3 };
        all.Add(_row);
        all.Add(_children);
        AddChild(all);
        Build();
        PointerEnter += (_, _) =>
        {
            if (Field.Tooltip is { } tooltip)
            {
                _section.Shell.Status(tooltip);
            }
        };
        _name.PointerDown += (_, e) =>
        {
            if (e.Button == PointerButton.Right && IsOverridden && _path.Length == 1)
            {
                e.Handled = true;
                ContextMenu menu = new();
                menu.Add("Revert", () => _section.Revert(Field.Name));
                menu.Open(_name, e.Position);
            }
        };
    }

    public SerializedField Field { get; }

    /// <summary>Whether a prefab instance's value differs from its prefab's (class <c>overridden</c>).</summary>
    public bool IsOverridden { get; private set; }

    /// <summary>The value shown, as JSON.</summary>
    public JsonNode? Value => _value;

    /// <summary>The controls in the row: number fields, a toggle, a drop-down, a swatch.</summary>
    public IEnumerable<UIElement> Controls => _row.Children.Where(child => child != _name);

    /// <summary>The editors inside it: a nested type's fields, or a list's items.</summary>
    public IReadOnlyList<FieldEditor> Nested => [.. _children.Children.OfType<FieldEditor>()];

    internal void Update(JsonNode? value, bool overridden)
    {
        _value = value;
        IsOverridden = overridden;
        Classes.Set("overridden", overridden);
        _name.Classes.Set("overridden", overridden);
        _show?.Invoke(value);
        if (Field.Kind is FieldKind.Serialized)
        {
            JsonObject? item = value as JsonObject;
            for (int i = 0; i < _items.Count; ++i)
            {
                _items[i].Update(item?[_items[i].Field.Name], overridden);
            }
        }
        else if (Field.Kind is FieldKind.List or FieldKind.Array)
        {
            JsonArray? list = value as JsonArray;
            if ((list?.Count ?? 0) != _itemCount)
            {
                BuildItems(list?.Count ?? 0);
            }
            for (int i = 0; i < _items.Count; ++i)
            {
                _items[i].Update(list![i], overridden);
            }
        }
    }

    private void Commit(JsonNode? value, string? title = null) => _section.Edit(_path, value, title ?? $"Edit {_label}");

    // A drag (a number scrubbed, a slider, a color picked) is one undo step.
    private void BeginDrag()
    {
        if (!_grouped)
        {
            _grouped = true;
            _section.Shell.History.BeginGroup($"Edit {_label}");
        }
    }

    private void EndDrag()
    {
        if (_grouped)
        {
            _grouped = false;
            _section.Shell.History.EndGroup();
        }
    }

    protected override void OnDetached(UIRoot root) => EndDrag();

    private void Add(UIElement control) => _row.Add(control);

    private void Build()
    {
        switch (Field.Kind)
        {
            case FieldKind.Boolean:
                {
                    Toggle toggle = new() { VerticalAlignment = VerticalAlignment.Center };
                    bool showing = false;
                    toggle.Changed += t =>
                    {
                        if (!showing)
                        {
                            Commit(JsonValue.Create(t.IsOn));
                        }
                    };
                    Add(toggle);
                    _show = value =>
                    {
                        showing = true;
                        toggle.IsOn = value?.GetValueKind() == System.Text.Json.JsonValueKind.True;
                        showing = false;
                    };
                    break;
                }
            case FieldKind.Byte or FieldKind.SByte or FieldKind.Int16 or FieldKind.UInt16 or FieldKind.Int32
                or FieldKind.UInt32 or FieldKind.Int64 or FieldKind.UInt64 or FieldKind.Single or FieldKind.Double:
                {
                    bool whole = Field.Kind is not (FieldKind.Single or FieldKind.Double);
                    (double min, double max) = Limits(Field.Kind);
                    if (Field.HasRange)
                    {
                        min = Math.Max(min, Field.Min);
                        max = Math.Min(max, Field.Max);
                    }
                    NumberField number = Number(whole ? 1 : 0.01, min, max, value => Commit(Number(value, whole)));
                    Slider? slider = null;
                    if (Field.HasRange)
                    {
                        slider = new Slider
                        {
                            Minimum = (float)min, Maximum = (float)max, Step = whole ? 1 : 0, MinWidth = 80,
                            VerticalAlignment = VerticalAlignment.Center,
                        };
                        bool showing = false;
                        slider.ValueChanged += s =>
                        {
                            if (!showing)
                            {
                                if ((s.State & UIState.Pressed) != 0)
                                {
                                    BeginDrag();
                                }
                                Commit(Number(s.Value, whole));
                            }
                        };
                        slider.PointerUp += (_, _) => EndDrag();
                        number.Width = 64;
                        DockPanel.SetDock(number, Dock.Right);
                        Add(number);
                        Add(slider);
                        _show = value =>
                        {
                            double shown = value is JsonValue v && v.TryGetValue(out double d) ? d : 0;
                            if (!number.IsFocused)
                            {
                                number.Value = shown;
                            }
                            showing = true;
                            slider.Value = (float)shown;
                            showing = false;
                        };
                    }
                    else
                    {
                        Add(number);
                        _show = value =>
                        {
                            if (!number.IsFocused)
                            {
                                number.Value = value is JsonValue v && v.TryGetValue(out double d) ? d : 0;
                            }
                        };
                    }
                    break;
                }
            case FieldKind.String or FieldKind.Guid:
                {
                    TextField text = new() { VerticalAlignment = VerticalAlignment.Center };
                    text.Committed += t =>
                    {
                        if (Field.Kind == FieldKind.Guid && !Guid.TryParse(t.Text, out _))
                        {
                            return; // shown again from the value next frame
                        }
                        Commit(JsonValue.Create(t.Text));
                    };
                    Add(text);
                    _show = value =>
                    {
                        if (!text.IsFocused)
                        {
                            text.Text = value is JsonValue v && v.TryGetValue(out string? s) ? s : "";
                        }
                    };
                    break;
                }
            case FieldKind.Enum:
                Choice([.. Field.EnumNames], [.. Field.EnumNames], value => value is JsonValue v && v.TryGetValue(out string? s) ? s : "");
                break;
            case FieldKind.AssetId:
                {
                    (string Label, string Value)[] assets = _section.Shell.Database is { } database
                        ? [.. database.Assets.Select(asset => (asset.Path, asset.Id.ToString()))]
                        : [];
                    Choice(["None", .. assets.Select(asset => asset.Label)], ["", .. assets.Select(asset => asset.Value)],
                        value => value is JsonValue v && v.TryGetValue(out string? s) ? s : "");
                    break;
                }
            case FieldKind.Entity:
                {
                    World world = _section.Shell.World;
                    SceneIds ids = SceneIds.Of(world);
                    (string Label, string Value)[] entities =
                    [
                        .. ids.Entities.Select(entity => (Entity: entity, Id: ids.TryToScene(entity, out ulong id) ? id : 0))
                            .OrderBy(item => _section.Shell.LabelOf(item.Entity), StringComparer.Ordinal)
                            .Select(item => (_section.Shell.LabelOf(item.Entity), item.Id.ToString("x16"))),
                    ];
                    Choice(["None", .. entities.Select(entity => entity.Label)], ["", .. entities.Select(entity => entity.Value)],
                        value => value is JsonValue v && v.TryGetValue(out string? s) ? s : "");
                    break;
                }
            case FieldKind.Vector2 or FieldKind.Vector3 or FieldKind.Vector4 when Field.IsColor:
                {
                    ColorField color = new() { HasAlpha = Field.Kind == FieldKind.Vector4, MinWidth = 64 };
                    bool showing = false;
                    color.ColorChanged += c =>
                    {
                        if (showing)
                        {
                            return;
                        }
                        if (c.Picker.IsOpen)
                        {
                            BeginDrag();
                        }
                        Vector4 v = c.Color;
                        Commit(Floats(Field.Kind == FieldKind.Vector4 ? [v.X, v.Y, v.Z, v.W] : [v.X, v.Y, v.Z]));
                    };
                    color.Picker.Closed += _ => EndDrag();
                    Add(color);
                    _show = value =>
                    {
                        float[] f = ReadFloats(value, Field.Kind == FieldKind.Vector4 ? 4 : 3);
                        showing = true;
                        color.Color = new Vector4(f[0], f[1], f[2], f.Length > 3 ? f[3] : 1);
                        showing = false;
                    };
                    break;
                }
            case FieldKind.Vector2 or FieldKind.Vector3 or FieldKind.Vector4:
                Numbers(Field.Kind == FieldKind.Vector2 ? 2 : Field.Kind == FieldKind.Vector3 ? 3 : 4);
                break;
            case FieldKind.Quaternion:
                Rotation();
                break;
            case FieldKind.Matrix4x4:
                {
                    Label note = new("Matrix") { VerticalAlignment = VerticalAlignment.Center };
                    note.Classes.Add("placeholder");
                    Add(note);
                    for (int row = 0; row < 4; ++row)
                    {
                        _children.Add(new MatrixRow(this, row));
                    }
                    break;
                }
            case FieldKind.Nullable:
                {
                    Toggle has = new() { VerticalAlignment = VerticalAlignment.Center };
                    bool showing = false;
                    has.Changed += t =>
                    {
                        if (!showing)
                        {
                            Commit(t.IsOn ? DefaultJson(Field.Element!) : null);
                        }
                    };
                    DockPanel.SetDock(has, Dock.Left);
                    Add(has);
                    FieldEditor inner = new(_section, Field.Element!, "Value", _path);
                    _children.Add(inner);
                    _show = value =>
                    {
                        showing = true;
                        has.IsOn = value is not null;
                        showing = false;
                        inner.Visibility = value is null ? Visibility.Collapsed : Visibility.Visible;
                        if (value is not null)
                        {
                            inner.Update(value, IsOverridden);
                        }
                    };
                    break;
                }
            case FieldKind.Serialized:
                {
                    Fold();
                    foreach (SerializedField field in Field.Fields)
                    {
                        FieldEditor editor = new(_section, field, InspectorPanel.Pretty(field.Name), [.. _path, field.Name]);
                        _items.Add(editor);
                        _children.Add(editor);
                    }
                    break;
                }
            case FieldKind.List or FieldKind.Array:
                {
                    Label count = new() { VerticalAlignment = VerticalAlignment.Center };
                    count.Classes.Add("placeholder");
                    Button add = new("+"), remove = new("−");
                    add.Classes.Add("tool");
                    remove.Classes.Add("tool");
                    add.Clicked += _ =>
                    {
                        JsonArray list = _value?.DeepClone() as JsonArray ?? [];
                        list.Add(DefaultJson(Field.Element!));
                        Commit(list, $"Add to {_label}");
                    };
                    remove.Clicked += _ =>
                    {
                        if (_value?.DeepClone() is JsonArray { Count: > 0 } list)
                        {
                            list.RemoveAt(list.Count - 1);
                            Commit(list, $"Remove from {_label}");
                        }
                    };
                    DockPanel.SetDock(remove, Dock.Right);
                    DockPanel.SetDock(add, Dock.Right);
                    Add(remove);
                    Add(add);
                    Add(count);
                    _show = value => count.Text = $"{(value as JsonArray)?.Count ?? 0} items";
                    break;
                }
            case FieldKind.Dictionary:
                {
                    Label count = new() { VerticalAlignment = VerticalAlignment.Center };
                    count.Classes.Add("placeholder");
                    Add(count);
                    StackPanel entries = new() { Spacing = 2 };
                    _children.Add(entries);
                    string shown = "";
                    _show = value =>
                    {
                        JsonObject? map = value as JsonObject;
                        count.Text = $"{map?.Count ?? 0} entries (read-only)";
                        string text = map?.ToJsonString() ?? "";
                        if (text == shown)
                        {
                            return;
                        }
                        shown = text;
                        entries.Clear();
                        foreach ((string key, JsonNode? entry) in map ?? [])
                        {
                            entries.Add(new Label($"{key}: {entry?.ToJsonString()}") { Wrap = true });
                        }
                    };
                    break;
                }
        }
    }

    private void Fold()
    {
        IconButton fold = new(IconButton.Icon.Open);
        fold.Clicked += _ =>
        {
            _children.Visibility = _children.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
            fold.Shows = _children.Visibility == Visibility.Visible ? IconButton.Icon.Open : IconButton.Icon.Closed;
        };
        DockPanel.SetDock(fold, Dock.Left);
        Add(fold);
    }

    private void BuildItems(int count)
    {
        _itemCount = count;
        foreach (FieldEditor item in _items)
        {
            _children.Remove(item);
        }
        _items.Clear();
        for (int i = 0; i < count; ++i)
        {
            FieldEditor item = new(_section, Field.Element!, $"{i}", [.. _path, i]);
            _items.Add(item);
            _children.Add(item);
        }
    }

    // A drop-down of labels standing for JSON strings ("" for null).
    private void Choice(string[] labels, string[] values, Func<JsonNode?, string> current)
    {
        ComboBox box = new() { Options = labels, VerticalAlignment = VerticalAlignment.Center };
        bool showing = false;
        box.SelectionChanged += b =>
        {
            if (!showing && b.SelectedIndex >= 0)
            {
                string chosen = values[b.SelectedIndex];
                Commit(chosen.Length == 0 && Field.Kind != FieldKind.Enum ? null : JsonValue.Create(chosen));
            }
        };
        Add(box);
        _show = value =>
        {
            showing = true;
            box.SelectedIndex = Array.IndexOf(values, current(value));
            showing = false;
        };
    }

    // Number fields side by side, one per component of a JSON array of floats.
    private void Numbers(int count)
    {
        Grid row = EvenRow(count);
        NumberField[] fields = new NumberField[count];
        for (int i = 0; i < count; ++i)
        {
            int component = i;
            fields[i] = Number(0.01, double.NegativeInfinity, double.PositiveInfinity, value =>
            {
                float[] floats = ReadFloats(_value, count);
                floats[component] = (float)value;
                Commit(Floats(floats));
            });
            Grid.SetColumn(fields[i], i);
            row.Add(fields[i]);
        }
        Add(row);
        _show = value =>
        {
            float[] floats = ReadFloats(value, count);
            for (int i = 0; i < count; ++i)
            {
                if (!fields[i].IsFocused)
                {
                    fields[i].Value = Math.Round(floats[i], 4);
                }
            }
        };
    }

    // A rotation as three angles in degrees (X pitch, Y yaw, Z roll), stored back as a
    // quaternion; the angles shown stay put until the rotation changes elsewhere, so
    // one edited angle doesn't make the others jump between equivalent sets.
    private void Rotation()
    {
        Grid row = EvenRow(3);
        NumberField[] fields = new NumberField[3];
        Vector3 angles = Vector3.Zero;
        for (int i = 0; i < 3; ++i)
        {
            int axis = i;
            fields[i] = Number(1, double.NegativeInfinity, double.PositiveInfinity, value =>
            {
                angles[axis] = (float)value;
                Vector3 radians = angles * (MathF.PI / 180);
                Quaternion rotation = Quaternion.CreateFromYawPitchRoll(radians.Y, radians.X, radians.Z);
                _shownRotation = rotation;
                Commit(Floats([rotation.X, rotation.Y, rotation.Z, rotation.W]));
            });
            Grid.SetColumn(fields[i], i);
            row.Add(fields[i]);
        }
        Add(row);
        _show = value =>
        {
            float[] q = ReadFloats(value, 4);
            Quaternion rotation = new(q[0], q[1], q[2], q[3]);
            if (!(Quaternion.Dot(rotation, _shownRotation) is var dot && MathF.Abs(dot) > 0.999999f))
            {
                angles = EulerDegrees(rotation);
                _shownRotation = rotation;
            }
            for (int i = 0; i < 3; ++i)
            {
                if (!fields[i].IsFocused)
                {
                    fields[i].Value = Math.Round(angles[i], 2);
                }
            }
        };
    }

    /// <summary>A rotation's angles in degrees, as <c>Quaternion.CreateFromYawPitchRoll</c> takes them: (pitch, yaw, roll).</summary>
    public static Vector3 EulerDegrees(Quaternion q)
    {
        q = Quaternion.Normalize(q);
        // Pitch around X, between yaw around Y and roll around Z (Y, X, Z order).
        float sinPitch = Math.Clamp(2 * (q.W * q.X - q.Y * q.Z), -1, 1);
        float pitch = MathF.Asin(sinPitch);
        float yaw = MathF.Atan2(2 * (q.W * q.Y + q.X * q.Z), 1 - 2 * (q.X * q.X + q.Y * q.Y));
        float roll = MathF.Atan2(2 * (q.W * q.Z + q.X * q.Y), 1 - 2 * (q.X * q.X + q.Z * q.Z));
        return new Vector3(pitch, yaw, roll) * (180 / MathF.PI);
    }

    // Columns sharing the row's width evenly.
    private static Grid EvenRow(int count)
    {
        Grid row = new() { ColumnSpacing = 3 };
        for (int i = 0; i < count; ++i)
        {
            row.Columns.Add(GridLength.Star());
        }
        return row;
    }

    private NumberField Number(double step, double min, double max, Action<double> changed)
    {
        NumberField field = new()
        {
            Step = step, Minimum = min, Maximum = max, MinWidth = 36, VerticalAlignment = VerticalAlignment.Center,
        };
        bool showing = true;
        field.GotFocus += (_, _) => showing = false;
        field.LostFocus += (_, _) =>
        {
            showing = true;
            EndDrag();
        };
        field.ValueChanged += f =>
        {
            if (showing && !f.IsFocused)
            {
                return; // set from the value, not by the user
            }
            if ((f.State & UIState.Pressed) != 0)
            {
                BeginDrag(); // scrubbing
            }
            changed(f.Value);
        };
        field.PointerUp += (_, _) => EndDrag();
        return field;
    }

    private static JsonNode Number(double value, bool whole) =>
        whole ? JsonValue.Create((long)Math.Round(value)) : JsonValue.Create((float)value);

    private static (double Min, double Max) Limits(FieldKind kind) => kind switch
    {
        FieldKind.Byte => (byte.MinValue, byte.MaxValue),
        FieldKind.SByte => (sbyte.MinValue, sbyte.MaxValue),
        FieldKind.Int16 => (short.MinValue, short.MaxValue),
        FieldKind.UInt16 => (ushort.MinValue, ushort.MaxValue),
        FieldKind.Int32 => (int.MinValue, int.MaxValue),
        FieldKind.UInt32 => (uint.MinValue, uint.MaxValue),
        FieldKind.Int64 => (long.MinValue, long.MaxValue),
        FieldKind.UInt64 => (0, ulong.MaxValue),
        _ => (double.NegativeInfinity, double.PositiveInfinity),
    };

    private static float[] ReadFloats(JsonNode? value, int count)
    {
        float[] floats = new float[count];
        if (value is JsonArray array)
        {
            for (int i = 0; i < Math.Min(count, array.Count); ++i)
            {
                floats[i] = array[i] is JsonValue v && v.TryGetValue(out float f) ? f : 0;
            }
        }
        return floats;
    }

    private static JsonArray Floats(float[] values) => [.. values.Select(value => (JsonNode)JsonValue.Create(value))];

    /// <summary>A new item's or value's JSON: zeros, empties, the first enum name, identity for a rotation.</summary>
    internal static JsonNode? DefaultJson(SerializedField field, int depth = 0) => field.Kind switch
    {
        FieldKind.Boolean => JsonValue.Create(false),
        FieldKind.Single or FieldKind.Double => JsonValue.Create(0f),
        FieldKind.Byte or FieldKind.SByte or FieldKind.Int16 or FieldKind.UInt16 or FieldKind.Int32 or FieldKind.UInt32
            or FieldKind.Int64 or FieldKind.UInt64 => JsonValue.Create(0),
        FieldKind.String => JsonValue.Create(""),
        FieldKind.Guid => JsonValue.Create(Guid.Empty.ToString()),
        FieldKind.Enum => field.EnumNames.Count > 0 ? JsonValue.Create(field.EnumNames[0]) : null,
        FieldKind.Vector2 => Floats([0, 0]),
        FieldKind.Vector3 => Floats([0, 0, 0]),
        FieldKind.Vector4 => Floats([0, 0, 0, 0]),
        FieldKind.Quaternion => Floats([0, 0, 0, 1]),
        FieldKind.Matrix4x4 => Floats([1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1]),
        FieldKind.Array or FieldKind.List => new JsonArray(),
        FieldKind.Dictionary => new JsonObject(),
        FieldKind.Serialized when depth < 8 => new JsonObject(
            field.Fields.Select(inner => KeyValuePair.Create(inner.Name, DefaultJson(inner, depth + 1)))),
        _ => null, // AssetId, Entity, Nullable, and types nested too deep
    };

    // A matrix row: four numbers at an offset of the matrix's sixteen.
    private sealed class MatrixRow : UIElement
    {
        public MatrixRow(FieldEditor owner, int row)
        {
            Grid numbers = EvenRow(4);
            numbers.Margin = new Thickness(LabelWidth, 0, 0, 0);
            NumberField[] fields = new NumberField[4];
            for (int i = 0; i < 4; ++i)
            {
                int index = row * 4 + i;
                fields[i] = owner.Number(0.01, double.NegativeInfinity, double.PositiveInfinity, value =>
                {
                    float[] floats = ReadFloats(owner._value, 16);
                    floats[index] = (float)value;
                    owner.Commit(Floats(floats));
                });
                Grid.SetColumn(fields[i], i);
                numbers.Add(fields[i]);
            }
            AddChild(numbers);
            Action<JsonNode?>? before = owner._show;
            owner._show = value =>
            {
                before?.Invoke(value);
                float[] floats = ReadFloats(value, 16);
                for (int i = 0; i < 4; ++i)
                {
                    if (!fields[i].IsFocused)
                    {
                        fields[i].Value = Math.Round(floats[row * 4 + i], 4);
                    }
                }
            };
        }
    }
}

/// <summary>A small button drawn as a fold's chevron (open, closed) or a menu's three dots.</summary>
internal sealed class IconButton : ButtonBase
{
    public enum Icon
    {
        Open,
        Closed,
        Menu,
    }

    private Icon _shows;

    public IconButton(Icon shows)
    {
        _shows = shows;
        Width = 20;
        Height = 20;
        VerticalAlignment = VerticalAlignment.Center;
    }

    public Icon Shows
    {
        get => _shows;
        set
        {
            _shows = value;
            InvalidateVisual();
        }
    }

    protected override void OnDraw(Canvas canvas)
    {
        base.OnDraw(canvas);
        Vector2 size = ActualSize;
        if (_shows == Icon.Menu)
        {
            for (int i = -1; i <= 1; ++i)
            {
                canvas.FillRect(size * 0.5f + new Vector2(i * 5 - 1.5f, -1.5f), new Vector2(3), Foreground, 1.5f);
            }
            return;
        }
        Chevron.Draw(canvas, size, Foreground, _shows == Icon.Open ? NavigationDirection.Down : NavigationDirection.Right);
    }
}

/// <summary>
/// What a prefab says an instance's entity holds (ADR 0069): the prefab
/// loaded into a scratch world as saving does, the entity found by the
/// scene ID its instance derives, and its saved components as JSON.
/// </summary>
internal sealed class PrefabValues
{
    private PrefabValues(Dictionary<string, JsonNode?> components) => Components = components;

    public IReadOnlyDictionary<string, JsonNode?> Components { get; }

    /// <summary>Null when the entity isn't part of an instance (or its prefab can't be read).</summary>
    public static PrefabValues? Of(World world, Entity entity, PrefabLibrary? library)
    {
        if (library is null)
        {
            return null;
        }
        ComponentId instanceComponent = Crucible.Ecs.Components.Of<PrefabInstance>();
        Entity root = entity;
        while (!root.IsNull && !world.Has(root, instanceComponent))
        {
            root = world.ParentOf(root);
        }
        if (root.IsNull)
        {
            return null;
        }
        ulong instance = SceneIds.IdOf(world, root), wanted = SceneIds.IdOf(world, entity);
        try
        {
            SceneFile file = library.Get(world.Read<PrefabInstance>(root).Prefab);
            using World scratch = new();
            Frame frame = new SceneLoader(scratch, library).Load(file, Entity.Null, outermost: false);
            ulong innerRoot = file.Roots[0].Id;
            ExpectedIds expected = new();
            Entity found = Entity.Null;
            foreach (Frame.Entry entry in frame.Entries)
            {
                ulong id = entry.Id == innerRoot ? instance : PrefabIds.Derive(instance, entry.Id);
                expected.Add(entry.Entity, id);
                if (id == wanted)
                {
                    found = entry.Entity;
                }
            }
            if (found.IsNull)
            {
                return null;
            }
            Dictionary<string, JsonNode?> components = [];
            using (EntityReferences.Use(expected))
            {
                foreach (SavedComponent saved in SavedComponents.All().Where(saved => scratch.Has(found, saved.Id)))
                {
                    components[saved.Name] = ComponentDiff.ToNode(saved, scratch, found);
                }
            }
            return new PrefabValues(components);
        }
        catch (Exception exception) when (exception is System.Text.Json.JsonException or InvalidDataException)
        {
            return null;
        }
    }

    // The scratch world's entities by the scene IDs the instance gives them.
    private sealed class ExpectedIds : IEntityMap
    {
        private readonly Dictionary<Entity, ulong> _toScene = [];
        private readonly Dictionary<ulong, Entity> _toWorld = [];

        public void Add(Entity entity, ulong id)
        {
            _toScene.TryAdd(entity, id);
            _toWorld.TryAdd(id, entity);
        }

        public bool TryToScene(Entity entity, out ulong sceneId) => _toScene.TryGetValue(entity, out sceneId);

        public Entity ToWorld(ulong sceneId) => _toWorld.GetValueOrDefault(sceneId);
    }
}
