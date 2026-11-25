using System.Numerics;
using Crucible.Assets;
using Crucible.Assets.Importers;
using Crucible.Editor;
using Crucible.Render;
using Crucible.UI;

namespace Crucible.Tilemaps.Editor;

/// <summary>
/// New Tileset (ADR 0112): the one way a tileset is made. An image chosen
/// from the project's textures (or given: dropped on the Palette, chosen in
/// the Assets panel), its grid set by Tile Width, Tile Height, Margin, and
/// Spacing and seen over it; Create makes the tileset beside the image,
/// making the image a sprite if it isn't one, and in map mode adds it to the
/// map. In map mode the grid starts at the map's tile size and must stay it.
/// Editing a tileset, it changes that tileset's grid instead (Save).
/// </summary>
public sealed class TilesetDialog : Popup
{
    private readonly MapMode _maps;
    private readonly AssetId _editing;
    private readonly ComboBox _image = new() { MinWidth = 260 };
    private readonly NumberField _width = Whole(1), _height = Whole(1), _margin = Whole(0), _spacing = Whole(0);
    private readonly Label _note = new() { Wrap = true };
    private readonly Button _create;
    private readonly List<AssetId> _textures = [];
    private readonly TilesetData _grid = new();
    private TextureHandle _handle;
    private AssetId _texture;
    private bool _answered;

    internal TilesetDialog(MapMode maps, AssetId texture, AssetId editing)
    {
        _maps = maps;
        _editing = editing;
        IsModal = true;
        _note.Classes.Add("hint");
        Preview = new TileGridView { Width = 420, Height = 300 };
        _create = new Button(editing.IsNone ? "Create" : "Save");
        _create.Classes.Add("primary");
        Button cancel = new("Cancel");
        _create.Clicked += _ => Answer(true);
        cancel.Clicked += _ => Answer(false);

        TilesetData? edited = editing.IsNone ? null : maps.ReadTileset(editing);
        foreach (AssetEntry entry in (maps.Database?.Assets ?? []).Where(entry => entry.Importer == "Texture").OrderBy(entry => entry.Path))
        {
            _textures.Add(entry.Id);
        }
        _image.Options = [.. _textures.Select(id => maps.Database?.Find(id)?.Path ?? "")];
        _image.SelectionChanged += box =>
        {
            if (box.SelectedIndex >= 0 && box.SelectedIndex < _textures.Count)
            {
                UseTexture(_textures[box.SelectedIndex], resetGrid: true);
            }
        };
        foreach (NumberField field in (NumberField[])[_width, _height, _margin, _spacing])
        {
            field.ValueChanged += _ => GridChanged();
        }

        StackPanel fields = new() { Orientation = Orientation.Horizontal, Spacing = 6 };
        foreach ((string label, NumberField field) in new[]
                 {
                     ("Tile Width", _width), ("Tile Height", _height), ("Margin", _margin), ("Spacing", _spacing),
                 })
        {
            fields.Add(new Label(label) { VerticalAlignment = VerticalAlignment.Center });
            fields.Add(field);
        }
        StackPanel image = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
        image.Add(new Label("Image") { VerticalAlignment = VerticalAlignment.Center });
        image.Add(_image);
        StackPanel buttons = new() { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.End };
        buttons.Add(cancel);
        buttons.Add(_create);
        StackPanel body = new() { Spacing = 10 };
        body.Classes.Add("dialog");
        body.Add(new Label(editing.IsNone ? "New Tileset" : $"Tileset {maps.Database?.Find(editing)?.Path}") { Classes = { "heading" } });
        if (editing.IsNone)
        {
            body.Add(image);
        }
        body.Add(fields);
        body.Add(Preview);
        body.Add(_note);
        body.Add(buttons);
        Content = body;
        Closed += _ =>
        {
            ReleaseTexture();
            if (!_answered)
            {
                _answered = true;
                Done?.Invoke(this, AssetId.None);
            }
        };

        if (edited is not null)
        {
            _width.Value = edited.TileWidth;
            _height.Value = edited.TileHeight;
            _margin.Value = edited.Margin;
            _spacing.Value = edited.Spacing;
            UseTexture(edited.Texture, resetGrid: false);
        }
        else if (!texture.IsNone)
        {
            UseTexture(texture, resetGrid: true);
        }
        else
        {
            GridChanged();
        }
    }

    /// <summary>The image with the grid over it.</summary>
    internal TileGridView Preview { get; }

    /// <summary>The image chosen; none yet.</summary>
    public AssetId Texture => _texture;

    /// <summary>The tileset made or edited when Create (or Save) is chosen; none when canceled.</summary>
    public event Action<TilesetDialog, AssetId>? Done;

    /// <summary>The grid's fields, for the checks.</summary>
    internal (NumberField Width, NumberField Height, NumberField Margin, NumberField Spacing) Fields => (_width, _height, _margin, _spacing);

    /// <summary>An image chosen: shown, and the grid started from its sprite sheet (or the map's tile size).</summary>
    public void UseTexture(AssetId texture, bool resetGrid)
    {
        _texture = texture;
        _image.SelectedIndex = _textures.IndexOf(texture);
        if (resetGrid)
        {
            (int width, int height, int margin, int spacing) = _maps.GridFor(texture);
            _width.Value = width;
            _height.Value = height;
            _margin.Value = margin;
            _spacing.Value = spacing;
        }
        ReleaseTexture();
        if (!texture.IsNone && _maps.Database?.ImportedFile(texture) is null)
        {
            // An image that doesn't import as it's set (one whose size block
            // compression can't take): a sprite, which a tileset needs anyway.
            _maps.MakeSprite(texture);
        }
        if (!texture.IsNone && _maps.Database?.ImportedFile(texture) is not null)
        {
            _handle = RenderAssets.AcquireTexture(texture);
        }
        Preview.Texture = _handle;
        Preview.FitNextDraw();
        GridChanged();
    }

    // The grid read from the fields: shown over the image, and what's wrong with it said.
    private void GridChanged()
    {
        _grid.TileWidth = Math.Max((int)_width.Value, 1);
        _grid.TileHeight = Math.Max((int)_height.Value, 1);
        _grid.Margin = Math.Max((int)_margin.Value, 0);
        _grid.Spacing = Math.Max((int)_spacing.Value, 0);
        Preview.Tileset = _grid;
        Preview.InvalidateVisual();
        string? problem = Problem();
        _note.Text = problem ?? (_texture.IsNone
            ? ""
            : _maps.Database?.ImportedFile(_texture) is null
                ? "The image doesn't import: see the Log."
                : $"{Preview.Grid.Columns} by {Preview.Grid.Rows} tiles.");
        _create.State = (_create.State & ~UIState.Disabled) | (problem is null ? 0 : UIState.Disabled);
    }

    // Why Create can't be chosen; null if it can.
    private string? Problem()
    {
        if (_texture.IsNone)
        {
            return "Choose an image.";
        }
        if (_editing.IsNone && _maps.InMapMode && _maps.Map.TileSize is var (width, height) &&
            (_grid.TileWidth != width || _grid.TileHeight != height))
        {
            return $"This map's tiles are {width}x{height}: a tileset added to it needs tiles that size.";
        }
        (int imageWidth, int imageHeight) = Preview.TextureSize;
        if (imageWidth > 0 && _grid.Grid(imageWidth, imageHeight) is var (columns, rows) && (columns == 0 || rows == 0))
        {
            return $"{_grid.TileWidth}x{_grid.TileHeight} tiles don't fit the {imageWidth}x{imageHeight} image.";
        }
        return null;
    }

    /// <summary>Create (or Save) when <paramref name="create"/>, else Cancel.</summary>
    public void Answer(bool create)
    {
        GridChanged();
        if (_answered || (create && Problem() is not null))
        {
            return;
        }
        _answered = true;
        AssetId made = AssetId.None;
        if (create && !_editing.IsNone)
        {
            (int width, int height, int margin, int spacing) = (_grid.TileWidth, _grid.TileHeight, _grid.Margin, _grid.Spacing);
            if (_maps.EditTileset(_editing, "Tile Grid", t => (t.TileWidth, t.TileHeight, t.Margin, t.Spacing) = (width, height, margin, spacing)))
            {
                made = _editing;
            }
        }
        else if (create)
        {
            _maps.History.BeginGroup("New Tileset");
            made = _maps.CreateTileset(_texture, _grid.TileWidth, _grid.TileHeight, _grid.Margin, _grid.Spacing);
            if (!made.IsNone && _maps.InMapMode)
            {
                _maps.AddTilesetToMap(made);
            }
            _maps.History.EndGroup();
        }
        Close();
        Done?.Invoke(this, made);
    }

    private void ReleaseTexture()
    {
        if (_handle.Generation != 0)
        {
            RenderAssets.ReleaseTexture(_handle);
            _handle = default;
            Preview.Texture = default;
        }
    }

    private static NumberField Whole(int least) => new() { MinWidth = 0, Width = 56, Minimum = least, Step = 1 };
}
