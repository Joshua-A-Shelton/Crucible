using Crucible.Assets.Importers;
using Crucible.Editor;
using Crucible.UI;

namespace Crucible.Tilemaps.Editor;

/// <summary>
/// Layers (ADR 0112): map mode's list of the map's layers, the top one
/// first, as a map editor shows them. Clicking a layer makes it the one the
/// tools paint on; Visible shows or hides it (in the game too), Collides
/// gives its entity a Collider2D of shape Tilemap or takes it away, and
/// double-clicking its name renames it. Add Layer puts one above the layer
/// being edited; Up and Down move it; Delete deletes it. A layer whose
/// entity was deleted or moved away outside map mode is offered back:
/// Re-create, or Delete Layer.
/// </summary>
public sealed class LayersPanel : EditorPanel
{
    private readonly MapMode _maps;
    private readonly StackPanel _missing = new() { Spacing = 4, Margin = new Thickness(8, 6, 8, 0) };
    private readonly StackPanel _rows = new() { Spacing = 2, Margin = new Thickness(6, 4, 6, 4) };
    private readonly Label _hint = new() { Wrap = true, Margin = new Thickness(8, 4, 8, 6) };
    private string _shown = "";
    private RenameField? _renaming;

    internal LayersPanel(MapMode maps) : base("Layers", "Layers")
    {
        _maps = maps;
        _hint.Classes.Add("hint");
        Button add = EditorIcons.Button("plus", "Add Layer: above the one being painted");
        Button up = EditorIcons.Button("arrow-up", "Move Up: drawn over the layer above");
        Button down = EditorIcons.Button("arrow-down", "Move Down: drawn under the layer below");
        Button delete = EditorIcons.Button("trash-2", "Delete Layer");
        add.Clicked += _ => _maps.AddLayer();
        up.Clicked += _ => _maps.MoveLayer(_maps.Map.ActiveLayer, +1);
        down.Clicked += _ => _maps.MoveLayer(_maps.Map.ActiveLayer, -1);
        delete.Clicked += _ => _maps.DeleteLayer(_maps.Map.ActiveLayer);
        // One row of icons, which fits a narrow panel.
        StackPanel bar = new() { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(6, 4, 6, 4) };
        foreach (Button button in (Button[])[add, up, down, delete])
        {
            bar.Add(button);
        }
        StackPanel head = new();
        head.Add(_missing);
        head.Add(bar);
        DockPanel.SetDock(head, Dock.Top);
        DockPanel.SetDock(_hint, Dock.Bottom);
        DockPanel layout = new();
        layout.Add(head);
        layout.Add(_hint);
        ScrollView scroll = new() { Content = _rows };
        layout.Add(scroll);
        AddChild(layout);
        _maps.LayersChanged += () => _shown = "";
        _maps.AssetsChanged += () => _shown = "";
    }

    /// <summary>Each frame: the rows made again when the layers, their names, or their switches changed.</summary>
    public override void Tick()
    {
        MapEditor map = _maps.Map;
        string key = map.IsOpen
            ? string.Join(";", map.Data.Layers.Select(layer =>
                  $"{layer.Id},{layer.Name},{_maps.IsLayerVisible(layer.Id)},{_maps.LayerCollides(layer.Id)},{_maps.LayerEntity(map.Root, layer.Id) is not null}")) +
              $"|{map.ActiveLayer}"
            : "";
        if (key != _shown && _renaming is null)
        {
            _shown = key;
            Rebuild();
        }
    }

    private void Rebuild()
    {
        MapEditor map = _maps.Map;
        _rows.Clear();
        _missing.Clear();
        if (!map.IsOpen)
        {
            _hint.Text = "The map's layers show here in map mode.";
            return;
        }
        foreach (TileLayer layer in _maps.MissingLayers().ToList())
        {
            int id = layer.Id;
            Label message = new($"{layer.Name} has no entity") { Wrap = true };
            Button again = new("Re-create"), drop = new("Delete Layer");
            again.Clicked += _ => _maps.RecreateLayerEntity(id);
            drop.Clicked += _ => _maps.DeleteMissingLayer(id);
            StackPanel row = new() { Orientation = Orientation.Horizontal, Spacing = 6 };
            row.Add(again);
            row.Add(drop);
            _missing.Add(message);
            _missing.Add(row);
        }
        for (int i = map.Data.Layers.Count - 1; i >= 0; --i)
        {
            _rows.Add(Row(map.Data.Layers[i]));
        }
        _hint.Text = "Click a layer to paint on it; double-click its name to rename it. The eye hides it in the game too.";
    }

    private UIElement Row(TileLayer layer)
    {
        int id = layer.Id;
        bool missing = _maps.LayerEntity(_maps.Map.Root, id) is null;
        bool shown = _maps.IsLayerVisible(id);
        Toggle visible = EditorIcons.Toggle(shown ? "eye" : "eye-off", "Visible: shown, in the game too");
        visible.IsOn = shown;
        Toggle collides = EditorIcons.Toggle("brick-wall", "Collides: a collider of its tiles' shapes");
        collides.IsOn = _maps.LayerCollides(id);
        // Quiet: their icons light up when on, without a fill (ADR 0133), as a row has two.
        visible.Classes.Add("quiet");
        collides.Classes.Add("quiet");
        visible.Changed += toggle => _maps.SetLayerVisible(id, toggle.IsOn);
        collides.Changed += toggle => _maps.SetLayerCollides(id, toggle.IsOn);
        if (missing)
        {
            visible.State |= UIState.Disabled;
            collides.State |= UIState.Disabled;
        }
        LayerName name = new(layer.Name) { VerticalAlignment = VerticalAlignment.Center };
        name.Pressed += () => Activate(id);
        name.DoublePressed += () => BeginRename(name, layer);
        DockPanel row = new() { Margin = new Thickness(2) };
        StackPanel switches = new() { Orientation = Orientation.Horizontal, Spacing = 4 };
        switches.Add(visible);
        switches.Add(collides);
        DockPanel.SetDock(switches, Dock.Right);
        row.Add(switches);
        row.Add(name);
        row.Classes.Set("selected", id == _maps.Map.ActiveLayer);
        name.Classes.Set("primary", id == _maps.Map.ActiveLayer);
        return row;
    }

    /// <summary>A layer made the one the tools paint on.</summary>
    public void Activate(int id)
    {
        if (_maps.Map.Data.Layer(id) is not null)
        {
            _maps.Map.ActiveLayer = id;
            _shown = "";
        }
    }

    // Its name as a field to type a new one in; Enter (or leaving it) renames.
    private void BeginRename(LayerName label, TileLayer layer)
    {
        int id = layer.Id;
        RenameField field = new(layer.Name);
        _renaming = field;
        bool done = false;
        void Finish()
        {
            if (done)
            {
                return;
            }
            done = true;
            _renaming = null;
            _maps.RenameLayer(id, field.Text);
            _shown = "";
        }
        field.Committed += _ => Finish();
        field.Ended += Finish;
        if (label.Parent is Panel row)
        {
            row.Remove(label);
            row.Add(field);
            field.Focus();
            field.SelectAll();
        }
    }

    /// <summary>A layer renamed as typing its new name and pressing Enter would (the checks).</summary>
    internal void Rename(int id, string name) => _maps.RenameLayer(id, name);
}

/// <summary>A layer's name in the Layers panel: pressed (activate), pressed twice quickly (rename).</summary>
internal sealed class LayerName(string text) : Label(text)
{
    private double _last = double.NegativeInfinity;

    public event Action? Pressed;

    public event Action? DoublePressed;

    protected override void OnPointerDown(PointerEvent e)
    {
        if (e.Button != PointerButton.Left)
        {
            return;
        }
        e.Handled = true;
        double now = Environment.TickCount64 / 1000.0;
        if (now - _last < 0.4)
        {
            DoublePressed?.Invoke();
            _last = double.NegativeInfinity;
            return;
        }
        _last = now;
        Pressed?.Invoke();
    }
}

/// <summary>A layer's name being typed: ends (renaming, or not if unchanged) when it loses focus.</summary>
internal sealed class RenameField(string text) : TextField(text)
{
    public event Action? Ended;

    protected override void OnLostFocus(UIEvent e)
    {
        base.OnLostFocus(e);
        Ended?.Invoke();
    }
}
