using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using Crucible.Assets;
using Crucible.Assets.Importers;
using Crucible.Binary;
using Crucible.Physics;
using Crucible.UI;

namespace Crucible.Editor;

/// <summary>
/// Under a Mesh or Convex Collider whose model isn't imported with that
/// kind of collision for its mesh (ADR 0087), unless the collider's Generate
/// If Missing builds it at load: says so, with a button that sets the
/// model's Collision import setting and reimports it. A model is imported
/// with one kind for all its meshes, so the other kind replaces it.
/// </summary>
internal sealed class ColliderNotice : UIElement
{
    private readonly Shell _shell;
    private readonly Label _text = new() { Wrap = true };
    private readonly Button _reimport = new("");
    private string? _checked; // the model, kind, and mesh last looked at
    private AssetId _model;
    private ShapeKind _kind;

    public ColliderNotice(Shell shell)
    {
        _shell = shell;
        _text.Classes.Add("log-warning");
        _reimport.Clicked += _ => Reimport();
        _reimport.HorizontalAlignment = HorizontalAlignment.Start;
        StackPanel body = new() { Spacing = 4, Margin = new Thickness(0, 4, 0, 0) };
        body.Add(_text);
        body.Add(_reimport);
        AddChild(body);
        Visibility = Visibility.Collapsed;
    }

    public Label Text => _text;

    public Button ReimportButton => _reimport;

    protected override void OnAttached(UIRoot root) => _shell.AssetsChanged += Forget;

    protected override void OnDetached(UIRoot root) => _shell.AssetsChanged -= Forget;

    private void Forget() => _checked = null;

    /// <summary>Each frame, with the collider's JSON: looked at again when its model, shape, or mesh changes.</summary>
    public void Update(JsonNode? collider)
    {
        ShapeKind kind = Shape(collider?["Shape"]);
        AssetId.TryParse(collider?["Mesh"]?.GetValue<string>(), out AssetId model);
        int index = collider?["MeshIndex"] is JsonValue value && value.TryGetValue(out int number) ? number : 0;
        bool generates = collider?["GenerateIfMissing"] is JsonValue flag && flag.TryGetValue(out bool on) && on;
        string key = $"{model}:{kind}:{index}:{generates}";
        if (key == _checked)
        {
            return;
        }
        _checked = key;
        _model = model;
        _kind = kind;
        AssetEntry? entry = _shell.Database?.Find(model);
        bool missing = kind is ShapeKind.Mesh or ShapeKind.Convex && !generates && entry is not null &&
                       entry.Importer is "Mesh" or "Fbx" && !HasData(entry.Id, kind, index);
        Visibility = missing ? Visibility.Visible : Visibility.Collapsed;
        if (!missing)
        {
            return;
        }
        string imported = _shell.Database!.SettingsOf(model)?["Collision"]?.GetValue<string>() ?? "None";
        string name = Path.GetFileName(entry!.Path);
        _text.Text = $"{name} isn't imported with {kind} collision";
        _reimport.Text = imported is "None" || imported == kind.ToString()
            ? $"Reimport {name} with {kind} collision"
            : $"Reimport {name} with {kind} collision (replaces {imported})";
    }

    // Whether the model's imported file has data of the kind for the mesh;
    // a model not imported yet is left alone (its import reports it).
    private bool HasData(AssetId model, ShapeKind kind, int index)
    {
        if (_shell.Database!.ImportedFile(model) is not { } path)
        {
            return true;
        }
        try
        {
            using ImportedFile file = ImportedFile.Open(path, MeshImporter.Kind, MeshImporter.KindVersion);
            int section = file.Find(Tag.Of(kind == ShapeKind.Mesh ? "COLM" : "COLH"));
            if (section < 0)
            {
                return false;
            }
            ReadOnlySpan<CollisionEntry> entries = MemoryMarshal.Cast<byte, CollisionEntry>(file.Read(section));
            return index >= 0 && index < entries.Length && entries[index].Size > 0;
        }
        catch (CrucibleException)
        {
            return true; // unreadable: the runtime's warning covers it
        }
    }

    private void Reimport()
    {
        if (_shell.Database?.SettingsOf(_model) is not { } settings)
        {
            return;
        }
        settings["Collision"] = _kind.ToString();
        _shell.ApplyImportSettings(_model, settings);
    }

    private static ShapeKind Shape(JsonNode? node) => node is JsonValue value && value.TryGetValue(out string? name) &&
                                                      Enum.TryParse(name, out ShapeKind parsed)
        ? parsed
        : node is JsonValue number && number.TryGetValue(out uint raw) ? (ShapeKind)raw : ShapeKind.Box;
}
