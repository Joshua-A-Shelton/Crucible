using System.Drawing;
using System.Numerics;
using System.Text.Json.Nodes;
using Crucible.Assets;
using Crucible.Render;
using Crucible.Scenes;
using Crucible.UI;

namespace Crucible.Editor;

/// <summary>What an asset drag carries (ADR 0071): the asset, and its path under Assets.</summary>
public sealed record AssetDrag(AssetId Id, string Path);

/// <summary>
/// The project's assets (ADR 0071): the folders of Assets on the left, the
/// chosen folder's subfolders and files as <see cref="AssetTile"/>s on the
/// right, under a breadcrumb. A tile is selected by a click; a double-click
/// opens a folder or a scene; a drag carries an <see cref="AssetDrag"/> (a
/// model or prefab placed where it's dropped); the right-click menu
/// reimports. Read again whenever the shell's assets change.
/// </summary>
public sealed class AssetsPanel : EditorPanel
{
    private const float FolderWidth = 170;

    private readonly Shell _shell;
    private readonly TreeView _folders = new() { Width = FolderWidth };
    private readonly Label _crumb = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly TileGrid _tiles = new();
    private readonly Label _empty = new("No project open");
    private readonly Dictionary<string, ulong> _folderIds = new(StringComparer.Ordinal) { [""] = 1 };
    private readonly Dictionary<ulong, string> _folderPaths = new() { [1] = "" };
    private string _folder = "";
    private bool _stale = true;

    internal AssetsPanel(Shell shell) : base("Assets", "Assets")
    {
        _shell = shell;
        _folders.ChildrenOf = FolderChildren;
        _folders.BindRow = row => row.Text = row.Node == 1 ? "Assets" : Path.GetFileName(_folderPaths[row.Node]);
        _folders.SetExpanded(1, true);
        _folders.SelectionChanged += tree =>
        {
            if (tree.SelectedNode is { } node && _folderPaths.TryGetValue(node, out string? folder) && folder != _folder)
            {
                ShowFolder(folder);
            }
        };
        _crumb.Classes.Add("asset-crumb");
        _empty.Classes.Add("placeholder");
        DockPanel right = new();
        DockPanel.SetDock(_crumb, Dock.Top);
        right.Add(_crumb);
        right.Add(new ScrollView { Content = _tiles, Padding = new Thickness(6) });
        DockPanel page = new();
        DockPanel.SetDock(_folders, Dock.Left);
        page.Add(_folders);
        page.Add(right);
        AddChild(page);
    }

    /// <summary>The folder shown, as a path under Assets ("" for Assets itself, "Models").</summary>
    public string Folder => _folder;

    /// <summary>The selected tile's asset, if it's one the database knows.</summary>
    public AssetEntry? SelectedAsset => _tiles.Children.OfType<AssetTile>().FirstOrDefault(tile => tile.IsSelected)?.Asset;

    public IReadOnlyList<AssetTile> Tiles => [.. _tiles.Children.OfType<AssetTile>()];

    public TreeView Folders => _folders;

    internal Shell Shell => _shell;

    protected override void OnAttached(UIRoot root)
    {
        _shell.AssetsChanged += Invalidate;
        _stale = true;
    }

    protected override void OnDetached(UIRoot root) => _shell.AssetsChanged -= Invalidate;

    private void Invalidate() => _stale = true;

    /// <summary>Each frame: read again if the assets changed.</summary>
    internal void Update()
    {
        if (!_stale)
        {
            return;
        }
        _stale = false;
        string? keep = SelectedAsset?.Path;
        if (_shell.Project is null || !Directory.Exists(Path.Combine(_shell.Project.AssetsFolder, _folder)))
        {
            _folder = "";
        }
        _folders.Refresh();
        _folders.SelectedNode = FolderId(_folder);
        ShowFolder(_folder, keep);
    }

    /// <summary>Shows a folder's contents (a path under Assets), selecting <paramref name="select"/> if it's there.</summary>
    public void ShowFolder(string folder, string? select = null)
    {
        _folder = folder;
        _crumb.Text = folder.Length == 0 ? "Assets" : "Assets / " + folder.Replace("/", " / ", StringComparison.Ordinal);
        _tiles.Clear();
        if (_shell.Project is not { } project)
        {
            _tiles.Add(_empty);
            return;
        }
        string full = Path.Combine(project.AssetsFolder, folder);
        if (!Directory.Exists(full))
        {
            return;
        }
        foreach (string directory in Directory.EnumerateDirectories(full).Where(Visible).Order(StringComparer.Ordinal))
        {
            string path = Join(folder, Path.GetFileName(directory));
            _tiles.Add(new AssetTile(this, path, null, isFolder: true));
        }
        foreach (string file in Directory.EnumerateFiles(full).Where(Visible).Order(StringComparer.Ordinal))
        {
            if (file.EndsWith(Sidecar.Extension, StringComparison.Ordinal))
            {
                continue;
            }
            string path = Join(folder, Path.GetFileName(file));
            AssetTile tile = new(this, path, _shell.Database?.Find(path), isFolder: false);
            tile.IsSelected = path == select;
            _tiles.Add(tile);
        }
        _folders.SelectedNode = FolderId(folder);
    }

    internal void Select(AssetTile tile)
    {
        foreach (AssetTile other in _tiles.Children.OfType<AssetTile>())
        {
            other.IsSelected = other == tile;
        }
        AssetEntry? asset = tile.Asset;
        _shell.InspectedAsset = asset?.Id ?? AssetId.None;
        _shell.Status(tile.IsFolder ? tile.Path
            : asset is null ? $"{tile.Path} (not an asset)"
            : $"{tile.Path} — {(asset.Importer.Length > 0 ? asset.Importer : "no importer")}" +
              (_shell.Database?.ImportedFile(asset.Id) is null && asset.Importer.Length > 0 ? ", not imported" : ""));
    }

    internal void Open(AssetTile tile)
    {
        if (tile.IsFolder)
        {
            ExpandTo(tile.Path);
            ShowFolder(tile.Path);
        }
        else if (tile.Path.EndsWith(Scene.Extension, StringComparison.OrdinalIgnoreCase) && _shell.Project is { } project)
        {
            string file = Path.Combine(project.AssetsFolder, tile.Path);
            _shell.ConfirmDiscard("opening another scene", () => _shell.OpenScene(file));
        }
    }

    internal ContextMenu ShowMenu(AssetTile tile, Vector2 at)
    {
        ContextMenu menu = new();
        if (tile.IsFolder || tile.Path.EndsWith(Scene.Extension, StringComparison.OrdinalIgnoreCase))
        {
            menu.Add("Open", () => Open(tile));
        }
        if (tile.Asset is { Importer.Length: > 0 } asset)
        {
            menu.Add("Reimport", () => _shell.Reimport(asset.Id));
            // Its settings in the inspector (ADR 0087); a model's Collision
            // also here, applied and reimported at once.
            if (_shell.Database?.SettingsOf(asset.Id) is { } settings)
            {
                menu.Add("Import Settings…", () => Select(tile));
                if (settings["Collision"] is JsonValue current && current.TryGetValue(out string? chosen))
                {
                    Menu collision = new();
                    foreach (string kind in (string[])["None", "Mesh", "Convex"])
                    {
                        MenuItem item = collision.Add(kind, () =>
                        {
                            settings["Collision"] = kind;
                            _shell.ApplyImportSettings(asset.Id, settings);
                        });
                        item.IsChecked = kind == chosen;
                    }
                    menu.Add(new MenuItem("Collision") { Submenu = collision });
                }
            }
        }
        menu.Add("Refresh", _shell.RefreshAssets);
        menu.Open(tile, at);
        return menu;
    }

    private void ExpandTo(string folder)
    {
        string path = "";
        _folders.SetExpanded(1, true);
        foreach (string part in folder.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            path = Join(path, part);
            _folders.SetExpanded(FolderId(path), true);
        }
    }

    private IReadOnlyList<ulong> FolderChildren(ulong? node)
    {
        if (node is null)
        {
            return _shell.Project is null ? [] : [1];
        }
        if (_shell.Project is not { } project || !_folderPaths.TryGetValue(node.Value, out string? folder))
        {
            return [];
        }
        string full = Path.Combine(project.AssetsFolder, folder);
        return Directory.Exists(full)
            ? [.. Directory.EnumerateDirectories(full).Where(Visible).Order(StringComparer.Ordinal)
                .Select(directory => FolderId(Join(folder, Path.GetFileName(directory))))]
            : [];
    }

    // Folders keep their node key while the panel lives, so expansion survives refreshes.
    private ulong FolderId(string folder)
    {
        if (!_folderIds.TryGetValue(folder, out ulong id))
        {
            id = (ulong)_folderIds.Count + 1;
            _folderIds[folder] = id;
            _folderPaths[id] = folder;
        }
        return id;
    }

    private static string Join(string folder, string name) => folder.Length == 0 ? name : folder + "/" + name;

    private static bool Visible(string path)
    {
        string name = Path.GetFileName(path);
        return !name.StartsWith('.') && !name.Contains(".tmp-", StringComparison.Ordinal);
    }

    // Tiles in rows as wide as there's room for.
    private sealed class TileGrid : UIElement
    {
        private const float Gap = 6;

        public void Add(UIElement tile) => AddChild(tile);

        public void Clear() => ClearChildren();

        protected override Vector2 MeasureCore(Vector2 available)
        {
            float width = float.IsFinite(available.X) ? available.X : 600;
            float x = 0, y = 0, row = 0, widest = 0;
            foreach (UIElement child in Children)
            {
                child.Measure(new Vector2(width, float.PositiveInfinity));
                Vector2 size = child.DesiredSize;
                if (x > 0 && x + size.X > width)
                {
                    x = 0;
                    y += row + Gap;
                    row = 0;
                }
                x += size.X + Gap;
                widest = MathF.Max(widest, x);
                row = MathF.Max(row, size.Y);
            }
            return new Vector2(MathF.Min(widest, width), y + row);
        }

        protected override void ArrangeCore(RectangleF content)
        {
            float x = 0, y = 0, row = 0;
            foreach (UIElement child in Children)
            {
                Vector2 size = child.DesiredSize;
                if (x > 0 && x + size.X > content.Width)
                {
                    x = 0;
                    y += row + Gap;
                    row = 0;
                }
                child.Arrange(new RectangleF(content.X + x, content.Y + y, size.X, size.Y));
                x += size.X + Gap;
                row = MathF.Max(row, size.Y);
            }
        }
    }
}

/// <summary>
/// One folder or file in the <see cref="AssetsPanel"/> (ADR 0071): an icon
/// over its name. A texture shows itself; other kinds a drawn icon. It has
/// the class <c>selected</c> while selected.
/// </summary>
public sealed class AssetTile : UIElement
{
    private const float DragThreshold = 4;

    private readonly AssetsPanel _panel;
    private readonly Icon _icon;
    private Vector2? _pressedAt;
    private bool _selected;

    internal AssetTile(AssetsPanel panel, string path, AssetEntry? asset, bool isFolder)
    {
        _panel = panel;
        Path = path;
        Asset = asset;
        IsFolder = isFolder;
        IsFocusable = true;
        _icon = new Icon(this);
        Label name = new(System.IO.Path.GetFileName(path)) { Wrap = true, HorizontalAlignment = HorizontalAlignment.Center };
        name.Classes.Add("asset-name");
        StackPanel body = new() { Spacing = 4, Width = 84 };
        body.Add(_icon);
        body.Add(name);
        AddChild(body);
    }

    /// <summary>Its path under Assets.</summary>
    public string Path { get; }

    /// <summary>The database's entry for it; null for a folder, or a file that isn't an asset.</summary>
    public AssetEntry? Asset { get; }

    public bool IsFolder { get; }

    public bool IsSelected
    {
        get => _selected;
        set
        {
            _selected = value;
            Classes.Set("selected", value);
        }
    }

    /// <summary>Its kind, as the icon shows it: Folder, Model, Prefab, Texture, Scene, Shader, Font, Style, Material, or File.</summary>
    public string Kind =>
        IsFolder ? "Folder"
        : Path.EndsWith(Prefab.Extension, StringComparison.OrdinalIgnoreCase) ? "Prefab"
        : Path.EndsWith(Scene.Extension, StringComparison.OrdinalIgnoreCase) ? "Scene"
        : Asset?.Importer switch
        {
            "Mesh" or "Fbx" => "Model",
            "Texture" or "Shader" or "Font" or "Style" or "Material" => Asset.Importer,
            _ => "File",
        };

    /// <summary>Whether dropping it in the scene places an instance (a model or a prefab).</summary>
    public bool CanPlace => Asset is not null && Kind is "Model" or "Prefab";

    protected override void OnPointerDown(PointerEvent e)
    {
        e.Handled = true;
        if (e.Button == PointerButton.Right)
        {
            _panel.Select(this);
            _panel.ShowMenu(this, e.Position);
            return;
        }
        if (e.Button != PointerButton.Left)
        {
            return;
        }
        _panel.Select(this);
        _pressedAt = e.Position;
        if (e.ClickCount == 2)
        {
            _pressedAt = null;
            _panel.Open(this);
        }
    }

    protected override void OnPointerMove(PointerEvent e)
    {
        if (_pressedAt is not { } from || Vector2.Distance(from, e.Position) < DragThreshold || Asset is null || Root is not { } root)
        {
            return;
        }
        _pressedAt = null;
        Label preview = new(System.IO.Path.GetFileName(Path));
        preview.Classes.Add("drag-preview");
        root.BeginDrag(new AssetDrag(Asset.Id, Path), preview);
    }

    protected override void OnPointerUp(PointerEvent e) => _pressedAt = null;

    protected override void OnActivate(UIEvent e)
    {
        e.Handled = true;
        _panel.Select(this);
    }

    // The icon: a texture's own image, or a drawing by kind.
    private sealed class Icon : UIElement
    {
        private readonly AssetTile _tile;
        private TextureHandle _texture;

        public Icon(AssetTile tile)
        {
            _tile = tile;
            Width = 64;
            Height = 64;
            HorizontalAlignment = HorizontalAlignment.Center;
            IsHitTestVisible = false;
        }

        protected override void OnAttached(UIRoot root)
        {
            if (_tile.Kind == "Texture" && _tile.Asset is { } asset &&
                _tile._panel.Shell.Database?.ImportedFile(asset.Id) is not null)
            {
                _texture = RenderAssets.AcquireTexture(asset.Id);
            }
        }

        protected override void OnDetached(UIRoot root)
        {
            if (_texture.Generation != 0)
            {
                RenderAssets.ReleaseTexture(_texture);
                _texture = default;
            }
        }

        protected override void OnDraw(Canvas canvas)
        {
            Vector2 size = ActualSize;
            Vector4 line = Foreground, fill = Foreground with { W = 0.18f };
            if (_texture.Generation != 0)
            {
                canvas.DrawImage(_texture, new Vector2(4), size - new Vector2(8));
                return;
            }
            switch (_tile.Kind)
            {
                case "Folder":
                    canvas.FillRect(new Vector2(6, 14), new Vector2(24, 8), fill, 2);
                    canvas.FillRect(new Vector2(6, 20), new Vector2(52, 34), fill, 3);
                    canvas.StrokeRect(new Vector2(6, 20), new Vector2(52, 34), line, 1.5f, new Vector4(3));
                    break;
                case "Model" or "Prefab":
                    {
                        // A cube: front square, top and side faces.
                        Vector2 a = new(14, 24), b = new(42, 24), c = new(42, 52), d = new(14, 52), up = new(10, -10);
                        if (_tile.Kind == "Prefab")
                        {
                            canvas.FillRect(a, c - a, fill);
                        }
                        canvas.DrawPolyline([a, b, c, d, a], 1.5f, line);
                        canvas.DrawPolyline([a, a + up, b + up, b], 1.5f, line);
                        canvas.DrawPolyline([b + up, c + up, c], 1.5f, line);
                        break;
                    }
                case "Scene":
                    canvas.StrokeRect(new Vector2(8, 12), new Vector2(48, 40), line, 1.5f, new Vector4(3));
                    canvas.DrawPolyline([new(10, 48), new(24, 30), new(34, 40), new(42, 32), new(54, 48)], 1.5f, line);
                    canvas.FillRect(new Vector2(40, 18), new Vector2(7, 7), line, 3.5f);
                    break;
                default:
                    {
                        // A page with its corner turned, and the kind's initial.
                        canvas.DrawPolyline([new(16, 8), new(40, 8), new(50, 18), new(50, 56), new(16, 56), new(16, 8)], 1.5f, line);
                        canvas.DrawPolyline([new(40, 8), new(40, 18), new(50, 18)], 1.5f, line);
                        float y = 28;
                        for (int i = 0; i < 3; ++i, y += 8)
                        {
                            canvas.FillRect(new Vector2(22, y), new Vector2(i == 2 ? 14 : 22, 2), fill with { W = 0.5f });
                        }
                        break;
                    }
            }
        }
    }
}
