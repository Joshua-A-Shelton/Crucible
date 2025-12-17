namespace Crucible.Core;
/// <summary>
/// Allows customization of the game updates
/// </summary>
public abstract class GameLoop
{
    /// <summary>
    /// Overrideable behavior that executes when the loop is first initialized
    /// </summary>
    public virtual void Initialize()
    {
        
    }
    /// <summary>
    /// Overridable behavior that executes once per frame for updating the game world
    /// </summary>
    /// <param name="deltaTime"></param>
    public abstract void Update(double deltaTime);
}