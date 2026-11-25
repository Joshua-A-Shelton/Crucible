using Crucible.Ecs;
using Crucible.Scenes;

namespace Crucible.Editor;

/// <summary>
/// Files in the Assets folder written as undo steps: the editor's own, and
/// extensions' (EditorAssets.Write, Delete; ADR 0117, 0123).
/// </summary>
internal sealed partial class Shell
{
    // A file in the Assets folder written or deleted (null), and back on undo.
    private sealed class AssetFileEdit(Shell shell, string title, string path, byte[]? before, byte[]? after) : UndoStep(title)
    {
        public AssetFileEdit(Shell shell, string title, string path, string? before, string? after)
            : this(shell, title, path, Bytes(before), Bytes(after))
        {
        }

        public override bool ChangesScene => false;

        public override void Undo(World world) => Write(shell, path, before);

        public override void Redo(World world) => Write(shell, path, after);

        public static bool Write(Shell shell, string path, string? text) => Write(shell, path, Bytes(text));

        public static bool Write(Shell shell, string path, byte[]? contents)
        {
            if (shell.Project is not { } project || !shell.CanChange(path))
            {
                return false;
            }
            string file = project.FileOf(path);
            try
            {
                if (contents is null)
                {
                    File.Delete(file);
                }
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                    File.WriteAllBytes(file, contents);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                shell.Log($"Can't {(contents is null ? "delete" : "save")} {path}: {exception.Message}");
                return false;
            }
            shell.RefreshAssets();
            return true;
        }

        // As File.WriteAllText writes it: UTF-8, no byte order mark.
        private static byte[]? Bytes(string? text) => text is null ? null : System.Text.Encoding.UTF8.GetBytes(text);
    }


    // An entity and everything under it.
    private List<Entity> SubtreeOf(Entity root)
    {
        List<Entity> found = [];
        Stack<Entity> pending = new([root]);
        while (pending.Count > 0)
        {
            Entity entity = pending.Pop();
            found.Add(entity);
            foreach (Entity child in SceneIds.ChildrenOf(World, entity))
            {
                pending.Push(child);
            }
        }
        return found;
    }
}
