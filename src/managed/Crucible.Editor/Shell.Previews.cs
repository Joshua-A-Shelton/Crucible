using Crucible.UI;

namespace Crucible.Editor;

/// <summary>
/// Preview views (ADR 0125): every one made, ticked each frame (their
/// clocks, their animation, drawn while shown), and freed when the panel or
/// workspace holding it closes, or the editor stops.
/// </summary>
internal sealed partial class Shell
{
    private readonly List<PreviewView> _previews = [];

    /// <summary>The previews not yet disposed (the checks).</summary>
    internal IReadOnlyList<PreviewView> Previews => _previews;

    internal void AddPreview(PreviewView preview) => _previews.Add(preview);

    internal void RemovePreview(PreviewView preview) => _previews.Remove(preview);

    // A panel closing (or replaced) frees the previews inside it.
    private void DisposePreviewsIn(UIElement element)
    {
        foreach (PreviewView preview in _previews.ToArray())
        {
            for (UIElement? at = preview; at is not null; at = at.Parent)
            {
                if (at == element)
                {
                    preview.Dispose();
                    break;
                }
            }
        }
    }
}
