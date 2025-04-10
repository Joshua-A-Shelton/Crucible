using System.Runtime.InteropServices;

namespace Crucible.Core;

public class NodeBehavior
{
    public virtual void Update(double deltaTime){}

    private static void UpdateNode(IntPtr behavior, double deltaTime)
    {
        NodeBehavior? node = (NodeBehavior?)GCHandle.FromIntPtr(behavior).Target;
        node?.Update(deltaTime);
    }
}