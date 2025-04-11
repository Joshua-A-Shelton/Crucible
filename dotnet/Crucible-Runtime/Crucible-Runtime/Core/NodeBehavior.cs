using System.Runtime.InteropServices;

namespace Crucible.Core;
/// <summary>
/// Base class for scripting node behaviors
/// </summary>
public class NodeBehavior
{
    //Called once per frame
    public virtual void Update(double deltaTime){}

    private static void UpdateNode(IntPtr behavior, double deltaTime)
    {
        NodeBehavior? node = (NodeBehavior?)GCHandle.FromIntPtr(behavior).Target;
        node?.Update(deltaTime);
    }
}