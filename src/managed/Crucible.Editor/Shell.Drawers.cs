using Crucible.Serialization;

namespace Crucible.Editor;

/// <summary>
/// Custom inspectors (ADR 0126): the drawers extensions registered, found
/// for a component, a field's type, or a field's [Drawer] name; the
/// Inspector made again when they change, so a reloaded extension's drawers
/// replace the old ones (and the old code can unload).
/// </summary>
internal sealed partial class Shell
{
    private readonly List<DrawerRegistration> _drawers = [];

    /// <summary>Changes when drawers are added or taken out (the Inspector rebuilds).</summary>
    internal int DrawersVersion { get; private set; }

    // The editor's own drawers (ADR 0126), on the same API as extensions':
    // the Collider's missing-collision notice, the Collider2D's Edit Points.
    private void RegisterDrawers()
    {
        EditorContext editor = new(this, "Crucible.Editor");
        editor.Inspector.AddComponentDrawer(() => new ColliderDrawer(this));
        editor.Inspector.AddComponentDrawer(() => new Collider2DDrawer(this));
    }

    internal void AddDrawer(DrawerRegistration drawer)
    {
        _drawers.Add(drawer);
        ++DrawersVersion;
        drawer.Owner.OnStop(() =>
        {
            _drawers.Remove(drawer);
            ++DrawersVersion;
        });
    }

    /// <summary>The drawer for a component's section, if one is registered (the last wins).</summary>
    internal DrawerRegistration? ComponentDrawer(Type component) =>
        _drawers.LastOrDefault(drawer => drawer.Target == DrawerTarget.Component && drawer.Type == component);

    /// <summary>The drawer for a field: its [Drawer] name's, else its type's; null for the generated editor.</summary>
    internal DrawerRegistration? FieldDrawer(SerializedField field)
    {
        if (field.Drawer is { } name)
        {
            if (_drawers.LastOrDefault(drawer => drawer.Target == DrawerTarget.Named && drawer.Name == name) is { } named)
            {
                return named;
            }
        }
        if (field.Kind != FieldKind.Serialized)
        {
            return null;
        }
        IReadOnlyList<SerializedField> table = field.Fields;
        return _drawers.LastOrDefault(drawer => drawer.Target == DrawerTarget.FieldType && ReferenceEquals(drawer.Table, table));
    }

    // A drawer's own code throwing: logged, and the generated fields shown instead.
    internal bool GuardDrawer(DrawerRegistration drawer, string what, Action run)
    {
        try
        {
            run();
            return true;
        }
        catch (Exception exception)
        {
            LogError($"{drawer.Owner.Extension}: {drawer.Type.Name}'s drawer: {what} failed: {Message(exception)}");
            return false;
        }
    }
}
