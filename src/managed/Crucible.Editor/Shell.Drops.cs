using System.Numerics;
using System.Text.Json.Nodes;
using Crucible.Assets;
using Crucible.Ecs;
using Crucible.Platform;
using Crucible.Render;
using Crucible.Render2D;
using Crucible.Scenes;
using Crucible.UI;

namespace Crucible.Editor;

/// <summary>
/// Other programs' files dragged over the editor (ADR 0115), as a UI drag:
/// the element under the pointer says where they'd go. The paths arrive
/// only when they're dropped.
/// </summary>
public sealed class FileDrag
{
    /// <summary>The dropped files and folders, full paths; empty until the drop.</summary>
    public List<string> Paths { get; } = [];

    // Set by the drop target under the pointer, each move.
    internal string? Folder;        // the Assets panel: the folder they'd go in
    internal SceneView? View;       // a viewport: placed where dropped
    internal Vector2 ViewPoint;     // in the view, logical units
    internal bool OnHierarchy;      // the hierarchy: placed under a row
    internal Entity Parent;

    internal void ClearTarget()
    {
        Folder = null;
        View = null;
        OnHierarchy = false;
        Parent = Entity.Null;
    }
}

/// <summary>Where dropped models and pixel art are placed (ADR 0115): at a point in a viewport, or under a hierarchy row.</summary>
public readonly record struct DropPlacement(bool InView, Vector3 Point, Vector2 Point2D, Entity Parent);

// Importing dropped files (ADR 0115): copied into the Assets folder (a
// folder whole, a .gltf with its buffers and images), asking about each
// name already there and how to import each new image, then imported; a
// model or pixel art dropped on a viewport or the hierarchy is placed.
internal sealed partial class Shell
{
    private static readonly string[] _imageExtensions = [".png", ".jpg", ".jpeg", ".tga"];

    private FileDrag? _fileDrag;
    private WindowHandle _fileDragWindow;
    private Vector2 _fileDragAt; // logical units in that window
    private readonly Label _dropNote = new();
    private DropImport? _dropImport;

    /// <summary>The question a drop is waiting on, or null (the checks answer it).</summary>
    public DropQuestion? DropQuestion { get; private set; }

    /// <summary>Whether a drop is still asking or copying.</summary>
    public bool Dropping => _dropImport is not null;

    /// <summary>The note beside the pointer while files are dragged over: "Drop to import into Textures/".</summary>
    public string DropNote => _dropNote.Text;

    private void FileDragMoved(WindowHandle over, Vector2 pixels)
    {
        _fileDrag ??= new FileDrag();
        MoveFileDrag(over, pixels);
    }

    private void FileDropped(WindowHandle over, Vector2 pixels, string? path)
    {
        _fileDrag ??= new FileDrag();
        if (!string.IsNullOrEmpty(path))
        {
            _fileDrag.Paths.Add(path);
        }
        MoveFileDrag(over, pixels);
    }

    private void MoveFileDrag(WindowHandle over, Vector2 pixels)
    {
        FileDrag drag = _fileDrag!;
        EditorWindow? target = _windows.Find(window => window.Window.Value == over.Value);
        foreach (EditorWindow window in _windows)
        {
            if (window != target && window.Root.DragPayload == drag)
            {
                window.Root.CancelDrag();
            }
        }
        if (target is null)
        {
            return;
        }
        _fileDragWindow = over;
        _fileDragAt = pixels / target.Root.Scale;
        if (_dropNote.Classes.Count == 0)
        {
            _dropNote.Classes.Add("drag-preview");
        }
        drag.ClearTarget();
        target.Root.ContinueDrag(drag, _dropNote, _fileDragAt);
        _dropNote.Text = Project is null ? "Open a project to import files" : $"Drop to import into {FolderLabel(DropFolder(drag))}";
    }

    // The drag over a window ended: dropped (its paths came first), or left.
    private void FileDragEnded()
    {
        if (_fileDrag is not { } drag)
        {
            return;
        }
        _fileDrag = null;
        EditorWindow? target = _windows.Find(window => window.Window.Value == _fileDragWindow.Value);
        DropPlacement? place = null;
        if (drag.Paths.Count > 0 && target is not null && target.Root.DragPayload == drag)
        {
            drag.ClearTarget();
            target.Root.DropAt(_fileDragAt); // the target under it fills in where they go
            place = PlacementOf(drag);
        }
        foreach (EditorWindow window in _windows)
        {
            if (window.Root.DragPayload == drag)
            {
                window.Root.CancelDrag();
            }
        }
        if (drag.Paths.Count > 0)
        {
            ImportFiles(drag.Paths, DropFolder(drag), place);
        }
    }

    // The Assets panel's folder under the pointer, else the one it shows.
    private string DropFolder(FileDrag drag) =>
        drag.Folder ?? (_open.GetValueOrDefault("Assets") as AssetsPanel)?.Folder ?? "";

    private static string FolderLabel(string folder) =>
        folder.Length == 0 ? "Assets/" : folder.EndsWith(AssetPaths.PluginSeparator) ? folder : folder + "/";

    private DropPlacement? PlacementOf(FileDrag drag)
    {
        if (drag.OnHierarchy)
        {
            return new DropPlacement(false, default, default, drag.Parent);
        }
        if (drag.View is not { } view)
        {
            return null;
        }
        Vector2 size = view.ActualSize;
        Vector3 point = PlacementPoint(view.Camera, size, drag.ViewPoint, Render.Api.PickResult(view.Surface));
        // Pixel art goes on the 2D plane (z = 0) under the pointer.
        (Vector3 origin, Vector3 direction) = view.Camera.Ray(drag.ViewPoint, size);
        Vector2 flat = MathF.Abs(direction.Z) > 1e-5f && -origin.Z / direction.Z is var t and > 0
            ? new Vector2(origin.X + direction.X * t, origin.Y + direction.Y * t)
            : new Vector2(point.X, point.Y);
        return new DropPlacement(true, point, flat, Entity.Null);
    }

    /// <summary>
    /// Copies files and folders (full paths) into a folder of the Assets
    /// folder and imports them, asking first where a name is taken and how a
    /// new image imports; then places what <paramref name="place"/> says.
    /// The questions come one at a time (<see cref="DropQuestion"/>).
    /// </summary>
    public void ImportFiles(IReadOnlyList<string> paths, string folder, DropPlacement? place = null)
    {
        if (Project is null || Database is null)
        {
            Status("Open a project to import files");
            return;
        }
        if (_dropImport is not null)
        {
            Status("Finish the drop in progress first");
            return;
        }
        if (!CanChange(folder))
        {
            return;
        }
        _dropImport = new DropImport(this, [.. paths], folder, place);
        _dropImport.Continue();
    }

    private void AskDrop(string message, IReadOnlyList<string> choices, int suggested, bool offerRest, Action<int, bool> answered,
        string? restLabel = null)
    {
        DropQuestion question = new(message, choices, suggested, offerRest, restLabel);
        question.Chosen += (asked, choice, rest) =>
        {
            if (DropQuestion == asked)
            {
                DropQuestion = null;
            }
            answered(choice, rest);
        };
        DropQuestion = question;
        question.Show(Main.Root);
    }

    /// <summary>A sprite of a whole texture, at a point of the 2D plane, under a parent (or at the top), selected.</summary>
    internal Entity CreateSprite(AssetId texture, Vector2 at, Entity parent)
    {
        if (!_context.HasWorld)
        {
            return Entity.Null;
        }
        Entity entity = World.CreateEntity();
        World.Set(entity, Transform2D.Identity with { Position = at });
        World.Set(entity, new SceneEntityId { Value = SceneIds.NewId() });
        World.Set(entity, new Sprite { Texture = texture });
        string name = Path.GetFileNameWithoutExtension(Database?.Find(texture)?.Path ?? "Sprite");
        World.SetManaged(entity, new EntityName { Value = name });
        if (HasLive(parent))
        {
            World.SetParent(entity, parent);
        }
        RecordCreated($"Place {name}", entity);
        Selection.Selected = entity;
        return entity;
    }

    // One drop's work: the questions first, then the copies, then the import.
    private sealed class DropImport(Shell shell, List<string> paths, string folder, DropPlacement? place)
    {
        private enum Clash
        {
            Replace,
            KeepBoth,
            Skip,
        }

        private static readonly string[] _clashChoices = ["Replace", "Keep Both", "Skip"];
        private static readonly string[] _imageChoices = ["Pixel Art", "Texture", "UI"];

        // A file to copy: from, to (a path under Assets), and what it brings.
        private sealed class Copy
        {
            public required string From;
            public required string To;
            public bool Replaces;      // over a file already there: its sidecar (and GUID) stay
            public bool TopLevel;      // dropped itself, not inside a folder or beside a .gltf
            public ImageGuess? Image;  // a new image to ask about
            public ImageKind? Kind;    // the answer
            public JsonObject? Gltf;   // a .gltf whose companions were renamed: written instead of copied
        }

        private readonly List<Copy> _copies = [];
        private readonly HashSet<string> _planned = new(StringComparer.OrdinalIgnoreCase);
        private int _next;          // the dropped path being planned
        private int _nextImage;     // the image being asked about
        private Clash? _clashRest;
        private ImageKind? _imageRest;

        private string Full(string assetPath) => shell.Project!.FileOf(assetPath);

        private static string Join(string folder, string name) => AssetPaths.Join(folder, name);

        /// <summary>Plans, asks, and finishes as far as it can before the next question.</summary>
        public void Continue()
        {
            while (_next < paths.Count)
            {
                string source = Path.GetFullPath(paths[_next]);
                if (!Plan(source))
                {
                    return; // asking
                }
                ++_next;
            }
            while (_nextImage < _copies.Count)
            {
                Copy copy = _copies[_nextImage];
                if (copy.Image is { } guess && copy.Kind is null && !AskImage(copy, guess))
                {
                    return; // asking
                }
                ++_nextImage;
            }
            Finish();
        }

        private void Stop()
        {
            shell._dropImport = null;
            shell.Status("Import canceled");
        }

        // A dropped path planned; false while a question about it is open.
        private bool Plan(string source)
        {
            string name = Path.GetFileName(source.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            bool isFolder = Directory.Exists(source);
            if (!isFolder && !File.Exists(source))
            {
                shell.Log($"Can't import {source}: it isn't there any more");
                return true;
            }
            if (name.EndsWith(Sidecar.Extension, StringComparison.OrdinalIgnoreCase))
            {
                return true; // comes with its file
            }
            string to = Join(folder, name);
            if (Path.GetFullPath(Full(to)) == source)
            {
                shell.Log($"{to} is already in the project");
                return true;
            }
            if (!isFolder && _planned.Contains(to))
            {
                return true; // brought already, beside a .gltf dropped with it
            }
            Clash clash = Clash.KeepBoth;
            bool taken = File.Exists(Full(to)) || Directory.Exists(Full(to)) || _planned.Contains(to);
            if (taken)
            {
                if (_clashRest is { } rest)
                {
                    clash = rest;
                }
                else
                {
                    bool more = paths.Skip(_next + 1).Any(other => Exists(Join(folder, Path.GetFileName(other))));
                    shell.AskDrop($"{name} is already there", _clashChoices, 1, more, (choice, forRest) =>
                    {
                        if (choice < 0)
                        {
                            Stop();
                            return;
                        }
                        Clash answer = (Clash)choice;
                        if (forRest)
                        {
                            _clashRest = answer;
                        }
                        Planned(source, name, isFolder, answer);
                        ++_next;
                        Continue();
                    });
                    return false;
                }
            }
            Planned(source, name, isFolder, taken ? clash : null);
            return true;
        }

        private bool Exists(string assetPath) => File.Exists(Full(assetPath)) || Directory.Exists(Full(assetPath));

        private void Planned(string source, string name, bool isFolder, Clash? clash)
        {
            if (clash == Clash.Skip)
            {
                shell.Log($"Skipped {name}: one is already there");
                return;
            }
            string to = Join(folder, name);
            if (clash == Clash.KeepBoth)
            {
                to = Free(folder, name, isFolder);
            }
            bool replace = clash == Clash.Replace;
            if (isFolder)
            {
                foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
                {
                    string relative = Path.GetRelativePath(source, file).Replace('\\', '/');
                    if (!relative.Split('/').All(AssetsPanel.Visible) || relative.EndsWith(Sidecar.Extension, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    AddFile(file, Join(to, relative), replace, topLevel: false);
                }
            }
            else
            {
                AddFile(source, to, replace, topLevel: true);
            }
        }

        // A name not taken in the folder (nor planned): "Grass 2.png", "Trees 2".
        private string Free(string inFolder, string name, bool isFolder)
        {
            string stem = isFolder ? name : Path.GetFileNameWithoutExtension(name);
            string extension = isFolder ? "" : Path.GetExtension(name);
            string path = Join(inFolder, name);
            for (int n = 2; Exists(path) || _planned.Contains(path); ++n)
            {
                path = Join(inFolder, $"{stem} {n}{extension}");
            }
            return path;
        }

        private void AddFile(string from, string to, bool replace, bool topLevel)
        {
            bool replaces = replace && File.Exists(Full(to));
            Copy copy = new() { From = from, To = to, Replaces = replaces, TopLevel = topLevel };
            _copies.Add(copy);
            _planned.Add(to);
            string extension = Path.GetExtension(from).ToLowerInvariant();
            // A replaced file keeps its settings; one with a sidecar beside it brings its own.
            if (!replaces && !File.Exists(from + Sidecar.Extension) && _imageExtensions.Contains(extension))
            {
                try
                {
                    copy.Image = ImageGuess.Of(File.ReadAllBytes(from));
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    shell.Log($"Can't read {from}: {exception.Message}");
                }
            }
            if (extension == ".gltf")
            {
                AddCompanions(copy, replace);
            }
        }

        // A .gltf's buffers and images by relative URI, beside it as they were;
        // one that would land on a different file gets a free name, and the
        // copy's URI follows it.
        private void AddCompanions(Copy gltf, bool replace)
        {
            JsonObject? document;
            try
            {
                document = JsonNode.Parse(File.ReadAllText(gltf.From)) as JsonObject;
            }
            catch (Exception exception) when (exception is IOException or System.Text.Json.JsonException)
            {
                shell.Log($"Can't read {Path.GetFileName(gltf.From)}'s buffers and images: {exception.Message}");
                return;
            }
            if (document is null)
            {
                return;
            }
            string sourceFolder = Path.GetDirectoryName(gltf.From) ?? "";
            string targetFolder = Path.GetDirectoryName(gltf.To)?.Replace('\\', '/') ?? "";
            bool renamed = false;
            foreach (string list in (string[])["buffers", "images"])
            {
                if (document[list] is not JsonArray items)
                {
                    continue;
                }
                foreach (JsonNode? item in items)
                {
                    if (item is not JsonObject entry || entry["uri"]?.GetValue<string>() is not { } uri ||
                        uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase) || uri.Contains("://", StringComparison.Ordinal))
                    {
                        continue;
                    }
                    string relative = Uri.UnescapeDataString(uri).Replace('\\', '/');
                    string from = Path.GetFullPath(Path.Combine(sourceFolder, relative));
                    if (!File.Exists(from))
                    {
                        shell.Log($"{Path.GetFileName(gltf.From)} names {relative}, which isn't there");
                        continue;
                    }
                    // Beside it as it was; outside its folder (..), by name alone.
                    string placed = relative.Split('/').Contains("..") || Path.IsPathRooted(relative) ? Path.GetFileName(from) : relative;
                    string to = Join(targetFolder, placed);
                    if (_planned.Contains(to))
                    {
                        continue; // named twice
                    }
                    if (File.Exists(Full(to)) && !replace)
                    {
                        if (SameBytes(from, Full(to)))
                        {
                            _planned.Add(to);
                            continue; // already there as it is
                        }
                        to = Free(Path.GetDirectoryName(to)?.Replace('\\', '/') ?? "", Path.GetFileName(to), isFolder: false);
                        string fromGltf = string.IsNullOrEmpty(targetFolder) ? to : Path.GetRelativePath(targetFolder, to).Replace('\\', '/');
                        entry["uri"] = Uri.EscapeDataString(fromGltf).Replace("%2F", "/", StringComparison.Ordinal);
                        renamed = true;
                    }
                    _copies.Add(new Copy { From = from, To = to, Replaces = File.Exists(Full(to)) });
                    _planned.Add(to);
                }
            }
            if (renamed)
            {
                gltf.Gltf = document;
            }
        }

        private static bool SameBytes(string a, string b)
        {
            try
            {
                return new FileInfo(a).Length == new FileInfo(b).Length && File.ReadAllBytes(a).AsSpan().SequenceEqual(File.ReadAllBytes(b));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }

        // How an image imports; false while asking.
        private bool AskImage(Copy copy, ImageGuess guess)
        {
            if (_imageRest is { } rest)
            {
                copy.Kind = rest == ImageKind.Texture && !guess.TextureAllowed ? guess.Kind : rest;
                return true;
            }
            string[] choices = guess.TextureAllowed ? _imageChoices : [_imageChoices[0], _imageChoices[2]];
            ImageKind[] kinds = guess.TextureAllowed ? [ImageKind.PixelArt, ImageKind.Texture, ImageKind.Ui] : [ImageKind.PixelArt, ImageKind.Ui];
            bool more = _copies.Skip(_nextImage + 1).Any(other => other.Image is not null);
            shell.AskDrop($"Import {Path.GetFileName(copy.To)} as", choices, Array.IndexOf(kinds, guess.Kind), more, (choice, forRest) =>
            {
                if (choice < 0)
                {
                    Stop();
                    return;
                }
                copy.Kind = kinds[choice];
                if (forRest)
                {
                    _imageRest = kinds[choice];
                }
                ++_nextImage;
                Continue();
            });
            return false;
        }

        private void Finish()
        {
            shell._dropImport = null;
            int copied = 0;
            foreach (Copy copy in _copies)
            {
                if (Write(copy))
                {
                    ++copied;
                }
            }
            if (copied == 0)
            {
                return;
            }
            shell.RefreshAssets();
            AssetDatabase database = shell.Database!;
            foreach (Copy copy in _copies)
            {
                AssetEntry? entry = database.Find(copy.To);
                bool own = copy.To.EndsWith(Scene.Extension, StringComparison.OrdinalIgnoreCase) ||
                           copy.To.EndsWith(Prefab.Extension, StringComparison.OrdinalIgnoreCase);
                if (entry is null || (entry.Importer.Length == 0 && !own))
                {
                    shell.Log($"Copied {copy.To}: nothing imports {Path.GetExtension(copy.To)} files");
                }
            }
            shell.Log($"Dropped {copied} file{(copied == 1 ? "" : "s")} into {FolderLabel(folder)}");
            shell.Status($"Dropped {copied} file{(copied == 1 ? "" : "s")} into {FolderLabel(folder)}");
            PlaceDropped(database);
            List<Copy> pixelArt = [.. _copies.Where(copy => copy.Kind == ImageKind.PixelArt)];
            if (pixelArt.Count == 1 && database.Find(pixelArt[0].To) is { } sheet)
            {
                shell.ShowSpriteSheet(sheet.Id);
            }
        }

        // The file (and a new image's or a brought sidecar's settings) in place.
        private bool Write(Copy copy)
        {
            string to = Full(copy.To);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                // The sidecar first, so a rescan never imports it with the defaults.
                if (!copy.Replaces && copy.Kind is { } kind)
                {
                    WriteSidecar(to, "Texture", ImageGuess.SettingsFor(kind));
                }
                else if (!copy.Replaces && File.Exists(copy.From + Sidecar.Extension) &&
                         JsonNode.Parse(File.ReadAllText(copy.From + Sidecar.Extension)) is JsonObject brought)
                {
                    // Its settings kept, with an identity of its own here.
                    WriteSidecar(to, brought["Importer"]?.GetValue<string>() ?? "", brought["Settings"] as JsonObject);
                }
                if (copy.Gltf is { } document)
                {
                    File.WriteAllText(to, document.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
                }
                else
                {
                    File.Copy(copy.From, to, overwrite: true);
                }
                return true;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
            {
                shell.Log($"Can't copy {copy.From} to {copy.To}: {exception.Message}");
                return false;
            }
        }

        private static void WriteSidecar(string file, string importer, JsonObject? settings)
        {
            JsonObject sidecar = new()
            {
                ["Guid"] = AssetId.New().ToString(),
                ["Importer"] = importer,
            };
            if (settings is not null)
            {
                sidecar["Settings"] = settings.DeepClone();
            }
            File.WriteAllText(file + Sidecar.Extension, sidecar.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }) + "\n");
        }

        // Dropped on a viewport or the hierarchy: each model, prefab, or pixel art dropped placed.
        private void PlaceDropped(AssetDatabase database)
        {
            if (place is not { } where || !shell._context.HasWorld)
            {
                return;
            }
            foreach (Copy copy in _copies.Where(copy => copy.TopLevel || copy.Kind == ImageKind.PixelArt))
            {
                if (database.Find(copy.To) is not { } entry)
                {
                    continue;
                }
                if (copy.Kind == ImageKind.PixelArt)
                {
                    shell.CreateSprite(entry.Id, where.InView ? where.Point2D : Vector2.Zero, where.Parent);
                }
                else if (shell.CanPlace(new AssetDrag(entry.Id, copy.To)))
                {
                    shell.Place(new AssetDrag(entry.Id, copy.To), where.Parent, where.InView ? where.Point : null);
                }
            }
        }
    }
}
