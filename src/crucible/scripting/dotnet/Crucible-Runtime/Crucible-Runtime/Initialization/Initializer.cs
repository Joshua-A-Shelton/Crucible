using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Crucible.Initialization;

public static class Initializer
{
    public enum CrucibleModule
    {
        Rendering = 1,
        Scripting = 2
    }
    [UnmanagedCallersOnly(EntryPoint = "RuntimeEntry")]
    public static unsafe int RuntimeEntry(IntPtr entryArgs, int argLength)
    {
        var submoduleArray = (IntPtr*)entryArgs;
        for(int i = 0; i< argLength; i++)
        {
            var submodule = submoduleArray[i];
            var modelId_ptr = (CrucibleModule*)submodule;
            var moduleID =  *modelId_ptr;
            switch (moduleID)
            {
                case CrucibleModule.Rendering:
                    var renderingModule = (RenderingModule*)submodule;
                    renderingModule->Initialize();
                    break;
                case CrucibleModule.Scripting:
                    var scriptingModule = (ScriptingModule*)submodule;
                    scriptingModule->Initialize();
                    break;
                default:
                    return 1;
            }
        }
        return 0;
    }
}