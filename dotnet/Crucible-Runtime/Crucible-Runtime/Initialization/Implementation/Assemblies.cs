using System.Reflection;
using System.Runtime.Loader;

namespace Crucible.Initialization.Implementation;

internal static class Assemblies
{
    private class AssemblyCount
    {
        public Assembly Assembly;
        public uint Count;

        public AssemblyCount(Assembly assembly)
        {
            Assembly = assembly;
            Count = 1;
        }
    }
    private static Dictionary<string,AssemblyLoadContext> _loadedContexts = new Dictionary<string, AssemblyLoadContext>();
    private static Dictionary<string,AssemblyCount> _loadedAssemblies = new Dictionary<string,AssemblyCount>();

    private static void AddAssemblyReference(Assembly assembly)
    {
        AssemblyCount? assemblyCount = null;
        if (!_loadedAssemblies.TryGetValue(assembly.GetName().FullName, out assemblyCount))
        {
            _loadedAssemblies.Add(assembly.GetName().Name, new AssemblyCount(assembly));
        }
        else
        {
            assemblyCount.Count++;
        }
    }

    private static void RemoveAssemblyReference(string assemblyName)
    {
        AssemblyCount? assemblyCount = null;
        if (!_loadedAssemblies.TryGetValue(assemblyName, out assemblyCount))
        {
            throw new KeyNotFoundException("Assembly not found: " + assemblyName);
        }
        else
        {
            assemblyCount.Count--;
            if (assemblyCount.Count == 0)
            {
                _loadedAssemblies.Remove(assemblyName);
            }
        }   
    }
    
    
    private static Assembly? ResolveAssembly(AssemblyLoadContext? context, AssemblyName assemblyName)
    {
        //if we've already loaded an assembly with the given name, re-use it instead of loading it again
        if (_loadedAssemblies.TryGetValue(assemblyName.FullName, out var loadedAssembly))
        {
            return loadedAssembly.Assembly;
        }

        return null;
    }
    
    public static void LoadAssembly(string contextName, string path, bool collectible)
    {
        try
        {
            var full = Path.GetFullPath(path);
            AssemblyLoadContext? context = null;
            if (string.IsNullOrEmpty(contextName))
            {
                context = AssemblyLoadContext.Default;
            }
            else if (_loadedContexts.ContainsKey(contextName))
            {
                context = _loadedContexts[contextName];
            }
            else
            {
                context = new AssemblyLoadContext(full,collectible);
                context.Resolving += ResolveAssembly;
                context.Unloading += loadContext =>
                {
                    foreach (var assembly in loadContext.Assemblies)
                    {
                        RemoveAssemblyReference(assembly.GetName().FullName);
                    }
                };
                
                _loadedContexts[contextName] = context;
            }

            var assembly = context.LoadFromAssemblyPath(full);
            AddAssemblyReference(assembly);
            foreach (var asmbly in context.Assemblies)
            {
                AddAssemblyReference(asmbly);
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
    }
    
    public static void UnloadContext(string contextName)
    {
        AssemblyLoadContext? context;
        if (_loadedContexts.TryGetValue(contextName, out context))
        {
            context.Unload();
            _loadedContexts.Remove(contextName);
        }
        else
        {
            throw new KeyNotFoundException("Context not found: " + contextName);
        }
    }

    public static void UnloadAllContexts()
    {
        foreach (var context in _loadedContexts)
        {
            context.Value.Unload();
        }
        _loadedContexts.Clear();
    }
}