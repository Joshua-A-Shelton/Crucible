using Crucible;

namespace CrucibleEditor;

public abstract class EditorWindow
{
    public string Title { get; private set; }

    protected abstract void render();
    
    public void Draw()
    {
        beginWindow();
        render();
        endWindow();
    }
    
    private unsafe void beginWindow()
    {
        Unmanaged.ImGuiBegin(Title);
    }

    private unsafe void endWindow()
    {
        Unmanaged.ImGuiEnd();
    }
}

internal unsafe class Unmanaged
{
    public static delegate* unmanaged<string,void*> ImGuiBegin = null;
    public static delegate* unmanaged<void*> ImGuiEnd = null;
}