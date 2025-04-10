using System.Reflection;
using System.Runtime.InteropServices;

namespace Crucible.Core;

public unsafe static class GameManager
{
    private static GameLoop? _loop;
    
    private static void Initialize()
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
    
    private static void RunLoop(double deltaTime)
    {
        _loop?.Update(deltaTime);
    }

    private static void CleanUp()
    {
        _loop = null;
        GC.Collect();
        GC.WaitForPendingFinalizers();
    }
    
#pragma warning disable 0649
    private static delegate* unmanaged<ref Node, void> _gameManagerGetRootNode_ptr;
#pragma warning restore 0649

    public static NodeReference RootNode
    {
        get
        {
            Node node = new Node(IntPtr.Zero);
            _gameManagerGetRootNode_ptr(ref node);
            return new NodeReference(node.Uuid());
        }
    }
}