using System.Text.Json.Nodes;
using Crucible.Physics;
using Crucible.Physics2D;
using Crucible.Serialization;
using Crucible.UI;

namespace Crucible.Editor;

/// <summary>
/// The Collider's section (ADR 0087, 0126): its generated fields, with the
/// notice that its model has no collision of that kind right under where the
/// model is chosen, the model's fields marked while it shows.
/// </summary>
internal sealed class ColliderDrawer(Shell shell) : Drawer<Collider>
{
    private readonly List<FieldEditor> _model = [];
    private ColliderNotice? _notice;

    public override UIElement Build(DrawerContext<Collider> context)
    {
        StackPanel fields = new() { Spacing = 3 };
        foreach (SerializedField field in Collider.SerializedFields)
        {
            if (context.Default(field.Name) is FieldEditor editor)
            {
                fields.Add(editor);
                if (field.Name is "Mesh" or "MeshIndex")
                {
                    _model.Add(editor);
                }
            }
            if (field.Name == "MeshIndex")
            {
                _notice = new ColliderNotice(shell);
                fields.Add(_notice);
            }
        }
        return fields;
    }

    public override void Update(DrawerContext<Collider> context)
    {
        if (_notice is null)
        {
            return;
        }
        _notice.Update(JsonNode.Parse(Json.Serialize(context.Value)));
        bool missing = _notice.Visibility == Visibility.Visible;
        foreach (FieldEditor editor in _model)
        {
            editor.Warns = missing;
        }
    }
}

/// <summary>
/// The Collider2D's section (ADR 0095, 0126): its generated fields, with
/// Edit Points under its Chain, turning the Edit Points tool on (or Move
/// back) while it's a chain.
/// </summary>
internal sealed class Collider2DDrawer(Shell shell) : Drawer<Collider2D>
{
    private readonly Button _editPoints = new("Edit Points") { HorizontalAlignment = HorizontalAlignment.Start };

    public override UIElement Build(DrawerContext<Collider2D> context)
    {
        StackPanel fields = new() { Spacing = 3 };
        foreach (SerializedField field in Collider2D.SerializedFields)
        {
            if (context.Default(field.Name) is { } editor)
            {
                fields.Add(editor);
            }
            if (field.Name == "Chain")
            {
                _editPoints.Clicked += _ => context.Editor.Tools.Activate(shell.Chains.IsEditing ? "Scene.Move" : "Scene.EditPoints");
                fields.Add(_editPoints);
            }
        }
        return fields;
    }

    public override void Update(DrawerContext<Collider2D> context)
    {
        _editPoints.Visibility = shell.Chains.CanEdit(context.Entity) ? Visibility.Visible : Visibility.Collapsed;
        _editPoints.Classes.Set("on", shell.Chains.IsEditing && shell.Chains.Entity.Value == context.Entity.Value);
    }
}
