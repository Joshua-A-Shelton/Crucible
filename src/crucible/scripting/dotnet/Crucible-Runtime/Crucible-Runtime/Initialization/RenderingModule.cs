using System.Runtime.InteropServices;

namespace Crucible.Initialization;

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct RenderingModule
{
    Initializer.CrucibleModule module;

    public void Initialize()
    {
        
    }
}