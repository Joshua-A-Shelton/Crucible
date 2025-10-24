using System.Reflection;

namespace Crucible.Core;

internal static unsafe class GameManager
{
    private static GameLoop? _loop;
    
    internal static void Initialize()
    {
        var subclasses =
            from assembly in AppDomain.CurrentDomain.GetAssemblies()
            from type in assembly.GetTypes()
            where type.IsSubclassOf(typeof(GameLoop))
            select type;
        if (subclasses.Count() == 0)
        {
            throw new NotImplementedException("No class inheriting from \"GameLoop\" was found");
        }
        else if (subclasses.Count() > 1)
        {
            throw new AmbiguousMatchException("More than one class inherits \"GameLoop\"");
        }

        var gameLoop = subclasses.ElementAt(0);
        var gameLoopInstance = Activator.CreateInstance(gameLoop);
        if (gameLoopInstance == null)
        {
            throw new TargetException("Unable to create instance of type \"" + gameLoop.FullName + "\"");
        }

        _loop = (GameLoop)gameLoopInstance;
        
        _loop.Initialize();
    }

    internal static void CleanUp()
    {
        _loop = null;
    }
}