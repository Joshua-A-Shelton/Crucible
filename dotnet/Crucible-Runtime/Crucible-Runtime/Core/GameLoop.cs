namespace Crucible.Core;

public abstract class GameLoop
{
    public virtual void Initialize()
    {
        
    }
    
    public abstract void Update(double deltaTime);
}