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

/// <summary>What a folder drag carries: its path under Assets (the Build Settings window's include list takes it).</summary>
public sealed record FolderDrag(string Path);

/// <summary>
/// The project's assets (ADR 0071): the folders of Assets, and of each
/// plugin's (ADR 0118), on the left, the
/// chosen folder's subfolders and files as <see cref="AssetTile"/>s on the
/// right, under a breadcrumb. A tile is selected by a click; a double-click
/// opens a folder or a scene; a drag carries an <see cref="AssetDrag"/> (a
/// model or prefab placed where it's dropped); the right-click menu
/// reimports. Read again whenever the shell's assets change.
/// </summary>
public sealed class AssetsPanel : EditorPanel
{
    private const float FolderWidth = 170;

    /// <summary>The tile size's range (ADR 0132): the picture's width and height.</summary>
    public const float MinTileSize = 48, MaxTileSize = 160;

    private readonly Shell _shell;
    private readonly TreeView _folders = new() { Width = FolderWidth };
    private readonly Label _crumb = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly Slider _size = new() { Minimum = MinTileSize, Maximum = MaxTileSize, Step = 8, Width = 96, VerticalAlignment = VerticalAlignment.Center };
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
        _folders.BindRow = row => row.Text = RowText(_folderPaths[row.Node]);
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
        _size.Value = shell.Settings.AssetTileSize;
        _size.Classes.Add("tile-size");
        _size.ToolTip = "Tile size";
        _size.ValueChanged += slider => TileSize = slider.Value;
        DockPanel bar = new();
        DockPanel.SetDock(_size, Dock.Right);
        bar.Add(_size);
        bar.Add(_crumb);
        DockPanel right = new();
        DockPanel.SetDock(bar, Dock.Top);
        right.Add(bar);
        right.Add(new ScrollView { Content = _tiles, Padding = new Thickness(6) });
        DockPanel page = new();
        DockPanel.SetDock(_folders, Dock.Left);
        page.Add(_folders);
        page.Add(right);
        AddChild(page);
        AllowDrop = true;
    }

    // Files from other programs (ADR 0115): into the folder shown, outlined.
    protected override void OnDragOver(DragEvent e)
    {
        if (e.Payload is FileDrag files)
        {
            e.Accepted = true;
            files.Folder = _folder;
            Classes.Add("drop-over");
        }
    }

    protected override void OnDragLeave(DragEvent e) => Classes.Remove("drop-over");

    protected override void OnDrop(DragEvent e) => Classes.Remove("drop-over");

    /// <summary>
    /// The tiles' size (ADR 0132), <see cref="MinTileSize"/> to
    /// <see cref="MaxTileSize"/>: its pictures' width and height, kept in the
    /// person's editor settings.
    /// </summary>
    public float TileSize
    {
        get => _shell.Settings.AssetTileSize;
        set
        {
            float size = Math.Clamp(MathF.Round(value), MinTileSize, MaxTileSize);
            if (size == _shell.Settings.AssetTileSize)
            {
                return;
            }
            _shell.Settings.AssetTileSize = size;
            _size.Value = size;
            foreach (AssetTile tile in Tiles)
            {
                tile.Resize(size);
            }
            _shell.SaveSettings();
        }
    }

    /// <summary>The folder shown, as a path under Assets ("" for Assets itself, "Models"; "Dialogue:", "Dialogue:Icons" in a plugin's).</summary>
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
        if (_shell.Project is null || !Directory.Exists(_shell.Project.FileOf(_folder)))
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
        string within = AssetPaths.WithinRoot(folder);
        string root = AssetPaths.PluginOf(folder) ?? "Assets";
        _crumb.Text = within.Length == 0 ? root : root + " / " + within.Replace("/", " / ", StringComparison.Ordinal);
        _tiles.Clear();
        if (_shell.Project is not { } project)
        {
            _tiles.Add(_empty);
            return;
        }
        string full = project.FileOf(folder);
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
            string file = project.FileOf(tile.Path);
            _shell.ConfirmDiscard("opening another scene", () => _shell.OpenScene(file));
        }
        else if (tile.Asset is { } asset)
        {
            // Its editor (ADR 0120): the one there is, the one always chosen, else asked.
            _shell.OpenAsset(asset);
        }
    }

    internal ContextMenu ShowMenu(AssetTile tile, Vector2 at)
    {
        ContextMenu menu = new();
        if (tile.IsFolder || tile.Path.EndsWith(Scene.Extension, StringComparison.OrdinalIgnoreCase))
        {
            menu.Add("Open", () => Open(tile));
        }
        // Its editors (ADR 0120): "Open in …", or Open With.
        if (tile.Asset is { } editable)
        {
            _shell.AddOpenItems(menu, editable);
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
                    menu.Add(new MenuItem("Generate Collision") { Submenu = collision });
                }
            }
        }
        // New assets beside it, or in it if it's a folder.
        string folder = tile.IsFolder ? tile.Path : AssetPaths.FolderOf(tile.Path);
        Menu create = new();
        create.Add("Chain", () => _shell.CreateChain(folder));
        // Extensions' (ADR 0123): the Tilemaps plugin's Tileset…, say.
        _shell.AddAssetCreateItems(create, folder, tile.IsFolder ? null : tile.Asset);
        menu.Add(new MenuItem("Create") { Submenu = create });
        menu.Add("Refresh", _shell.RefreshAssets);
        menu.Open(tile, at);
        return menu;
    }

    private void ExpandTo(string folder)
    {
        string path = AssetPaths.RootOf(folder);
        _folders.SetExpanded(FolderId(path), true);
        foreach (string part in AssetPaths.WithinRoot(folder).Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            path = Join(path, part);
            _folders.SetExpanded(FolderId(path), true);
        }
    }

    private IReadOnlyList<ulong> FolderChildren(ulong? node)
    {
        if (node is null)
        {
            // Assets, then each plugin's (ADR 0118).
            return _shell.Project is not { } open ? [] : [1, .. open.Plugins.Select(plugin => FolderId(plugin.AssetRoot))];
        }
        if (_shell.Project is not { } project || !_folderPaths.TryGetValue(node.Value, out string? folder))
        {
            return [];
        }
        string full = project.FileOf(folder);
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

    private static string Join(string folder, string name) => AssetPaths.Join(folder, name);

    // A folder's row: "Assets", a plugin's name, or the folder's own name.
    private static string RowText(string folder) =>
        folder.Length == 0 ? "Assets"
        : AssetPaths.WithinRoot(folder).Length == 0 ? AssetPaths.PluginOf(folder)!
        : Path.GetFileName(AssetPaths.WithinRoot(folder));

    internal static bool Visible(string path)
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
    private readonly Picture _picture;
    private readonly StackPanel _body;
    private Vector2? _pressedAt;
    private bool _selected;

    internal AssetTile(AssetsPanel panel, string path, AssetEntry? asset, bool isFolder)
    {
        _panel = panel;
        Path = path;
        Asset = asset;
        IsFolder = isFolder;
        IsFocusable = true;
        _picture = new Picture(this);
        Label name = new(System.IO.Path.GetFileName(path)) { Wrap = true, HorizontalAlignment = HorizontalAlignment.Center };
        name.Classes.Add("asset-name");
        _body = new StackPanel { Spacing = 4, Width = panel.TileSize + 20 };
        _body.Add(_picture);
        _body.Add(name);
        AddChild(_body);
        AllowDrop = isFolder;
    }

    // Files from other programs dropped on a folder go into it (ADR 0115).
    protected override void OnDragOver(DragEvent e)
    {
        if (e.Payload is FileDrag files && IsFolder)
        {
            e.Accepted = true;
            files.Folder = Path;
            Classes.Add("drop-over");
        }
    }

    protected override void OnDragLeave(DragEvent e) => Classes.Remove("drop-over");

    protected override void OnDrop(DragEvent e) => Classes.Remove("drop-over");

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

    /// <summary>
    /// Its kind, as its picture shows it: Folder, Model, Prefab, Texture,
    /// Scene, Shader, Font, Style, Material, Sound, StateMachine, Chain,
    /// Tilemap, Tileset, or File.
    /// </summary>
    public string Kind =>
        IsFolder ? "Folder"
        : Path.EndsWith(Prefab.Extension, StringComparison.OrdinalIgnoreCase) ? "Prefab"
        : Path.EndsWith(Scene.Extension, StringComparison.OrdinalIgnoreCase) ? "Scene"
        : Asset?.Importer switch
        {
            "Mesh" or "Fbx" => "Model",
            "Texture" or "Shader" or "Font" or "Style" or "Material" or "Sound" or "StateMachine" or "Chain" or "Tilemap"
                or "Tileset" => Asset.Importer,
            _ => "File",
        };

    /// <summary>Its rendered thumbnail (ADR 0132), while it has one.</summary>
    internal Thumbnail? Thumbnail => _picture.Thumbnail;

    /// <summary>Whether its picture is an image (a texture's, a tileset's, or a thumbnail), not its kind's icon.</summary>
    internal bool ShowsImage => _picture.ShowsImage;

    /// <summary>Whether its thumbnail carries its kind's badge.</summary>
    internal bool ShowsBadge => _picture.ShowsBadge;

    /// <summary>Its picture's size.</summary>
    internal float PictureSize => _picture.Width;

    // The panel's tile size changed.
    internal void Resize(float size)
    {
        _picture.Resize(size);
        _body.Width = size + 20;
    }

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
        if (_pressedAt is not { } from || Vector2.Distance(from, e.Position) < DragThreshold || (Asset is null && !IsFolder) ||
            Root is not { } root)
        {
            return;
        }
        _pressedAt = null;
        Label preview = new(System.IO.Path.GetFileName(Path));
        preview.Classes.Add("drag-preview");
        root.BeginDrag(Asset is not null ? new AssetDrag(Asset.Id, Path) : new FolderDrag(Path), preview);
    }

    protected override void OnPointerUp(PointerEvent e) => _pressedAt = null;

    protected override void OnActivate(UIEvent e)
    {
        e.Handled = true;
        _panel.Select(this);
    }

    // The picture: a texture's (or a tileset's) own image, a rendered
    // thumbnail (ADR 0132) with its kind's badge, or the kind's icon.
    private sealed class Picture : UIElement
    {
        private readonly AssetTile _tile;
        private readonly UI.Icon _kind;
        private readonly StackPanel _badge;
        private TextureHandle _texture;
        private Thumbnail? _thumbnail;

        public Picture(AssetTile tile)
        {
            _tile = tile;
            HorizontalAlignment = HorizontalAlignment.Center;
            IsHitTestVisible = false;
            _kind = EditorIcons.Icon(IconOf(tile.Kind));
            _kind.Classes.Add("asset-icon");
            AddChild(_kind);
            _badge = new StackPanel { HorizontalAlignment = HorizontalAlignment.End, VerticalAlignment = VerticalAlignment.End };
            _badge.Classes.Add("asset-badge");
            UI.Icon glyph = EditorIcons.Icon(IconOf(tile.Kind));
            glyph.Classes.Add("asset-badge-icon");
            _badge.Add(glyph);
            AddChild(_badge);
            Resize(tile._panel.TileSize);
        }

        public void Resize(float size)
        {
            Width = size;
            Height = size;
            // The kind's icon about half the picture; no smaller than the old drawings read.
            _kind.FontSize = MathF.Round(MathF.Max(size * 0.5f, 24));
            Update();
        }

        public Thumbnail? Thumbnail => _thumbnail;

        public bool ShowsImage => Image.Generation != 0;

        public bool ShowsBadge => _badge.Visibility == Visibility.Visible;

        // The image, if one's ready to draw.
        private TextureHandle Image =>
            _texture.Generation != 0 ? _texture : _thumbnail?.Texture ?? default;

        protected override void OnAttached(UIRoot root)
        {
            Shell shell = _tile._panel.Shell;
            if (_tile.Asset is { } asset && shell.Database is { } database)
            {
                // A tileset shows its sheet; a thumbnail knows for itself whether it's up to date (a prefab isn't imported).
                AssetId? image = database.ImportedFile(asset.Id) is null ? null : _tile.Kind switch
                {
                    "Texture" => asset.Id,
                    "Tileset" => database.ReferencesOf(asset.Id).FirstOrDefault(id => database.Find(id)?.Importer == "Texture"),
                    _ => null,
                };
                if (image is { IsNone: false } texture)
                {
                    _texture = RenderAssets.AcquireTexture(texture);
                }
                else if ((_thumbnail = shell.Thumbnails.Acquire(asset, _tile.Kind)) is not null)
                {
                    _thumbnail.Ready += Update;
                }
            }
            Update();
        }

        protected override void OnDetached(UIRoot root)
        {
            if (_texture.Generation != 0)
            {
                RenderAssets.ReleaseTexture(_texture);
                _texture = default;
            }
            if (_thumbnail is not null)
            {
                _thumbnail.Ready -= Update;
                _tile._panel.Shell.Thumbnails.Release(_thumbnail);
                _thumbnail = null;
            }
        }

        // Which parts show: the kind's icon until there's an image; the
        // badge on a rendered thumbnail big enough to carry one.
        private void Update()
        {
            bool image = Image.Generation != 0;
            _kind.Visibility = image ? Visibility.Collapsed : Visibility.Visible;
            _badge.Visibility = image && _thumbnail?.Texture.Generation is not (null or 0) && Width >= 64
                ? Visibility.Visible
                : Visibility.Collapsed;
            InvalidateVisual();
        }

        protected override void OnDraw(Canvas canvas)
        {
            TextureHandle image = Image;
            if (image.Generation == 0)
            {
                return;
            }
            // Fitted, keeping its shape (a texture may not be square).
            Vector2 area = ActualSize - new Vector2(4);
            TextureSize texels = Crucible.Render.Api.TextureSize(image);
            Vector2 size = area;
            if (texels.Width > 0 && texels.Height > 0)
            {
                float aspect = texels.Width / (float)texels.Height;
                size = aspect >= 1 ? new Vector2(area.X, area.X / aspect) : new Vector2(area.Y * aspect, area.Y);
            }
            canvas.DrawImage(image, (ActualSize - size) * 0.5f, size);
        }
    }

    /// <summary>The Lucide icon a kind shows (ADR 0132).</summary>
    internal static string IconOf(string kind) => kind switch
    {
        "Folder" => "folder",
        "Model" => "box",
        "Prefab" => "package",
        "Texture" => "image",
        "Scene" => "clapperboard",
        "Material" => "circle-dot",
        "Shader" => "code-xml",
        "Font" => "type",
        "Style" => "palette",
        "Sound" => "audio-lines",
        "StateMachine" => "workflow",
        "Chain" => "spline",
        "Tilemap" => "grid-3x3",
        "Tileset" => "layout-grid",
        _ => "file",
    };
}
