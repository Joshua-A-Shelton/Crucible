using Crucible.Nodes;

namespace Crucible;

public abstract class Script
{
    public Node Node { get; private set; }
    public virtual void Update(float deltaTime)
    {
        return;
    }
}