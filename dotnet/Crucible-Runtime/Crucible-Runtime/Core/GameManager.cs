﻿using System.Reflection;
using System.Runtime.InteropServices;
using Arch.Core;

namespace Crucible.Core;

public static class GameManager
{
    private static GameLoop _loop;
    private static World _gameWorld = World.Create();

    [UnmanagedCallersOnly]
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

    }
    [UnmanagedCallersOnly]
    private static void RunLoop(double deltaTime)
    {
        _loop.Update(deltaTime);
    }


    private static void CreateEntity(ref Entity entity)
    {
        entity = _gameWorld.Create();
    }
    

    private static void DestroyEntity(ref Entity entity)
    {
        _gameWorld.Destroy(entity);
    }
}